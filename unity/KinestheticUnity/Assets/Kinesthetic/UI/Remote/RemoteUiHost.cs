using System.Collections.Generic;
using Kinesthetic.Shell;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.UI.Remote
{
    /// The Mac side. Every Host board is serialized up to ten times a second and what changed goes to the
    /// relay as a patch; a resync request gets a full snapshot; a press from a headset is resolved on the
    /// named board and pressed exactly as a local click would be. The UI logic itself is untouched — the
    /// menu, the HUDs and the studio keep their own handlers, and this only watches trees and pushes
    /// buttons.
    ///
    /// It mirrors only while someone is listening: idle until the first `ui.resync` of a connection, and
    /// idle again when the connection drops, since the relay asks for a snapshot on behalf of every client
    /// that is waiting when the host returns. Presses are handled whenever they arrive.
    ///
    /// One per process, created on load on anything that is not a headset. It survives scene changes so a
    /// board that goes away with its scene is reported as `unboard` and the next scene's boards as `board`.
    public sealed class RemoteUiHost : MonoBehaviour
    {
        public const string Url = "ws://127.0.0.1:8767/ui?role=host";
        const float Interval = .1f, OversizeBackoffSeconds = 5f;
        const int MaxPayload = 1024 * 1024;   // the relay's host→relay limit; beyond it the socket is closed
        const int RememberedPresses = 512, RememberedClients = 32;

        public static RemoteUiHost Instance { get; private set; }

        UiSocket socket;
        int generation;
        long rev;
        float next, backoffUntil;
        bool active, resyncRequested, warnedOversize;
        int dropped;
        Dictionary<string, JObject> last = new();

        // A retried press must act once: (client, seq) already handled is acked again and not pressed.
        // Bounded in both dimensions, so a relay handing out fresh ids forever cannot grow this.
        readonly Dictionary<string, Queue<long>> order = new();
        readonly Dictionary<string, HashSet<long>> handled = new();
        readonly Queue<string> clients = new();

        // Two headsets pressing the same button inside a bounce are one press, like two local input paths.
        readonly PressGate gate = new();

        readonly HashSet<string> warnedDuplicates = new();

        public bool Connected => socket != null && socket.connected;
        public bool Active => active;
        public long Rev => rev;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Application.platform == RuntimePlatform.Android) return;
            Ensure();
        }

        public static RemoteUiHost Ensure()
        {
            if (Instance) return Instance;
            var go = new GameObject("Remote UI host");
            DontDestroyOnLoad(go);
            return go.AddComponent<RemoteUiHost>();
        }

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            socket = new UiSocket(Url);
            UiCue.Sender = Send;   // cues ride this socket, next to the trees
        }

        void OnDestroy()
        {
            socket?.Dispose();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (socket == null) return;   // a duplicate instance on its way out
            if (socket.generation != generation)
            {
                // A new connection knows nothing of the last: wait to be asked again.
                generation = socket.generation;
                active = false;
                resyncRequested = false;
                last.Clear();
            }
            // Presses are handled the frame they arrive; trees are diffed on the slower clock.
            while (socket.TryReceive(out var text)) Handle(text);
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + Interval;
            Tick();
        }

        void Handle(string text)
        {
            JObject message;
            try { message = JObject.Parse(text); } catch (JsonException) { return; }
            switch ((string)message["type"])
            {
                case "ui.resync": resyncRequested = true; break;
                case "ui.press": Press(message); break;
            }
        }

        /// Serialize every live Host board; send a snapshot if one was asked for, otherwise what changed.
        void Tick()
        {
            if (!socket.connected) { active = false; resyncRequested = false; return; }
            if (!active && !resyncRequested) return;   // nobody is listening
            if (Time.unscaledTime < backoffUntil) return;   // the last snapshot was too big; do not loop on it

            var trees = new Dictionary<string, JObject>();
            foreach (var board in RemoteBoard.Live)
            {
                if (!board.IsHost || string.IsNullOrEmpty(board.id)) continue;
                var root = board.Root;
                if (root == null) continue;
                if (trees.ContainsKey(board.id))
                {
                    if (warnedDuplicates.Add(board.id)) Debug.LogWarning($"Remote UI: two host boards named '{board.id}'; only the first is mirrored.");
                    continue;
                }
                trees[board.id] = PanelWire.Serialize(root);
            }

            if (resyncRequested)
            {
                var boards = new JArray();
                foreach (var pair in trees) boards.Add(new JObject { ["id"] = pair.Key, ["tree"] = pair.Value });
                var snapshot = new JObject { ["type"] = "ui.snapshot", ["proto"] = PanelWire.Proto, ["vocab"] = PanelWire.VocabHash, ["rev"] = rev + 1, ["boards"] = boards };
                if (!Send(snapshot)) { backoffUntil = Time.unscaledTime + OversizeBackoffSeconds; return; }
                rev++;
                last = trees;
                active = true;
                resyncRequested = false;
                return;
            }

            var ops = new JArray();
            foreach (var pair in trees)
            {
                if (last.TryGetValue(pair.Key, out var old)) PanelWire.Diff(old, pair.Value, "", pair.Key, ops);
                else ops.Add(new JObject { ["op"] = "board", ["id"] = pair.Key, ["tree"] = pair.Value });
            }
            foreach (var id in last.Keys)
                if (!trees.ContainsKey(id)) ops.Add(new JObject { ["op"] = "unboard", ["id"] = id });
            if (ops.Count == 0) { last = trees; return; }

            var patch = new JObject { ["type"] = "ui.patch", ["proto"] = PanelWire.Proto, ["vocab"] = PanelWire.VocabHash, ["rev"] = rev + 1, ["base"] = rev, ["ops"] = ops };
            if (!Send(patch)) { resyncRequested = true; return; }   // a snapshot is the only honest recovery
            rev++;
            last = trees;
        }

        /// False when the relay would close the socket over the size; `rev` is only ever advanced for a
        /// message that went out.
        bool Send(JObject message)
        {
            string text = message.ToString(Formatting.None);
            if (text.Length <= MaxPayload) { socket.Send(text); return true; }
            dropped++;
            if (!warnedOversize)
            {
                warnedOversize = true;
                Debug.LogError($"Remote UI: a {(string)message["type"]} is {text.Length} bytes, over the relay's 1 MiB limit, and was not sent. " +
                               "Headsets will not update until the boards shrink; later drops are counted silently.");
            }
            return false;
        }

        /// A press from a headset: dedupe, resolve, gate, click, ack. It is acked whatever happened to it,
        /// so the headset stops resending; an ignored press is still a handled one.
        void Press(JObject message)
        {
            string client = (string)message["client"];
            var seqToken = message["seq"];
            if (string.IsNullOrEmpty(client) || seqToken == null || seqToken.Type != JTokenType.Integer) return;
            long seq = (long)seqToken;
            if (!Remember(client, seq)) { Ack(client, seq); return; }

            string boardId = (string)message["board"], path = (string)message["path"] ?? "", name = (string)message["name"];
            var board = RemoteBoard.Find(boardId, replica: false);
            var button = board?.Root == null ? null : Resolve(board.Root, path, name);
            if (button != null && gate.Accept(boardId + "/" + name)) PanelWire.Click(button);
            Ack(client, seq);
        }

        /// The Button a press names, or null: the path must land on an enabled Button with that name.
        public static Button Resolve(VisualElement root, string path, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            return PanelWire.Resolve(root, path) is Button button && button.name == name && button.enabledInHierarchy ? button : null;
        }

        bool Remember(string client, long seq)
        {
            if (!handled.TryGetValue(client, out var seen))
            {
                while (clients.Count >= RememberedClients)
                {
                    var oldest = clients.Dequeue();
                    handled.Remove(oldest);
                    order.Remove(oldest);
                }
                clients.Enqueue(client);
                seen = handled[client] = new HashSet<long>();
                order[client] = new Queue<long>();
            }
            if (!seen.Add(seq)) return false;
            var queue = order[client];
            queue.Enqueue(seq);
            while (queue.Count > RememberedPresses) seen.Remove(queue.Dequeue());
            return true;
        }

        void Ack(string client, long seq) => Send(new JObject { ["type"] = "ui.ack", ["client"] = client, ["seq"] = seq });
    }
}

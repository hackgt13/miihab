using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Kinesthetic.Golf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.UI.Remote
{
    /// The headset side. It asks for a snapshot, rebuilds each Replica board from it, keeps them current
    /// with patches, and sends what the local pick pressed back to the Mac. It runs no UI logic: a press
    /// changes nothing here until the Mac's next patch says so.
    ///
    /// The headset's scene follows the Mac's a second late, so a board usually appears here after the host
    /// reported it. The client remembers every id the host has live and, whenever a Replica with one of
    /// those ids is enabled and not yet built — or rebuilt its root — it asks for a snapshot. A resync is
    /// repeated every second until the snapshot arrives.
    ///
    /// Created on load on Android; `Ensure()` starts one anywhere else, for a Mac standing in as a headset.
    /// `Receive` and `Poll` are public so the verification can drive it with no socket at all.
    public sealed class RemoteUiClient : MonoBehaviour
    {
        const float ResendSeconds = .5f, GiveUpSeconds = 5f, ResyncRepeatSeconds = 1f, StaleSeconds = 2f;
        const int MaxPress = 1024;   // the relay's client→relay limit

        // What the relay accepts; anything else it drops without a word, so it is not worth retrying.
        static readonly Regex Token = new(@"^[\w.-]{1,80}$");
        static readonly Regex PathShape = new(@"^(\d{1,4}(/\d{1,4}){0,63})?$");

        public static RemoteUiClient Instance { get; private set; }

        /// What fills an image the wire could only name. Set by whoever rebuilds the golf minimap locally.
        public static IReplicaImageSource ImageSource { get => PanelWire.ImageSource; set => PanelWire.ImageSource = value; }

        sealed class Pending { public long seq; public string json; public float first, sent; }

        UiSocket socket;
        string url;
        string client;
        long rev = -1;
        long seq;
        bool refused;
        int generation;
        bool resyncWanted;
        float resyncSentAt = float.NegativeInfinity, disconnectedSince = -1;
        readonly List<Pending> outbox = new();
        readonly HashSet<string> warnedPresses = new();

        // The ids the host has live, from its snapshots and board/unboard ops.
        readonly HashSet<string> hostBoards = new();

        // The root each board was last built into: a UIDocument that is disabled and enabled again makes a
        // new root, and the mirrored tree with it is gone.
        readonly Dictionary<string, VisualElement> built = new();

        public bool Connected => socket != null && socket.connected;
        public long Rev => rev;
        public string ClientId => client;

        /// A resync is needed and not yet answered by a snapshot.
        public bool ResyncWanted => resyncWanted;

        /// How many times a resync became necessary, for the verification.
        public int ResyncRequests { get; private set; }

        public IReadOnlyCollection<string> HostBoards => hostBoards;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Application.platform != RuntimePlatform.Android) return;
            Ensure();
        }

        public static RemoteUiClient Ensure()
        {
            if (Instance) return Instance;
            var go = new GameObject("Remote UI client");
            DontDestroyOnLoad(go);
            return go.AddComponent<RemoteUiClient>();
        }

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            var config = Resources.Load<QuestHostConfig>(QuestHostConfig.ResourcePath);
            url = config
                ? $"ws://{config.host}:{config.port}/ui?role=client&token={Uri.EscapeDataString(config.token ?? "")}"
                : "ws://127.0.0.1:8767/ui?role=client";
            socket = new UiSocket(url);
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
                // A fresh connection: whatever was in flight is gone and the welcome will restart it. What was
                // drawn belongs to the connection that is gone, so every board shows its own tree again.
                generation = socket.generation;
                client = null;
                rev = -1;
                UnboardAll();
            }
            // The socket only counts connections, not drops: a headset that lost the relay would keep showing
            // the Mac's last tree as if it were live. Two seconds without a connection is a drop.
            if (socket.connected) disconnectedSince = -1;
            else if (disconnectedSince < 0) disconnectedSince = Time.unscaledTime;
            else if (Time.unscaledTime - disconnectedSince > StaleSeconds) UnboardAll();
            while (socket.TryReceive(out var text)) Receive(text);
            Poll();
        }

        /// One message from the relay.
        public void Receive(string text)
        {
            JObject message;
            try { message = JObject.Parse(text); } catch (JsonException) { return; }
            switch ((string)message["type"])
            {
                case "ui.welcome":
                    client = (string)message["client"];
                    rev = -1;
                    RequestResync();
                    break;
                case "ui.snapshot":
                    if (!Compatible(message)) { Refuse(); break; }
                    Snapshot(message);
                    break;
                case "ui.patch":
                    if (!Compatible(message)) { Refuse(); break; }
                    Patch(message);
                    break;
                case "ui.ack":
                    if (message["seq"] != null) { long acked = (long)message["seq"]; outbox.RemoveAll(p => p.seq == acked); }
                    break;
                case "ui.host-disconnected":
                    // The next host starts its own count and the relay makes it snapshot; nothing to ask for.
                    // What was mirrored is the last thing a Mac that has gone said, so every board goes back
                    // to its own "waiting" tree rather than standing there looking live.
                    rev = -1;
                    hostBoards.Clear();
                    resyncWanted = false;
                    UnboardAll();
                    break;
            }
        }

        /// Housekeeping between messages: presses to resend, replicas that appeared or rebuilt their root,
        /// and a resync to send or repeat.
        public void Poll()
        {
            Resend();
            CheckBoards();
            SendResync();
        }

        static bool Compatible(JObject message) =>
            message["proto"] != null && (int)message["proto"] == PanelWire.Proto && (string)message["vocab"] == PanelWire.VocabHash;

        void Snapshot(JObject message)
        {
            refused = false;
            resyncWanted = false;
            rev = (long)message["rev"];
            hostBoards.Clear();
            foreach (JObject entry in (JArray)message["boards"] ?? new JArray())
                Board((string)entry["id"], (JObject)entry["tree"]);
            // A snapshot is the whole truth: a replica the host no longer has is emptied.
            foreach (var board in RemoteBoard.Live)
                if (board.IsReplica && !string.IsNullOrEmpty(board.id) && !hostBoards.Contains(board.id)) Unboard(board.id);
        }

        void Patch(JObject message)
        {
            if ((long)message["base"] != rev) { RequestResync(); return; }
            try
            {
                foreach (JObject op in (JArray)message["ops"])
                {
                    switch ((string)op["op"])
                    {
                        case "board": Board((string)op["id"], (JObject)op["tree"]); break;
                        case "unboard": Unboard((string)op["id"]); break;
                        case "set":
                        case "replace":
                            var board = RemoteBoard.Find((string)op["board"], replica: true);
                            var root = board?.Root;
                            // Not built here yet, or built into a root that is gone: Poll asks for a snapshot.
                            if (root == null || !built.TryGetValue(board.id, out var target) || target != root) break;
                            PanelWire.Apply(root, new JArray { op }, board.id);
                            break;
                    }
                }
                rev = (long)message["rev"];
            }
            catch (Exception e)
            {
                Debug.LogWarning("Remote UI: patch did not apply, resyncing: " + e.Message);
                RequestResync();
            }
        }

        /// A board the host has live. Built now if its replica is here; remembered either way, so a replica
        /// that is enabled later is noticed by Poll.
        void Board(string id, JObject tree)
        {
            if (string.IsNullOrEmpty(id) || tree == null) return;
            hostBoards.Add(id);
            var root = RemoteBoard.Find(id, replica: true)?.Root;
            if (root == null) return;
            PanelWire.Rebuild(root, tree, id);
            built[id] = root;
        }

        void UnboardAll()
        {
            foreach (var id in new List<string>(built.Keys)) Unboard(id);
        }

        /// A board the host no longer has: the replica shows its own tree again (RemoteBoard.ShowOwnTree).
        void Unboard(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            hostBoards.Remove(id);
            RemoteBoard.Find(id, replica: true)?.ShowOwnTree();
            built.Remove(id);
        }

        /// A live replica the host has a board for, that is not drawn into its current root, needs a snapshot.
        void CheckBoards()
        {
            if (refused) return;   // asking again would only get the same incompatible answer
            foreach (var board in RemoteBoard.Live)
            {
                if (!board.IsReplica || string.IsNullOrEmpty(board.id) || !hostBoards.Contains(board.id)) continue;
                var root = board.Root;
                if (root == null) continue;
                if (!built.TryGetValue(board.id, out var target) || target != root) { RequestResync(); return; }
            }
        }

        void RequestResync()
        {
            if (resyncWanted) return;
            resyncWanted = true;
            ResyncRequests++;
            resyncSentAt = float.NegativeInfinity;
        }

        /// Repeated every second until a snapshot answers it, so a request lost on the way is not the end.
        void SendResync()
        {
            if (!resyncWanted || socket == null || !socket.connected || client == null) return;
            if (Time.unscaledTime - resyncSentAt < ResyncRepeatSeconds) return;
            resyncSentAt = Time.unscaledTime;
            socket.Send("{\"type\":\"ui.resync\"}");
        }

        /// The one message shown on a board that cannot be trusted to draw what the host means.
        void Refuse()
        {
            resyncWanted = false;
            if (refused) return;
            refused = true;
            Debug.LogError("Remote UI: the Mac speaks a different protocol or vocabulary; update the headset app.");
            foreach (var board in RemoteBoard.Live)
            {
                var root = board.IsReplica ? board.Root : null;
                if (root == null) continue;
                root.Clear();
                root.Add(new KText { text = "Update the headset app", size = KText.Size.Heading });
                built.Remove(board.id);
            }
        }

        /// Send a press for a Button the local pick resolved on a Replica board. Kept in the outbox and
        /// resent every half second until the Mac acks it, dropped after five.
        public void Press(RemoteBoard board, Button button)
        {
            var root = board?.Root;
            if (root == null || button == null || string.IsNullOrEmpty(button.name)) return;
            string path = PanelWire.PathOf(root, button);
            string key = board.id + "/" + button.name;
            if (path == null || !Token.IsMatch(board.id ?? "") || !Token.IsMatch(button.name) || !PathShape.IsMatch(path))
            {
                if (warnedPresses.Add(key))
                    Debug.LogWarning($"Remote UI: press '{key}' at '{path}' is not something the relay accepts; not sent.");
                return;
            }
            var message = new JObject { ["type"] = "ui.press", ["seq"] = ++seq, ["board"] = board.id, ["path"] = path, ["name"] = button.name };
            string json = message.ToString(Formatting.None);
            if (json.Length > MaxPress) { if (warnedPresses.Add(key)) Debug.LogWarning($"Remote UI: press '{key}' is over 1 KiB; not sent."); return; }
            outbox.Add(new Pending { seq = seq, json = json, first = Time.unscaledTime, sent = Time.unscaledTime });
            if (socket != null && socket.connected) socket.Send(json);
        }

        void Resend()
        {
            float now = Time.unscaledTime;
            for (int i = outbox.Count - 1; i >= 0; i--)
            {
                var pending = outbox[i];
                if (now - pending.first > GiveUpSeconds) { outbox.RemoveAt(i); continue; }
                if (now - pending.sent < ResendSeconds || socket == null || !socket.connected) continue;
                pending.sent = now;
                socket.Send(pending.json);
            }
        }
    }
}

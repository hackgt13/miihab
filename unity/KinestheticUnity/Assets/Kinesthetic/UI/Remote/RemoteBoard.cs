using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.UI.Remote
{
    /// Names a board for the wire. Sits next to a UIDocument; the Mac mirrors every Host board's tree and the
    /// headset rebuilds it into the Replica board with the same id. Quest scenes are generated from the Mac
    /// scenes, so the ids line up by construction.
    ///
    /// Runs in edit mode too, so `Live` is true there as well: the verification enables a board late and
    /// expects the client to notice it, with no play mode and no socket.
    [ExecuteAlways]
    public sealed class RemoteBoard : MonoBehaviour
    {
        public enum Role { Auto, Host, Replica }

        /// Stable across scenes and builds: `golf.hud`, `menu.home`.
        public string id;

        /// Auto is Replica on Android and Host everywhere else — the Mac is the authority, the headset renders.
        public Role role = Role.Auto;

        /// Every enabled board, whichever role. Cheaper and more honest than a scene search per tick.
        public static readonly List<RemoteBoard> Live = new();

        UIDocument document;

        public bool IsReplica => role == Role.Replica || (role == Role.Auto && Application.platform == RuntimePlatform.Android);
        public bool IsHost => !IsReplica;

        public VisualElement Root
        {
            get
            {
                if (!document) document = GetComponent<UIDocument>();
                return document && document.enabled ? document.rootVisualElement : null;
            }
        }

        void OnEnable() { if (!Live.Contains(this)) Live.Add(this); }
        void OnDisable() { Live.Remove(this); }

        public static RemoteBoard Find(string id, bool replica)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var board in Live)
                if (board.id == id && board.IsReplica == replica) return board;
            return null;
        }

        /// The seam the local inputs call before committing a press. On a Replica board there is no handler
        /// worth running here — the Mac holds the logic — so the press crosses the wire instead and the
        /// local Committed event stays silent. On every other board this is false and nothing changes.
        public static bool Intercepts(GameObject owner, Button button)
        {
            if (!owner || button == null) return false;
            var board = owner.GetComponent<RemoteBoard>();
            if (board == null || !board.IsReplica) return false;
            var client = RemoteUiClient.Instance;
            if (client != null) client.Press(board, button);
            return true;
        }
    }
}

using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Kinesthetic.UI.Remote
{
    /// The one thing the /ui channel carries that is not a board tree: the Mac telling every headset that
    /// something happened in the world rather than on a panel. Two kinds so far — `scene`, the Mac is leaving
    /// one scene for another through a venue's doorway, and `face`, the menu ring turned to a slot. A headset
    /// acts on a cue the way it acts on a press: the Mac decided, this only says so.
    ///
    /// Kept apart from the host and the client so neither needs to know what a cue means. The host installs
    /// `Sender` when it has a socket; the client hands every `ui.cue` to `Deliver`. Anywhere with neither, a
    /// cue is silently not sent and nothing is received, which is the Mac playing alone.
    public static class UiCue
    {
        public const string Type = "ui.cue";
        public const string Scene = "scene", Face = "face";
        public const string Leave = "leave", Arrive = "arrive";

        /// Every cue the relay delivered, in order. Subscribers unsubscribe in OnDisable: this is static and
        /// outlives a scene.
        public static event Action<JObject> Received;

        /// Installed by the Mac's RemoteUiHost. Null where there is no host, and a cue then goes nowhere.
        public static Func<JObject, bool> Sender;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Received = null; Sender = null; }

        public static bool Send(string kind, JObject fields)
        {
            if (string.IsNullOrEmpty(kind) || Sender == null) return false;
            var message = new JObject { ["type"] = Type, ["kind"] = kind };
            if (fields != null) foreach (var pair in fields) message[pair.Key] = pair.Value;
            return Sender(message);
        }

        /// The Mac is leaving `scene`'s predecessor for `scene`, through the doorway of `venue` (null for the
        /// menu, which has no doorway), and says so twice: `leave` as the move starts, `arrive` once the
        /// scene is up. A headset that hears `leave` walks through its own copy of the door; one that only
        /// hears `arrive` — it was reconnecting — simply fades across.
        public static bool SendScene(string scene, string venue, string phase) =>
            Send(Scene, new JObject { ["scene"] = scene, ["venue"] = venue, ["phase"] = phase });

        /// The menu ring turned to face `slot`.
        public static bool SendFace(string slot) => Send(Face, new JObject { ["slot"] = slot });

        public static void Deliver(JObject message)
        {
            if (message == null || (string)message["type"] != Type) return;
            Received?.Invoke(message);
        }
    }
}

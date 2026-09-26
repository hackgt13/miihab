using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Kinesthetic.Menu
{
    /// What the friends pane and the group session share: the coordinator's address, the two request
    /// shapes they make, and the vocabulary a message is written in.
    ///
    /// The coordinator is on this Mac (coordinator/server.ts, 8766). On a headset 127.0.0.1 would be the
    /// headset itself, so these surfaces are Mac-side, like the rest of the menu's data.
    public static class SocialBridge
    {
        public const string Url = "http://127.0.0.1:8766";

        /// The fixed encouragements (coordinator/messages.ts ENCOURAGEMENTS), in the order a composer
        /// offers them. A stored message carries only the kind; this turns it back into words.
        public static readonly (string kind, string label)[] Encouragements =
        {
            ("nice_one", "Nice one"),
            ("welcome_back", "Welcome back"),
            ("that_looked_hard", "That looked hard"),
            ("with_you", "With you"),
            ("strong_finish", "Strong finish"),
        };

        public static string LabelFor(string kind)
        {
            foreach (var (k, label) in Encouragements) if (k == kind) return label;
            return kind;
        }

        public static string ShortTime(string iso)
            => DateTime.TryParse(iso, out var when) ? when.ToLocalTime().ToString("HH:mm") : "";

        /// For hand-built JSON bodies: JsonUtility cannot write the partial objects these routes take.
        public static string Escape(string value)
            => (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");

        public static IEnumerator Get(string path, Action<string> done, Action failed = null, int timeout = 5)
        {
            using var request = UnityWebRequest.Get(Url + path);
            request.timeout = timeout;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success) done?.Invoke(request.downloadHandler.text);
            else failed?.Invoke();
        }

        /// `failed` gets the coordinator's own error line when it sent one ("That group is full"), else null.
        public static IEnumerator Post(string path, string body, Action<string> done = null, Action<string> failed = null, int timeout = 5)
        {
            using var request = new UnityWebRequest(Url + path, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body ?? "{}"));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = timeout;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success) { done?.Invoke(request.downloadHandler.text); yield break; }
            string reason = null;
            try { reason = JsonUtility.FromJson<Error>(request.downloadHandler?.text ?? "")?.error; } catch { }
            failed?.Invoke(string.IsNullOrEmpty(reason) ? null : reason);
        }

#pragma warning disable 0649
        [Serializable] class Error { public string error; }
#pragma warning restore 0649
    }
}

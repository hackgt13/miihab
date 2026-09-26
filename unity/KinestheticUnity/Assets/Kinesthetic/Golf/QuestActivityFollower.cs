using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kinesthetic.Golf
{
    // Headset only: one Quest app holds every activity and follows the Mac. Whichever the Mac is hosting
    // (golf.state, bowling.state or rehab.state) is the scene the headset shows; when the Mac switches, the
    // headset switches. With no host live, the headset stays where it is.
    public sealed class QuestActivityFollower : MonoBehaviour
    {
        public const string GolfScene = "QuestGolf", BowlingScene = "QuestBowling", RehabScene = "QuestRehab";
        // Channel, the state type its host publishes, and the headset scene that renders it.
        static readonly (string path, string type, string scene)[] Activities =
            { ("/state", "golf.state", GolfScene), ("/bowling-state", "bowling.state", BowlingScene), ("/rehab-state", "rehab.state", RehabScene) };
        LatestSocket[] sockets;
        float[] liveAt;
        AsyncOperation loading;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.platform != RuntimePlatform.Android || FindAnyObjectByType<QuestActivityFollower>()) return;
            var go = new GameObject("Quest activity follower");
            DontDestroyOnLoad(go);
            go.AddComponent<QuestActivityFollower>();
        }

        void Start()
        {
            var config = Resources.Load<QuestHostConfig>(QuestHostConfig.ResourcePath);
            if (!config || string.IsNullOrEmpty(config.token)) { enabled = false; return; }
            sockets = Array.ConvertAll(Activities, a => new LatestSocket(config.Url(a.path + "?role=client")));
            liveAt = new float[Activities.Length];
            for (int i = 0; i < liveAt.Length; i++) liveAt[i] = -99;
        }

        void Update()
        {
            float now = Time.unscaledTime;
            // Whichever host published most recently is what the Mac is running now.
            int newest = -1;
            for (int i = 0; i < Activities.Length; i++)
            {
                if (sockets[i].Take(out var text) && text.Contains($"\"{Activities[i].type}\"")) liveAt[i] = now;
                if (now - liveAt[i] < 2 && (newest < 0 || liveAt[i] > liveAt[newest])) newest = i;
            }
            if (newest < 0 || loading is { isDone: false } || SceneManager.GetActiveScene().name == Activities[newest].scene) return;
            loading = SceneManager.LoadSceneAsync(Activities[newest].scene);
        }

        void OnDestroy() { if (sockets != null) foreach (var s in sockets) s?.Dispose(); }
    }
}

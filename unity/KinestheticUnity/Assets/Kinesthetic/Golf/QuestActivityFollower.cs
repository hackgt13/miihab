using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kinesthetic.Golf
{
    // Headset only: one Quest app holds both games and follows the Mac. Whichever game the Mac is hosting
    // (golf publishes golf.state, bowling publishes bowling.state) is the scene the headset shows; when the
    // Mac switches, the headset switches. With no host live, the headset stays where it is.
    public sealed class QuestActivityFollower : MonoBehaviour
    {
        public const string GolfScene = "QuestGolf", BowlingScene = "QuestBowling";
        LatestSocket golf, bowling;
        float golfAt = -99, bowlingAt = -99;
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
            string Url(string path) => $"ws://{config.host}:{config.port}{path}?role=client&token={Uri.EscapeDataString(config.token)}";
            golf = new LatestSocket(Url("/state"));
            bowling = new LatestSocket(Url("/bowling-state"));
        }

        void Update()
        {
            float now = Time.unscaledTime;
            if (golf.Take(out var g) && g.Contains("\"golf.state\"")) golfAt = now;
            if (bowling.Take(out var b) && b.Contains("\"bowling.state\"")) bowlingAt = now;
            bool golfLive = now - golfAt < 2, bowlingLive = now - bowlingAt < 2;
            string want = bowlingLive && (!golfLive || bowlingAt > golfAt) ? BowlingScene : golfLive ? GolfScene : null;
            if (want == null || loading is { isDone: false } || SceneManager.GetActiveScene().name == want) return;
            loading = SceneManager.LoadSceneAsync(want);
        }

        void OnDestroy() { golf?.Dispose(); bowling?.Dispose(); }
    }
}

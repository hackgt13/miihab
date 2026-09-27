using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kinesthetic.Golf;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kinesthetic.Tutorial
{
    /// Mac host: publishes the intro's coach as it is being rendered — where Alex sits, every bone, and whether
    /// a demonstration is running — about 30 times a second on the relay's /tutorial-state channel, so a headset
    /// shows the same Alex doing the same arm lift (TutorialStateClient). The sequencer, the sensors and the voice
    /// stay on the Mac; this reads what it draws and decides nothing.
    ///
    /// Attaches itself whenever the Tutorial scene loads on a Mac, the way RemoteUiHost does, so the generated
    /// scene never has to carry it.
    public sealed class TutorialStatePublisher : MonoBehaviour
    {
        public const string MacScene = "Tutorial";
        public string url = "ws://127.0.0.1:8767/tutorial-state?role=host";
        LatestSocket socket; List<Transform> bones; Kinesthetic.Coach.CoachDemonstrator coach;
        long seq; float next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.platform == RuntimePlatform.Android) return;
            SceneManager.sceneLoaded += (scene, _) => { if (scene.name == MacScene) Ensure(); };
            if (SceneManager.GetActiveScene().name == MacScene) Ensure();
        }

        public static TutorialStatePublisher Ensure()
        {
            var existing = FindAnyObjectByType<TutorialStatePublisher>();
            if (existing) return existing;
            return new GameObject("Tutorial state publisher").AddComponent<TutorialStatePublisher>();
        }

        void Start() { socket = new LatestSocket(url); }

        void LateUpdate()
        {
            if (socket == null || Time.unscaledTime < next || !socket.connected) return;
            next = Time.unscaledTime + 1f / 30;
            // The sequencer spawns Alex in its own Start; take the coach whenever it appears.
            if (!coach || bones == null)
            {
                coach = FindAnyObjectByType<Kinesthetic.Coach.CoachDemonstrator>();
                if (!coach || !coach.model) return;
                bones = coach.model.GetComponentsInChildren<Transform>(true).ToList();
            }
            var b = new StringBuilder(8192);
            b.Append("{\"type\":\"tutorial.state\",\"seq\":").Append(++seq)
             .Append(",\"demonstrating\":").Append(coach.demonstrating ? "true" : "false")
             .Append(",\"coach\":{\"p\":"); GolfStateFormat.Vec(b, coach.transform.position);
            b.Append(",\"r\":"); GolfStateFormat.Quat(b, coach.transform.rotation);
            b.Append(",\"pose\":{\"n\":").Append(bones.Count).Append(",\"lp\":[");
            for (int k = 0; k < bones.Count; k++) { if (k > 0) b.Append(','); GolfStateFormat.Vec(b, bones[k].localPosition); }
            b.Append("],\"lr\":[");
            for (int k = 0; k < bones.Count; k++) { if (k > 0) b.Append(','); GolfStateFormat.Quat(b, bones[k].localRotation); }
            b.Append("]}}}");
            socket.Send(b.ToString());
        }

        void OnDestroy() => socket?.Dispose();
    }
}

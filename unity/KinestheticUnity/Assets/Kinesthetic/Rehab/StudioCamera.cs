using UnityEngine;
using UnityEngine.InputSystem;

namespace Kinesthetic.Rehab
{
    /// The studio's camera, like golf and bowling: third person from behind the patient's chair to set the
    /// scene, then a glide into their own eyes when the session starts — mirror ahead-left, coach ahead-right,
    /// your own arm in front of you. Back to third person when the set ends. C toggles by hand.
    ///
    /// It drives the scene's own view camera, so the teammate's V toggle (ViewDirector) keeps working.
    public sealed class StudioCamera : MonoBehaviour
    {
        public RehabSession session;
        public float glideSeconds = 2.2f;
        Camera view; Transform seat, head; PoseRig rig;
        float blend; bool manual, manualFirstPerson;

        void Start()
        {
            rig = session.rig; rig.Initialize();
            seat = rig.transform.parent ? rig.transform.parent : rig.transform;
            head = System.Array.Find(rig.avatar.GetComponentsInChildren<Transform>(true), t => t.name == "head");
            var named = GameObject.Find("Patient view camera");
            view = named ? named.GetComponent<Camera>() : Camera.main;
        }

        void LateUpdate()
        {
            if (!view || !view.enabled) return;   // another view is live (V toggle)
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.cKey.wasPressedThisFrame) { manual = true; manualFirstPerson = blend < .5f; }
            bool firstPerson = manual ? manualFirstPerson : session.IsRunning;
            if (manual && !session.IsRunning && !manualFirstPerson && blend <= 0) manual = false;   // hand back once home
            blend = Mathf.MoveTowards(blend, firstPerson ? 1 : 0, Time.unscaledDeltaTime / glideSeconds);
            float k = Mathf.SmoothStep(0, 1, blend);

            // The Mii faces its local -Z; its anatomical right is local -X.
            var forward = Vector3.ProjectOnPlane(rig.transform.TransformDirection(Vector3.back), Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, forward);
            var chair = seat.position;
            var thirdPosition = chair - forward * 3.1f + Vector3.up * 2.05f + right * .45f;
            var thirdRotation = Quaternion.LookRotation(chair + forward * 1.4f + Vector3.up * .85f - thirdPosition);
            var eye = (head ? head.position : chair + Vector3.up * 1.15f) + forward * .07f + Vector3.up * .03f;
            var firstRotation = Quaternion.LookRotation(Quaternion.AngleAxis(9, right) * forward);   // a little down, toward your own arm

            view.transform.SetPositionAndRotation(Vector3.Lerp(thirdPosition, eye, k), Quaternion.Slerp(thirdRotation, firstRotation, k));
            view.fieldOfView = Mathf.Lerp(42, 74, k);
            view.nearClipPlane = Mathf.Lerp(.1f, .03f, k);
            // Inside your own head, hide it (FirstPersonView already put it on its own layer).
            const int ownBody = FirstPersonView.OwnBodyLayer;
            view.cullingMask = k > .8f ? view.cullingMask & ~(1 << ownBody) : view.cullingMask | (1 << ownBody);
        }
    }
}

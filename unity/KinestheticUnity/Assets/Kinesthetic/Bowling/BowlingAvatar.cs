using System;
using System.Linq;
using UnityEngine;

namespace Kinesthetic.Bowling
{
    // Authored animation primitives, driven by the accepted wrist swing. This is
    // presentation, not an inferred body pose. Quest receives the resolved bones.
    [DefaultExecutionOrder(200)]
    public sealed class BowlingAvatar : MonoBehaviour
    {
        public BowlingGame game;
        public Transform model;
        public Transform[] joints;
        [SerializeField] Quaternion[] bindRotations;
        [SerializeField] Vector3[] bindPositions;
        static readonly string[] Names = { "hip", "spine.001", "neck", "head", "bicep.R", "forearm.R", "hand.R",
            "bicep.L", "forearm.L", "hand.L", "thigh.R", "calf.R", "foot.R", "thigh.L", "calf.L", "foot.L" };
        float releasedAt = -100, displayedSwing;
        public Vector3 BallGrip { get; private set; }
        Transform Bone(string name) => joints[Array.IndexOf(Names, name)];

        public void Initialize()
        {
            if (joints == null || joints.Length != Names.Length)
            {
                var all = model.GetComponentsInChildren<Transform>(true);
                joints = Names.Select(name => all.First(t => t.name == name)).ToArray();
                bindRotations = joints.Select(t => t.localRotation).ToArray();
                bindPositions = joints.Select(t => t.localPosition).ToArray();
            }
        }
        void Awake() => Initialize();
        static void Aim(Transform joint, Transform child, Vector3 direction) =>
            joint.rotation = Quaternion.FromToRotation(child.position - joint.position, direction) * joint.rotation;

        public void DrawPose(float swingDegrees, float follow, float heading, bool holding)
        {
            Initialize();
            for (int i = 0; i < joints.Length; i++) { joints[i].localRotation = bindRotations[i]; joints[i].localPosition = bindPositions[i]; }
            var forward = Quaternion.Euler(0, heading, 0) * transform.forward;
            var right = Vector3.Cross(Vector3.up, forward);
            var spine = Bone("spine.001");
            spine.rotation = Quaternion.AngleAxis(7 + follow * 8, right) * spine.rotation;
            float angle = swingDegrees * Mathf.Deg2Rad;
            var arm = -Vector3.up * Mathf.Cos(angle) + forward * Mathf.Sin(angle) + right * .09f;
            Aim(Bone("bicep.R"), Bone("forearm.R"), arm);
            Aim(Bone("forearm.R"), Bone("hand.R"), arm + forward * .08f);
            // The free arm counterbalances the throw, with a soft elbow.
            Aim(Bone("bicep.L"), Bone("forearm.L"), -Vector3.up - right * (.18f + follow * .45f));
            Aim(Bone("forearm.L"), Bone("hand.L"), -Vector3.up + forward * .3f - right * .2f);
            var hand = Bone("hand.R");
            BallGrip = hand.position + arm.normalized * .08f + forward * .055f - Vector3.up * .08f;
            if (holding && game && game.ball && game.ball.isKinematic)
            {
                game.ball.position = BallGrip;
                game.ball.transform.SetPositionAndRotation(BallGrip, Quaternion.identity);
            }
        }
        public void Prepare()
        {
            releasedAt = -100; displayedSwing = 0;
            DrawPose(0, 0, 0, true);
        }
        public void Release(float heading)
        {
            // Match the held ball to the release pose before enabling physics.
            displayedSwing = 0; releasedAt = Time.time;
            DrawPose(0, 0, heading, true);
        }
        void LateUpdate()
        {
            if (!game || game.renderOnly || game.Paused) return;
            bool holding = game.Phase == "Setup" || game.Phase == "Ready";
            float elapsed = Time.time - releasedAt;
            float follow = holding ? 0 : elapsed < .3f ? Mathf.SmoothStep(0, 1, elapsed / .3f) : 1 - Mathf.SmoothStep(0, 1, (elapsed - .55f) / .9f);
            float target = holding ? -Mathf.Min(65, game.Swing.SwingAngle) : follow * 68;
            displayedSwing = Mathf.Lerp(displayedSwing, target, 1 - Mathf.Exp(-20 * Time.deltaTime));
            DrawPose(displayedSwing, follow, holding ? game.Swing.Aim : game.LastAim, holding);
        }
    }
}

using UnityEngine;
using Kinesthetic.Activities;
using Kinesthetic.Menu;
using Kinesthetic.UI;

namespace Kinesthetic.Rehab
{
    /// <summary>
    /// The other person in a group session, sitting where the mirror window stands when you are on your own:
    /// ahead and to your left, turned to face you, doing the same movement. RehabSession adds this instead of the
    /// mirror when the patient is in a group (GroupPanel), so the generated scene needs no change.
    ///
    /// They are a copy of the patient's own seated Mii and wheelchair, recoloured with their Mii's skin, hair and
    /// shirt (KMiiFace), so the person in the room matches the face in the member list. Their arm follows
    /// PeerMotion: the second Mac's AirPods when they are streaming, otherwise — for a sample person only — a
    /// steady fake raise. A real person with no stream sits at rest; nobody is ever animated as if they were live.
    ///
    /// Presentation only. Nothing here is measured, scored or sent: the partner's angle never leaves this Mac.
    /// </summary>
    public sealed class PeerAvatar : MonoBehaviour
    {
        public IRehabView view;
        [Tooltip("Metres from the patient's hips, to their left and forward: where the mirror window stands.")]
        public float offsetLeft = 1.6f, offsetForward = 2.2f;
        [Tooltip("Degrees turned away from facing the patient, so a forward raise is seen from the side rather than end-on. Positive shows the measured arm's side.")]
        public float turnDeg = 55;

        readonly PeerMotion motion = new();
        Transform source, copy;
        PoseRig rig;
        TextMesh nameLabel, stateLabel;
        Transform label;
        int tinted = int.MinValue;
        float shown;

        void Start()
        {
            var patient = view?.Rig;
            if (!patient) { enabled = false; return; }
            patient.Initialize();
            source = patient.transform.parent ? patient.transform.parent : patient.transform;   // the patient and their wheelchair
            Build(patient);
        }

        void Build(PoseRig patient)
        {
            // Copied inside an inactive holder so nothing in it wakes before it is stripped: only the rig that poses
            // it and the idle life that makes it blink stay. Everything else (the patient's pose client, the session,
            // colliders) belongs to the patient.
            var holder = new GameObject("Partner holder"); holder.SetActive(false);
            copy = Instantiate(source.gameObject, holder.transform).transform;
            copy.name = "Group partner";
            foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour is not PoseRig && behaviour is not Kinesthetic.Golf.MiiIdleLife) DestroyImmediate(behaviour);
            foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(collider);
            foreach (var camera in copy.GetComponentsInChildren<Camera>(true)) DestroyImmediate(camera);
            foreach (var listener in copy.GetComponentsInChildren<AudioListener>(true)) DestroyImmediate(listener);
            // A headset hides the patient's own head from its camera by layer; the partner's head must show.
            foreach (var r in copy.GetComponentsInChildren<Renderer>(true)) { r.gameObject.layer = 0; if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = true; }
            rig = copy.GetComponentInChildren<PoseRig>(true);

            // Where the mirror stands: to the patient's left and forward, on their floor, turned to face them.
            var hip = patient.Hip.position;
            var left = patient.transform.TransformDirection(Vector3.right);      // the Mii's anatomical left is local +X
            var forward = patient.transform.TransformDirection(Vector3.back);    // it faces local -Z
            copy.SetParent(null, true);
            copy.SetPositionAndRotation(source.position + left * offsetLeft + forward * offsetForward, source.rotation);
            Destroy(holder);
            rig.Initialize();   // its bone table is filled here, and the copy was made asleep
            var facing = Flat(rig.transform.TransformDirection(Vector3.back));
            var toPatient = Flat(hip - rig.Hip.position);
            if (facing.sqrMagnitude > 1e-4f && toPatient.sqrMagnitude > 1e-4f)
                copy.rotation = Quaternion.FromToRotation(facing, toPatient) * copy.rotation;
            // Face-on, a forward raise points straight at the patient and cannot be seen. Three-quarters, with the
            // measured arm's side toward them, it reads in profile while they still half face each other.
            float turn = view.Side == "left" ? -turnDeg : turnDeg;
            copy.RotateAround(rig.Hip.position, Vector3.up, turn);

            // Their name, over their head, turned to whoever is looking.
            label = new GameObject("Partner label").transform;
            label.SetParent(copy, false);
            nameLabel = Text("Name", 80, .014f, Palette.Prussian50, .06f);
            stateLabel = Text("State", 56, .009f, Palette.Slate70, 0);
            copy.gameObject.SetActive(false);
        }

        // Stands on its own at the scene root, so it leaves with this component (the group was left).
        void OnDestroy() { if (copy) Destroy(copy.gameObject); }

        TextMesh Text(string name, int size, float scale, Color color, float y)
        {
            var text = new GameObject(name).AddComponent<TextMesh>();
            text.transform.SetParent(label, false);
            text.transform.localPosition = new Vector3(0, y, 0);
            text.transform.localScale = Vector3.one * scale;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            text.fontSize = size; text.anchor = TextAnchor.LowerCenter; text.alignment = TextAlignment.Center;
            text.color = color;
            return text;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        /// The movement's body model, or a straight-arm raise when the catalog has none for this kind (the plan's
        /// single-AirPod shoulder raise, arm-elevation.v1, has no movement tile of its own): a partner who cannot
        /// be drawn doing the movement should at least be drawn raising an arm, not sitting frozen.
        BodyModel Body => ActivityCatalog.MovementFor(view.ExerciseKind)?.Body
            ?? ActivityCatalog.ById("movement.arm-raise")?.Body;

        // What is being shown: worked out here on the Mac, handed over by RehabStateClient on a headset.
        public bool Remote { get; private set; }
        public bool Showing { get; private set; }
        public string ShownName { get; private set; } = "";
        public string ShownState { get; private set; } = "";
        public int ShownMii { get; private set; }
        /// The partner's rig once it is built, for publishing its bones (Mac) or posing it from them (headset).
        public PoseRig Rig => copy ? rig : null;

        /// Headset: show this person, whose bones the caller poses. Nothing is read or computed here.
        public void Present(bool showing, string name, string state, int mii)
        {
            Remote = true; Showing = showing; ShownName = name ?? ""; ShownState = state ?? ""; ShownMii = mii;
        }

        void LateUpdate()
        {
            if (!copy || view == null) return;
            if (!Remote) Decide();
            if (copy.gameObject.activeSelf != Showing) copy.gameObject.SetActive(Showing);
            if (!Showing) return;
            if (ShownMii != tinted) { Tint(ShownMii); tinted = ShownMii; }
            nameLabel.text = ShownName;
            stateLabel.text = ShownState;
            label.position = rig.Hip.position + Vector3.up * 1.32f;
            var cam = Camera.main;
            if (cam) label.rotation = Quaternion.LookRotation(Flat(label.position - cam.transform.position).normalized, Vector3.up);
        }

        /// Mac: who is there, how they are moving, and the pose that follows from it.
        void Decide()
        {
            motion.Read();
            var partner = GroupPanel.Instance?.Partner;
            // The second Mac streaming with nobody else in the room is still someone: a guest with no name yet.
            Showing = partner != null || motion.Live;
            if (!Showing) return;
            ShownMii = partner?.Mii ?? 1;
            ShownName = partner?.Name ?? "Guest";
            ShownState = motion.Calibrating ? "Hold still a moment…"
                : motion.Live ? "LIVE"
                : partner?.Sample == true ? "SAMPLE" : "Not streaming";

            // Live beats fake; a fake is only ever a sample person's.
            float? target = motion.LiveAngle;
            if (target == null && !motion.Live && partner?.Sample == true)
                target = PeerMotion.FakeAngle(view.TargetDeg, ShownMii, Time.time);
            shown = Mathf.Lerp(shown, target ?? 0, 1 - Mathf.Exp(-12 * Time.deltaTime));
            rig.Apply(null);
            rig.ApplyMovement(Body, view.Side == "left", shown);
        }

        /// Their skin, hair and shirt, from the same palette their face in the member list is drawn with.
        void Tint(int mii)
        {
            foreach (var r in copy.GetComponentsInChildren<Renderer>(true))
            {
                var materials = r.materials;   // instances: the patient's own materials are shared and stay theirs
                foreach (var m in materials)
                {
                    if (!m) continue;
                    string n = m.name.ToLowerInvariant();
                    Color? c = n.Contains("mii shirt") ? KMiiFace.ShirtOf(mii)
                        : n.Contains("mii hair") ? KMiiFace.HairOf(mii)
                        : n.Contains("mii skin") ? KMiiFace.SkinOf(mii) : null;
                    if (c is Color colour) { if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour); m.color = colour; }
                }
                r.materials = materials;
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using Kinesthetic.Activities;
using Kinesthetic.Menu;
using Kinesthetic.UI;

namespace Kinesthetic.Rehab
{
    /// <summary>
    /// The rest of a group session, seated around the patient: everyone on the Tab list, on an arc to the patient's
    /// left that starts where the mirror window stands when they are on their own, each turned toward them and doing
    /// the same movement. RehabSession adds this instead of the mirror when the patient is in a group (GroupPanel),
    /// so the generated scene needs no change.
    ///
    /// Each person is a copy of the patient's own seated Mii and wheelchair, recoloured with their Mii's skin, hair
    /// and shirt (KMiiFace), so the person in the room matches the face in the member list. How they move:
    ///   · the second Mac's AirPods (PeerMotion) drive one of them — the first real member, or the first person
    ///     when everyone is a sample — and are shown LIVE;
    ///   · every other sample person does a steady fake raise, and says SAMPLE;
    ///   · a real person with no stream sits at rest. Nobody is ever animated as if they were live.
    ///
    /// Presentation only. Nothing here is measured, scored or sent anywhere but this patient's own headset.
    /// </summary>
    public sealed class PeerAvatar : MonoBehaviour
    {
        public IRehabView view;
        [Tooltip("How far the seats are from the patient's hips, in metres. The first stands where the mirror does.")]
        public float radius = 2.72f;
        [Tooltip("Where the arc starts and how far apart the seats are, in degrees to the patient's left of straight ahead.")]
        public float firstSeatDeg = 36, seatSpacingDeg = 24;
        [Tooltip("Degrees each person is turned away from facing the patient. 0 faces them; ~55 shows a forward raise in profile instead of end-on.")]
        public float turnDeg = 0;

        /// One person in one seat. Seats are kept and reused as people come and go; an empty one is hidden.
        public sealed class Seat
        {
            public bool Showing;
            public string Name = "", State = "";
            public int Mii;
            internal Transform copy, label;
            internal PoseRig rig;
            internal TextMesh nameLabel, stateLabel;
            internal int tinted = int.MinValue;
            internal float shown;
            /// Their rig, for publishing its bones (Mac) or posing it from them (headset).
            public PoseRig Rig => copy ? rig : null;
        }

        readonly PeerMotion motion = new();
        readonly List<Seat> seats = new();
        Transform source;
        PoseRig patient;

        public IReadOnlyList<Seat> Seats => seats;
        /// True on a headset: who is shown is handed over (Present), and their bones are posed by the caller.
        public bool Remote { get; private set; }

        void Start()
        {
            patient = view?.Rig;
            if (!patient) { enabled = false; return; }
            patient.Initialize();
            source = patient.transform.parent ? patient.transform.parent : patient.transform;   // the patient and their wheelchair
        }

        // Each seat stands on its own at the scene root, so they leave with this component (the group was left).
        void OnDestroy() { foreach (var seat in seats) if (seat.copy) Destroy(seat.copy.gameObject); }

        /// Headset: show these people, in these seats, whose bones the caller poses. Nothing is read or computed.
        public void Present(IReadOnlyList<(string name, string state, int mii)> people)
        {
            Remote = true;
            for (int i = 0; i < people.Count; i++)
            {
                var seat = SeatAt(i);
                if (seat == null) return;
                (seat.Name, seat.State, seat.Mii) = (people[i].name ?? "", people[i].state ?? "", people[i].mii);
                seat.Showing = true;
            }
            for (int i = people.Count; i < seats.Count; i++) seats[i].Showing = false;
        }

        void LateUpdate()
        {
            if (!source || view == null) return;
            if (!Remote) Decide();
            var cam = Camera.main;
            foreach (var seat in seats)
            {
                if (!seat.copy) continue;
                if (seat.copy.gameObject.activeSelf != seat.Showing) seat.copy.gameObject.SetActive(seat.Showing);
                if (!seat.Showing) continue;
                if (seat.Mii != seat.tinted) { Tint(seat.copy, seat.Mii); seat.tinted = seat.Mii; }
                seat.nameLabel.text = seat.Name;
                seat.stateLabel.text = seat.State;
                seat.label.position = seat.rig.Hip.position + Vector3.up * 1.32f;
                if (cam) seat.label.rotation = Quaternion.LookRotation(Flat(seat.label.position - cam.transform.position).normalized, Vector3.up);
            }
        }

        /// Mac: who is there, who the second Mac's AirPods are, and the pose that follows for each.
        void Decide()
        {
            motion.Read();
            var others = GroupPanel.Instance?.Others ?? new List<GroupPanel.Person>();
            // The second Mac streaming with nobody else in the room is still someone: a guest with no name yet.
            if (others.Count == 0 && motion.Live) others.Add(new GroupPanel.Person("guest", "Guest", 1, false));
            // One stream, one person: the first real member, since the second Mac is a real person; with only samples
            // in the room, the first of them, so a demo partner can be driven live too.
            int streamed = others.FindIndex(p => !p.Sample);
            if (streamed < 0) streamed = 0;

            var body = Body;
            for (int i = 0; i < others.Count; i++)
            {
                var seat = SeatAt(i);
                if (seat == null) break;
                var person = others[i];
                (seat.Name, seat.Mii, seat.Showing) = (person.Name, person.Mii, true);
                bool live = i == streamed && motion.Live;
                seat.State = live ? (motion.Calibrating ? "Hold still a moment…" : "LIVE")
                    : person.Sample ? "SAMPLE" : "Not streaming";
                float? target = live ? motion.LiveAngle
                    : person.Sample ? PeerMotion.FakeAngle(view.TargetDeg, person.Mii + i, Time.time) : null;
                seat.shown = Mathf.Lerp(seat.shown, target ?? 0, 1 - Mathf.Exp(-12 * Time.deltaTime));
                seat.rig.Apply(null);
                seat.rig.ApplyMovement(body, view.Side == "left", seat.shown);
            }
            for (int i = others.Count; i < seats.Count; i++) seats[i].Showing = false;
        }

        /// The seat at `index`, built on first use. Null past the room's capacity.
        Seat SeatAt(int index)
        {
            if (index >= 5 || !source) return null;
            while (seats.Count <= index) seats.Add(Build(seats.Count));
            return seats[index];
        }

        Seat Build(int index)
        {
            // Copied inside an inactive holder so nothing in it wakes before it is stripped: only the rig that poses
            // it and the idle life that makes it blink stay. Everything else (the patient's pose client, the session,
            // colliders) belongs to the patient.
            var holder = new GameObject("Group seat holder"); holder.SetActive(false);
            var copy = Instantiate(source.gameObject, holder.transform).transform;
            copy.name = $"Group member {index + 1}";
            foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour is not PoseRig && behaviour is not Kinesthetic.Golf.MiiIdleLife) DestroyImmediate(behaviour);
            foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(collider);
            foreach (var camera in copy.GetComponentsInChildren<Camera>(true)) DestroyImmediate(camera);
            foreach (var listener in copy.GetComponentsInChildren<AudioListener>(true)) DestroyImmediate(listener);
            // A headset hides the patient's own head from its camera by layer; everyone else's head must show.
            foreach (var r in copy.GetComponentsInChildren<Renderer>(true)) { r.gameObject.layer = 0; if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = true; }
            var rig = copy.GetComponentInChildren<PoseRig>(true);

            // On the arc to the patient's left, on their floor.
            var hip = patient.Hip.position;
            var left = patient.transform.TransformDirection(Vector3.right);      // the Mii's anatomical left is local +X
            var forward = patient.transform.TransformDirection(Vector3.back);    // it faces local -Z
            float a = (firstSeatDeg + seatSpacingDeg * index) * Mathf.Deg2Rad;
            copy.SetParent(null, true);
            copy.SetPositionAndRotation(source.position + (left * Mathf.Sin(a) + forward * Mathf.Cos(a)) * radius, source.rotation);
            Destroy(holder);
            rig.Initialize();   // its bone table is filled here, and the copy was made asleep
            // Turned toward the patient, face-on, then by turnDeg (none by default: company faces you).
            var facing = Flat(rig.transform.TransformDirection(Vector3.back));
            var toPatient = Flat(hip - rig.Hip.position);
            if (facing.sqrMagnitude > 1e-4f && toPatient.sqrMagnitude > 1e-4f)
                copy.rotation = Quaternion.FromToRotation(facing, toPatient) * copy.rotation;
            copy.RotateAround(rig.Hip.position, Vector3.up, view.Side == "left" ? -turnDeg : turnDeg);

            // Their name, over their head, turned to whoever is looking.
            var seat = new Seat { copy = copy, rig = rig };
            seat.label = new GameObject("Name tag").transform;
            seat.label.SetParent(copy, false);
            seat.nameLabel = Text(seat.label, "Name", 80, .014f, Palette.Prussian50, .06f);
            seat.stateLabel = Text(seat.label, "State", 56, .009f, Palette.Slate70, 0);
            copy.gameObject.SetActive(false);
            return seat;
        }

        static TextMesh Text(Transform parent, string name, int size, float scale, Color color, float y)
        {
            var text = new GameObject(name).AddComponent<TextMesh>();
            text.transform.SetParent(parent, false);
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
        /// single-AirPod shoulder raise, arm-elevation.v1, has no movement tile of its own): someone who cannot be
        /// drawn doing the movement should at least be drawn raising an arm, not sitting frozen.
        BodyModel Body => ActivityCatalog.MovementFor(view.ExerciseKind)?.Body
            ?? ActivityCatalog.ById("movement.arm-raise")?.Body;

        /// Their skin, hair and shirt, from the same palette their face in the member list is drawn with.
        static void Tint(Transform copy, int mii)
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

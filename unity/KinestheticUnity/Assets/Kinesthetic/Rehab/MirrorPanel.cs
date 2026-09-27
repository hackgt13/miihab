using UnityEngine;

namespace Kinesthetic.Rehab
{
    /// A mirror window on the patient's left (the coach is on their right): a mirror-image copy of the patient in
    /// their wheelchair, posed from the real one after every frame, with ghost arms at the plan's target and safe
    /// ceiling and the band between them lit by where the live arm is — below, in, or over. It stands ahead and to
    /// the left, facing the patient, so it sits in view from their eyes.
    ///
    /// It is a posed copy, not a second camera rendering a reflection, so on a headset it costs one extra skinned
    /// mesh rather than a whole extra view. The window faces whoever is looking: the Mac view today, the patient's
    /// own eyes in a headset. Added at runtime by RehabSession, so the generated scene needs no change.
    ///
    /// Reading as a mirror is the copy's whole job — a patient who takes it for a poster of someone else is not
    /// watching themselves. That read is carried by the surface rather than by the reflection: a silvered backing
    /// cooler than the room, a frame with some weight and a bevel at its lip, and two sheens raked across the
    /// glass. All three are static geometry, so none of it costs a frame.
    public sealed class MirrorPanel : MonoBehaviour
    {
        public IRehabView view;
        public float width = 1.12f, height = 1.42f, depth = .34f;
        [Tooltip("Metres from the patient's hips: to their left, and forward.")]
        public float offsetLeft = 1.6f, offsetForward = 2.2f;

        Transform source, copy, window;
        Material frameMaterial;
        /// The window's centre, for the coach to point at and the patient to look at.
        public Vector3? WindowPosition => window ? window.position : null;
        Transform[] from, to;
        Renderer[] fromRenderers, toRenderers;
        float scale;
        LineRenderer targetGhost, targetGhostForearm, ceilingGhost, ceilingGhostForearm, band;
        Transform targetHand, ceilingHand;   // a ghost hand at the end of each ghost arm
        float nextMaterialSync;

        void Start()
        {
            var rig = view?.Rig;
            if (!rig) { enabled = false; return; }
            rig.Initialize();
            source = rig.transform.parent ? rig.transform.parent : rig.transform;   // the patient and their wheelchair
            Build(rig);
        }

        void Build(PoseRig rig)
        {
            // Place the window to the patient's left and a little forward, standing on their floor.
            var hip = rig.Hip.position;
            var left = rig.transform.TransformDirection(Vector3.right);      // the Mii's anatomical left is local +X
            var forward = rig.transform.TransformDirection(Vector3.back);    // it faces local -Z
            var centre = hip + left * offsetLeft + forward * offsetForward;
            centre.y = source.position.y + height * .5f + .06f;
            window = new GameObject("Mirror window").transform;
            // Face the patient's eyes: that is who the mirror is for, in first person and in the headset alike.
            var viewer = hip + Vector3.up * .8f;
            var facing = viewer - centre; facing.y = 0;
            window.SetPositionAndRotation(centre, Quaternion.LookRotation(facing.sqrMagnitude > 1e-4f ? facing.normalized : -left));

            // The copy is made inside an inactive holder, so none of its scripts (the rig, the face) ever wake.
            var holder = new GameObject("Mirror holder"); holder.SetActive(false);
            copy = Instantiate(source.gameObject, holder.transform).transform;
            copy.name = "Reflection";
            foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(behaviour);
            foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(collider);
            from = source.GetComponentsInChildren<Transform>(true);
            to = copy.GetComponentsInChildren<Transform>(true);
            fromRenderers = source.GetComponentsInChildren<Renderer>(true);
            toRenderers = copy.GetComponentsInChildren<Renderer>(true);
            foreach (var skin in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.updateWhenOffscreen = true;
            // A headset hides the patient's own head from its camera by layer; the reflection must show it.
            foreach (var r in copy.GetComponentsInChildren<Renderer>(true)) r.gameObject.layer = 0;

            // Mirror image: flip one axis, then turn it to face out of the window. Fit it inside the frame.
            copy.SetParent(window, false);
            copy.localRotation = Quaternion.Euler(0, 180, 0);
            var bounds = WorldBounds(fromRenderers);
            scale = height * .85f / Mathf.Max(.1f, bounds.size.y);
            copy.localScale = new Vector3(-scale, scale, scale);
            copy.localPosition = Vector3.zero;
            Destroy(holder);

            var placed = WorldBounds(toRenderers);
            var inside = window.TransformPoint(new Vector3(0, 0, -depth * .5f));
            copy.position += new Vector3(inside.x - placed.center.x, inside.y - placed.center.y, inside.z - placed.center.z);

            // Silvered backing. A mirror's ground is cooler and a shade darker than the room it stands in, and that
            // is what puts the reflection *in* the glass rather than on a sheet of paper pinned to the wall. It was
            // sand before, the same warm near-white as the studio's walls, which is why it read as a poster.
            Box(window, "Backing", new Vector3(0, 0, -depth), new Vector3(width, height, .02f), Lit(Palette.Slate40, .85f));

            // Frame: heavier than a picture frame and closed at the corners, with a bevelled liner at its lip so the
            // opening is set into something. It stays shallow and sits near the front, clear of the reflection, which
            // is a solid seated figure and reaches further forward than any frame would want to contain.
            frameMaterial = Lit(Palette.Sand00);
            const float bar = .055f, lip = .02f;
            Border(window, "Frame", width * .5f, height * .5f, bar, -.075f, .11f, frameMaterial);
            Border(window, "Frame liner", width * .5f - lip * .5f, height * .5f - lip * .5f, lip, -.035f, .03f, Lit(Palette.Slate30));

            // Two sheens raked across the upper left: the cue that reads as glass at a glance and at two metres.
            // Sprites/Default honours alpha without a custom shader, the same trick the ghost arms use. They sit in
            // the corner the seated figure never reaches, so nothing of the patient crosses in front of the glass.
            Pane(window, "Glass sheen", new Vector3(-.357f * width, .268f * height, -.02f),
                new Vector3(.049f * width, .282f * height, .004f), Fade(Palette.Sand00, .22f), Rake);
            Pane(window, "Glass sheen second", new Vector3(-.250f * width, .183f * height, -.02f),
                new Vector3(.027f * width, .148f * height, .004f), Fade(Palette.Sand00, .14f), Rake);

            // Ghosts are wider than the real arm and see-through, so they read around it rather than behind it.
            targetGhost = Line(window, "Ghost arm · target", .075f);
            targetGhostForearm = Line(window, "Ghost forearm · target", .075f);
            ceilingGhost = Line(window, "Ghost arm · safe ceiling", .06f);
            ceilingGhostForearm = Line(window, "Ghost forearm · safe ceiling", .06f);
            band = Line(window, "Safe band", .05f);
            targetHand = Orb(window, "Ghost hand · target"); ceilingHand = Orb(window, "Ghost hand · safe ceiling");
        }

        // The window is its own object, not a child of this one: it goes when the mirror does (a group session
        // puts the other person where it stood).
        void OnDestroy() { if (window) Destroy(window.gameObject); }

        void LateUpdate()
        {
            if (!copy || view == null) return;
            // Pose: every transform under the patient, copied into the reflection (the root keeps its placement).
            for (int i = 1; i < from.Length && i < to.Length; i++)
            {
                if (!from[i] || !to[i]) continue;
                to[i].localPosition = from[i].localPosition; to[i].localRotation = from[i].localRotation; to[i].localScale = from[i].localScale;
            }
            // Blinks and mouth shapes swap material textures; share the source's materials a few times a second.
            if (Time.unscaledTime > nextMaterialSync)
            {
                nextMaterialSync = Time.unscaledTime + .1f;
                for (int i = 0; i < fromRenderers.Length && i < toRenderers.Length; i++)
                    if (fromRenderers[i] && toRenderers[i]) toRenderers[i].sharedMaterials = fromRenderers[i].sharedMaterials;
            }
            DrawGhosts();
            // When the coach hands over, the frame pulses to draw the patient's eye here.
            float pulse = view.CoachHandingOff ? .5f + .5f * Mathf.Sin(Time.time * 7) : 0;
            frameMaterial.color = Color.Lerp(Palette.Sand00, Palette.Cerulean40, pulse);
        }

        // Ghost arms and the band are drawn on the real patient's geometry, then carried through the reflection,
        // so they land exactly where a mirror would put them.
        void DrawGhosts()
        {
            var rig = view.Rig;
            bool left = view.Side == "left";
            float target = view.TargetDeg, ceiling = view.TargetDeg + view.BandDeg;
            Vector3 Reflect(Vector3 p) => copy.TransformPoint(source.InverseTransformPoint(p));
            // The measured segment, whichever it is (PoseRig.MovementSegment): its ghost at the target and at the
            // ceiling, and the band just beyond its end, so the live limb points into the band rather than covering it.
            var body = Kinesthetic.Activities.ActivityCatalog.MovementFor(view.ExerciseKind)?.Body;
            targetGhostForearm.enabled = ceilingGhostForearm.enabled = false;
            if (!rig.MovementSegment(body, left, target, out var origin, out var toTarget, out var length))
            {
                targetGhost.enabled = ceilingGhost.enabled = band.enabled = false;
                return;
            }
            rig.MovementSegment(body, left, ceiling, out _, out var toCeiling, out _);
            Segment(targetGhost, Reflect(origin), Reflect(origin + toTarget * length));
            Segment(ceilingGhost, Reflect(origin), Reflect(origin + toCeiling * length));
            targetHand.position = Reflect(origin + toTarget * length);
            ceilingHand.position = Reflect(origin + toCeiling * length);
            Arc(n => { rig.MovementSegment(body, left, Mathf.Lerp(target, ceiling, n), out _, out var d, out _); return Reflect(origin + d * length * 1.2f); });

            // Palette roles: cerulean is the measured arm still climbing, jungle is in the band (good), coral is over the ceiling.
            var angle = view.ShownAngle;
            Color bandColor = angle is not float a ? Fade(Palette.Cerulean20, .7f)
                : a > ceiling ? Palette.Coral40 : a >= target ? Palette.Jungle40 : Palette.Cerulean40;
            band.startColor = band.endColor = bandColor;
            var ghost = Fade(Palette.Sand00, .42f);
            var ceilingGhostColor = Fade(Palette.Coral40, angle is float over && over > ceiling ? .85f : .38f);
            targetGhost.startColor = targetGhost.endColor = targetGhostForearm.startColor = targetGhostForearm.endColor = ghost;
            ceilingGhost.startColor = ceilingGhost.endColor = ceilingGhostForearm.startColor = ceilingGhostForearm.endColor = ceilingGhostColor;
            targetHand.GetComponent<Renderer>().material.color = Fade(Palette.Sand00, .7f);
            ceilingHand.GetComponent<Renderer>().material.color = Fade(Palette.Coral40, .7f);
        }

        void Arc(System.Func<float, Vector3> at)
        {
            const int n = 16; band.positionCount = n; band.enabled = true;
            for (int i = 0; i < n; i++) band.SetPosition(i, at(i / (n - 1f)));
        }
        static void Segment(LineRenderer line, Vector3 a, Vector3 b)
        {
            line.enabled = true; line.positionCount = 2; line.SetPosition(0, a); line.SetPosition(1, b);
        }
        LineRenderer Line(Transform parent, string name, float width)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            line.transform.SetParent(parent, false);
            line.useWorldSpace = true; line.widthMultiplier = width * scale / .7f; line.numCapVertices = 6;
            // Sprites/Default honours vertex colour alpha, so ghosts can be see-through without a custom shader.
            line.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return line;
        }
        static Color Fade(Color c, float alpha) { c.a = alpha; return c; }
        Transform Orb(Transform parent, string name)
        {
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere); orb.name = name;
            Destroy(orb.GetComponent<Collider>());
            orb.transform.SetParent(parent, false); orb.transform.localScale = Vector3.one * .11f * scale / .7f;
            orb.GetComponent<Renderer>().material = new Material(Shader.Find("Sprites/Default"));
            orb.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return orb.transform;
        }
        static Material Lit(Color c, float smoothness = .35f)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.color = c; m.SetFloat("_Smoothness", smoothness); return m;
        }
        static Transform Box(Transform parent, string name, Vector3 at, Vector3 size, Material material)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = name;
            Destroy(box.GetComponent<Collider>());
            box.transform.SetParent(parent, false); box.transform.localPosition = at; box.transform.localScale = size;
            if (material) box.GetComponent<Renderer>().sharedMaterial = material;
            return box.transform;
        }

        /// A closed rectangle of four bars. Top and bottom run the full width, so the corners meet instead of
        /// leaving the four-separate-sticks gap a picture frame gives away.
        static void Border(Transform parent, string name, float halfW, float halfH, float bar, float z, float thick, Material m)
        {
            Box(parent, name + " top", new Vector3(0, halfH + bar * .5f, z), new Vector3(halfW * 2 + bar * 2, bar, thick), m);
            Box(parent, name + " bottom", new Vector3(0, -halfH - bar * .5f, z), new Vector3(halfW * 2 + bar * 2, bar, thick), m);
            Box(parent, name + " left", new Vector3(-halfW - bar * .5f, 0, z), new Vector3(bar, halfH * 2, thick), m);
            Box(parent, name + " right", new Vector3(halfW + bar * .5f, 0, z), new Vector3(bar, halfH * 2, thick), m);
        }

        /// The angle the sheens rake across the glass, from vertical.
        const float Rake = -28f;

        /// A flat translucent card lying on the glass: unlit, so it reads as a sheen on the surface rather than as
        /// one more lit object standing inside the frame.
        static void Pane(Transform parent, string name, Vector3 at, Vector3 size, Color colour, float rakeDegrees)
        {
            var pane = Box(parent, name, at, size, null);
            pane.localRotation = Quaternion.Euler(0, 0, rakeDegrees);
            var renderer = pane.GetComponent<Renderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = colour };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        static Bounds WorldBounds(Renderer[] renderers)
        {
            Bounds b = default; bool any = false;
            foreach (var r in renderers) { if (!r) continue; if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            return b;
        }
    }
}

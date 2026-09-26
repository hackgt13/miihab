using UnityEngine;

namespace Kinesthetic.Rehab
{
    /// A mirror window on the patient's left (the coach is on their right): a mirror-image copy of the patient in
    /// their wheelchair, posed from the real one after every frame, with ghost arms at the plan's target and safe
    /// ceiling and the band between them lit by where the live arm is — below, in, or over.
    ///
    /// It is a posed copy, not a second camera rendering a reflection, so on a headset it costs one extra skinned
    /// mesh rather than a whole extra view. The window faces whoever is looking: the Mac view today, the patient's
    /// own eyes in a headset. Added at runtime by RehabSession, so the generated scene needs no change.
    public sealed class MirrorPanel : MonoBehaviour
    {
        public RehabSession session;
        public float width = .9f, height = 1.2f, depth = .5f;
        [Tooltip("Metres from the patient's hips: to their left, and forward.")]
        public float offsetLeft = 1.15f, offsetForward = .45f;

        Transform source, copy;
        Transform[] from, to;
        Renderer[] fromRenderers, toRenderers;
        float scale;
        LineRenderer targetGhost, targetGhostForearm, ceilingGhost, ceilingGhostForearm, band;
        Transform targetHand, ceilingHand;   // a ghost hand at the end of each ghost arm
        float nextMaterialSync;
        static readonly Color InBand = new(.35f, .95f, .5f);

        void Start()
        {
            var rig = session ? session.rig : null;
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
            var window = new GameObject("Mirror window").transform;
            var viewer = Camera.main ? Camera.main.transform.position : hip;
            var facing = viewer - centre; facing.y = 0;
            window.SetPositionAndRotation(centre, Quaternion.LookRotation(facing.sqrMagnitude > 1e-4f ? facing.normalized : -left));

            // Frame: a shallow box, so the reflection sits inside it rather than on a flat sheet.
            var frameMaterial = Lit(Palette.Glaucous30); var backMaterial = Lit(Palette.Ice10);
            const float t = .05f;
            Box(window, "Frame top", new Vector3(0, height * .5f + t * .5f, -depth * .5f), new Vector3(width + 2 * t, t, depth), frameMaterial);
            Box(window, "Frame bottom", new Vector3(0, -height * .5f - t * .5f, -depth * .5f), new Vector3(width + 2 * t, t, depth), frameMaterial);
            Box(window, "Frame left", new Vector3(-width * .5f - t * .5f, 0, -depth * .5f), new Vector3(t, height, depth), frameMaterial);
            Box(window, "Frame right", new Vector3(width * .5f + t * .5f, 0, -depth * .5f), new Vector3(t, height, depth), frameMaterial);
            Box(window, "Backing", new Vector3(0, 0, -depth), new Vector3(width, height, .02f), backMaterial);

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

            // Ghosts are wider than the real arm and see-through, so they read around it rather than behind it.
            targetGhost = Line(window, "Ghost arm · target", .075f);
            targetGhostForearm = Line(window, "Ghost forearm · target", .075f);
            ceilingGhost = Line(window, "Ghost arm · safe ceiling", .06f);
            ceilingGhostForearm = Line(window, "Ghost forearm · safe ceiling", .06f);
            band = Line(window, "Safe band", .05f);
            targetHand = Orb(window, "Ghost hand · target"); ceilingHand = Orb(window, "Ghost hand · safe ceiling");
        }

        void LateUpdate()
        {
            if (!copy || !session) return;
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
        }

        // Ghost arms and the band are drawn on the real patient's geometry, then carried through the reflection,
        // so they land exactly where a mirror would put them.
        void DrawGhosts()
        {
            var rig = session.rig;
            bool left = session.side == "left", curl = session.ExerciseKind == "elbow-flexion.v1";
            float target = session.targetDeg, ceiling = session.targetDeg + session.bandDeg;
            Transform upper = left ? rig.LeftUpperArm : rig.RightUpperArm, forearm = left ? rig.LeftForearm : rig.RightForearm,
                hand = left ? rig.LeftHand : rig.RightHand;
            float upperLength = Vector3.Distance(upper.position, forearm.position), forearmLength = Vector3.Distance(forearm.position, hand.position);
            var shoulder = upper.position;
            Vector3 Reflect(Vector3 p) => copy.TransformPoint(source.InverseTransformPoint(p));

            if (curl)
            {
                var elbow = shoulder + rig.ImuUpperArmAtSide(left) * upperLength;
                Segment(targetGhost, Reflect(shoulder), Reflect(elbow));
                Segment(targetGhostForearm, Reflect(elbow), Reflect(elbow + rig.ImuForearmDirection(target) * forearmLength));
                Segment(ceilingGhost, Reflect(shoulder), Reflect(elbow));
                Segment(ceilingGhostForearm, Reflect(elbow), Reflect(elbow + rig.ImuForearmDirection(ceiling) * forearmLength));
                targetHand.position = Reflect(elbow + rig.ImuForearmDirection(target) * forearmLength);
                ceilingHand.position = Reflect(elbow + rig.ImuForearmDirection(ceiling) * forearmLength);
                Arc(n => Reflect(elbow + rig.ImuForearmDirection(Mathf.Lerp(target, ceiling, n)) * forearmLength * 1.25f));
            }
            else
            {
                float reach = upperLength + forearmLength;
                Segment(targetGhost, Reflect(shoulder), Reflect(shoulder + rig.ImuArmDirection(left, target) * reach));
                Segment(ceilingGhost, Reflect(shoulder), Reflect(shoulder + rig.ImuArmDirection(left, ceiling) * reach));
                targetGhostForearm.enabled = ceilingGhostForearm.enabled = false;
                targetHand.position = Reflect(shoulder + rig.ImuArmDirection(left, target) * reach);
                ceilingHand.position = Reflect(shoulder + rig.ImuArmDirection(left, ceiling) * reach);
                // The band sits just beyond the hand, so the live arm points into it rather than covering it.
                Arc(n => Reflect(shoulder + rig.ImuArmDirection(left, Mathf.Lerp(target, ceiling, n)) * reach * 1.2f));
            }

            // Below the target it is still climbing; inside the band it is right; over the ceiling it is too high.
            var angle = session.ShownAngle;
            Color bandColor = angle is not float a ? Fade(Palette.Ice30, .7f)
                : a > ceiling ? Palette.Coral40 : a >= target ? InBand : Palette.Ice40;
            band.startColor = band.endColor = bandColor;
            var ghost = Fade(Color.white, .42f);
            var ceilingGhostColor = Fade(Palette.Coral40, angle is float over && over > ceiling ? .85f : .38f);
            targetGhost.startColor = targetGhost.endColor = targetGhostForearm.startColor = targetGhostForearm.endColor = ghost;
            ceilingGhost.startColor = ceilingGhost.endColor = ceilingGhostForearm.startColor = ceilingGhostForearm.endColor = ceilingGhostColor;
            targetHand.GetComponent<Renderer>().material.color = Fade(Color.white, .7f);
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
        static Material Lit(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.color = c; m.SetFloat("_Smoothness", .35f); return m;
        }
        static void Box(Transform parent, string name, Vector3 at, Vector3 size, Material material)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = name;
            Destroy(box.GetComponent<Collider>());
            box.transform.SetParent(parent, false); box.transform.localPosition = at; box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = material;
        }
        static Bounds WorldBounds(Renderer[] renderers)
        {
            Bounds b = default; bool any = false;
            foreach (var r in renderers) { if (!r) continue; if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            return b;
        }
    }
}

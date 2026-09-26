using UnityEngine;

namespace Kinesthetic.Rehab.Mechanics
{
    /// An in-world mechanic that answers to how a rep is being made: a thing balanced on the hand, a pace to
    /// follow. It reads a RepFeel from whichever IRehabView is running the studio — the Mac's session or a
    /// headset's state client — so the same component renders in both places and computes nothing clinical.
    ///
    /// Composable: a mechanic is one component with no knowledge of any other. Attach() adds it to a host, and
    /// RepMechanics.AttachAll() is the one list of which mechanics a studio runs. A mechanic that has nothing to
    /// answer to (a pacer with no tempo, a hold ring with no hold) hides itself; it is never conditionally added.
    public abstract class RepMechanic : MonoBehaviour
    {
        protected IRehabView view;

        public static T Attach<T>(GameObject host, IRehabView view) where T : RepMechanic
        {
            var mechanic = host.GetComponent<T>() ?? host.AddComponent<T>();
            mechanic.view = view;
            return mechanic;
        }

        /// The measured segment at `deg`: where it pivots, the direction it points, and its length. False when the
        /// rig cannot model this movement, in which case the mechanic has nowhere to be and should hide.
        protected bool Segment(float deg, out Vector3 origin, out Vector3 direction, out float length)
        {
            var rig = view?.Rig;
            if (rig == null) { origin = direction = default; length = 0; return false; }
            var body = Kinesthetic.Activities.ActivityCatalog.MovementFor(view.ExerciseKind)?.Body;
            return rig.MovementSegment(body, view.Side == "left", deg, out origin, out direction, out length);
        }

        /// The end of the segment at `deg`, and the direction it moves in as the angle grows.
        protected bool Along(float deg, out Vector3 point, out Vector3 tangent)
        {
            point = tangent = default;
            if (!Segment(deg, out var origin, out var direction, out var length)) return false;
            point = origin + direction * length;
            Segment(deg + 2, out _, out var ahead, out _);
            tangent = (ahead - direction).sqrMagnitude > 1e-8f ? (ahead - direction).normalized : Vector3.up;
            return true;
        }

        void LateUpdate()
        {
            if (view == null) return;
            Render(view.Feel, view.ShownAngle, Time.unscaledDeltaTime);
        }

        /// Called after the rig has been posed for this frame. `angle` is null when nothing live is measured.
        protected abstract void Render(RepFeel feel, float? angle, float dt);

        // ── Shared drawing helpers, in the palette ────────────────────────────────────────────────────

        protected static Material Lit(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.color = c; m.SetFloat("_Smoothness", .4f); return m;
        }
        /// Sprites/Default honours vertex alpha, so a ghost can be see-through without a custom shader.
        protected static Material Ghost() => new Material(Shader.Find("Sprites/Default"));
        protected static Color Fade(Color c, float alpha) { c.a = alpha; return c; }

        protected Transform Primitive(PrimitiveType type, string name, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false); go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>(); r.sharedMaterial = material; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }
        protected LineRenderer Line(string name, float width)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            line.useWorldSpace = true; line.widthMultiplier = width; line.numCapVertices = 4; line.loop = false;
            line.sharedMaterial = Ghost(); line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return line;
        }
        /// An arc of `fraction` of a full circle around `center` in the plane normal to `up`, starting at `start`.
        protected static void Ring(LineRenderer line, Vector3 center, Vector3 up, Vector3 start, float radius, float fraction)
        {
            const int n = 32;
            int count = Mathf.Max(2, Mathf.CeilToInt(n * Mathf.Clamp01(fraction)) + 1);
            line.positionCount = count;
            var from = Vector3.ProjectOnPlane(start, up).normalized;
            if (from.sqrMagnitude < 1e-6f) from = Vector3.Cross(up, Vector3.forward).normalized;
            for (int i = 0; i < count; i++)
            {
                float a = 360f * Mathf.Clamp01(fraction) * i / (count - 1);
                line.SetPosition(i, center + Quaternion.AngleAxis(a, up) * from * radius);
            }
        }
    }

    /// The studio's mechanics, attached in one place so the Mac and a headset run the same set.
    public static class RepMechanics
    {
        public static void AttachAll(GameObject host, IRehabView view)
        {
            RepMechanic.Attach<PacerMechanic>(host, view);     // first: the reticle reads the pace it sets this frame
            RepMechanic.Attach<BalanceMechanic>(host, view);
            RepMechanic.Attach<ReticleMechanic>(host, view);
            RepMechanic.Attach<DialMechanic>(host, view);
        }
    }
}

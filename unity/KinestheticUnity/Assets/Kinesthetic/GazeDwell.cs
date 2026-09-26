using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic
{
    /// Selection that does not care what moves the head. A ray leaves the centre of the view, finds the
    /// world-space UI panel in front of it, picks the element under the hit point and commits after a dwell.
    /// Mouse-drag, webcam head tracking and a real HMD all drive it identically, so the input can be replaced
    /// later without touching a single panel.
    ///
    /// Nothing here synthesises UI Toolkit events: it reports the element's name and the menu maps that to the
    /// same action its clicked handler runs. So pointer clicking keeps working untouched, and dwell needs no
    /// event-pooling API that might not be public.
    ///
    /// Panel picking is self-calibrating — the hit is normalised against the collider UIDocument maintains for
    /// the panel, so it does not depend on the panel's world size or reference resolution being any particular
    /// number.
    public sealed class GazeDwell : MonoBehaviour
    {
        public float dwellSeconds = 1.1f;
        public float maxDistance = 12;
        public float releaseSeconds = .25f;     // brief grace so a shaky head does not reset the ring

        public event Action<string> Entered;
        public event Action<string> Committed;

        UIDocument document;
        GazeReticle reticle;
        string hot;
        float held, lost;

        void Awake()
        {
            document = GetComponent<UIDocument>();
            reticle = GazeReticle.Create();
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (!cam || !document) { reticle.Hide(); return; }

            string under = Under(cam);
            if (under == null)
            {
                lost += Time.unscaledDeltaTime;
                if (lost < releaseSeconds && hot != null) { reticle.Show(cam, held / dwellSeconds); return; }
                hot = null; held = 0; reticle.Hide(); return;
            }

            lost = 0;
            if (under != hot) { hot = under; held = 0; Entered?.Invoke(hot); }
            held += Time.unscaledDeltaTime;
            reticle.Show(cam, held / dwellSeconds);

            if (held < dwellSeconds) return;
            held = 0;
            string fired = hot;
            hot = null;
            reticle.Hide();
            Committed?.Invoke(fired);
        }

        // The name of the nearest Button under the centre of the view, or null.
        string Under(Camera cam)
        {
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            if (!Physics.Raycast(ray, out var hit, maxDistance)) return null;
            var box = hit.collider as BoxCollider;
            if (!box || !box.transform.IsChildOf(transform)) return null;

            var local = box.transform.InverseTransformPoint(hit.point) - box.center;
            var tree = document.rootVisualElement?.panel?.visualTree;
            if (tree == null || box.size.x <= 0 || box.size.y <= 0) return null;

            // Panel space is y-down from the top-left; the collider is centred and y-up.
            var point = new Vector2(
                (local.x / box.size.x + .5f) * tree.layout.width,
                (.5f - local.y / box.size.y) * tree.layout.height);

            for (var element = document.rootVisualElement.panel.Pick(point); element != null; element = element.parent)
                if (element is Button && !string.IsNullOrEmpty(element.name)) return element.name;
            return null;
        }
    }

    /// A ring at the centre of the view that fills as the dwell completes. Procedural geometry parented to the
    /// live camera rather than a screen-space overlay, because a screen-space panel does not render in stereo —
    /// the reticle is the one element that has to exist on a headset too.
    sealed class GazeReticle : MonoBehaviour
    {
        const int Segments = 48;
        const float Distance = 1.8f, Radius = .028f, Thickness = .005f;

        Mesh mesh;
        MeshRenderer render;
        readonly List<Vector3> vertices = new();
        readonly List<int> triangles = new();

        public static GazeReticle Create()
        {
            var go = new GameObject("Gaze reticle");
            var reticle = go.AddComponent<GazeReticle>();
            reticle.mesh = new Mesh { name = "Gaze reticle" };
            go.AddComponent<MeshFilter>().sharedMesh = reticle.mesh;
            reticle.render = go.AddComponent<MeshRenderer>();
            reticle.render.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(1, 1, 1, .92f) };
            reticle.render.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            reticle.render.receiveShadows = false;
            reticle.render.enabled = false;
            return reticle;
        }

        public void Hide() => render.enabled = false;

        public void Show(Camera cam, float progress)
        {
            transform.SetPositionAndRotation(cam.transform.position + cam.transform.forward * Distance, cam.transform.rotation);
            Arc(Mathf.Clamp01(progress));
            render.enabled = true;
        }

        void Arc(float progress)
        {
            vertices.Clear(); triangles.Clear();
            int steps = Mathf.Max(2, Mathf.CeilToInt(Segments * progress));
            for (int i = 0; i <= steps; i++)
            {
                float angle = Mathf.PI * -.5f + progress * Mathf.PI * 2 * (i / (float)steps);
                var direction = new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0);
                vertices.Add(direction * Radius);
                vertices.Add(direction * (Radius + Thickness));
                if (i == 0) continue;
                int b = (i - 1) * 2;
                triangles.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 });
            }
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
        }
    }
}

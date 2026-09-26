using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
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
    /// Elements are resolved by Panes.WorldPanelPick, not by panel.Pick: Pick returns null on a world-space
    /// panel at every point, including ones well inside the root. WorldPanelPick projects the hit through the
    /// panel's own transform once (Pane.TryProject) and picks in paint order, so a modal's shade blocks the
    /// buttons under it and a disabled button is never committed. PanePointerInput and CarouselChrome ask the
    /// same question the same way.
    public sealed class GazeDwell : MonoBehaviour
    {
        public float dwellSeconds = 1.1f;
        public float maxDistance = 12;
        public float releaseSeconds = .25f;     // brief grace so a shaky head does not reset the ring

        /// The element under the ray changed; null when the ray left the last one.
        public event Action<string> Entered;
        public event Action<string> Committed;

        /// Whether the centre of the view is a head at all: a tracked headset, or the Mac's look-drag
        /// (DevFreeLook) steered off its rest pose. A scripted camera that never moves is neither, and a
        /// dwell on whatever it happens to rest on is a press nobody made. BoardSet gates its dwells on this.
        public static bool HeadDriven(Camera cam)
        {
            foreach (var device in InputSystem.devices) if (device is XRHMD) return true;
            var look = cam ? cam.GetComponent<DevFreeLook>() : null;
            return look && look.Steered;
        }

        UIDocument document;
        GazeReticle reticle;
        string hot;
        Button hotButton;   // the element behind `hot`, for a replica board that sends the press away
        float held, lost;

        void Awake()
        {
            document = GetComponent<UIDocument>();
            Panes.WorldPanelPick.MakePickable(gameObject);
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
                Leave(); return;
            }

            lost = 0;
            if (under != hot) { hot = under; held = 0; Entered?.Invoke(hot); }
            held += Time.unscaledDeltaTime;
            reticle.Show(cam, held / dwellSeconds);

            if (held < dwellSeconds) return;
            held = 0;
            string fired = hot;
            var button = hotButton;
            hot = null;
            reticle.Hide();
            // A replica board (UI/Remote) has no handlers of its own: its press crosses to the Mac instead.
            if (Kinesthetic.UI.Remote.RemoteBoard.Intercepts(gameObject, button)) return;
            Committed?.Invoke(fired);
        }

        /// The ray left whatever it was on: subscribers hear a null so a highlight does not stick.
        void Leave()
        {
            bool had = hot != null;
            hot = null; hotButton = null; held = 0;
            reticle.Hide();
            if (had) Entered?.Invoke(null);
        }

        void OnDisable() { if (reticle) Leave(); }

        // The name of the Button under the centre of the view, or null.
        string Under(Camera cam)
        {
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            hotButton = Panes.WorldPanelPick.NamedButtonOn(gameObject, ray, maxDistance);
            return hotButton?.name;
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
            reticle.render.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = Palette.Sand10.At(.92f) };
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

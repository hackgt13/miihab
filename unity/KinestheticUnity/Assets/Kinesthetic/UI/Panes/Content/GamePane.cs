using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.Panes
{
    /// A small game — or any live 3D view — inside a pane.
    ///
    /// The stage is built 10 km from the venue and rendered by its own camera, which is the trick
    /// GolfHud already uses for the course minimap: every venue camera here has a far clip well
    /// under 600 m, so distance alone keeps the stage invisible without touching anyone's culling
    /// mask or the shared layer list. Layer 30 is ours; 31 is the golf minimap's.
    ///
    /// Pointer input arrives normalised from the bottom-left, so `point` maps straight onto a
    /// viewport ray with no per-game arithmetic.
    public sealed class GamePane : IPaneContent, IPanePointerTarget
    {
        public const int StageLayer = 30;
        static readonly Vector3 Elsewhere = new(0, 0, 10000);

        readonly Action<Vector2, bool> onPointer;
        readonly Action<GamePane> onTick;
        readonly Vector2Int pixels;

        GameObject stage;
        RenderTexture target;

        public string Title { get; }
        public Vector2 PreferredSize { get; }

        /// The stage root and its camera, for a game to build itself into. Both live under the
        /// far-away origin, so a game must parent to `StageRoot` and never to the scene.
        public Transform StageRoot => stage ? stage.transform : null;
        public Camera StageCamera { get; private set; }

        public GamePane(string title, Vector2 size, Vector2Int pixels,
                        Action<Vector2, bool> onPointer = null, Action<GamePane> onTick = null)
        {
            Title = title; PreferredSize = size; this.pixels = pixels;
            this.onPointer = onPointer; this.onTick = onTick;
        }

        public void Bind(VisualElement root)
        {
            root.Clear();

            stage = new GameObject("Pane stage · " + Title) { layer = StageLayer };
            stage.transform.position = Elsewhere;

            target = new RenderTexture(pixels.x, pixels.y, 16) { name = "Pane stage · " + Title };
            target.Create();

            var eye = new GameObject("Stage camera") { layer = StageLayer };
            eye.transform.SetParent(stage.transform, false);
            eye.transform.localPosition = new Vector3(0, 0, -4);
            StageCamera = eye.AddComponent<Camera>();
            StageCamera.cullingMask = 1 << StageLayer;      // the stage sees only itself
            StageCamera.clearFlags = CameraClearFlags.SolidColor;
            StageCamera.backgroundColor = Palette.Cerulean70;
            StageCamera.nearClipPlane = .05f; StageCamera.farClipPlane = 60;
            StageCamera.allowHDR = false; StageCamera.allowMSAA = false;
            StageCamera.depth = -30;                        // before the venue camera, like the minimap
            StageCamera.targetTexture = target;

            var view = new Image { image = target, scaleMode = ScaleMode.ScaleAndCrop };
            view.style.flexGrow = 1;
            view.pickingMode = PickingMode.Ignore;          // PaneHost routes the pointer instead
            root.Add(view);
        }

        /// Everything a game adds must be parented here and put on the stage layer, or the stage
        /// camera will not see it.
        public GameObject Spawn(string name, PrimitiveType shape)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name; go.layer = StageLayer;
            go.transform.SetParent(stage.transform, false);
            var collider = go.GetComponent<Collider>();
            if (collider) UnityEngine.Object.Destroy(collider);         // the pane's own collider is the only one that picks
            return go;
        }

        public void Tick() => onTick?.Invoke(this);

        public void OnPointer(Vector2 point, bool pressed) => onPointer?.Invoke(point, pressed);

        public void Dispose()
        {
            if (stage) UnityEngine.Object.Destroy(stage);
            if (target) { target.Release(); UnityEngine.Object.Destroy(target); }
            stage = null; target = null; StageCamera = null;
        }
    }
}

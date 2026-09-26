using System.Collections;
using UnityEngine;

namespace Kinesthetic.Shell
{
    /// The view's own curtain. A quad a metre in front of whichever camera is live, drawn over everything,
    /// that can fade the view to the doorway shade or narrow it to a tunnel. World geometry rather than a
    /// screen-space panel because a screen-space panel never reaches a stereo eye, and this is the one piece
    /// of chrome that has to exist on the headset: it is what hides the cut between two scenes.
    ///
    /// One per process, and it outlives scenes: it is at full cover at the moment a scene is swapped, and it
    /// has to still be there, in front of the new scene's camera, to fade back up. So it is never a child of
    /// a camera — a child dies with its parent when the old scene unloads, and the new scene would simply
    /// appear. It stays a root object and follows Camera.main every frame, again just before render so a
    /// tracked head never sees it lag, and sizes the quad from the camera's field of view so the same tunnel
    /// setting reads the same on a Mac window and in a headset eye.
    public sealed class HeadFade : MonoBehaviour
    {
        const string ShaderPath = "Shell/HeadFade";
        const float Distance = 1f, Margin = 1.6f;
        static readonly int CoverId = Shader.PropertyToID("_Cover"), TunnelId = Shader.PropertyToID("_Tunnel"), ColorId = Shader.PropertyToID("_Color");

        static HeadFade instance;

        /// The colour the view fades to: the same shade every doorway's vestibule is painted, so the door,
        /// the fade and the darkness between scenes are one thing.
        public static Color Shade => Palette.Prussian60;

        Material material;
        MeshRenderer render;
        Camera attached;
        float fov, aspect, cover, tunnel;

        public float Cover { get => cover; set { cover = Mathf.Clamp01(value); Push(); } }
        public float Tunnel { get => tunnel; set { tunnel = Mathf.Clamp01(value); Push(); } }
        public Camera Attached => attached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { instance = null; }

        public static HeadFade Ensure()
        {
            if (instance) return instance;
            var go = new GameObject("Head fade");
            DontDestroyOnLoad(go);
            return go.AddComponent<HeadFade>();
        }

        void Awake()
        {
            if (instance && instance != this) { Destroy(gameObject); return; }
            instance = this;
            var shader = Resources.Load<Shader>(ShaderPath);
            if (!shader) Debug.LogError("HeadFade: shader missing at Resources/" + ShaderPath + "; the view cannot fade.");
            material = new Material(shader ? shader : Shader.Find("Sprites/Default")) { name = "Head fade" };
            material.SetColor(ColorId, Shade);
            gameObject.AddComponent<MeshFilter>().sharedMesh = Quad();
            render = gameObject.AddComponent<MeshRenderer>();
            render.sharedMaterial = material;
            render.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            render.receiveShadows = false;
            render.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            render.enabled = false;
        }

        // A unit quad in the camera's XY plane. Cull is off in the shader, so which way it faces is moot.
        static Mesh Quad() => new()
        {
            name = "Head fade quad",
            vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0) },
            uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) },
            triangles = new[] { 0, 2, 1, 0, 3, 2 },
        };

        void OnEnable() { Application.onBeforeRender += Follow; }
        void OnDisable() { Application.onBeforeRender -= Follow; }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != attached || (cam && (cam.fieldOfView != fov || cam.aspect != aspect))) Attach(cam);
            Follow();
        }

        void Follow()
        {
            if (!attached) return;
            var eye = attached.transform;
            transform.SetPositionAndRotation(eye.position + eye.rotation * new Vector3(0, 0, Distance), eye.rotation);
        }

        void Attach(Camera cam)
        {
            attached = cam;
            if (!cam) { render.enabled = false; return; }
            fov = cam.fieldOfView; aspect = cam.aspect;
            // Wide enough that the view's corners stay inside it with room to spare, on any camera.
            float side = 2 * Mathf.Tan(fov * .5f * Mathf.Deg2Rad) * Distance * Mathf.Max(aspect, 1) * Margin;
            transform.localScale = new Vector3(side, side, 1);
            Push();
        }

        void Push()
        {
            if (!material || !render) return;
            material.SetFloat(CoverId, cover);
            material.SetFloat(TunnelId, tunnel);
            render.enabled = attached && (cover > 0 || tunnel > 0);
        }

        /// Unscaled time, like every other piece of shell motion: a scene that paused the game must still be
        /// leavable.
        public IEnumerator CoverTo(float target, float seconds)
        {
            float start = cover, t = 0;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                Cover = Mathf.Lerp(start, target, Mathf.Clamp01(t / seconds));
                yield return null;
            }
            Cover = target;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
            if (material) Destroy(material);
        }
    }
}

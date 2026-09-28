using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kinesthetic.Shell
{
    /// The view's own curtain. A quad a metre in front of whichever camera is live, drawn over everything,
    /// that can fade the view to the doorway shade or narrow it to a tunnel. World geometry rather than a
    /// screen-space panel because a screen-space panel never reaches a stereo eye, and this is the one piece
    /// of chrome that has to exist on the headset: it is what hides the cut between two scenes.
    ///
    /// One per process, and it outlives scenes: it is at full cover at the moment a scene is swapped, and it
    /// has to still be there, in front of the new scene's camera, to fade back up. It rides as a child of
    /// the live camera — the only way it stays exactly head-locked on a headset, where the pose is written
    /// again right before render, after any script that tried to follow it — so whoever swaps a scene calls
    /// Detach() first: a child dies with its parent when the old scene unloads. It re-parents itself to
    /// Camera.main whenever that changes.
    public sealed class HeadFade : MonoBehaviour
    {
        const string ShaderPath = "Shell/HeadFade";
        // Ten metres ahead and 120 across: wider than any view — 80 degrees to each side — so cover never
        // depends on knowing the camera's field of view or aspect, which a camera that has not rendered yet
        // reports wrongly; the tunnel is scaled to the view through _Extent instead. Far rather than close
        // because the sheet is centred between the eyes: a metre out, the two eyes see its soft tunnel edge
        // nearly two degrees apart and the edge shimmers; ten metres out the difference is nothing.
        const float Distance = 10f, HalfSide = 60f;
        static readonly int CoverId = Shader.PropertyToID("_Cover"), TunnelId = Shader.PropertyToID("_Tunnel"), ColorId = Shader.PropertyToID("_Color"), ExtentId = Shader.PropertyToID("_Extent");

        static HeadFade instance;

        /// The colour the view fades to: the same shade every doorway's vestibule is painted, so the door,
        /// the fade and the darkness between scenes are one thing.
        public static Color Shade => Palette.Prussian60;

        Material material;
        MeshRenderer render;
        Camera attached;
        float cover, tunnel;

        public float Cover { get => cover; set { cover = Mathf.Clamp01(value); Push(); } }
        public float Tunnel { get => tunnel; set { tunnel = Mathf.Clamp01(value); Push(); } }
        public Camera Attached => attached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { instance = null; }

        /// The curtain if there is one, and null if nothing has raised one. Unlike Ensure, asking does
        /// not build it: a scene entered straight from the editor has no curtain, and giving it one just
        /// because something wanted to know would put a quad in front of the camera for no reason.
        public static HeadFade Current => instance;

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
            transform.localScale = new Vector3(HalfSide * 2, HalfSide * 2, 1);
            StartCoroutine(Warm());
        }

        // Draw once, invisibly, so the shader is compiled before the first walk rather than in its first frame.
        IEnumerator Warm()
        {
            yield return null;
            if (cover == 0 && tunnel == 0) { Cover = .004f; yield return null; yield return null; if (Mathf.Approximately(cover, .004f)) Cover = 0; }
        }

        // A unit quad in the camera's XY plane. Cull is off in the shader, so which way it faces is moot.
        static Mesh Quad() => new()
        {
            name = "Head fade quad",
            vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0) },
            uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) },
            triangles = new[] { 0, 2, 1, 0, 3, 2 },
        };

        void LateUpdate()
        {
            var cam = Camera.main;
            // The camera being left is still Camera.main for the frames the scene takes to swap. Riding it again would
            // parent the curtain into the old scene and unload it with that scene, so the new one arrived unfaded.
            if (cam && cam == leaving) return;
            leaving = null;
            if (cam != attached || (cam && transform.parent != cam.transform)) Attach(cam);
            if (!attached) return;
            // How far across the quad the edge of the view reaches, re-read every frame: a camera's aspect is
            // only right once it has rendered, and a wrong value here only mis-scales the tunnel for a frame.
            float reach = Mathf.Tan(attached.fieldOfView * .5f * Mathf.Deg2Rad) * Distance * Mathf.Max(attached.aspect, 1f);
            if (material) material.SetFloat(ExtentId, reach / HalfSide);
        }

        /// Off the camera and back to a root object, keeping its place in the world. Called by whoever is
        /// about to swap scenes, so the sheet survives the camera it was riding; it takes the next camera in
        /// LateUpdate. Still drawn meanwhile: whatever renders next is covered.
        Camera leaving;
        // Once the new scene is active the swap is over: whatever Camera.main is then — even a camera that survived
        // the swap — is the one to ride.
        void OnEnable() => SceneManager.activeSceneChanged += SceneSwapped;
        void OnDisable() => SceneManager.activeSceneChanged -= SceneSwapped;
        void SceneSwapped(Scene from, Scene to) => leaving = null;
        public void Detach()
        {
            if (attached) leaving = attached;
            if (transform.parent) transform.SetParent(null, true);
            // Parenting moved it into the camera's scene; back at the root it would still unload with that
            // scene unless it is made persistent again.
            DontDestroyOnLoad(gameObject);
            attached = null;
        }

        void Attach(Camera cam)
        {
            attached = cam;
            if (!cam) { if (transform.parent) transform.SetParent(null, true); DontDestroyOnLoad(gameObject); return; }
            transform.SetParent(cam.transform, false);
            transform.localPosition = new Vector3(0, 0, Distance);
            transform.localRotation = Quaternion.identity;
            transform.localScale = new Vector3(HalfSide * 2, HalfSide * 2, 1);
            Push();
        }

        void Push()
        {
            if (!material || !render) return;
            material.SetFloat(CoverId, cover);
            material.SetFloat(TunnelId, tunnel);
            render.enabled = cover > 0 || tunnel > 0;   // never gated on a camera: the swap frame has none
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

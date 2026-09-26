using Kinesthetic.Panes;
using Kinesthetic.Shell;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

/// Generates the pane surface's panel asset and a sandbox scene to exercise it.
///
/// Follows the repo rule that scene wiring lives in Assets/Editor/*SceneSetup.cs and never the
/// Inspector, so re-running this is the way to change the scene.
///
/// The sandbox is deliberately NOT added to EditorBuildSettings. Build indices here are reordered
/// by Kinesthetic -> Quest -> Boot headset scene, and a stray scene at the wrong index is exactly
/// the failure that ships a broken player.
public static class PaneSandboxSetup
{
    const string Root = "Assets/Kinesthetic/Panes";
    const string PanelPath = Root + "/PanesPanel.asset";
    public const string ScenePath = Root + "/PaneSandbox.unity";

    [MenuItem("Kinesthetic/Panes/Create pane sandbox")]
    public static void Create()
    {
        Debug.Log(Build());
    }

    public static string Build()
    {
        var panel = Panel();
        var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Root + "/Pane.uxml");
        if (!tree) throw new System.InvalidOperationException("Missing " + Root + "/Pane.uxml");

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // No RenderSettings writes here. Three setup scripts already hand-write conflicting
        // global lighting, and under additive loading the last scene wins; the sandbox stays out
        // of that argument.
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 1.05f;
        sun.transform.rotation = Quaternion.Euler(44, 32, 0);

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor"; floor.transform.localScale = Vector3.one * 1.4f;
        Object.DestroyImmediate(floor.GetComponent<Collider>());   // only panes should be pickable

        var camera = new GameObject("Sandbox camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        camera.tag = "MainCamera";
        // Parked at the ring's centre: the carousel turns the furniture, never the viewpoint.
        camera.transform.SetPositionAndRotation(new Vector3(0, 1.25f, 0), Quaternion.identity);
        camera.fieldOfView = 52; camera.nearClipPlane = .05f;
        camera.farClipPlane = 600;                                  // keeps the 10 km stage clipped
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Kinesthetic.Palette.Cerulean10;

        var host = new GameObject("Pane host", typeof(PaneCarousel), typeof(PaneHost), typeof(PanePointer), typeof(PaneSandbox));
        host.transform.position = camera.transform.position;   // the ring stands around the person
        var paneHost = host.GetComponent<PaneHost>();
        paneHost.panelSettings = panel; paneHost.paneTree = tree;
        paneHost.carousel = host.GetComponent<PaneCarousel>();  // placement is the ring's, not the host's

        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
        AssetDatabase.SaveAssets();
        return "Pane sandbox created at " + ScenePath + ". Not added to build settings on purpose.";
    }

    /// Cloned from RehabPanel.asset so the panes inherit the patient app's theme stylesheet and
    /// Nunito rather than restating them — the same trick MainMenuSetup uses for its panels.
    static PanelSettings Panel()
    {
        var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
        if (!panel)
        {
            var template = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Kinesthetic/Rehab/RehabPanel.asset");
            panel = template ? Object.Instantiate(template) : ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(panel, PanelPath);
        }
        panel.renderMode = PanelRenderMode.WorldSpace;
        panel.scaleMode = PanelScaleMode.ConstantPixelSize;    // world size is metres; pixels stay honest
        panel.referenceResolution = new Vector2Int(760, 500);
        panel.sortingOrder = 50;
        EditorUtility.SetDirty(panel);
        return panel;
    }
}

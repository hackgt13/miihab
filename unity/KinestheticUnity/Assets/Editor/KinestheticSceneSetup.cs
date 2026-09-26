using System;
using System.IO;
using System.Linq;
using Kinesthetic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

public static class KinestheticSceneSetup
{
    const string Root = "Assets/Kinesthetic";
    public const string ScenePath = Root + "/MotionProof.unity";
    [MenuItem("Kinesthetic/Create motion proof scene")]
    public static void CreateScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Art/Mii/KinestheticMii.glb");
        if (!prefab) throw new InvalidOperationException("glTFast has not imported the articulated avatar.");
        var actor = new GameObject("Patient pose presentation");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, actor.transform);
        // Blender's -Y front exports as Unity +Z; the spectator camera faces +Z.
        model.transform.localRotation = Quaternion.Euler(0, 180, 0);
        // Keep the Mii likeness while making the head proportional to the body.
        // All facial meshes and hair follow this skinned head bone during replay.
        var head = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "head");
        if (!head) throw new InvalidOperationException("Mii head bone is missing.");
        head.localScale *= .78f;
        foreach (var a in model.GetComponentsInChildren<Animation>()) { a.playAutomatically = false; a.enabled = false; }
        foreach (var a in model.GetComponentsInChildren<Animator>()) a.enabled = false;
        var renderers = model.GetComponentsInChildren<Renderer>();
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        float scale = 1.75f / bounds.size.y;
        model.transform.localScale *= scale;
        foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;
        var rig = actor.AddComponent<PoseRig>(); rig.avatar = model.transform; rig.Initialize(); rig.Apply(null);
        var ankle = rig.RightAnkle;
        actor.transform.position += Vector3.up * (.12f - ankle.position.y);
        // Chair and patient share one root so any later wheelchair translation moves both.
        var wheelchairPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Art/BlenderProps/MiiWheelchair.glb");
        if (!wheelchairPrefab) throw new InvalidOperationException("Blender wheelchair GLB has not imported.");
        var wheelchair = (GameObject)PrefabUtility.InstantiatePrefab(wheelchairPrefab, actor.transform);
        wheelchair.transform.localRotation = Quaternion.Euler(0, 180, 0);
        wheelchair.transform.position = new Vector3(actor.transform.position.x, 0, actor.transform.position.z);
        Surface("Floor", PrimitiveType.Plane, Vector3.zero, Vector3.one, Material("Floor", new Color(.07f, .11f, .14f)));
        var target = Surface("Shoulder test target", PrimitiveType.Sphere,
            rig.RightUpperArm.position + new Vector3(-.5f, .3f, 0), Vector3.one * .12f, Material("Target", Color.gray));
        var eyes = actor.AddComponent<EyeAnchor>();
        eyes.ownBody = model.transform; eyes.lookAt = target.transform;
        var camera = new GameObject("Spectator camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(1.5f, 1.3f, -3.3f);
        camera.transform.LookAt(new Vector3(0, .69f, 0));
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.035f, .065f, .085f);
        camera.fieldOfView = 40;
        camera.rect = new Rect(0, 0, 1, 1);
        camera.tag = "MainCamera";
        var light = new GameObject("Key light").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.2f; light.transform.rotation = Quaternion.Euler(35, -35, 0);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.6f, .65f, .7f);
        var uiObject = new GameObject("Motion replay controls");
        var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(Root + "/ReplayPanel.asset");
        if (!panel) { panel = ScriptableObject.CreateInstance<PanelSettings>(); AssetDatabase.CreateAsset(panel, Root + "/ReplayPanel.asset"); }
        panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        panel.referenceResolution = new Vector2Int(1400, 900);
        EditorUtility.SetDirty(panel);
        var document = uiObject.AddComponent<UIDocument>();
        document.panelSettings = panel;
        document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Root + "/Replay.uxml");
        var replay = uiObject.AddComponent<PoseReplay>(); replay.rig = rig; replay.target = target; replay.sceneCamera = camera;
        PlayerSettings.companyName = "Kinesthetic";
        PlayerSettings.productName = "Kinesthetic Motion Proof";
        PlayerSettings.defaultScreenWidth = 1400; PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.runInBackground = true;
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
        // Additive, like every other setup here. Assigning the whole list instead drops every other scene from
        // Build Settings, which boots a build into this diagnostic scene and leaves the menu unable to load
        // any activity, since LoadScene only reaches scenes on the list.
        var builds = EditorBuildSettings.scenes.ToList();
        if (!builds.Any(s => s.path == ScenePath)) builds.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = builds.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("[Kinesthetic] Motion scene created with articulated avatar and replay controls.");
    }
    static Material Material(string name, Color color)
    {
        string path = Root + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.color = color; m.SetFloat("_Smoothness", .25f); EditorUtility.SetDirty(m); return m;
    }
    static Renderer Surface(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.position = position; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material; return go.GetComponent<Renderer>();
    }
    public static void BuildMac()
    {
        CreateScene();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { ScenePath }, locationPathName = "Builds/Kinesthetic Motion Proof.app",
            target = BuildTarget.StandaloneOSX, options = BuildOptions.Development });
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new Exception("Mac build failed: " + report.summary.result);
        Debug.Log("[Kinesthetic] Mac build passed.");
    }
    public static void VerifyCapture()
    {
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "-captureJson");
        if (at < 0 || at + 1 >= args.Length) throw new Exception("Pass -captureJson with the local test export.");
        CreateScene(); VerifyFile(args[at + 1]);
    }
    public static string VerifyFile(string path)
    {
        var capture = PoseJson.Read<PoseCapture>(File.ReadAllText(path));
        var rig = UnityEngine.Object.FindAnyObjectByType<PoseRig>();
        var replay = UnityEngine.Object.FindAnyObjectByType<PoseReplay>();
        int right = 0, left = 0, shoulder = 0;
        float maxElbowChange = 0;
        Quaternion first = Quaternion.identity;
        bool haveFirst = false;
        foreach (var frame in capture.frames)
        {
            replay.ApplyFrame(frame);
            if (rig.RightArmTracked)
            {
                right++;
                if (!haveFirst) { first = rig.RightForearm.localRotation; haveFirst = true; }
                maxElbowChange = Mathf.Max(maxElbowChange, Quaternion.Angle(first, rig.RightForearm.localRotation));
            }
            if (rig.LeftArmTracked) left++;
            if (replay.ShoulderValid) shoulder++;
            if (!PoseMath.Finite(rig.RightHand.position.x)) throw new Exception("Nonfinite rig position.");
        }
        if (right < 1 || maxElbowChange < 1) throw new Exception("Capture failed to articulate the right elbow independently.");
        var last = rig.RightHand.position;
        rig.Apply(null);
        if (rig.RightArmTracked || rig.LeftArmTracked) throw new Exception("Dropout retained tracked status.");
        rig.Apply(capture.frames[^1]);
        if (Vector3.Distance(last, rig.RightHand.position) > .0001f) throw new Exception("Replay depends on previous frame state.");
        var nullPoint = PoseJson.Read<PosePoint>("{\"x\":null,\"y\":0.5,\"z\":0,\"visibility\":1}");
        if (PoseMath.Finite(nullPoint.x)) throw new Exception("Null coordinate became valid.");
        replay.ApplyFrame(null);
        var result = $"VERIFIED frames={capture.frames.Length}, rightArm={right}, leftArm={left}, shoulder={shoulder}, independentElbowRotationDegrees={maxElbowChange:F2}, dropout=pass, repeatability=pass, nullCoordinates=pass";
        Debug.Log("[Kinesthetic] " + result);
        return result;
    }
    public static string VerifyGeometry()
    {
        var replay = UnityEngine.Object.FindAnyObjectByType<PoseReplay>();
        var frame = new PoseFrame {subjectDetected=true, imageLandmarks=new PosePoint[33],worldLandmarks=new PosePoint[33]};
        foreach (var index in new[] {12,14,16,24})
        {
            frame.imageLandmarks[index] = new PosePoint {x=.5f,y=.5f,z=0,visibility=1};
            frame.worldLandmarks[index] = new PosePoint {x=0,y=0,z=0,visibility=1};
        }
        frame.worldLandmarks[14].x=1;
        frame.worldLandmarks[16].x=1; frame.worldLandmarks[16].y=-1;
        frame.worldLandmarks[24].y=1;
        replay.ApplyFrame(frame);
        if (!PoseMath.Shoulder(frame,false,out var shoulder) || Mathf.Abs(shoulder-90)>.001f ||
            !PoseMath.Elbow(frame,false,out var elbow) || Mathf.Abs(elbow-90)>.001f || !replay.TargetReached)
            throw new Exception("Known 90-degree geometry failed.");
        frame.worldLandmarks[24].x=float.NaN; replay.ApplyFrame(frame);
        if (replay.ShoulderValid || replay.TargetReached) throw new Exception("Invalid hip did not pause the target.");
        frame.imageLandmarks[14].x=1.1f; replay.ApplyFrame(frame);
        if (replay.rig.RightArmTracked) throw new Exception("Offscreen elbow was tracked.");
        replay.ApplyFrame(null);
        return "Known 90-degree shoulder/elbow geometry, target gating, invalid hip, offscreen elbow: pass";
    }
}

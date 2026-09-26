using System;
using System.Linq;
using Kinesthetic;
using Kinesthetic.Rehab;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.SceneManagement;

// Builds the patient's seated shoulder-raise scene: same Mii + wheelchair rig as golf, a calm practice room,
// and guides driven by the coordinator's measurement engine.
public static class RehabSceneSetup
{
    const string Root = "Assets/Kinesthetic/Rehab";
    public const string ScenePath = Root + "/Rehab.unity";

    [MenuItem("Kinesthetic/Rehab/Create shoulder raise scene")]
    public static string Create()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            var open = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if (open.isDirty && !string.IsNullOrEmpty(open.path)) EditorSceneManager.SaveScene(open);
        }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var slot = new GameObject("Patient and wheelchair").transform;
        var actor = new GameObject("Body pose").transform; actor.SetParent(slot, false);
        var avatar = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Kinesthetic/Art/Mii/KinestheticMii.glb"), actor);
        avatar.transform.localRotation = Quaternion.Euler(0, 180, 0);
        var head = avatar.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "head"); if (head) head.localScale *= .78f;
        foreach (var a in avatar.GetComponentsInChildren<Animation>()) { a.playAutomatically = false; a.enabled = false; }
        foreach (var a in avatar.GetComponentsInChildren<Animator>()) a.enabled = false;
        var renders = avatar.GetComponentsInChildren<Renderer>(); var bounds = renders[0].bounds;
        foreach (var r in renders) bounds.Encapsulate(r.bounds);
        avatar.transform.localScale *= 1.75f / bounds.size.y;
        foreach (var r in avatar.GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;
        var rig = actor.gameObject.AddComponent<PoseRig>(); rig.avatar = avatar.transform; rig.seated = true; rig.usePresentationSpace = true;
        rig.Initialize(); rig.Apply(null);
        actor.position += Vector3.up * (.12f - rig.RightAnkle.position.y);
        var chair = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Kinesthetic/Art/BlenderProps/MiiWheelchair.glb"), slot);
        chair.transform.localRotation = Quaternion.Euler(0, 180, 0);
        actor.gameObject.AddComponent<Kinesthetic.Golf.MiiIdleLife>(); // blink + breathing, paused while arms are tracked

        // Practice room: soft floor, back wall, a mat, a window of light.
        Solid("Floor", PrimitiveType.Plane, new Vector3(0, 0, 0), new Vector3(2, 1, 2), new Color(.78f, .74f, .66f));
        Solid("Back wall", PrimitiveType.Cube, new Vector3(0, 2, 3.2f), new Vector3(12, 4, .1f), new Color(.72f, .85f, .9f));
        Solid("Mat", PrimitiveType.Cube, new Vector3(0, .005f, 0), new Vector3(2.2f, .01f, 1.6f), new Color(.45f, .74f, .56f));

        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        Material Glow(string name, Color c) { var m = new Material(unlit) { name = name, color = c };
            AssetDatabase.CreateAsset(m, $"{Root}/{name}.mat"); return m; }
        var band = new GameObject("Target band").AddComponent<LineRenderer>();
        band.widthMultiplier = .09f; band.numCapVertices = 6; band.sharedMaterial = Glow("RehabBand", new Color(.85f, .9f, .95f));
        var guide = new GameObject("Measured arm angle").AddComponent<LineRenderer>();
        guide.widthMultiplier = .025f; guide.sharedMaterial = Glow("RehabGuide", new Color(.7f, .9f, 1f));
        var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere); orb.name = "Target orb";
        UnityEngine.Object.DestroyImmediate(orb.GetComponent<Collider>()); orb.GetComponent<Renderer>().sharedMaterial = Glow("RehabOrb", new Color(1, .86f, .3f));
        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere); marker.name = "Measured hand marker";
        UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>()); marker.transform.localScale = Vector3.one * .08f;
        marker.GetComponent<Renderer>().sharedMaterial = Glow("RehabMarker", new Color(.55f, .88f, 1f));

        var camera = new GameObject("Patient view camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(.9f, 1.45f, -2.9f); camera.transform.LookAt(new Vector3(-.15f, .95f, 0));
        camera.fieldOfView = 42; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.72f, .85f, .9f);
        camera.tag = "MainCamera";
        var light = new GameObject("Key light").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.15f; light.shadows = LightShadows.Soft; light.transform.rotation = Quaternion.Euler(40, -30, 0);
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.62f, .66f, .7f);

        var ui = new GameObject("Rehab session");
        var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(Root + "/RehabPanel.asset");
        if (!panel) { panel = ScriptableObject.CreateInstance<PanelSettings>(); AssetDatabase.CreateAsset(panel, Root + "/RehabPanel.asset"); }
        panel.scaleMode = PanelScaleMode.ScaleWithScreenSize; panel.referenceResolution = new Vector2Int(1400, 900); EditorUtility.SetDirty(panel);
        var doc = ui.AddComponent<UIDocument>(); doc.panelSettings = panel;
        doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Root + "/Rehab.uxml");
        var session = ui.AddComponent<RehabSession>();
        session.rig = rig; session.targetBand = band; session.armGuide = guide; session.targetOrb = orb.transform; session.liveMarker = marker.transform;

        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
        var scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Any(s => s.path == ScenePath)) { scenes.Add(new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = scenes.ToArray(); }
        AssetDatabase.SaveAssets();
        return "Rehab scene created at " + ScenePath;
    }

    static void Solid(string name, PrimitiveType type, Vector3 position, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.position = position; go.transform.localScale = scale;
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetColor("_BaseColor", color); m.SetFloat("_Smoothness", .15f);
        AssetDatabase.CreateAsset(m, $"{Root}/{name.Replace(' ', '-')}.mat");
        go.GetComponent<Renderer>().sharedMaterial = m;
    }
}

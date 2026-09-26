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

        RehabStudioBuilder.Build();
        slot.position = Vector3.up * .062f;

        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        Material Glow(string name, Color c) {
            var m = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/{name}.mat");
            if (!m) { m = new Material(unlit); AssetDatabase.CreateAsset(m, $"{Root}/{name}.mat"); }
            m.shader = unlit; m.name = name; m.color = c; EditorUtility.SetDirty(m); return m; }
        var band = new GameObject("Target band").AddComponent<LineRenderer>();
        band.widthMultiplier = .035f; band.numCapVertices = 6; band.sharedMaterial = Glow("RehabBand", Color.white);
        var guide = new GameObject("Measured arm angle").AddComponent<LineRenderer>();
        guide.widthMultiplier = .025f; guide.sharedMaterial = Glow("RehabGuide", Palette.Cerulean40);
        var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere); orb.name = "Target orb";
        UnityEngine.Object.DestroyImmediate(orb.GetComponent<Collider>()); orb.GetComponent<Renderer>().sharedMaterial = Glow("RehabOrb", Palette.Coral40);
        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere); marker.name = "Measured hand marker";
        UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>()); marker.transform.localScale = Vector3.one * .08f;
        marker.GetComponent<Renderer>().sharedMaterial = Glow("RehabMarker", Palette.Cerulean50);

        // Seeing your own arm reach the target is the point of the exercise, so the studio gets the
        // patient's-eye view too, not just the clinician's framing.
        var eyes = actor.gameObject.AddComponent<EyeAnchor>();
        eyes.ownBody = avatar.transform; eyes.lookAt = orb.transform;

        var camera = new GameObject("Patient view camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(1.7f, 1.95f, -5.2f); camera.transform.LookAt(new Vector3(-.15f, 1.0f, .25f));
        camera.fieldOfView = 40; camera.nearClipPlane = .1f; camera.farClipPlane = 50;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.83f, .92f, .91f);
        camera.tag = "MainCamera";
        camera.gameObject.AddComponent<AudioListener>();
        var light = new GameObject("Key light").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.05f; light.color = new Color(1, .97f, .89f);
        light.shadows = LightShadows.Soft; light.shadowStrength = .25f; light.shadowBias = .03f;
        light.transform.rotation = Quaternion.Euler(42, -145, 0);
        var fill = new GameObject("Soft front fill").AddComponent<Light>();
        fill.type = LightType.Directional; fill.intensity = .45f; fill.color = new Color(.86f, .94f, 1);
        fill.transform.rotation = Quaternion.Euler(25, 20, 0);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.79f, .85f, .85f);
        RenderSettings.ambientEquatorColor = new Color(.72f, .77f, .71f);
        RenderSettings.ambientGroundColor = new Color(.55f, .49f, .39f);

        var ui = new GameObject("Rehab session");
        var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(Root + "/RehabPanel.asset");
        if (!panel) { panel = ScriptableObject.CreateInstance<PanelSettings>(); AssetDatabase.CreateAsset(panel, Root + "/RehabPanel.asset"); }
        panel.scaleMode = PanelScaleMode.ScaleWithScreenSize; panel.referenceResolution = new Vector2Int(1600, 900);
        panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; panel.match = .5f; EditorUtility.SetDirty(panel);
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

}

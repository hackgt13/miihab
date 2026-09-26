using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Kinesthetic.Golf;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.UIElements;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

// The headset scene is a render-only copy of AdaptiveGolf: the Mac stays the only golf authority and streams
// its state; the Quest adds local head tracking from the patient's seat.
public static class QuestSceneSetup
{
    public const string ScenePath = "Assets/Kinesthetic/Golf/QuestGolf.unity";
    public const string MenuScenePath = "Assets/Kinesthetic/Menu/MainMenu.unity";
    const int LocalHeadLayer = 30, MinimapLayer = 31;

    [MenuItem("Kinesthetic/Quest/Create headset scene")]
    public static string Create()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
        var source = EditorSceneManager.OpenScene(AdaptiveGolfSceneSetup.ScenePath, OpenSceneMode.Single);
        EditorSceneManager.SaveScene(source, ScenePath, true);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var game = UnityEngine.Object.FindAnyObjectByType<KinestheticGolf>();
        game.enabled = false;                                         // no sensors, physics or rules on the headset
        var doc = game.GetComponent<UIDocument>(); if (doc) doc.enabled = false;
        if (game.spectator) game.spectator.gameObject.SetActive(false);
        game.ball.isKinematic = true;

        // The patient's own head would sit in front of the headset camera.
        foreach (var r in game.rigs[0].avatar.GetComponentsInChildren<Renderer>(true))
            if (r.name != "MiiBody") r.gameObject.layer = LocalHeadLayer;

        var anchor = new GameObject("Patient seat anchor").transform;
        anchor.SetPositionAndRotation(game.players[0].position, game.players[0].rotation);
        var originGo = new GameObject("XR Origin"); originGo.transform.SetParent(anchor, false);
        var offset = new GameObject("Camera Offset"); offset.transform.SetParent(originGo.transform, false);
        var camGo = new GameObject("Headset camera"); camGo.transform.SetParent(offset.transform, false); camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = .05f; cam.farClipPlane = 800;
        cam.cullingMask = ~((1 << LocalHeadLayer) | (1 << MinimapLayer));
        cam.clearFlags = CameraClearFlags.Skybox;
        camGo.AddComponent<AudioListener>();
        var pose = camGo.AddComponent<TrackedPoseDriver>();
        pose.positionInput = new InputActionProperty(new InputAction("Head position", binding: "<XRHMD>/centerEyePosition"));
        pose.rotationInput = new InputActionProperty(new InputAction("Head rotation", binding: "<XRHMD>/centerEyeRotation"));
        var origin = originGo.AddComponent<XROrigin>();
        origin.Origin = originGo; origin.CameraFloorOffsetObject = offset; origin.Camera = cam;
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

        var hudGo = new GameObject("Headset HUD");
        var hud = hudGo.AddComponent<TextMesh>();
        hud.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        hudGo.GetComponent<MeshRenderer>().sharedMaterial = hud.font.material;
        hud.fontSize = 96; hud.characterSize = .025f; hud.anchor = TextAnchor.MiddleCenter; hud.alignment = TextAlignment.Center;
        hud.color = Color.white; hud.text = "Connecting to the Kinesthetic Mac…";

        var client = new GameObject("Headset game-state client").AddComponent<GolfStateClient>();
        client.game = game; client.patientAnchor = anchor; client.hud = hud;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        SetBootScene(ScenePath);
        WriteHostConfig();
        ConfigureAndroidXR();
        return "Created " + ScenePath + " and configured OpenXR for Android.";
    }

    // Bakes the Mac's LAN address and a pairing token into the headset build. The relay requires the same token.
    [MenuItem("Kinesthetic/Quest/Write host config")]
    public static string WriteHostConfig()
    {
        var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
        var tokenPath = Path.Combine(root, "local-data/pair-token.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(tokenPath));
        if (!File.Exists(tokenPath)) File.WriteAllText(tokenPath, Guid.NewGuid().ToString("N"));
        var token = File.ReadAllText(tokenPath).Trim();
        var ip = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))?.ToString() ?? "127.0.0.1";
        const string directory = "Assets/Kinesthetic/Golf/Resources/Golf";
        Directory.CreateDirectory(directory);
        AssetDatabase.Refresh();   // CreateAsset fails on a folder the database has not imported yet
        var assetPath = directory + "/QuestHostConfig.asset";
        var config = AssetDatabase.LoadAssetAtPath<QuestHostConfig>(assetPath);
        if (!config) { config = ScriptableObject.CreateInstance<QuestHostConfig>(); AssetDatabase.CreateAsset(config, assetPath); }
        config.host = ip; config.port = 8767; config.token = token;
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return $"Headset will connect to {ip}:8767";
    }

    // A build boots scene index 0. MainMenu is screen-space UI Toolkit driven by keyboard and pointer,
    // so in the headset it renders wrong and cannot be operated: put QuestGolf first before a Quest build.
    [MenuItem("Kinesthetic/Quest/Boot headset scene (before a Quest build)")]
    public static string BootHeadsetScene() => SetBootScene(ScenePath);

    [MenuItem("Kinesthetic/Quest/Boot menu scene (back to Mac)")]
    public static string BootMenuScene() => SetBootScene(MenuScenePath);

    static string SetBootScene(string path)
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        return "Build index 0 is now " + path;
    }

    public static void ConfigureAndroidXR()
    {
        var perTarget = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
        if (!perTarget)
        {
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget buildTargets);
            if (!buildTargets)
            {
                buildTargets = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                Directory.CreateDirectory("Assets/XR");
                AssetDatabase.CreateAsset(buildTargets, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, buildTargets, true);
            }
            if (!buildTargets.HasSettingsForBuildTarget(BuildTargetGroup.Android)) buildTargets.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
            if (!buildTargets.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android)) buildTargets.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            perTarget = buildTargets.SettingsForBuildTarget(BuildTargetGroup.Android);
        }
        perTarget.InitManagerOnStart = true;
        XRPackageMetadataStore.AssignLoader(perTarget.AssignedSettings, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Android);
        var openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        foreach (var feature in openxr.GetFeatures())
        {
            var type = feature.GetType();
            if (type.Name == "MetaQuestFeature" || type.Name == "OculusTouchControllerProfile" ||
                ((type.Namespace ?? "").Contains("Meta") && type.Name.Contains("Session")))
                feature.enabled = true;
        }
        EditorUtility.SetDirty(openxr);
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)32;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.kinesthetic.questgolf");
        PlayerSettings.colorSpace = ColorSpace.Linear;
        AssetDatabase.SaveAssets();
    }
}

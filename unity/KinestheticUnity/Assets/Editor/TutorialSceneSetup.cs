using System;
using Kinesthetic;
using Kinesthetic.Tutorial;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The intro: Alex greets the patient and shows the arm lift before the first session, in the movement studio. The
// scene carries the studio room, its light, a camera and the sequencer; the sequencer seats the coach.
public static class TutorialSceneSetup
{
    public const string ScenePath = "Assets/Kinesthetic/Tutorial/Tutorial.unity";
    const string StudioPrefab = "Assets/Kinesthetic/Rehab/Studio/RehabStudio.prefab";

    [MenuItem("Kinesthetic/Tutorial/Create intro scene")]
    public static string Create()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // The intro happens in the movement studio the patient is about to use, lit as that scene is. The room is
        // the studio's own prefab, placed rather than rebuilt, so the rehab scene's copy is never rewritten from here.
        var room = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(StudioPrefab));
        room.name = "Movement studio";
        var light = new GameObject("Key light").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.05f; light.color = Palette.Sand00;
        light.shadows = LightShadows.Soft; light.shadowStrength = .25f; light.shadowBias = .03f;
        light.transform.rotation = Quaternion.Euler(42, -145, 0);
        var fill = new GameObject("Soft front fill").AddComponent<Light>();
        fill.type = LightType.Directional; fill.intensity = .45f; fill.color = Palette.Cerulean10;
        fill.transform.rotation = Quaternion.Euler(25, 20, 0);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Palette.Cerulean20;
        RenderSettings.ambientEquatorColor = Palette.Sand30;
        RenderSettings.ambientGroundColor = Palette.Sand40;

        var camera = new GameObject("Main Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.gameObject.AddComponent<AudioListener>();

        new GameObject("TutorialManager").AddComponent<TutorialSequencer>();

        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
        MainMenuSetup.ConfigureMacBuildScenes();
        return "Created " + ScenePath;
    }
}

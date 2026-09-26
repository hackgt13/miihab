using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;

// One headset app with every activity: golf, bowling and the rehab studio. Regenerates both Quest scenes from the current Mac scenes, bakes this Mac's
// address and pairing token, then builds. QuestActivityFollower switches scenes to whatever the Mac is hosting.
public static class QuestCombinedBuild
{
    public const string Output = "../../local-data/builds/RehabMiiQuest.apk";

    [MenuItem("Kinesthetic/Quest/Build combined headset app (golf + bowling + rehab)")]
    public static string Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
        var macScenes = EditorBuildSettings.scenes;   // scene setup reorders these for Quest; the Mac keeps the menu first
        QuestSceneSetup.Create();            // QuestGolf from the current AdaptiveGolf, host config, OpenXR
        BowlingSceneSetup.CreateQuest();     // QuestBowling from the current bowling setup
        QuestRehabSetup.Create();            // QuestRehab: the studio first person, mirror left, coach right
        QuestSceneSetup.WriteHostConfig();
        QuestSceneSetup.ConfigureAndroidXR();
        var scenes = new[] { QuestSceneSetup.ScenePath, BowlingSceneSetup.QuestScenePath, QuestRehabSetup.ScenePath };   // index 0 boots
        var output = Path.GetFullPath(Output);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        // The Mac build settings (menu first) and app identifier stay as they are; only this build differs.
        var target = UnityEditor.Build.NamedBuildTarget.Android;
        string identifier = PlayerSettings.GetApplicationIdentifier(target);
        try
        {
            PlayerSettings.SetApplicationIdentifier(target, "com.kinesthetic.rehabmii");
            PlayerSettings.Android.forceInternetPermission = true;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, locationPathName = output, target = BuildTarget.Android });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Combined Quest build {report.summary.result}: {report.summary.totalErrors} errors.");
            return $"{output} ({report.summary.totalSize / 1048576} MB, {report.summary.totalTime.TotalSeconds:0}s)";
        }
        finally
        {
            PlayerSettings.SetApplicationIdentifier(target, identifier);
            EditorBuildSettings.scenes = macScenes;
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(AdaptiveGolfSceneSetup.ScenePath);
        }
    }
}

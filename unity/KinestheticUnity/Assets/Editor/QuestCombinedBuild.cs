using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;

// One headset app with the plaza and every activity: golf, bowling and the rehab studio. Regenerates both Quest scenes from the current Mac scenes, bakes this Mac's
// address and pairing token, then builds. QuestActivityFollower switches scenes to whatever the Mac is hosting.
public static class QuestCombinedBuild
{
    public const string Output = "../../local-data/builds/RehabMiiQuest.apk";
    /// The one headset app. The golf-only and bowling-only APKs from when each game was the whole product had their
    /// own identifiers, so a headset could carry them beside this one and boot straight into a game from the library.
    public const string Identifier = "com.kinesthetic.rehabmii";

    [MenuItem("Kinesthetic/Quest/Build combined headset app (golf + bowling + rehab)")]
    public static string Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
        var macScenes = EditorBuildSettings.scenes;   // scene setup reorders these for Quest; the Mac keeps the menu first
        QuestSceneSetup.Create();            // QuestGolf from the current AdaptiveGolf, host config, OpenXR
        BowlingSceneSetup.CreateQuest();     // QuestBowling from the current bowling setup
        QuestRehabSetup.Create();            // QuestRehab: the studio first person, mirror left, coach right
        QuestMenuSetup.Create();             // QuestMenu: the plaza, its doorways, and the menu's boards as replicas
        QuestSceneSetup.WriteHostConfig();
        QuestSceneSetup.ConfigureAndroidXR();
        // The plaza boots: the headset waits there, on the board, for the Mac; every activity is entered through a door.
        var scenes = new[] { QuestMenuSetup.ScenePath, QuestSceneSetup.ScenePath, BowlingSceneSetup.QuestScenePath, QuestRehabSetup.ScenePath };
        var output = Path.GetFullPath(Output);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        // The Mac build settings (menu first) stay as they are; only this build's scene list differs.
        try
        {
            PlayerSettings.Android.forceInternetPermission = true;
            // LZ4HC: Unity compresses its data files itself and the APK stores them as-is. The default (zip deflate) makes
            // Unity split every data file into 1 MB chunks that the player re-inflates on each random read; on a Quest 2
            // that pinned Loading.AsyncRead for minutes on the first scene and the app never got past the loading screen.
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, locationPathName = output, target = BuildTarget.Android, options = BuildOptions.CompressWithLz4HC });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Combined Quest build {report.summary.result}: {report.summary.totalErrors} errors.");
            return $"{output} ({report.summary.totalSize / 1048576} MB, {report.summary.totalTime.TotalSeconds:0}s)";
        }
        finally
        {
            EditorBuildSettings.scenes = macScenes;
            AssetDatabase.SaveAssets();
            // Leave the editor where the app starts, so Play after a build begins in the menu, not in golf.
            EditorSceneManager.OpenScene(MainMenuSetup.ScenePath);
        }
    }
}

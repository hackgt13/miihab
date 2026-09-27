using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kinesthetic.Activities;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;

// One headset app with the plaza and every activity the menu can reach: golf, bowling, the rehab studio, the intro
// and the therapist visit. Regenerates every Quest scene from the current Mac scenes, bakes this Mac's address and
// pairing token, then builds. QuestActivityFollower switches scenes to whatever the Mac is hosting.
public static class QuestCombinedBuild
{
    public const string Output = "../../local-data/builds/RehabMiiQuest.apk";
    /// The one headset app. The golf-only and bowling-only APKs from when each game was the whole product had their
    /// own identifiers, so a headset could carry them beside this one and boot straight into a game from the library.
    public const string Identifier = "com.kinesthetic.rehabmii";

    /// Every headset scene, plaza first: the plaza boots, the headset waits there for the Mac, and every activity is
    /// entered through a door. This list and the catalog's `questScene` entries must agree (VerifyCoverage).
    public static string[] Scenes => new[]
    {
        QuestMenuSetup.ScenePath, QuestSceneSetup.ScenePath, BowlingSceneSetup.QuestScenePath, QuestRehabSetup.ScenePath,
        QuestTutorialSetup.ScenePath, QuestVisitSetup.ScenePath,
    };

    [MenuItem("Kinesthetic/Quest/Build combined headset app (all activities)")]
    public static string Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
        var macScenes = EditorBuildSettings.scenes;   // scene setup reorders these for Quest; the Mac keeps the menu first
        QuestSceneSetup.Create();            // QuestGolf from the current AdaptiveGolf, host config, OpenXR
        BowlingSceneSetup.CreateQuest();     // QuestBowling from the current bowling setup
        QuestRehabSetup.Create();            // QuestRehab: the studio first person, mirror left, coach right
        QuestTutorialSetup.Create();         // QuestTutorial: the studio first person, Alex posed by the Mac
        QuestVisitSetup.Create();            // QuestVisit: the clinic first person, whiteboard and Alex as on the Mac
        QuestMenuSetup.Create();             // QuestMenu: the plaza, its doorways, and the menu's boards as replicas
        QuestSceneSetup.WriteHostConfig();
        QuestSceneSetup.ConfigureAndroidXR();
        RemoteUiRegistries.Refresh();        // every stylesheet and image a board can name, or the headset resolves none added since the last manual refresh
        var scenes = Scenes;
        VerifyCoverage(scenes);              // nothing the menu can reach is left off the headset
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

    /// Every Mac scene the menu can reach has a headset copy, and that copy is in the build. The therapist visit
    /// and the intro shipped on the Mac for half a day with `questScene: null` and nothing said so: the follower
    /// silently stayed in the plaza. Now the build refuses, naming what is missing.
    [MenuItem("Kinesthetic/Quest/Verify headset scene coverage")]
    public static string VerifyCoverageFromMenu() => VerifyCoverage(Scenes);

    public static string VerifyCoverage(string[] scenes)
    {
        ActivityCatalog.Invalidate();
        var shipped = scenes.ToDictionary(Path.GetFileNameWithoutExtension, p => p);
        var problems = new List<string>();
        foreach (var path in scenes)
            if (!File.Exists(path)) problems.Add($"headset scene missing on disk: {path}");
        // One line per Mac scene, not per activity: the studio's movements all share Rehab.
        foreach (var group in ActivityCatalog.All.Where(a => !string.IsNullOrEmpty(a.Scene)).GroupBy(a => a.Scene))
        {
            var quest = group.Select(a => a.QuestScene).FirstOrDefault(q => !string.IsNullOrEmpty(q));
            var ids = string.Join(", ", group.Select(a => a.Id).Take(3));
            if (quest == null)
                problems.Add($"'{group.Key}' ({ids}) has no questScene in coordinator/activities.json; add a Kinesthetic/Quest setup for it and name it there");
            else if (!shipped.ContainsKey(quest))
                problems.Add($"'{group.Key}' names questScene '{quest}', which is not in QuestCombinedBuild.Scenes");
            foreach (var a in group.Where(a => !string.IsNullOrEmpty(a.QuestScene) && a.QuestScene != quest))
                problems.Add($"'{a.Id}' names questScene '{a.QuestScene}' but '{group.Key}' is otherwise '{quest}'");
        }
        if (problems.Count > 0)
            throw new InvalidOperationException("Headset scene coverage:\n  " + string.Join("\n  ", problems));
        return $"Headset scene coverage: every catalog scene has a copy in the build ({scenes.Length} scenes).";
    }
}

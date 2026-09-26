using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Kinesthetic.Rehab;
using Kinesthetic.UI.Boards;
using Kinesthetic.UI.Remote;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

/// The studio on boards, checked. Regenerates Rehab and QuestRehab and asserts what the Quest needs of
/// them: no screen-space panel anywhere, every board world-space at its station within the seat's
/// comfortable yaw and off the mirror's side, laid out at the station's density, every name RehabSession
/// asks for answered through the BoardSet, and the headset's copies enabled as Replicas with no TextMesh
/// HUD left. Whatever scene was open is reopened afterwards. It refuses to start while the editor is
/// playing or any open scene is unsaved: it regenerates and saves scenes, and must never save anyone
/// else's work on the way.
public static class RehabBoardsVerification
{
    const string SessionSource = "Assets/Kinesthetic/Rehab/RehabSession.cs";

    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    [MenuItem("Kinesthetic/Rehab/Verify boards")]
    public static string Run()
    {
        RehabSceneSetup.RequireIdleEditor("Verify boards");
        var lines = new List<string>();
        int failures = 0;
        void Pass(string m) { lines.Add("PASS: " + m); Debug.Log("PASS: " + m); }
        void Fail(string m) { failures++; lines.Add("FAIL: " + m); Debug.LogError("FAIL: " + m); }

        var before = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            RehabSceneSetup.Create();
            try { Pass(MacScene()); } catch (Exception e) { Fail("Rehab: " + e.Message); }
            QuestRehabSetup.Create();
            try { Pass(QuestScene()); } catch (Exception e) { Fail("QuestRehab: " + e.Message); }
        }
        finally
        {
            // Whatever was open comes back — unless it was an unsaved, untitled scene, which cannot be reopened.
            if (before.Length > 0 && before.All(s => !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(before);
        }
        string summary = failures == 0 ? $"PASS: rehab boards — {lines.Count} checks." : $"FAIL: rehab boards — {failures} of {lines.Count} checks failed.";
        Debug.Log(summary + "\n" + string.Join("\n", lines));
        return summary;
    }

    static string MacScene()
    {
        var seat = UnityEngine.Object.FindAnyObjectByType<SeatRig>();
        Check(seat, "no SeatRig");
        var session = UnityEngine.Object.FindAnyObjectByType<RehabSession>();
        Check(session && session.boards, "RehabSession has no BoardSet");
        var documents = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include);
        foreach (var document in documents)
            Check(document.panelSettings && document.panelSettings.renderMode == PanelRenderMode.WorldSpace,
                  $"'{document.name}' is not a world-space panel");
        Check(documents.Length == RehabSceneSetup.Boards.Length, $"{documents.Length} documents for {RehabSceneSetup.Boards.Length} boards");

        // The mirror's right edge, as the seat sees it: nothing may reach past it. MirrorPanel is added at
        // runtime with its defaults, so its defaults are read off a throwaway component.
        var probe = new GameObject("Mirror probe") { hideFlags = HideFlags.HideAndDontSave };
        var mirror = probe.AddComponent<MirrorPanel>();
        float mirrorYaw = -Mathf.Atan2(mirror.offsetLeft, mirror.offsetForward) * Mathf.Rad2Deg;
        float mirrorDistance = new Vector2(mirror.offsetLeft, mirror.offsetForward).magnitude;
        float mirrorEdge = mirrorYaw + Mathf.Atan2(mirror.width * .5f, mirrorDistance) * Mathf.Rad2Deg;
        UnityEngine.Object.DestroyImmediate(probe);

        var placements = seat.Placements;
        Check(placements.Count == RehabSceneSetup.Boards.Length, "the seat remembers a different number of boards");
        foreach (var expected in RehabSceneSetup.Boards)
        {
            var board = UnityEngine.Object.FindObjectsByType<RemoteBoard>().FirstOrDefault(b => b.id == expected.id);
            Check(board, $"no board '{expected.id}'");
            var document = board.GetComponent<UIDocument>();
            var placement = placements.FirstOrDefault(p => p.board == board.transform);
            Check(placement.board, $"'{expected.id}' was not stood by the seat");
            Check(placement.station.name == expected.station.name, $"'{expected.id}' remembers station {placement.station.name}, not {expected.station.name}");

            var pose = seat.PoseAt(expected.station);
            Check(Vector3.Distance(board.transform.position, pose.position) < .001f, $"'{expected.id}' is {Vector3.Distance(board.transform.position, pose.position):0.000} m from its station");
            Check(Quaternion.Angle(board.transform.rotation, pose.rotation) < .1f, $"'{expected.id}' is turned {Quaternion.Angle(board.transform.rotation, pose.rotation):0.0}° off its station");
            seat.Bearing(board.transform.position, out float yaw, out float pitch, out float distance);
            Check(Mathf.Abs(yaw - expected.station.yawDegrees) < .1f && Mathf.Abs(pitch - expected.station.pitchDegrees) < .1f && Mathf.Abs(distance - expected.station.distanceMetres) < .001f,
                  $"'{expected.id}' bears yaw {yaw:0.0}°, pitch {pitch:0.0}°, {distance:0.00} m; wanted {expected.station}");
            Check(Mathf.Abs(yaw) <= Station.MaxYawDegrees, $"'{expected.id}' stands at {yaw:0}°, beyond ±{Station.MaxYawDegrees}° of the facing");

            var size = BoardBuilder.SizeMetres(document);
            float halfWidth = Mathf.Atan2(size.x * .5f, distance) * Mathf.Rad2Deg;
            Check(yaw >= -.01f, $"'{expected.id}' stands left of centre at {yaw:0}°, the mirror's side");
            Check(yaw - halfWidth > mirrorEdge, $"'{expected.id}' reaches {yaw - halfWidth:0.0}° left, past the mirror's edge at {mirrorEdge:0.0}°");

            float wantedDensity = expected.station.PixelsPerMetre, density = BoardBuilder.PixelsPerMetre(document);
            Check(Mathf.Abs(density - wantedDensity) < .01f, $"'{expected.id}' lays out at {density:0.0} px/m, not {wantedDensity:0.0} (1000 / {distance:0.0} m)");
            Check((size - expected.size).magnitude < .001f, $"'{expected.id}' comes out {size.x:0.00} x {size.y:0.00} m, not {expected.size.x:0.00} x {expected.size.y:0.00}");
            Check(document.worldSpaceSizeMode == WorldSpaceSizeMode.Fixed, $"'{expected.id}' does not lay out at a fixed size");
            Check(board.GetComponent<Kinesthetic.Panes.Pane>() && board.GetComponent<Kinesthetic.GazeDwell>() && board.GetComponent<Kinesthetic.Shell.PanePointerInput>(),
                  $"'{expected.id}' is missing its Pane, GazeDwell or PanePointerInput");
            Check(session.boards.Boards.Contains(document), $"'{expected.id}' is not in the session's BoardSet");
        }

        // Every element RehabSession asks for, by name, through the set. Read from the source, so a query
        // added there is a check added here.
        var names = QueriedNames();
        Check(names.Count > 20, $"only {names.Count} queried names found in RehabSession.cs");
        var trees = new List<VisualElement>();
        foreach (var document in session.boards.Boards)
        {
            var tree = document.rootVisualElement?.panel != null && document.rootVisualElement.childCount > 0
                ? document.rootVisualElement : document.visualTreeAsset.Instantiate();
            trees.Add(tree);
        }
        var missing = names.Where(n => trees.All(t => t.Q(n) == null)).ToList();
        Check(missing.Count == 0, "names RehabSession queries that no board answers: " + string.Join(", ", missing));
        var duplicated = names.Where(n => trees.Count(t => t.Q(n) != null) > 1).ToList();
        Check(duplicated.Count == 0, "names on more than one board: " + string.Join(", ", duplicated));

        return $"Rehab: {documents.Length} world-space boards at their stations (yaw 0…{RehabSceneSetup.Boards.Max(b => b.station.yawDegrees):0}°, right of the mirror's edge at {mirrorEdge:0}°), " +
               $"density 1000/distance, {names.Count} queried names resolve through the BoardSet";
    }

    static string QuestScene()
    {
        var session = UnityEngine.Object.FindAnyObjectByType<RehabSession>();
        Check(session && !session.enabled, "RehabSession is still enabled on the headset");
        var boards = UnityEngine.Object.FindObjectsByType<RemoteBoard>(FindObjectsInactive.Include);
        Check(boards.Length == RehabSceneSetup.Boards.Length, $"{boards.Length} boards on the headset for {RehabSceneSetup.Boards.Length}");
        foreach (var board in boards)
        {
            var document = board.GetComponent<UIDocument>();
            Check(board.role == RemoteBoard.Role.Replica, $"'{board.id}' is {board.role}, not Replica");
            Check(document && document.enabled && board.gameObject.activeInHierarchy, $"'{board.id}' document is disabled on the headset");
            Check(document.panelSettings.renderMode == PanelRenderMode.WorldSpace, $"'{board.id}' is not world-space");
            if (board.id == "rehab.dock") Check(document.visualTreeAsset && document.visualTreeAsset.name == "Waiting", "the dock does not carry the waiting card");
            else Check(document.visualTreeAsset == null, $"'{board.id}' still carries the Mac's tree as its own");
        }
        // The studio prefab's wall signs are TextMeshes too; a HUD would be a loose one.
        var loose = UnityEngine.Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include)
            .Where(t => !PrefabUtility.IsPartOfPrefabInstance(t.gameObject)).Select(t => t.name).ToList();
        Check(loose.Count == 0, "a TextMesh HUD remains: " + string.Join(", ", loose));
        Check(UnityEngine.Object.FindAnyObjectByType<RemoteUiClient>(), "no RemoteUiClient in the headset scene");
        Check(UnityEngine.Object.FindAnyObjectByType<RehabStateClient>(), "no RehabStateClient in the headset scene");
        var origin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
        Check(origin && origin.transform.parent && origin.transform.parent.GetComponent<SeatRig>(), "the XR origin is not parked on the seat");
        Check(origin.Camera && origin.Camera.CompareTag("MainCamera"), "the headset camera is not the main camera");
        foreach (var cam in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            Check(cam == origin.Camera || !cam.gameObject.activeInHierarchy, $"camera '{cam.name}' is still active beside the headset's");
        return $"QuestRehab: {boards.Length} boards enabled as Replicas on the seat's XR origin, dock waiting for the Mac, no TextMesh HUD";
    }

    /// Every `Q<T>("name")` and `Q("name")` in RehabSession.cs.
    static List<string> QueriedNames()
    {
        string source = File.ReadAllText(Path.GetFullPath(SessionSource));
        var names = new HashSet<string>();
        foreach (Match m in Regex.Matches(source, @"\.Q(?:<[\w.]+>)?\(\s*""([^""]+)""")) names.Add(m.Groups[1].Value);
        return names.OrderBy(n => n).ToList();
    }
}

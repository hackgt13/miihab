using System;
using System.Collections;
using Kinesthetic.Activities;
using Kinesthetic.Golf;
using Kinesthetic.Menu;
using Kinesthetic.Shell;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// The walk through a doorway, without leaving the scene: every catalog venue has a door in the plaza, the
/// headset scene mapping holds, and a stand-in walker carried through the studio door ends up inside the
/// vestibule with the door open, the view fully covered and the tunnel having closed in on the way — in the
/// time the sequence promises. Then the view comes back. The real scene swap on a covered frame is what
/// MenuIntegrationVerification's bowling round trip exercises, through ActivityNavigation.Load.
public static class PlazaPortalVerification
{
    static IEnumerator routine;
    static double deadline;
    public static string Result { get; private set; } = "Not run";

    [MenuItem("Kinesthetic/Menu/Verify plaza doorways (Play mode)")]
    public static string Run()
    {
        if (!EditorApplication.isPlaying || SceneManager.GetActiveScene().name != ActivityNavigation.MenuScene)
            throw new InvalidOperationException("Run from MainMenu in Play mode.");
        if (routine != null) throw new InvalidOperationException("Verification is already running.");
        Result = "Running"; deadline = EditorApplication.timeSinceStartup + 30;
        routine = Verify(); EditorApplication.update += Step;
        return Result;
    }

    static void Step()
    {
        try
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline)
                throw new Exception("Verification interrupted or timed out.");
            if (routine.MoveNext()) { EditorApplication.QueuePlayerLoopUpdate(); return; }
            Result = "PASS: a door per venue, headset scene mapping, and the walk through the studio door covers the view inside the vestibule.";
        }
        catch (Exception e) { Result = "FAIL: " + e; }
        (routine as IDisposable)?.Dispose(); routine = null;
        EditorApplication.update -= Step;
        Debug.Log(Result);
    }

    static void Check(bool condition, string message) { if (!condition) throw new Exception("Plaza doorways: " + message); }

    static IEnumerator Wrap(IEnumerator inner, Action done) { yield return inner; done(); }

    static IEnumerator Verify()
    {
        Check(ActivityCatalog.All.Length > 0, "the catalog is empty");
        foreach (var entry in ActivityCatalog.All)
        {
            var door = Portal.Find(entry.Venue);
            Check(door != null, $"no doorway for venue '{entry.Venue}'");
            Check(door.hinge && door.stop, $"the '{entry.Venue}' doorway has no leaf or no stop");
            Check(Vector3.Distance(door.Stop, door.transform.position) < 1.5f, $"the '{entry.Venue}' stop is not inside its vestibule");
            var vista = door.transform.Find("Vista");
            Check(vista != null && vista.GetComponent<MeshRenderer>(), $"the '{entry.Venue}' vestibule has no back wall to picture");
            // A venue whose scene exists has a still of it; one still being built keeps the plain shade.
            if (Application.CanStreamedLevelBeLoaded(entry.Scene))
                Check(vista.GetComponent<MeshRenderer>().sharedMaterial.mainTexture != null, $"the '{entry.Venue}' picture is blank; run Kinesthetic/Menu/Capture venue vistas");
        }
        Check(QuestActivityFollower.HeadsetSceneFor("MainMenu") == QuestActivityFollower.MenuScene, "the menu maps to the wrong headset scene");
        Check(QuestActivityFollower.HeadsetSceneFor("Rehab") == QuestActivityFollower.RehabScene, "Rehab maps to the wrong headset scene");
        Check(QuestActivityFollower.HeadsetSceneFor("Nowhere") == null, "an unknown scene mapped to something");

        var portal = Portal.Find("studio");
        var fade = HeadFade.Ensure();
        var cam = Camera.main;
        Check(cam != null, "no main camera");
        var walker = new GameObject("Verification walker").transform;
        walker.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);
        var start = walker.position;
        yield return null;
        Check(fade.Attached == cam, "the fade did not attach to the main camera");

        // Wall-clock, not a sum of frame deltas: this routine is stepped from EditorApplication.update, which
        // can tick more than once per player frame, so summing Time.unscaledDeltaTime here overcounts.
        bool finished = false; float maxTunnel = 0; float began = Time.realtimeSinceStartup;
        fade.StartCoroutine(Wrap(PlazaApproach.Enter(portal, walker, fade, turnToward: true), () => finished = true));
        while (!finished)
        {
            maxTunnel = Mathf.Max(maxTunnel, fade.Tunnel);
            yield return null;
        }
        float elapsed = Time.realtimeSinceStartup - began;
        float expected = PlazaApproach.BeatSeconds + PlazaApproach.DashSeconds + PlazaApproach.HoldSeconds;
        Check(elapsed > expected * .8f && elapsed < expected + 1.5f, $"the walk took {elapsed:0.00}s, expected about {expected:0.00}s");
        Check(Mathf.Approximately(fade.Cover, 1), $"the view is not covered after the walk (cover {fade.Cover:0.00})");
        Check(maxTunnel > PlazaApproach.TunnelDepth * .8f, $"the tunnel never closed in (max {maxTunnel:0.00})");
        Check(Mathf.Approximately(fade.Tunnel, 0), "the tunnel was left open under full cover");
        var stop = portal.Stop; var at = walker.position;
        Check(Mathf.Abs(at.x - stop.x) < .05f && Mathf.Abs(at.z - stop.z) < .05f, "the walker did not stop inside the vestibule");
        Check(Mathf.Approximately(at.y, start.y), "the walker changed height");
        Check(portal.IsOpen, "the door is not open");
        var toDoor = stop - start; toDoor.y = 0;
        Check(Vector3.Angle(walker.forward, toDoor) < 2f, "the flat view did not turn to face the door");

        finished = false;
        fade.StartCoroutine(Wrap(PlazaApproach.Arrive(fade), () => finished = true));
        while (!finished) yield return null;
        Check(Mathf.Approximately(fade.Cover, 0), "the view did not come back");
        yield return null;
        Check(!fade.GetComponent<MeshRenderer>().enabled, "the fade quad is still drawing at zero cover");

        portal.Close();
        while (!portal.IsClosed) yield return null;
        UnityEngine.Object.Destroy(walker.gameObject);
    }
}

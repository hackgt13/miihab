using System;
using System.Linq;
using Kinesthetic;
using Kinesthetic.Activities;
using Kinesthetic.UI.Boards;
using Kinesthetic.Visit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

// Builds the therapist visit: the studio room, a whiteboard on an easel ahead-left, and Alex standing beside
// it, ahead-right, with a speech balloon over their head. The patient's eyes are the camera; nothing is
// measured here, so there is no patient body, only the seat the boards stand around.
public static class TherapistVisitSetup
{
    const string Root = "Assets/Kinesthetic/Visit";
    public const string ScenePath = Root + "/TherapistVisit.unity";
    public const string ActivityId = "therapist.visit";

    /// Where things stand, from the patient's eyes. The whiteboard and the therapist split the view either
    /// side of straight ahead, both well inside the ±30° a seated patient can see without turning. The
    /// balloon has no fixed station: it is worked out from where the therapist's head is (SpeechStation).
    public static readonly Station Whiteboard = new("Whiteboard", -14, 7, 2.8f);
    public static readonly Vector2 WhiteboardSize = new(1.8f, 1.3f), SpeechSize = new(1f, .5f), DockSize = new(.9f, .42f);
    /// Where the balloon's tail tip sits against the top of the therapist's renderer bounds, metres. Negative
    /// because the bounds run a few centimetres above the hair that is actually drawn; at this value the tip
    /// just meets the hair in the patient's view.
    const float HeadClearance = -.04f;
    public const float TherapistYaw = 16, TherapistDistance = 2.3f;

    [MenuItem("Kinesthetic/Visit/Create therapist visit scene")]
    public static string Create()
    {
        RehabSceneSetup.RequireIdleEditor("Create therapist visit scene");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var studio = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Kinesthetic/Rehab/Studio/RehabStudio.prefab");
        if (studio) PrefabUtility.InstantiatePrefab(studio); else RehabStudioBuilder.Build();

        // The seat, a little back from the studio's mat so the board and the therapist have room in front.
        var seatGo = new GameObject("Patient seat", typeof(SeatRig), typeof(BoardSet));
        seatGo.transform.SetPositionAndRotation(new Vector3(0, 0, -.6f), Quaternion.identity);
        var seat = seatGo.GetComponent<SeatRig>();
        var boards = seatGo.GetComponent<BoardSet>();

        var board = BuildWhiteboard(seat);
        var therapist = BuildTherapist(seat);
        boards.Adopt(BoardBuilder.Build(seat, "Whiteboard board", "visit.whiteboard", Root + "/VisitWhiteboard.uxml", Whiteboard, WhiteboardSize));
        boards.Adopt(BoardBuilder.Build(seat, "Speech board", "visit.speech", Root + "/VisitSpeech.uxml", SpeechStation(seat, therapist), SpeechSize));
        boards.Adopt(BoardBuilder.Build(seat, "Dock board", "visit.dock", Root + "/VisitDock.uxml", Stations.Dock, DockSize));

        var camera = new GameObject("Patient eyes camera").AddComponent<Camera>();
        camera.transform.SetPositionAndRotation(seat.EyePoint, seat.Facing * Quaternion.Euler(-4, 4, 0));
        camera.fieldOfView = 62; camera.nearClipPlane = .05f; camera.farClipPlane = 50;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Palette.Cerulean10;
        camera.tag = "MainCamera";
        camera.gameObject.AddComponent<AudioListener>();

        var light = new GameObject("Key light").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.05f; light.color = Palette.Sand10;
        light.shadows = LightShadows.Soft; light.shadowStrength = .38f; light.shadowBias = .03f;
        light.transform.rotation = Quaternion.Euler(42, -145, 0);
        var fill = new GameObject("Soft front fill").AddComponent<Light>();
        fill.type = LightType.Directional; fill.intensity = .6f; fill.color = Palette.Cerulean10;
        fill.transform.rotation = Quaternion.Euler(25, 20, 0);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Palette.Sand10 * .7f;
        RenderSettings.ambientEquatorColor = Palette.Sand30 * .6f;
        RenderSettings.ambientGroundColor = Palette.Slate70 * .6f;

        var visit = new GameObject("Therapist visit").AddComponent<TherapistVisit>();
        visit.boards = boards; visit.therapist = therapist; visit.whiteboard = board; visit.seat = seat;

        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
        var scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Any(s => s.path == ScenePath)) { scenes.Add(new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = scenes.ToArray(); }
        AssetDatabase.SaveAssets();
        return "Therapist visit scene created at " + ScenePath;
    }

    /// A whiteboard on an easel, its face just behind where the whiteboard board's panel stands, so the
    /// writing sits on the white. The panel is transparent; this is the board you see.
    static Transform BuildWhiteboard(SeatRig seat)
    {
        var pose = seat.PoseAt(Whiteboard);
        var group = new GameObject("Whiteboard").transform;
        group.SetPositionAndRotation(pose.position, pose.rotation);
        var white = Mat("Whiteboard face", Palette.Sand00, .65f);
        var metal = Mat("Whiteboard frame", Palette.Slate30, .5f);
        var ink = Mat("Whiteboard marker", Palette.Prussian60, .3f);
        float w = WhiteboardSize.x + .12f, h = WhiteboardSize.y + .12f, rim = .035f;
        // The panel's front faces away from the viewer along +Z, so behind it is +Z.
        Part(group, "Board face", PrimitiveType.Cube, new(0, 0, .02f), new(w, h, .02f), white);
        Part(group, "Frame top", PrimitiveType.Cube, new(0, h / 2, .02f), new(w + rim, rim, .04f), metal);
        Part(group, "Frame bottom", PrimitiveType.Cube, new(0, -h / 2, .02f), new(w + rim, rim, .04f), metal);
        Part(group, "Frame left", PrimitiveType.Cube, new(-w / 2, 0, .02f), new(rim, h + rim, .04f), metal);
        Part(group, "Frame right", PrimitiveType.Cube, new(w / 2, 0, .02f), new(rim, h + rim, .04f), metal);
        Part(group, "Marker tray", PrimitiveType.Cube, new(0, -h / 2 - .02f, -.03f), new(w * .7f, .02f, .08f), metal);
        for (int i = 0; i < 2; i++)
        {
            var marker = Part(group, "Marker " + i, PrimitiveType.Cylinder, new(-.12f + i * .1f, -h / 2 + .003f, -.04f), new(.022f, .06f, .022f), ink);
            marker.transform.localRotation = Quaternion.Euler(0, 0, 90);
        }
        // Easel legs: straight down from behind each side of the frame to the floor. They stand in their own
        // level group, since a non-uniform scale under the tilted board would shear them.
        var easel = new GameObject("Easel").transform; easel.SetParent(group, true);
        easel.SetPositionAndRotation(new Vector3(pose.position.x, 0, pose.position.z), seat.Facing);
        foreach (var side in new[] { -1, 1 })
        {
            var top = group.TransformPoint(new Vector3(side * (w / 2 - .08f), h / 2, .06f));
            var foot = easel.InverseTransformPoint(new Vector3(top.x, 0, top.z));
            Part(easel, side < 0 ? "Easel leg left" : "Easel leg right", PrimitiveType.Cylinder, foot + Vector3.up * top.y / 2, new(.03f, top.y / 2, .03f), metal);
            Part(easel, side < 0 ? "Easel foot left" : "Easel foot right", PrimitiveType.Cube, foot + Vector3.up * .015f, new(.06f, .03f, .5f), metal);
        }
        return group;
    }

    /// The balloon's station: the point straight above the therapist's head where the board's centre must be
    /// for its bottom edge — the tail's tip, since the balloon sits on that edge — to clear the head, read
    /// back as a bearing from the seat. So the tail points down at the head from wherever the therapist stands.
    public static Station SpeechStation(SeatRig seat, Transform therapist)
    {
        var renderers = therapist.GetComponentsInChildren<Renderer>();
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        var centre = new Vector3(therapist.position.x, bounds.max.y + HeadClearance + SpeechSize.y / 2, therapist.position.z);
        seat.Bearing(centre, out float yaw, out float pitch, out float distance);
        return new Station("Speech", yaw, pitch, distance);
    }

    /// Alex, standing: the golf friend's standing Mii in the coach's colours, facing the patient.
    static Transform BuildTherapist(SeatRig seat)
    {
        var body = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Kinesthetic/Art/Mii/Friend/StandingFriendMii.glb");
        if (!body) throw new InvalidOperationException("The standing Mii is not imported.");
        var floor = seat.transform.position + seat.Facing * (Quaternion.Euler(0, TherapistYaw, 0) * Vector3.forward * TherapistDistance);
        var slot = new GameObject("Alex · therapist").transform;
        var toSeat = Vector3.ProjectOnPlane(seat.EyePoint - floor, Vector3.up);
        slot.SetPositionAndRotation(floor, Quaternion.LookRotation(toSeat, Vector3.up));
        // The Mii faces its actor's -Z; turning the actor round makes the slot's +Z the way it faces.
        var actor = new GameObject("Body pose").transform; actor.SetParent(slot, false);
        actor.localRotation = Quaternion.Euler(0, 180, 0);
        var avatar = (GameObject)PrefabUtility.InstantiatePrefab(body, actor);
        avatar.transform.localRotation = Quaternion.Euler(0, 180, 0);
        foreach (var a in avatar.GetComponentsInChildren<Animation>()) { a.playAutomatically = false; a.enabled = false; }
        foreach (var a in avatar.GetComponentsInChildren<Animator>()) a.enabled = false;
        var renderers = avatar.GetComponentsInChildren<Renderer>(); var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        avatar.transform.localScale *= 1.75f / bounds.size.y;
        foreach (var r in avatar.GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;
        var rig = actor.gameObject.AddComponent<PoseRig>(); rig.avatar = avatar.transform; rig.seated = false; rig.usePresentationSpace = true;
        rig.Initialize(); rig.Apply(null);
        actor.position += Vector3.up * (.08f - rig.RightAnkle.position.y);
        actor.gameObject.AddComponent<Kinesthetic.Golf.MiiIdleLife>().eyeStyle = Kinesthetic.Golf.MiiIdleLife.EyeStyle.Happy;
        // The coach's colours, so the Alex of the studio and the Alex of the visit are one person.
        var shirt = AssetDatabase.LoadAssetAtPath<Material>("Assets/Kinesthetic/Art/Coach/Alex shirt.mat");
        var trousers = AssetDatabase.LoadAssetAtPath<Material>("Assets/Kinesthetic/Art/Coach/Alex trousers.mat");
        var hair = AssetDatabase.LoadAssetAtPath<Material>("Assets/Kinesthetic/Art/Coach/Alex hair.mat");
        foreach (var renderer in renderers)
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (!materials[i]) continue;
                if (shirt && materials[i].name.Contains("shirt")) materials[i] = shirt;
                else if (trousers && materials[i].name.Contains("pants")) materials[i] = trousers;
                else if (hair && renderer.name == "Mii_hair") materials[i] = hair;
            }
            renderer.sharedMaterials = materials;
        }
        return slot;
    }

    static Material Mat(string name, Color color, float smoothness)
    {
        string folder = Root + "/Materials", path = $"{folder}/{name}.mat";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Root, "Materials");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", color); m.SetFloat("_Smoothness", smoothness); EditorUtility.SetDirty(m); return m;
    }

    static GameObject Part(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = position; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    /// What the scene must hold, read back from the saved file: the catalog loads it, every board is at its
    /// station, the whiteboard's face is behind its panel, and the therapist stands where a seated patient
    /// sees them without turning.
    [MenuItem("Kinesthetic/Visit/Verify therapist visit scene")]
    public static string Verify()
    {
        RehabSceneSetup.RequireIdleEditor("Verify therapist visit scene");
        ActivityCatalog.Invalidate();
        var entry = ActivityCatalog.ById(ActivityId);
        Check(entry != null, $"the catalog has no '{ActivityId}' (run Kinesthetic/Activities/Sync catalog from coordinator)");
        Check(entry.Scene == "TherapistVisit", $"the catalog loads '{entry.Scene}', not TherapistVisit");
        Check(EditorBuildSettings.scenes.Any(s => s.path == ScenePath && s.enabled), "the scene is not in the build settings");
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var seat = UnityEngine.Object.FindAnyObjectByType<SeatRig>();
        var visit = UnityEngine.Object.FindAnyObjectByType<TherapistVisit>();
        Check(seat && visit && visit.boards && visit.therapist && visit.whiteboard, "the visit is missing its seat, boards, therapist or whiteboard");
        Check(visit.boards.Boards.Count == 3, $"expected 3 boards, found {visit.boards.Boards.Count}");
        foreach (var name in new[] { "board-updates", "speech-line", "visit-replay", "visit-skip" })
            Check(visit.boards.Boards.Any(b => b.visualTreeAsset && b.visualTreeAsset.CloneTree().Q(name) != null), $"no board holds '{name}'");
        foreach (var placement in seat.Placements)
        {
            var pose = seat.PoseAt(placement.station);
            Check(Vector3.Distance(placement.board.position, pose.position) < .01f, $"{placement.board.name} is not at {placement.station}");
            Check(Mathf.Abs(placement.station.yawDegrees) <= Station.MaxYawDegrees, $"{placement.station} asks the patient to turn");
        }
        var face = visit.whiteboard.Find("Board face");
        var panel = visit.boards.Boards.First(b => b.name == "Whiteboard board").transform;
        Check(face && Vector3.Dot(face.position - panel.position, panel.forward) > 0, "the whiteboard's face is in front of its writing");
        var speech = visit.boards.Boards.First(b => b.name == "Speech board").transform;
        var flat = new Vector2(speech.position.x - visit.therapist.position.x, speech.position.z - visit.therapist.position.z);
        Check(flat.magnitude < .05f, $"the speech balloon is {flat.magnitude:0.00} m off the therapist's head");
        seat.Bearing(visit.therapist.position + Vector3.up * 1.2f, out float yaw, out _, out float distance);
        Check(Mathf.Abs(yaw) <= Station.MaxYawDegrees && distance < 4, $"the therapist stands at {yaw:0}°, {distance:0.0} m");
        return "Therapist visit scene verified.";
    }

    static void Check(bool ok, string failure) { if (!ok) throw new InvalidOperationException("Therapist visit: " + failure); }
}

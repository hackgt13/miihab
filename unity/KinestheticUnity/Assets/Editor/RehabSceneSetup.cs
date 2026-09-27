using System;
using System.Linq;
using Kinesthetic;
using Kinesthetic.Rehab;
using Kinesthetic.UI.Boards;
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

    /// The studio's boards and where they stand. The mirror has the front-left (MirrorPanel, 36° left), so
    /// nothing stands left of centre: the exercise card takes the Score station's mirror image, above the
    /// live figures at Measure, and the coach sits past both (CoachDemonstrator.SeatPose, 40° right).
    /// Sizes are metres in the room; BoardBuilder turns them into pixels at the station's density, divided by
    /// `magnify`.
    public readonly struct Board
    {
        public readonly string label, id, uxml;
        public readonly Station station;
        public readonly Vector2 size;
        public readonly float magnify;
        public Board(string label, string id, string uxml, Station station, Vector2 size, float magnify = 1) { this.label = label; this.id = id; this.uxml = uxml; this.station = station; this.size = size; this.magnify = magnify; }
    }

    /// How much larger than the station's density the studio's standing boards read. Tried in the headset: at
    /// 1 the dock and crown were too small, so each is twice its size in the room over the same pixels. The
    /// popups — the Focus modal and the briefing sheet — stay at 1: magnified, they filled the view.
    public const float Magnify = 2;

    /// The dock, 5° under the shared station: at twice the size its top-left corner would otherwise stand
    /// over the foot of the mirror (RehabBoardsVerification checks the two rectangles as the seat sees them).
    public static readonly Station Dock = new("Dock", Stations.Dock.yawDegrees, -43, Stations.Dock.distanceMetres);

    public static readonly Board[] Boards =
    {
        new("Dock board",    "rehab.dock",    Root + "/RehabDock.uxml",    Dock,                    new Vector2(.80f, .34f) * Magnify,   Magnify),
        new("Focus board",   "rehab.focus",   Root + "/RehabFocus.uxml",   Stations.Focus,          new Vector2(1.2f, .8f)),
        new("Crown board",   "rehab.crown",   Root + "/RehabCrown.uxml",   Stations.Crown,          new Vector2(.30f, .13f) * Magnify,   Magnify),
        new("Brief board",   "rehab.brief",   Root + "/RehabBrief.uxml",   Stations.Reading,        new Vector2(.216f, .279f)),
    };

    /// Refuse to touch scenes while the editor is playing or any open scene has unsaved work. Saving a
    /// dirty scene on someone's behalf is how another session's half-done menu ends up in a commit; the
    /// setups here replace the open scene, so the person decides what happens to it first.
    public static void RequireIdleEditor(string action)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException($"{action}: stop Play mode first.");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            var open = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if (open.isDirty) throw new InvalidOperationException(
                $"{action}: '{(string.IsNullOrEmpty(open.path) ? open.name : open.path)}' has unsaved changes. Save or discard them first; nothing was touched.");
        }
    }

    [MenuItem("Kinesthetic/Rehab/Create shoulder raise scene")]
    public static string Create()
    {
        RequireIdleEditor("Create shoulder raise scene");
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

        var studio = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Studio/RehabStudio.prefab");
        if (studio) PrefabUtility.InstantiatePrefab(studio); else RehabStudioBuilder.Build();
        slot.position = Vector3.up * .062f;
        // The patient faces into the room — windows, plants — so first person looks at the studio, with the
        // coach ahead-right and the mirror ahead-left, not at the open side of the set.
        slot.rotation = Quaternion.Euler(0, 180, 0);

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
        eyes.sceneOwnsView = true;   // StudioCamera is this scene's eye view; FirstPersonView adds no second one

        var camera = new GameObject("Patient view camera").AddComponent<Camera>();
        // Authored wide, behind the chair. StudioCamera opens it in the patient's eyes and only comes back
        // out here on C, so this pose is the wide shot, not what the scene starts on.
        camera.transform.position = new Vector3(1.35f, 2.55f, -4.34f); camera.transform.LookAt(new Vector3(0, .9f, 1.4f));
        camera.fieldOfView = 46; camera.nearClipPlane = .1f; camera.farClipPlane = 50;
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

        // The seat anchor: the floor point under the patient, facing where they face (the Mii faces its
        // local -Z). Boards stand around it, and QuestRehabSetup parks the headset's XR origin on it, so
        // the Mac's eye camera and a tracked headset see the boards in the same places.
        var forward = Vector3.ProjectOnPlane(rig.transform.TransformDirection(Vector3.back), Vector3.up).normalized;
        var seatGo = new GameObject("Patient seat", typeof(SeatRig), typeof(BoardSet));
        seatGo.transform.SetPositionAndRotation(slot.position, Quaternion.LookRotation(forward, Vector3.up));
        var seat = seatGo.GetComponent<SeatRig>(); seat.eyeHeight = eyes.eyeHeight;
        var boards = seatGo.GetComponent<BoardSet>();
        foreach (var board in Boards)
        {
            var built = BoardBuilder.Build(seat, board.label, board.id, board.uxml, board.station, board.size, board.magnify);
            // The crown rides with the view rather than standing at its station (ViewFollow keeps the station's density).
            if (board.id == "rehab.crown") built.gameObject.AddComponent<ViewFollow>().station = board.station;
            boards.Adopt(built);
        }

        var ui = new GameObject("Rehab session");
        var session = ui.AddComponent<RehabSession>();
        session.rig = rig; session.boards = boards;
        session.targetBand = band; session.armGuide = guide; session.targetOrb = orb.transform; session.liveMarker = marker.transform;

        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
        var scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Any(s => s.path == ScenePath)) { scenes.Add(new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = scenes.ToArray(); }
        AssetDatabase.SaveAssets();
        return "Rehab scene created at " + ScenePath;
    }

}

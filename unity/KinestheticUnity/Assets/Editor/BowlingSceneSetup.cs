using System;
using System.IO;
using System.Linq;
using Kinesthetic;
using Kinesthetic.Bowling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.XR.CoreUtils;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

public static class BowlingSceneSetup
{
    public const string Folder = "Assets/Kinesthetic/Bowling";
    public const string ScenePath = Folder + "/Bowling.unity";
    public const string QuestScenePath = Folder + "/QuestBowling.unity";

    [MenuItem("Kinesthetic/Bowling/Create Mac scene")]
    public static string Create() => Build(false);
    [MenuItem("Kinesthetic/Bowling/Create Quest scene")]
    public static string CreateQuest() => Build(true);

    // The headset is built once, with every activity, by QuestCombinedBuild; a bowling-only APK would boot into
    // the lane instead of the plaza.

    static string Build(bool quest)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode first.");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.78f, .83f, .9f);
        RenderSettings.fog = false;
        var environment = Model("BowlingAlley", "Bowling alley");
        environment.transform.rotation = Quaternion.Euler(0, 180, 0);
        foreach (var renderer in environment.GetComponentsInChildren<Renderer>()) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var sun = new GameObject("Soft ceiling light").AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 1.05f; sun.color = new Color(1, .94f, .83f);
        sun.transform.rotation = Quaternion.Euler(70, -30, 0); sun.shadows = LightShadows.Soft;
        var fill = new GameObject("Alley fill").AddComponent<Light>(); fill.type = LightType.Directional; fill.intensity = .35f;
        fill.transform.rotation = Quaternion.Euler(35, 170, 0);

        var laneMaterial = Physics("Lane", .04f, .04f, .06f);
        var pinMaterial = Physics("Pins", .35f, .25f, .28f);
        var ballMaterial = Physics("Ball", .14f, .1f, .18f);
        ColliderBox("Lane", new Vector3(0, -.1f, 9.6f), new Vector3(1.065f, .2f, 20.6f), laneMaterial);
        ColliderBox("Left gutter", new Vector3(-.68f, -.23f, 9.6f), new Vector3(.295f, .12f, 20.6f), laneMaterial);
        ColliderBox("Right gutter", new Vector3(.68f, -.23f, 9.6f), new Vector3(.295f, .12f, 20.6f), laneMaterial);
        ColliderBox("Left edge", new Vector3(-.86f, .02f, 9.6f), new Vector3(.06f, .5f, 21), laneMaterial);
        ColliderBox("Right edge", new Vector3(.86f, .02f, 9.6f), new Vector3(.06f, .5f, 21), laneMaterial);
        ColliderBox("Backstop", new Vector3(0, .25f, 20.05f), new Vector3(1.8f, 1, .2f), pinMaterial);

        var game = new GameObject(quest ? "Bowling render state" : "Bowling authority").AddComponent<BowlingGame>();
        game.renderOnly = quest; game.startServices = !quest;
        var ball = Model("BowlingBall", "Ball"); ball.transform.rotation = Quaternion.Euler(0, 180, 0);
        ball.transform.position = new Vector3(0, BowlingGame.BallRadius + .005f, .1f);
        var sphere = ball.AddComponent<SphereCollider>(); sphere.radius = BowlingGame.BallRadius; sphere.sharedMaterial = ballMaterial;
        game.ball = Body(ball, 5.5f); game.ball.maxAngularVelocity = 90;
        if (!quest) ball.AddComponent<BowlingImpactAudio>();
        game.pins = new Rigidbody[10]; int index = 0;
        var pinHull = PinHull();
        for (int row = 0; row < 4; row++) for (int col = 0; col <= row; col++)
        {
            var pin = new GameObject("Pin " + (index + 1));
            pin.transform.position = new Vector3((col - row * .5f) * .305f, .004f, BowlingGame.HeadPinZ + row * .264f);
            var visual = Model("BowlingPin", "Pin visual"); visual.transform.SetParent(pin.transform, false); visual.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var hull = pin.AddComponent<MeshCollider>(); hull.sharedMesh = pinHull; hull.convex = true; hull.sharedMaterial = pinMaterial;
            game.pins[index] = Body(pin, 1.5f); game.pins[index].centerOfMass = new Vector3(0, .135f, 0);
            if (!quest) pin.AddComponent<BowlingImpactAudio>();
            index++;
        }
        var aim = new GameObject("Aim guide").AddComponent<LineRenderer>();
        aim.sharedMaterial = Material("Aim", Palette.Cerulean50, true);
        aim.positionCount = 2; aim.startWidth = .025f; aim.endWidth = .007f; aim.numCapVertices = 5; aim.enabled = false; game.aimLine = aim;

        var actor = new GameObject("Mii bowler").AddComponent<BowlingAvatar>();
        var mii = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Kinesthetic/Art/Mii/KinestheticMii.glb"), actor.transform);
        mii.transform.localRotation = Quaternion.identity;
        foreach (var animation in mii.GetComponentsInChildren<Animation>()) { animation.playAutomatically = false; animation.enabled = false; }
        foreach (var animator in mii.GetComponentsInChildren<Animator>()) animator.enabled = false;
        var renderers = mii.GetComponentsInChildren<Renderer>(); var bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        mii.transform.localScale *= 1.75f / bounds.size.y;
        mii.GetComponentsInChildren<Transform>().First(t => t.name == "head").localScale = Vector3.one * .64f;
        foreach (var skin in mii.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.updateWhenOffscreen = true;
        actor.game = game; actor.model = mii.transform; game.avatar = actor;
        actor.Initialize(); actor.Prepare();
        // Center the actual hand release over the lane, and plant the feet on the approach.
        float floor = mii.GetComponentsInChildren<Transform>().First(t => t.name == "foot.R").position.y;
        actor.transform.position += new Vector3(-actor.BallGrip.x, .08f - floor, .1f - actor.BallGrip.z);
        actor.Prepare();

        if (quest) Headset(game);
        else
        {
            var cam = new GameObject("Bowling camera").AddComponent<Camera>(); cam.tag = "MainCamera";
            cam.transform.position = new Vector3(1.15f, 1.85f, -3.6f); cam.transform.LookAt(new Vector3(0, .4f, 5));
            cam.fieldOfView = 52; cam.nearClipPlane = .05f; cam.farClipPlane = 100; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.65f, .8f, .9f);
            cam.gameObject.AddComponent<AudioListener>(); game.spectator = cam;
            game.gameObject.AddComponent<BowlingStatePublisher>();
            var doc = new GameObject("Bowling HUD").AddComponent<UIDocument>();
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Folder + "/Bowling.uxml");
            string panelPath = Folder + "/BowlingPanel.asset";
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelPath);
            if (!panel) { panel = ScriptableObject.CreateInstance<PanelSettings>(); AssetDatabase.CreateAsset(panel, panelPath); }
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize; panel.referenceResolution = new Vector2Int(1600, 900);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; panel.match = .5f;
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Kinesthetic/Golf/GolfPanel.asset")?.themeStyleSheet;
            EditorUtility.SetDirty(panel); doc.panelSettings = panel;
            doc.gameObject.AddComponent<BowlingHud>().game = game;
        }
        string path = quest ? QuestScenePath : ScenePath;
        EditorSceneManager.SaveScene(scene, path); AssetDatabase.SaveAssets();
        return "Created " + path + ". Build settings and menu were not changed.";
    }
    static GameObject Model(string file, string name)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Art/" + file + ".glb");
        if (!asset) throw new InvalidOperationException("Missing bowling export: " + file);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset); go.name = name; return go;
    }
    static Rigidbody Body(GameObject go, float mass)
    {
        var body = go.AddComponent<Rigidbody>(); body.mass = mass; body.isKinematic = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; body.interpolation = RigidbodyInterpolation.Interpolate;
        body.linearDamping = .025f; body.angularDamping = .035f; body.solverIterations = 12; body.solverVelocityIterations = 4;
        return body;
    }
    static Mesh PinHull()
    {
        const string path = Folder + "/PinHull.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!mesh) { mesh = new Mesh { name = "Bowling pin collision hull" }; AssetDatabase.CreateAsset(mesh, path); }
        // Under PhysX's convex polygon limit, with a flat base and rounded shoulders.
        var heights = new[] { 0f, .025f, .095f, .16f, .225f, .29f, .345f, .382f };
        var radii = new[] { .032f, .046f, .059f, .05f, .026f, .024f, .036f, .018f };
        const int sides = 12;
        var vertices = new Vector3[heights.Length * sides]; var triangles = new System.Collections.Generic.List<int>();
        for (int row = 0; row < heights.Length; row++) for (int side = 0; side < sides; side++)
        {
            float angle = side * Mathf.PI * 2 / sides; int a = row * sides + side;
            vertices[a] = new Vector3(Mathf.Cos(angle) * radii[row], heights[row], Mathf.Sin(angle) * radii[row]);
            if (row == heights.Length - 1) continue;
            int b = row * sides + (side + 1) % sides, c = a + sides, d = b + sides;
            triangles.AddRange(new[] { a, c, b, b, c, d });
        }
        for (int side = 1; side < sides - 1; side++)
        {
            triangles.AddRange(new[] { 0, side, side + 1 });
            int top = (heights.Length - 1) * sides; triangles.AddRange(new[] { top, top + side + 1, top + side });
        }
        mesh.Clear(); mesh.vertices = vertices; mesh.triangles = triangles.ToArray(); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh); return mesh;
    }
    static PhysicsMaterial Physics(string name, float stationary, float dynamicFriction, float bounce)
    {
        string path = Folder + "/" + name + ".physicMaterial";
        var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (!mat) { mat = new PhysicsMaterial(name); AssetDatabase.CreateAsset(mat, path); }
        mat.staticFriction = stationary; mat.dynamicFriction = dynamicFriction; mat.bounciness = bounce;
        mat.frictionCombine = PhysicsMaterialCombine.Minimum; mat.bounceCombine = PhysicsMaterialCombine.Average;
        EditorUtility.SetDirty(mat); return mat;
    }
    static Material Material(string name, Color color, bool unlit)
    {
        string path = Folder + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
        mat.color = color; EditorUtility.SetDirty(mat); return mat;
    }
    static void ColliderBox(string name, Vector3 position, Vector3 size, PhysicsMaterial material)
    {
        var box = new GameObject(name + " collider").AddComponent<BoxCollider>(); box.transform.position = position; box.size = size; box.sharedMaterial = material;
    }
    static TextMesh Text(string name, Vector3 position, float size)
    {
        var text = new GameObject(name).AddComponent<TextMesh>(); text.transform.position = position;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
        text.fontSize = 72; text.characterSize = size; text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center;
        text.color = Palette.Ink; return text;
    }
    static void TextPanel(string name, Vector3 position, Vector2 size)
    {
        var rim = GameObject.CreatePrimitive(PrimitiveType.Quad); rim.name = name + " rim";
        UnityEngine.Object.DestroyImmediate(rim.GetComponent<Collider>());
        rim.transform.position = position + Vector3.forward * .025f; rim.transform.localScale = new Vector3(size.x + .045f, size.y + .045f, 1);
        rim.GetComponent<Renderer>().sharedMaterial = Material("HUD edge", Palette.Cerulean50, true);
        var panel = GameObject.CreatePrimitive(PrimitiveType.Quad); panel.name = name + " background";
        UnityEngine.Object.DestroyImmediate(panel.GetComponent<Collider>());
        panel.transform.position = position + Vector3.forward * .018f; panel.transform.localScale = new Vector3(size.x, size.y, 1);
        panel.GetComponent<Renderer>().sharedMaterial = Material("HUD surface", Palette.Sand00, true);
    }
    static void Headset(BowlingGame game)
    {
        // First person: the headset sees from the bowler's own eyes — the floor under the Mii, facing down the
        // lane, at the height of its eyes — with its head hidden from itself, as in golf and rehab.
        var model = game.avatar.model;
        var feet = game.avatar.transform.position;
        var seat = new GameObject("Bowler seat").transform;
        seat.SetPositionAndRotation(new Vector3(feet.x, 0, feet.z), Quaternion.LookRotation(Vector3.forward));
        QuestRigBuilder.HideOwnHead(model);
        QuestRigBuilder.Build(seat, QuestRigBuilder.EyeHeight(model, seat), 100, CameraClearFlags.Skybox, ~(1 << QuestRigBuilder.LocalHeadLayer));
        var hud = Text("Bowling instructions", new Vector3(0, 1.1f, .2f), .012f); hud.text = "Waiting for Bowling on your Mac…";
        TextPanel("Instructions", hud.transform.position, new Vector2(2.3f, .48f));
        var board = Text("Bowling scoreboard", new Vector3(0, 2.8f, 4), .023f); board.text = "REHABMII  /  BOWLING";
        TextPanel("Scoreboard", board.transform.position, new Vector2(3.7f, 1));
        var client = new GameObject("Bowling state client").AddComponent<BowlingStateClient>(); client.game = game; client.hud = hud; client.scoreboard = board;
    }
}

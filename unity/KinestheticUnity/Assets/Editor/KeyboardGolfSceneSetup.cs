using System;
using System.Linq;
using Kinesthetic;
using Kinesthetic.Golf;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class KeyboardGolfSceneSetup
{
    const string Root = "Assets/Kinesthetic/Golf";
    public const string ScenePath = Root + "/KeyboardGolf.unity";

    [MenuItem("Kinesthetic/Golf/Create keyboard golf scene")]
    public static void CreateScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var grass = Material("Rough", new Color(.25f, .42f, .25f));
        var fairway = Material("Fairway", new Color(.39f, .59f, .33f));
        var green = Material("Putting green", new Color(.53f, .72f, .40f));
        var tee = Material("Tee", new Color(.36f, .65f, .40f));
        var sand = Material("Sand", new Color(.82f, .72f, .52f));
        var water = Material("Water", new Color(.15f, .48f, .57f));
        var bark = Material("Tree bark", new Color(.29f, .22f, .14f));
        var leaves = Material("Tree leaves", new Color(.19f, .38f, .19f));
        var white = Material("Ball and flagpole", new Color(.91f, .94f, .91f));
        var flagMat = Material("Flag", new Color(.88f, .31f, .21f));
        var aimMat = Material("Aim arrow", Palette.Sand30);
        var cupMat = Material("Cup", new Color(.08f, .13f, .11f));

        Primitive("Rough course", PrimitiveType.Plane, new Vector3(0, -.025f, 0), new Vector3(12, 1, 18), grass);
        FairwayRibbon(fairway);
        Visual("Teeing ground", PrimitiveType.Cylinder, new Vector3(0, .002f, -38), new Vector3(10, .008f, 10), tee);
        Visual("Putting green", PrimitiveType.Cylinder, new Vector3(0, .005f, 42), new Vector3(18, .008f, 18), green);
        Visual("Left bunker", PrimitiveType.Cylinder, new Vector3(-13, .004f, 23), new Vector3(14, .009f, 9), sand);
        Visual("Right bunker", PrimitiveType.Cylinder, new Vector3(13, .004f, 30), new Vector3(12, .009f, 7), sand);
        Visual("Pond", PrimitiveType.Cylinder, new Vector3(30, -.005f, 10), new Vector3(27, .012f, 32), water);

        var cup = Visual("Cup", PrimitiveType.Cylinder, new Vector3(0, .014f, 42), new Vector3(1.1f, .004f, 1.1f), cupMat).transform;
        var pole = Visual("Flag pole", PrimitiveType.Cylinder, new Vector3(0, 1.57f, 42), new Vector3(.045f, 1.55f, .045f), white).transform;
        var flag = new GameObject("Flag cloth").transform;
        flag.position = new Vector3(0, 2.98f, 42);
        var flagMesh = new Mesh {vertices = new[]{Vector3.zero, new Vector3(1.14f,-.26f,0),new Vector3(0,-.52f,0)}, triangles = new[]{0,1,2}};
        flagMesh.RecalculateNormals(); flag.gameObject.AddComponent<MeshFilter>().sharedMesh = flagMesh;
        flag.gameObject.AddComponent<MeshRenderer>().sharedMaterial = flagMat;
        pole.parent = cup; flag.parent = cup;

        var ballGo = Primitive("Golf ball", PrimitiveType.Sphere, new Vector3(0, .14f, -38), Vector3.one * .23f, white);
        ballGo.GetComponent<Renderer>().sharedMaterial = white;
        var body = ballGo.AddComponent<Rigidbody>(); body.mass = 1f; body.linearDamping = .12f; body.angularDamping = .1f;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional;
        sun.intensity = 1.6f; sun.transform.rotation = Quaternion.Euler(43, -22, 0);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.63f, .69f, .66f);
        RenderSettings.fog = true; RenderSettings.fogColor = new Color(.68f, .81f, .83f);
        RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 70; RenderSettings.fogEndDistance = 190;

        var camera = new GameObject("Golf camera").AddComponent<Camera>();
        camera.tag = "MainCamera"; camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.64f, .78f, .82f);
        camera.fieldOfView = 55; camera.transform.position = new Vector3(0, 3.8f, -46.5f);
        camera.transform.LookAt(new Vector3(0, .8f, -34));
        camera.gameObject.AddComponent<AudioListener>();

        for (int i = 0; i < 15; i++)
        {
            float z = -63 + i * 8.5f;
            Tree(new Vector3(-28 - (i % 3) * 2, 0, z), bark, leaves, i);
            Tree(new Vector3(28 + (i % 4) * 2, 0, z + 2), bark, leaves, i + 4);
        }

        var arrow = new GameObject("Aim arrow").transform;
        var shaft = Visual("Arrow shaft", PrimitiveType.Cube, Vector3.zero, new Vector3(.055f, .025f, 2.5f), aimMat).transform;
        shaft.parent = arrow; shaft.localPosition = new Vector3(0, 0, 1.35f);
        foreach (var side in new[]{-1,1})
        {
            var wing = Visual("Arrow wing", PrimitiveType.Cube, Vector3.zero, new Vector3(.055f,.025f,.8f), aimMat).transform;
            wing.parent = arrow; wing.localPosition = new Vector3(side*.25f,0,2.45f);
            wing.localRotation = Quaternion.Euler(0, side*35f, 0);
        }

        Transform clubDisplay = null;
        var clubPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Kinesthetic/Art/BlenderProps/GolfClub.glb");
        if (clubPrefab)
        {
            var club = (GameObject)PrefabUtility.InstantiatePrefab(clubPrefab);
            club.name = "Golf club display"; club.transform.position = new Vector3(2.8f, 1.16f, -37.4f);
            clubDisplay = club.transform;
        }

        var game = new GameObject("Keyboard golf controller").AddComponent<KeyboardGolfGame>();
        game.ball = body; game.spectatorCamera = camera; game.cup = cup; game.flag = flag;
        game.aimArrow = arrow; game.clubVisual = clubDisplay;
        game.teePosition = ballGo.transform.position; game.cupPosition = cup.position;
        game.aimDegrees = 0;
        PlayerSettings.defaultScreenWidth = 1400; PlayerSettings.defaultScreenHeight = 900;
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
        var scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Any(s => s.path == ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("[Kinesthetic] Keyboard golf scene created. MotionProof.unity remains separate.");
    }

    static Material Material(string name, Color color)
    {
        string path = Root + "/" + name.Replace(' ', '-') + ".mat";
        var result = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!result) { result = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(result, path); }
        result.color = color; result.SetFloat("_Smoothness", .18f); EditorUtility.SetDirty(result); return result;
    }
    static GameObject Primitive(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.position = position; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material; return go;
    }
    static GameObject Visual(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        var go = Primitive(name, type, position, scale, material);
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>()); return go;
    }
    static void FairwayRibbon(Material material)
    {
        const int segments = 24;
        var vertices = new Vector3[(segments+1)*2]; var triangles = new int[segments*6];
        for (int i = 0; i <= segments; i++)
        {
            float z = -39 + i * (79f / segments);
            float center = Mathf.Sin((z + 39) * .045f) * 2.4f;
            float width = 13f + Mathf.Sin(i * .62f) * 2f;
            vertices[2*i] = new Vector3(center-width, .003f, z);
            vertices[2*i+1] = new Vector3(center+width, .003f, z);
            if (i == segments) continue;
            int t = i*6, v=i*2;
            triangles[t] = v; triangles[t+1] = v+2; triangles[t+2] = v+1;
            triangles[t+3] = v+1; triangles[t+4] = v+2; triangles[t+5] = v+3;
        }
        var mesh = new Mesh {vertices=vertices,triangles=triangles}; mesh.RecalculateNormals();
        var go = new GameObject("Fairway"); go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
    }
    static void Tree(Vector3 position, Material bark, Material leaves, int index)
    {
        float size = .8f + (index % 4) * .12f;
        Primitive("Tree trunk", PrimitiveType.Cylinder, position+Vector3.up*1.15f*size,
            new Vector3(.3f*size,1.15f*size,.3f*size), bark);
        Primitive("Tree crown", PrimitiveType.Sphere, position+Vector3.up*2.65f*size,
            new Vector3(2.1f*size,2.6f*size,2.1f*size), leaves);
    }
}

using System;
using System.Linq;
using Kinesthetic.Coach;
using UnityEditor;
using UnityEngine;

// Builds Resources/Coach/TrainerCoach.prefab: the trainer model upright (+Y) facing +Z, 1.8 m tall, face attached
// to the head bone, seated on a physio stool. The rehab scene spawns it at runtime, so the scene is not edited.
public static class CoachSetup
{
    const string Art = "Assets/Kinesthetic/Art/Coach";
    const string PrefabPath = "Assets/Kinesthetic/Coach/Resources/Coach/TrainerCoach.prefab";
    // Sized to the Mii world rather than real adults: seated, the coach's shoulders sit a little above the patient's.
    const float CoachHeight = 1.6f;

    [MenuItem("Kinesthetic/Coach/Build trainer coach prefab")]
    public static string Build()
    {
        var body = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/trainerM.dae");
        var face = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/trainerM_face.dae");
        if (!body || !face) throw new InvalidOperationException("Trainer models are not imported.");
        var root = new GameObject("Trainer coach");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(body); model.name = "Trainer model";
        model.transform.SetParent(root.transform, false);
        foreach (var a in model.GetComponentsInChildren<Animator>()) a.enabled = false;
        Transform B(string n) => model.GetComponentsInChildren<Transform>(true).First(t => t.name == n);

        // Orient: head-over-hip becomes +Y, the direction the feet point becomes +Z.
        var up = (B("head").position - B("hip").position).normalized;
        var fwd = Vector3.ProjectOnPlane(B("toe_l").position - B("ankle_l").position, up).normalized;
        model.transform.rotation = Quaternion.Inverse(Quaternion.LookRotation(fwd, up)) * model.transform.rotation;
        var headObj = (GameObject)PrefabUtility.InstantiatePrefab(face); headObj.name = "Trainer face";
        headObj.transform.SetParent(B("head"), false);
        foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.updateWhenOffscreen = true;
        // Imported COLLADA materials use the built-in shader (magenta under URP): rebuild them as URP Lit with the
        // original textures; textures with transparency (eyes, face details) use alpha clipping.
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var tex = mats[i] ? mats[i].mainTexture as Texture2D : null;
                var path = $"{Art}/Coach_{(tex ? tex.name.Replace('.', '_') : r.name + i)}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!m) { m = new Material(lit); AssetDatabase.CreateAsset(m, path); }
                m.shader = lit; m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", .35f);
                bool cutout = tex && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) is TextureImporter ti && ti.DoesSourceTextureHaveAlpha();
                m.SetFloat("_AlphaClip", cutout ? 1 : 0); m.SetFloat("_Cutoff", .5f);
                if (cutout) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cull", cutout ? 0 : 2);
                EditorUtility.SetDirty(m); mats[i] = m;
            }
            r.sharedMaterials = mats;
        }

        var bounds = Bounds(model); float scale = CoachHeight / bounds.size.y;
        model.transform.localScale *= scale;
        bounds = Bounds(model);
        model.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);

        // Seat: posing the legs in play mode lowers the hips by about a thigh length; place the stool under them.
        float thigh = Vector3.Distance(B("leg_l1").position, B("leg_l2").position);
        float shin = Vector3.Distance(B("leg_l2").position, B("ankle_l").position) + (B("ankle_l").position.y - bounds.min.y);
        float hipY = B("hip").position.y;
        model.transform.position += Vector3.up * (shin - hipY + .02f) + Vector3.back * thigh * .55f;
        // Chair with a backrest, matching the studio: teal seat and back, light legs.
        var chair = new GameObject("Coach chair"); chair.transform.SetParent(root.transform, false);
        var seatMat = MakeMat("CoachChair", new Color(.36f, .66f, .71f)); var legMat = MakeMat("CoachChairLegs", new Color(.86f, .88f, .9f));
        float seatY = shin - .02f, depth = .46f, width = .5f; var c = new Vector3(0, 0, -thigh * .55f);
        Part(chair, "Seat", PrimitiveType.Cube, c + new Vector3(0, seatY - .03f, 0), new Vector3(width, .06f, depth), seatMat);
        Part(chair, "Backrest", PrimitiveType.Cube, c + new Vector3(0, seatY + .3f, -depth / 2 + .02f), new Vector3(width, .46f, .05f), seatMat);
        foreach (var x in new[] { -1, 1 }) foreach (var z in new[] { -1, 1 })
            Part(chair, "Leg", PrimitiveType.Cylinder, c + new Vector3(x * (width / 2 - .04f), (seatY - .06f) / 2, z * (depth / 2 - .04f)), new Vector3(.04f, (seatY - .06f) / 2, .04f), legMat);
        foreach (var x in new[] { -1, 1 })
            Part(chair, "Back post", PrimitiveType.Cylinder, c + new Vector3(x * (width / 2 - .04f), seatY + .12f, -depth / 2 + .02f), new Vector3(.035f, .2f, .035f), legMat);

        var demo = root.AddComponent<CoachDemonstrator>(); demo.model = model.transform;
        // Blink uses the trainer's own open / half / closed eye textures.
        demo.eyeTextures = new[] { "trainerM_eye.1", "trainerM_eye.2", "trainerM_eye.3" }
            .Select(n => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Art}/{n}.png")).ToArray();
        demo.eyeRenderer = headObj.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.sharedMaterial && r.sharedMaterial.GetTexture("_BaseMap") == demo.eyeTextures[0]);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        return $"Saved {PrefabPath} (scale {scale:0.###})";
    }

    static Material MakeMat(string name, Color color)
    {
        var path = $"{Art}/{name}.mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", color); m.SetFloat("_Smoothness", .3f); EditorUtility.SetDirty(m); return m;
    }
    static void Part(GameObject parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = position; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material; UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
    }
    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true); var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds); return b;
    }
}

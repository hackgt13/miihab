using System;
using System.Linq;
using Kinesthetic;
using Kinesthetic.Coach;
using UnityEditor;
using UnityEngine;

public static class CoachSetup
{
    const string Art = "Assets/Kinesthetic/Art/Coach";
    const string PrefabPath = "Assets/Kinesthetic/Coach/Resources/Coach/TrainerCoach.prefab";

    [MenuItem("Kinesthetic/Coach/Build trainer coach prefab")]
    public static string Build()
    {
        var body = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Kinesthetic/Art/Mii/KinestheticMii.glb");
        if (!body) throw new InvalidOperationException("Mii model is not imported.");
        var root = new GameObject("Alex · studio coach");
        var actor = new GameObject("Coach pose"); actor.transform.SetParent(root.transform, false);
        actor.transform.localRotation = Quaternion.Euler(0, 180, 0);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(body, actor.transform);
        model.name = "Coach Mii"; model.transform.localRotation = Quaternion.Euler(0, 180, 0);
        foreach (var a in model.GetComponentsInChildren<Animator>()) a.enabled = false;
        foreach (var a in model.GetComponentsInChildren<Animation>()) { a.playAutomatically = false; a.enabled = false; }
        var renderers = model.GetComponentsInChildren<Renderer>(); var bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        model.transform.localScale *= 1.75f / bounds.size.y;
        foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;
        var rig = actor.AddComponent<PoseRig>(); rig.avatar = model.transform; rig.seated = true; rig.usePresentationSpace = true;
        rig.Initialize(); rig.Apply(null);
        actor.transform.position += Vector3.up * (.12f - rig.RightAnkle.position.y);
        actor.AddComponent<Kinesthetic.Golf.MiiIdleLife>().eyeStyle = Kinesthetic.Golf.MiiIdleLife.EyeStyle.Happy;
        foreach (var renderer in renderers)
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i].name.Contains("shirt")) materials[i] = Mat("Alex shirt", Palette.Cerulean50);
                else if (materials[i].name.Contains("pants")) materials[i] = Mat("Alex trousers", Palette.Prussian60);
                else if (renderer.name == "Mii_hair") materials[i] = Mat("Alex hair", Palette.Sand40);
            }
            renderer.sharedMaterials = materials;
        }
        var chair = new GameObject("Coach chair"); chair.transform.SetParent(root.transform, false);
        var seatMat = Mat("CoachChair", Palette.Cerulean30); var legMat = Mat("CoachChairLegs", Palette.Sand00);
        float seatY = rig.Hip.position.y - .075f;
        var c = new Vector3(rig.Hip.position.x, 0, rig.Hip.position.z + .06f);
        Part(chair, "Seat", PrimitiveType.Cube, c + new Vector3(0, seatY, 0), new Vector3(.52f, .07f, .48f), seatMat);
        Part(chair, "Backrest", PrimitiveType.Cube, c + new Vector3(0, seatY + .28f, -.21f), new Vector3(.52f, .4f, .045f), seatMat);
        foreach (var x in new[] { -1, 1 }) foreach (var z in new[] { -1, 1 })
            Part(chair, "Leg", PrimitiveType.Cylinder, c + new Vector3(x * .21f, seatY / 2, z * .19f), new Vector3(.035f, seatY / 2, .035f), legMat);
        var demo = root.AddComponent<CoachDemonstrator>(); demo.model = model.transform; demo.miiRig = rig;
        demo.leanForwardDeg = 2; demo.demoReps = 1;
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root); AssetDatabase.SaveAssets();
        return "Saved Mii coach at " + PrefabPath;
    }

    static Material Mat(string name, Color color)
    {
        string path = Art + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", color); m.SetFloat("_Smoothness", .22f); EditorUtility.SetDirty(m); return m;
    }
    static void Part(GameObject parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = position; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material; UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
    }
}

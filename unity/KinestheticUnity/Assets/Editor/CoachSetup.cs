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
        TrainerHair(model);
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

    // Alex wears the Wii-style trainer's short, tidy cut rather than the patient's spiky Mii hair, so the coach reads
    // as the coach at a glance. The trainer model's three hair layers are baked from its rest pose, turned from its
    // frame (Z up, facing -X) into the Mii's, scaled to the Mii's larger head and seated on it, then skinned to
    // nothing: a static mesh under the head bone, so it nods and turns with the head.
    public const float HairFit = 1.12f, HairLift = .015f, HairBack = .03f;
    static void TrainerHair(GameObject model)
    {
        var bones = model.GetComponentsInChildren<Transform>(true);
        var head = bones.First(t => t.name == "head");
        var parts = model.GetComponentsInChildren<Renderer>(true);
        var skull = parts.First(r => r.name == "Mii_head").bounds;
        var nose = parts.First(r => r.name == "Mii_nose").bounds.center;
        var forward = Vector3.ProjectOnPlane(nose - skull.center, Vector3.up).normalized;
        var right = Vector3.Cross(Vector3.up, forward);
        var toMii = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(Vector3.left, Vector3.forward));

        var trainer = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/trainerM.dae"));
        var layers = trainer.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMaterial && r.sharedMaterial.name.Contains("hair")).ToArray();
        var vertices = new System.Collections.Generic.List<Vector3>(); var normals = new System.Collections.Generic.List<Vector3>();
        var uvs = new System.Collections.Generic.List<Vector2>(); var submeshes = new System.Collections.Generic.List<int[]>(); var materials = new System.Collections.Generic.List<Material>();
        foreach (var layer in layers)
        {
            var baked = new Mesh(); layer.BakeMesh(baked, true);
            int offset = vertices.Count;
            foreach (var v in baked.vertices) vertices.Add(toMii * (layer.transform.position + layer.transform.rotation * v));
            foreach (var n in baked.normals) normals.Add(toMii * (layer.transform.rotation * n));
            uvs.AddRange(baked.uv);
            submeshes.Add(baked.triangles.Select(i => i + offset).ToArray());
            materials.Add(Mat("Alex hair", Palette.Sand40));   // the trainer's cut, in Alex's own colour
        }
        UnityEngine.Object.DestroyImmediate(trainer);

        // Fit: as wide as the skull plus a little, its crown just above the skull's, centred and set slightly back.
        var hair = new Bounds(vertices[0], Vector3.zero); foreach (var v in vertices) hair.Encapsulate(v);
        float Across(Bounds b) => Mathf.Abs(Vector3.Dot(b.size, right));
        float scale = Across(skull) * HairFit / Across(hair);
        var crown = new Vector3(hair.center.x, hair.max.y, hair.center.z) * scale;
        var seat = new Vector3(skull.center.x, skull.max.y + HairLift * skull.size.y, skull.center.z) - forward * (HairBack * skull.size.y);
        for (int i = 0; i < vertices.Count; i++) vertices[i] = head.InverseTransformPoint(vertices[i] * scale - crown + seat);
        for (int i = 0; i < normals.Count; i++) normals[i] = head.InverseTransformDirection(normals[i]).normalized;

        var mesh = new Mesh { name = "Alex trainer hair" };
        mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uvs);
        mesh.subMeshCount = submeshes.Count;
        for (int i = 0; i < submeshes.Count; i++) mesh.SetTriangles(submeshes[i], i);
        mesh.RecalculateBounds();
        string meshPath = Art + "/Alex trainer hair.asset";
        AssetDatabase.DeleteAsset(meshPath); AssetDatabase.CreateAsset(mesh, meshPath);

        var go = new GameObject("Trainer hair"); go.transform.SetParent(head, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterials = materials.ToArray();
        // Off at the object, not the renderer: scenes that switch every renderer on (the intro) must not bring it back.
        foreach (var r in parts) if (r.name == "Mii_hair") r.gameObject.SetActive(false);
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

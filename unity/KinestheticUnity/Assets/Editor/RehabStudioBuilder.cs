using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Authored studio kit, saved as a reusable prefab. All dimensions are metres.
// Rebuilding updates the same assets so scene references and material GUIDs remain stable.
public static class RehabStudioBuilder
{
    const string Root = "Assets/Kinesthetic/Rehab/Studio";
    static readonly Dictionary<string, Mesh> Meshes = new();
    static Material ivory, porcelain, oak, teal, mint, leaf, leafLight, soil, sky;

    public static GameObject Build()
    {
        Directory.CreateDirectory(Root + "/Materials");
        Directory.CreateDirectory(Root + "/Meshes");
        AssetDatabase.Refresh();
        Meshes.Clear();
        ivory = Material("Warm plaster", "F3F1E6");
        porcelain = Material("Porcelain", "FEFFF8");
        oak = Material("Honey oak", "C6A574");
        teal = Material("Studio teal", "439C9A");
        mint = Material("Soft mint", "A4D0B5");
        leaf = Material("Leaf jade", "4C8460");
        leafLight = Material("Leaf sage", "8AA96E");
        leaf.SetFloat("_Cull", 0); leafLight.SetFloat("_Cull", 0);
        soil = Material("Potting soil", "615849");
        sky = Material("Morning sky", "D3EAE8", unlit: true);
        var room = new GameObject("Movement studio");
        var architecture = Group("Architecture", room.transform);
        var floor = Group("Oak plank floor", architecture);
        var grain = WoodGrain();
        var woods = new Material[6];
        string[] tones = { "DDD0B8", "E0D3BA", "DACEB5", "E3D5BC", "DDD1BA", "DACBB2" };
        for (int i = 0; i < woods.Length; i++)
        {
            woods[i] = Material("Oak plank " + (i + 1), tones[i], .2f);
            woods[i].SetTexture("_BaseMap", grain);
            EditorUtility.SetDirty(woods[i]);
        }
        Box("Subfloor", floor, new(0, -.09f, 0), new(12, .13f, 12), oak);
        for (int x = 0; x < 20; x++)
        for (int z = 0; z < 6; z++)
        {
            float start = -6 + z * 2.4f - (x % 2) * 1.2f;
            float from = Mathf.Max(-6, start), to = Mathf.Min(6, start + 2.4f);
            if (to <= from) continue;
            Box($"Plank {x + 1:00}-{z + 1:00}", floor, new(-5.7f + x * .6f, -.025f, (from + to) * .5f), new(.597f, .05f, to - from - .004f), woods[(x * 7 + z * 3) % woods.Length]);
        }
        // Open window bays, with a real landscape behind them instead of a flat window decal.
        Box("Rear wainscot", architecture, new(0, .49f, 3.8f), new(12, .98f, .24f), ivory);
        Box("Upper wall", architecture, new(0, 3.65f, 3.8f), new(12, 1.1f, .24f), ivory);
        Box("Left wall", architecture, new(-6, 2.1f, -.5f), new(.24f, 4.2f, 8.8f), ivory);
        Box("Right wall", architecture, new(6, 2.1f, -.5f), new(.24f, 4.2f, 8.8f), ivory);
        Box("Rear left pier", architecture, new(-5.3f, 2.04f, 3.8f), new(1.4f, 2.12f, .24f), ivory);
        Box("Rear right pier", architecture, new(4.35f, 2.04f, 3.8f), new(3.3f, 2.12f, .24f), ivory);
        Box("Continuous sill", architecture, new(-.9f, .99f, 3.61f), new(7.55f, .10f, .52f), porcelain);
        Box("Oak sill edge", architecture, new(-.9f, .945f, 3.41f), new(7.55f, .035f, .06f), oak);
        Box("Rear baseboard", architecture, new(0, .10f, 3.62f), new(12, .20f, .07f), porcelain);
        Box("Rear picture rail", architecture, new(0, 3.14f, 3.62f), new(12, .12f, .08f), porcelain);
        Box("Left baseboard", architecture, new(-5.84f, .10f, -.5f), new(.07f, .20f, 8.5f), porcelain);
        for (int i = 0; i < 4; i++)
        {
            float x = -4.6f + i * 2.43f;
            Box("Window mullion " + i, architecture, new(x, 2.05f, 3.67f), new(.10f, 2.16f, .20f), porcelain);
        }
        Box("Window transom", architecture, new(-.95f, 2.65f, 3.67f), new(7.3f, .065f, .20f), porcelain);
        Box("Window lower frame", architecture, new(-.95f, 1.08f, 3.67f), new(7.3f, .065f, .20f), porcelain);
        // Quiet inset wall panels give the side wall depth and a studio rhythm.
        for (int i = 0; i < 4; i++)
        {
            Box("Side wall pilaster " + i, architecture, new(-5.82f, 1.8f, -3 + i * 2), new(.06f, 3.3f, .065f), porcelain);
        }

        var outdoors = Group("Window garden", room.transform);
        Box("Sky", outdoors, new(0, 4, 14), new(40, 14, .15f), sky);
        var distant = Material("Distant hills", "B5D4BE", unlit: true);
        var near = Material("Garden hills", "96BFA0", unlit: true);
        var cloud = Material("Cloud ivory", "F5F6E8", unlit: true);
        Ball("Distant hill west", outdoors, new(-5, -.1f, 11), new(15, 4.1f, 3), distant);
        Ball("Distant hill east", outdoors, new(6, -.2f, 11.5f), new(18, 4.8f, 3), distant);
        Ball("Garden rise", outdoors, new(0, -.8f, 8), new(12, 3.7f, 2.5f), near);
        Ball("Cloud west", outdoors, new(-4, 3.6f, 12), new(3.5f, .5f, .3f), cloud);
        Ball("Cloud east", outdoors, new(4, 4.4f, 12), new(4.5f, .65f, .3f), cloud);
        for (int i = 0; i < 5; i++)
        {
            float x = -5 + i * 2.1f;
            Box("Garden tree trunk " + i, outdoors, new(x, .85f, 8.8f + i % 2), new(.12f, 1.7f, .12f), oak);
            Ball("Garden tree crown " + i, outdoors, new(x, 1.8f, 8.8f + i % 2), new(.95f, 1.45f, .9f), i % 2 == 0 ? near : distant);
        }

        var practice = Group("Practice space", room.transform);
        Rounded("Mat bound edge", practice, new(0, .022f, -.12f), 2.85f, 2.45f, .035f, .22f, Material("Mat binding", "BCD8C5"));
        Rounded("Cushioned exercise mat", practice, new(0, .043f, -.12f), 2.70f, 2.30f, .035f, .18f, Material("Mat woven sage", "7BB59F"));
        var line = Material("Mat detail", "ACD1BC");
        for (int i = 0; i < 5; i++)
            Rounded("Mat registration " + i, practice, new(-.18f + i * .09f, .063f, -.97f), .04f, .045f, .002f, .019f, line);
        Rounded("Mat left stripe", practice, new(-1.22f, .063f, -.12f), .012f, 1.9f, .002f, .005f, line);
        Rounded("Mat right stripe", practice, new(1.22f, .063f, -.12f), .012f, 1.9f, .002f, .005f, line);

        var furniture = Group("Studio furnishings", room.transform);
        Plant("Window plant", furniture, new(-3.7f, 0, 2.72f), 1.15f, 12);
        Plant("Studio plant", furniture, new(3.95f, 0, 2.55f), .95f, 9);
        var bench = Group("Oak recovery bench", furniture); bench.localPosition = new(2.2f, 0, 2.75f);
        Rounded("Bench seat", bench, new(0, .49f, 0), 1.7f, .52f, .09f, .08f, oak);
        for (int i = -1; i <= 1; i += 2)
        {
            Box("Bench leg " + i, bench, new(i * .64f, .23f, 0), new(.10f, .46f, .38f), porcelain);
            Box("Bench foot " + i, bench, new(i * .64f, .035f, 0), new(.22f, .06f, .45f), porcelain);
        }
        Rounded("Folded towel lower", bench, new(.33f, .58f, 0), .55f, .39f, .08f, .055f, porcelain);
        Rounded("Folded towel upper", bench, new(.34f, .655f, .015f), .52f, .36f, .07f, .05f, mint);
        Box("Towel woven band", bench, new(.50f, .692f, .015f), new(.035f, .003f, .28f), porcelain);
        Primitive("Water bottle", PrimitiveType.Cylinder, bench, new(-.48f, .70f, .02f), new(.12f, .16f, .12f), teal);
        Primitive("Bottle cap", PrimitiveType.Cylinder, bench, new(-.48f, .875f, .02f), new(.085f, .025f, .085f), porcelain);
        Box("Bottle label", bench, new(-.48f, .70f, -.041f), new(.058f, .09f, .006f), porcelain);

        var clock = Group("Quiet wall clock", furniture); clock.localPosition = new(3.6f, 2.73f, 3.60f);
        var rim = Primitive("Clock oak rim", PrimitiveType.Cylinder, clock, Vector3.zero, new(.64f, .025f, .64f), oak);
        rim.transform.localRotation = Quaternion.Euler(90, 0, 0);
        var face = Primitive("Clock face", PrimitiveType.Cylinder, clock, new(0, 0, -.03f), new(.58f, .009f, .58f), porcelain);
        face.transform.localRotation = Quaternion.Euler(90, 0, 0);
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.PI / 6;
            var tick = Box("Clock index " + i, clock, new(Mathf.Sin(a) * .24f, Mathf.Cos(a) * .24f, -.044f), new(.013f, .034f, .007f), teal);
            tick.transform.localRotation = Quaternion.Euler(0, 0, -i * 30);
        }
        var hour = Box("Clock hour hand", clock, new(-.045f, .058f, -.055f), new(.018f, .16f, .008f), teal);
        hour.transform.localRotation = Quaternion.Euler(0, 0, 38);
        var minute = Box("Clock minute hand", clock, new(.075f, .055f, -.06f), new(.012f, .21f, .008f), teal);
        minute.transform.localRotation = Quaternion.Euler(0, 0, -54);
        Ball("Clock centre", clock, new(0, 0, -.07f), Vector3.one * .035f, teal);
        WallText("Studio sign", furniture, "movement\nstudio", new(3.6f, 2.04f, 3.63f), .044f, new Color(.27f, .50f, .48f));
        WallText("Studio motto", furniture, "BREATHE.  REACH.  REPEAT.", new(3.6f, 1.51f, 3.62f), .013f, new Color(.40f, .56f, .51f));

        AssetDatabase.SaveAssets();
        PrefabUtility.SaveAsPrefabAssetAndConnect(room, Root + "/RehabStudio.prefab", InteractionMode.AutomatedAction);
        return room;
    }

    static Material Material(string name, string hex, float smoothness = .15f, bool unlit = false)
    {
        string path = Root + "/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = shader; m.name = name;
        ColorUtility.TryParseHtmlString("#" + hex, out var color);
        m.SetColor("_BaseColor", color); m.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(m); return m;
    }

    static Texture2D WoodGrain()
    {
        const string path = Root + "/OakGrain.png";
        var t = new Texture2D(256, 256, TextureFormat.RGB24, false);
        var pixels = new Color[256 * 256];
        for (int y = 0; y < 256; y++)
        for (int x = 0; x < 256; x++)
        {
            float warp = Mathf.PerlinNoise(x * .018f, y * .007f) * 7;
            float grain = Mathf.PerlinNoise(x * .14f + warp, y * .009f);
            float fine = Mathf.Sin(x * 2.3f + warp * 2 + y * .006f) * .004f;
            float v = .96f + grain * .04f + fine;
            pixels[y * 256 + x] = new Color(v, v, v);
        }
        t.SetPixels(pixels); t.Apply(); File.WriteAllBytes(path, t.EncodeToPNG()); Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Repeat; importer.filterMode = FilterMode.Bilinear;
        importer.maxTextureSize = 256; importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Transform Group(string name, Transform parent)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform;
    }
    static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = position; go.transform.localScale = scale;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = material; go.isStatic = true; return go;
    }
    static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material material) => Primitive(name, PrimitiveType.Cube, parent, position, size, material);
    static GameObject Ball(string name, Transform parent, Vector3 position, Vector3 size, Material material) => Primitive(name, PrimitiveType.Sphere, parent, position, size, material);

    static Mesh SaveMesh(string name, Mesh mesh)
    {
        var old = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/Meshes/" + name + ".asset");
        mesh.name = name; mesh.RecalculateNormals(); mesh.RecalculateBounds();
        if (old) { EditorUtility.CopySerialized(mesh, old); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(old); return old; }
        AssetDatabase.CreateAsset(mesh, Root + "/Meshes/" + name + ".asset"); return mesh;
    }
    static GameObject MeshObject(string name, Transform parent, Vector3 position, Mesh mesh, Material material)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false);
        go.transform.localPosition = position; go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = material; go.isStatic = true; return go;
    }
    static GameObject Rounded(string name, Transform parent, Vector3 position, float width, float depth, float height, float radius, Material material)
    {
        string key = System.FormattableString.Invariant($"Rounded {width:0.000} {depth:0.000} {height:0.000} {radius:0.000}");
        if (!Meshes.TryGetValue(key, out var mesh))
        {
            var outline = new List<Vector3>();
            for (int c = 0; c < 4; c++)
            for (int i = 0; i <= 6; i++)
            {
                float a = (c * 90 + i * 15) * Mathf.Deg2Rad;
                float cx = c == 0 || c == 3 ? width / 2 - radius : -width / 2 + radius;
                float cz = c < 2 ? depth / 2 - radius : -depth / 2 + radius;
                outline.Add(new Vector3(cx + Mathf.Cos(a) * radius, 0, cz + Mathf.Sin(a) * radius));
            }
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int i = 0; i < outline.Count; i++)
            {
                var a = outline[i]; var b = outline[(i + 1) % outline.Count];
                int k = vertices.Count;
                vertices.Add(Vector3.up * height / 2); vertices.Add(b + Vector3.up * height / 2); vertices.Add(a + Vector3.up * height / 2);
                vertices.Add(Vector3.down * height / 2); vertices.Add(a - Vector3.up * height / 2); vertices.Add(b - Vector3.up * height / 2);
                vertices.Add(a + Vector3.up * height / 2); vertices.Add(b + Vector3.up * height / 2);
                vertices.Add(a - Vector3.up * height / 2); vertices.Add(b - Vector3.up * height / 2);
                triangles.AddRange(new[] { k, k + 1, k + 2, k + 3, k + 4, k + 5, k + 6, k + 7, k + 8, k + 7, k + 9, k + 8 });
            }
            mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            mesh = SaveMesh(key, mesh); Meshes[key] = mesh;
        }
        return MeshObject(name, parent, position, mesh, material);
    }
    static void Plant(string name, Transform parent, Vector3 position, float scale, int leaves)
    {
        var plant = Group(name, parent); plant.localPosition = position; plant.localScale = Vector3.one * scale;
        if (!Meshes.TryGetValue("Ceramic planter", out var pot))
        {
            var vertices = new List<Vector3>(); var indices = new List<int>();
            Vector2[] profile = { new(.16f, 0), new(.175f, .035f), new(.245f, .47f), new(.245f, .51f), new(.21f, .51f), new(.21f, .46f), new(.16f, .035f) };
            const int count = 40;
            for (int p = 0; p < profile.Length; p++)
            for (int i = 0; i <= count; i++)
            {
                float a = i * Mathf.PI * 2 / count;
                vertices.Add(new Vector3(Mathf.Cos(a) * profile[p].x, profile[p].y, Mathf.Sin(a) * profile[p].x));
                if (p == 0 || i == 0) continue;
                int k = p * (count + 1) + i;
                indices.AddRange(new[] { k, k - count - 2, k - 1, k, k - count - 1, k - count - 2 });
            }
            pot = new Mesh(); pot.SetVertices(vertices); pot.SetTriangles(indices, 0); pot = SaveMesh("Ceramic planter", pot); Meshes["Ceramic planter"] = pot;
        }
        MeshObject("Ceramic planter", plant, Vector3.zero, pot, porcelain);
        Primitive("Soil", PrimitiveType.Cylinder, plant, new(0, .455f, 0), new(.41f, .012f, .41f), soil);
        if (!Meshes.TryGetValue("Curved leaf", out var blade))
        {
            Vector3[] vertices = { new(0, 0, 0), new(-.14f, .22f, .05f), new(0, .23f, -.01f), new(.14f, .22f, .05f), new(-.105f, .45f, .14f), new(0, .46f, .10f), new(.105f, .45f, .14f), new(0, .62f, .27f) };
            int[] front = { 0, 1, 2, 0, 2, 3, 1, 4, 5, 1, 5, 2, 2, 5, 6, 2, 6, 3, 4, 7, 5, 5, 7, 6 };
            var indices = new List<int>(front);
            blade = new Mesh { vertices = vertices, triangles = indices.ToArray() }; blade = SaveMesh("Curved leaf", blade); Meshes["Curved leaf"] = blade;
        }
        for (int i = 0; i < leaves; i++)
        {
            float a = i * 137.5f * Mathf.Deg2Rad, height = .6f + (i % 4) * .18f;
            var end = new Vector3(Mathf.Sin(a) * .22f, height, Mathf.Cos(a) * .22f);
            var from = new Vector3(0, .45f, 0);
            var stem = Primitive("Leaf stem " + i, PrimitiveType.Cylinder, plant, (from + end) / 2, new(.016f, (end - from).magnitude / 2, .016f), leaf);
            stem.transform.localRotation = Quaternion.FromToRotation(Vector3.up, end - from);
            var go = MeshObject("Leaf " + i, plant, end, blade, i % 3 == 0 ? leafLight : leaf);
            go.transform.localRotation = Quaternion.Euler(18 + i % 4 * 10, i * 137.5f, 0);
            go.transform.localScale = Vector3.one * (.75f + i % 3 * .13f);
        }
    }
    static void WallText(string name, Transform parent, string text, Vector3 position, float size, Color color)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position;
        // TextMesh faces local -Z, towards the patient camera.
        var label = go.AddComponent<TextMesh>(); label.text = text; label.fontSize = 72; label.characterSize = size;
        label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center; label.color = color;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        go.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
    }
}

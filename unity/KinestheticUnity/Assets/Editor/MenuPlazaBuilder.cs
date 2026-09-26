using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Resort plaza diorama that sits behind the main menu. All dimensions are metres.
// Nothing here is interactive: it is scenery the menu camera dollies across, so every
// renderer is static, colliders are stripped, and distant geometry is unlit.
// Rebuilding updates the same assets so scene references and material GUIDs remain stable.
public static class MenuPlazaBuilder
{
    const string Root = "Assets/Kinesthetic/Menu/Plaza";
    static readonly Dictionary<string, Mesh> Meshes = new();
    static Material stucco, trim, terracotta, paving, shade, teak, sea, sand, fairway, rough, canopy, accent, cream;

    public static GameObject Build()
    {
        Directory.CreateDirectory(Root + "/Materials");
        Directory.CreateDirectory(Root + "/Meshes");
        AssetDatabase.Refresh();
        Meshes.Clear();
        stucco = Material("Resort stucco", "FAF3E4");
        cream = Material("Sunlit cream", "FFFBF0");
        trim = Material("Painted trim", "FEFFF8");
        terracotta = Material("Terracotta tile", "D07A57", .25f);
        paving = Material("Plaza sandstone", "E7DCC3", .1f);
        shade = Material("Paving shade", "DCD0B6", .1f);
        teak = Material("Weathered teak", "B08A5F", .2f);
        accent = Material("Resort teal", "3FA8D4", .3f);
        canopy = Material("Parasol canopy", "F6F1E2");
        canopy.SetFloat("_Cull", 0);
        sand = Material("Bunker sand", "EFE2BE", .05f);
        fairway = Material("Fairway grass", "8CC46B", .05f);
        rough = Material("Course rough", "6FA855", .05f);
        sea = Material("Lagoon water", "56B7C8", unlit: true);

        var plaza = new GameObject("Resort plaza");

        Deck(Group("Plaza deck", plaza.transform));
        Course(Group("Course below", plaza.transform));
        Horizon(Group("Sea and sky", plaza.transform));
        Pavilion(Group("Movement studio pavilion", plaza.transform));
        ProShop(Group("Golf pro shop", plaza.transform));
        Furnishings(Group("Plaza furnishings", plaza.transform));
        Anchors(Group("Anchors", plaza.transform));

        // Anything MenuSway rotates must drop the static flag, or batching freezes it in place.
        foreach (var t in plaza.GetComponentsInChildren<Transform>())
            if (t.name.StartsWith("Sway "))
                foreach (var child in t.GetComponentsInChildren<Transform>())
                    child.gameObject.isStatic = false;

        AssetDatabase.SaveAssets();
        PrefabUtility.SaveAsPrefabAssetAndConnect(plaza, Root + "/MenuPlaza.prefab", InteractionMode.AutomatedAction);
        return plaza;
    }

    // ---------------------------------------------------------------- plaza

    static void Deck(Transform deck)
    {
        Box("Deck fill", deck, new(0, -.5f, 1), new(28, 1, 13), shade);
        var tones = new Material[5];
        string[] hex = { "E9DEC6", "E5D9BF", "ECE2CB", "E2D6BA", "E8DDC4" };
        for (int i = 0; i < tones.Length; i++) tones[i] = Material("Paving tone " + (i + 1), hex[i], .1f);
        for (int x = 0; x < 20; x++)
        for (int z = 0; z < 9; z++)
            Box($"Paver {x + 1:00}-{z + 1:00}", deck, new(-13.3f + x * 1.4f, .01f, -5.1f + z * 1.4f),
                new(1.33f, .04f, 1.33f), tones[(x * 3 + z * 7) % tones.Length]);

        // Balustrade along the overlook, then steps down to the first tee.
        var rail = Group("Overlook balustrade", deck);
        for (int i = -1; i <= 1; i += 2)
        {
            Box("Balustrade cap " + i, rail, new(i * 8.4f, .92f, 7.1f), new(10.4f, .14f, .5f), trim);
            Box("Balustrade kerb " + i, rail, new(i * 8.4f, .16f, 7.1f), new(10.4f, .32f, .58f), stucco);
            for (int b = 0; b < 16; b++)
                Baluster("Baluster " + i + "-" + b, rail, new(i * 8.4f - 5f + b * .66f, .52f, 7.1f));
            Box("Overlook pier " + i, rail, new(i * 13.1f, .62f, 7.1f), new(1.1f, 1.24f, .9f), stucco);
            Box("Pier cap " + i, rail, new(i * 13.1f, 1.29f, 7.1f), new(1.28f, .14f, 1.08f), trim);
            Ball("Pier finial " + i, rail, new(i * 13.1f, 1.47f, 7.1f), Vector3.one * .3f, trim);
        }
        Box("Stair gap pier west", rail, new(-3.1f, .62f, 7.1f), new(.7f, 1.24f, .9f), stucco);
        Box("Stair gap pier east", rail, new(3.1f, .62f, 7.1f), new(.7f, 1.24f, .9f), stucco);
        var stair = Group("Overlook steps", deck);
        for (int i = 0; i < 5; i++)
            Box("Step " + i, stair, new(0, -.14f - i * .22f, 7.4f + i * .42f), new(5.4f, .22f, .46f), paving);
        Box("Lower terrace", stair, new(0, -1.2f, 10.6f), new(9, .5f, 6), paving);
    }

    static void Baluster(string name, Transform parent, Vector3 position)
    {
        if (!Meshes.TryGetValue("Baluster", out var mesh))
        {
            Vector2[] profile = { new(.07f, -.34f), new(.075f, -.3f), new(.05f, -.2f), new(.085f, -.02f), new(.062f, .16f), new(.075f, .3f), new(.07f, .34f) };
            mesh = Lathe("Baluster", profile, 12);
        }
        MeshObject(name, parent, position, mesh, trim);
    }

    // ---------------------------------------------------------------- course

    static void Course(Transform course)
    {
        if (!Meshes.TryGetValue("Fairway", out var ground))
        {
            var vertices = new List<Vector3>(); var indices = new List<int>();
            const int cols = 26, rows = 30;
            for (int z = 0; z <= rows; z++)
            for (int x = 0; x <= cols; x++)
            {
                float u = x / (float)cols, v = z / (float)rows;
                float px = Mathf.Lerp(-34, 34, u), pz = Mathf.Lerp(11, 78, v);
                // Drops away from the overlook, with slow rolling relief either side of the fairway line.
                float fall = -1.4f - v * 2.6f;
                float roll = Mathf.PerlinNoise(u * 3.1f + .5f, v * 2.4f + .5f) * 1.9f;
                float bank = Mathf.Abs(px) > 12 ? (Mathf.Abs(px) - 12) * .13f : 0;
                vertices.Add(new Vector3(px, fall + roll + bank, pz));
                if (x == 0 || z == 0) continue;
                int k = z * (cols + 1) + x;
                indices.AddRange(new[] { k, k - 1, k - cols - 2, k, k - cols - 2, k - cols - 1 });
            }
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0);
            ground = SaveMesh("Fairway", mesh);
        }
        MeshObject("Fairway ground", course, Vector3.zero, ground, fairway);

        Primitive("Putting green", PrimitiveType.Cylinder, course, new(5.5f, -2.8f, 44), new(13, .12f, 13), Material("Putting green", "A6D47E", .05f));
        Primitive("Green collar", PrimitiveType.Cylinder, course, new(5.5f, -2.88f, 44), new(15.4f, .1f, 15.4f), rough);
        Ball("Greenside bunker", course, new(-4.2f, -3.1f, 39), new(9, 1.1f, 6.5f), sand);
        Ball("Fairway bunker", course, new(15.5f, -3.4f, 30), new(7.5f, 1f, 5f), sand);
        Ball("Far bunker", course, new(-13, -4.1f, 58), new(8, 1f, 5.5f), sand);

        var pin = Group("Course pin", course); pin.localPosition = new(5.5f, -2.74f, 44.5f);
        Primitive("Cup rim", PrimitiveType.Cylinder, pin, new(0, .005f, 0), new(.24f, .01f, .24f), trim);
        Primitive("Flagstick", PrimitiveType.Cylinder, pin, new(0, 1.1f, 0), new(.045f, 1.1f, .045f), trim);
        var flag = Group("Sway flag", pin); flag.localPosition = new(0, 1.92f, 0);
        var cloth = Box("Flag cloth", flag, new(.42f, -.14f, 0), new(.84f, .5f, .02f), Material("Pin flag", "E85C4A"));
        cloth.transform.localRotation = Quaternion.Euler(0, 0, -3);

        // Tree lines frame the hole and hide the mesh edge; unlit so they read as distance.
        var west = Material("Tree line west", "5E9660", unlit: true);
        var east = Material("Tree line east", "6FA46B", unlit: true);
        var trees = Group("Tree line", course);
        for (int i = 0; i < 11; i++)
        {
            float z = 15 + i * 6.2f, lift = -1.9f - i * .2f;
            Box("West trunk " + i, trees, new(-25 - i % 3, lift + 1.9f, z), new(.5f, 3.8f, .5f), teak);
            Ball("West crown " + i, trees, new(-25 - i % 3, lift + 5.2f, z), new(5.6f, 7.4f, 5.2f), i % 2 == 0 ? west : east);
            Box("East trunk " + i, trees, new(25 + i % 4, lift + 1.9f, z + 3), new(.5f, 3.8f, .5f), teak);
            Ball("East crown " + i, trees, new(25 + i % 4, lift + 5.2f, z + 3), new(5.2f, 7f, 5f), i % 2 == 0 ? east : west);
        }
        for (int i = 0; i < 5; i++)
            Ball("Headland rise " + i, trees, new(-30 + i * 16, -6.5f, 88 + (i % 2) * 9), new(34, 9.5f, 16), i % 2 == 0 ? west : east);
    }

    static void Horizon(Transform horizon)
    {
        Box("Lagoon", horizon, new(0, -5.1f, 168), new(620, .4f, 190), sea);
        var far = Material("Distant island", "9FC2C0", unlit: true);
        var haze = Material("Hazed island", "BBD2D2", unlit: true);
        Ball("Island west", horizon, new(-118, -7, 236), new(120, 26, 30), far);
        Ball("Island east", horizon, new(96, -8, 252), new(150, 30, 34), haze);
        Ball("Island far", horizon, new(6, -9, 282), new(190, 26, 30), haze);
        var cloud = Material("Cloud ivory", "FFF8EC", unlit: true);
        var lit = Material("Cloud gold", "FFEBD0", unlit: true);
        for (int i = 0; i < 7; i++)
        {
            float x = -150 + i * 52, y = 34 + (i % 3) * 11, z = 150 + (i % 4) * 26;
            Ball("Cloud " + i, horizon, new(x, y, z), new(42 + i * 4, 7, 12), i % 2 == 0 ? cloud : lit);
            Ball("Cloud crest " + i, horizon, new(x + 8, y + 4.5f, z - 2), new(24, 8, 10), cloud);
        }
    }

    // ---------------------------------------------------------------- buildings

    static void Pavilion(Transform pavilion)
    {
        pavilion.localPosition = new(-10.6f, 0, 3.6f);
        pavilion.localRotation = Quaternion.Euler(0, -40, 0);
        const float w = 7.2f, d = 5.4f, h = 3.3f;
        Box("Plinth", pavilion, new(0, .09f, 0), new(w + .7f, .18f, d + .7f), paving);
        Box("Rear wall", pavilion, new(0, h / 2 + .18f, d / 2), new(w, h, .28f), stucco);
        Box("West wall", pavilion, new(-w / 2, h / 2 + .18f, 0), new(.28f, h, d), stucco);
        Box("East wall", pavilion, new(w / 2, h / 2 + .18f, 0), new(.28f, h, d), stucco);
        Box("Front wall lower", pavilion, new(-2.55f, h / 2 + .18f, -d / 2), new(2.1f, h, .28f), stucco);
        Box("Front wall east", pavilion, new(2.2f, h / 2 + .18f, -d / 2), new(2.8f, h, .28f), stucco);
        Box("Front header", pavilion, new(-.35f, 2.86f, -d / 2), new(2.5f, .96f, .28f), stucco);
        Box("Door reveal", pavilion, new(-.35f, 1.28f, -d / 2 + .04f), new(1.62f, 2.2f, .2f), Material("Doorway shade", "6E6257"));
        Box("Door leaf", pavilion, new(-.35f, 1.24f, -d / 2 - .03f), new(1.5f, 2.12f, .08f), teak);
        Box("Door glazing", pavilion, new(-.35f, 1.72f, -d / 2 - .09f), new(1.16f, 1.02f, .04f), accent);
        Box("Door frame head", pavilion, new(-.35f, 2.38f, -d / 2 - .06f), new(1.86f, .16f, .14f), trim);

        var window = Group("Front window", pavilion); window.localPosition = new(2.2f, 1.72f, -d / 2 - .02f);
        Box("Window shade", window, new(0, 0, .12f), new(1.7f, 1.34f, .12f), Material("Window interior", "8CA8AE"));
        Box("Window glass", window, Vector3.zero, new(1.66f, 1.3f, .06f), Material("Window glass", "CFE9F0", .75f));
        Box("Window mullion", window, Vector3.zero, new(.07f, 1.32f, .09f), trim);
        Box("Window frame", window, new(0, 0, .02f), new(1.86f, 1.5f, .07f), trim);
        Box("Window sill", window, new(0, -.78f, -.02f), new(2.02f, .1f, .26f), trim);
        var side = Group("West window", pavilion); side.localPosition = new(-w / 2 - .02f, 1.72f, 1.1f);
        side.localRotation = Quaternion.Euler(0, -90, 0);
        Box("Side glass", side, Vector3.zero, new(1.3f, 1.3f, .06f), Material("Window glass", "CFE9F0", .75f));
        Box("Side frame", side, new(0, 0, .02f), new(1.5f, 1.5f, .07f), trim);

        // Fabric awning over the entrance, folded from four rotated slats.
        var awning = Group("Entrance awning", pavilion); awning.localPosition = new(-.35f, 2.52f, -d / 2 - .1f);
        for (int i = 0; i < 4; i++)
        {
            var slat = Box("Awning slat " + i, awning, new(-.9f + i * .6f, -.2f, -.52f), new(.58f, .05f, 1.26f),
                i % 2 == 0 ? Material("Awning stripe", "E9705C") : canopy);
            slat.transform.localRotation = Quaternion.Euler(-22, 0, 0);
        }
        Box("Awning bar", awning, new(0, -.02f, -.06f), new(2.6f, .09f, .09f), trim);
        Box("Awning valance", awning, new(0, -.58f, -1.08f), new(2.5f, .18f, .04f), canopy);

        Roof("Pavilion roof", pavilion, new(0, h + .18f, 0), w + 1.5f, d + 1.5f, 1.5f, 1.4f, terracotta);
        Box("Roof fascia", pavilion, new(0, h + .26f, -(d + 1.5f) / 2), new(w + 1.6f, .16f, .12f), trim);
        WallText("Studio sign", pavilion, "movement studio", new(-.35f, 3.32f, -d / 2 - .16f), .052f, new Color(.27f, .50f, .52f));
        WallText("Studio hours", pavilion, "OPEN  ·  ALL  WELCOME", new(-.35f, 3.04f, -d / 2 - .16f), .016f, new Color(.45f, .55f, .55f));
        Plant("Door planter west", pavilion, new(-1.9f, .18f, -d / 2 - .55f), 1.05f, 9);
        Plant("Door planter east", pavilion, new(1.2f, .18f, -d / 2 - .55f), .92f, 8);
    }

    static void ProShop(Transform shop)
    {
        shop.localPosition = new(10.2f, 0, 3.4f);
        shop.localRotation = Quaternion.Euler(0, 42, 0);
        const float w = 5f, d = 3.8f, h = 2.7f;
        Box("Plinth", shop, new(0, .09f, 0), new(w + .6f, .18f, d + .6f), paving);
        Box("Rear wall", shop, new(0, h / 2 + .18f, d / 2), new(w, h, .26f), cream);
        Box("West wall", shop, new(-w / 2, h / 2 + .18f, 0), new(.26f, h, d), cream);
        Box("East wall", shop, new(w / 2, h / 2 + .18f, 0), new(.26f, h, d), cream);
        Box("Counter back", shop, new(0, 1.9f, -d / 2), new(w, 1.16f, .26f), cream);
        Box("Counter interior", shop, new(0, .95f, -d / 2 + .3f), new(w - .5f, 1.9f, .2f), Material("Shop interior", "7E7266"));
        Box("Counter top", shop, new(0, 1.16f, -d / 2 - .12f), new(w + .24f, .12f, .62f), teak);
        Box("Counter front", shop, new(0, .55f, -d / 2 - .08f), new(w - .1f, 1.1f, .2f), cream);
        for (int i = -1; i <= 1; i += 2)
            Box("Counter post " + i, shop, new(i * (w / 2 - .2f), 1.9f, -d / 2 - .1f), new(.14f, 1.2f, .14f), trim);
        Roof("Shop roof", shop, new(0, h + .18f, 0), w + 1.4f, d + 1.4f, 1.2f, 1.1f, terracotta);
        Box("Shop fascia", shop, new(0, h + .24f, -(d + 1.4f) / 2), new(w + 1.5f, .14f, .12f), trim);
        WallText("Shop sign", shop, "pro shop", new(0, 3.04f, -d / 2 - .15f), .044f, new Color(.42f, .34f, .27f));
        Box("Scorecard board", shop, new(-w / 2 - .16f, 1.55f, -.3f), new(.07f, 1.1f, 1.5f), trim);
    }

    // ---------------------------------------------------------------- props

    static void Furnishings(Transform props)
    {
        Palm("Palm west", props, new(-5.4f, 0, 5.9f), 1.25f, 9, 6);
        Palm("Palm east", props, new(6.2f, 0, 6.2f), 1.1f, 8, -7);
        Palm("Palm far west", props, new(-13.4f, 0, -1.4f), 1.35f, 9, -4);
        Palm("Palm far east", props, new(13.6f, 0, -.6f), 1.2f, 8, 5);
        Palm("Palm terrace", props, new(-4.4f, -.95f, 10.4f), .95f, 8, 9);

        Bench("Overlook bench west", props, new(-2.4f, 0, 4.9f), 0);
        Bench("Overlook bench east", props, new(2.6f, 0, 5.1f), 0);
        Bench("Shade bench", props, new(-10.4f, 0, -3.6f), 30);
        Parasol("Cafe parasol west", props, new(-6.2f, 0, -3.8f), 1);
        Parasol("Cafe parasol east", props, new(4.8f, 0, .9f), -1);
        Plant("Plaza planter west", props, new(-9.6f, 0, -3.2f), 1.3f, 11);
        Plant("Plaza planter east", props, new(9.2f, 0, -3.4f), 1.2f, 10);

        var kit = Group("Golf kit", props); kit.localPosition = new(7.8f, 0, .9f);
        var bag = Primitive("Golf bag", PrimitiveType.Cylinder, kit, new(0, .48f, 0), new(.3f, .48f, .3f), Material("Bag canvas", "3E7FA8", .25f));
        bag.transform.localRotation = Quaternion.Euler(9, 0, -4);
        Primitive("Bag hood", PrimitiveType.Cylinder, kit, new(.06f, .96f, -.02f), new(.27f, .1f, .27f), Material("Bag hood", "2F6A8E", .25f));
        Box("Bag band", kit, new(0, .6f, -.29f), new(.3f, .12f, .05f), canopy);
        for (int i = 0; i < 4; i++)
        {
            var shaft = Primitive("Club shaft " + i, PrimitiveType.Cylinder, kit, new(.08f + i * .045f, 1.3f, .02f), new(.014f, .42f, .014f), trim);
            shaft.transform.localRotation = Quaternion.Euler(11, 0, -7 - i * 2);
            Box("Club grip " + i, kit, new(.15f + i * .06f, 1.72f, .06f), new(.036f, .16f, .036f), Material("Club grip", "3B3A39"));
        }
        Primitive("Range bucket", PrimitiveType.Cylinder, kit, new(-.82f, .17f, -.34f), new(.23f, .17f, .23f), Material("Range bucket", "E7B855", .2f));
        for (int i = 0; i < 7; i++)
            Ball("Range ball " + i, kit, new(-.82f + Mathf.Cos(i * 1.9f) * .12f, .36f + (i % 2) * .04f, -.34f + Mathf.Sin(i * 1.9f) * .12f), Vector3.one * .043f, trim);
        Ball("Stray ball", kit, new(-1.6f, .022f, -.95f), Vector3.one * .043f, trim);

        // Out past the panes. A pane sweeps a cylinder of 3.96 m around the viewpoint as the ring turns, and
        // this post's boards used to reach back to 2.96 m at exactly their height, so the sign cut through
        // them mid-turn. Same bearing, far enough out to clear the sweep with a margin.
        Signpost("Wayfinding post", props, new(3.87f, 0, -2.66f));
    }

    static void Bench(string name, Transform parent, Vector3 position, float yaw)
    {
        var bench = Group(name, parent); bench.localPosition = position; bench.localRotation = Quaternion.Euler(0, yaw, 0);
        Rounded("Bench seat", bench, new(0, .44f, 0), 1.65f, .48f, .08f, .07f, teak);
        var back = Rounded("Bench back", bench, new(0, .76f, .22f), 1.65f, .34f, .07f, .06f, teak);
        back.transform.localRotation = Quaternion.Euler(72, 0, 0);
        for (int i = -1; i <= 1; i += 2)
        {
            Box("Bench leg " + i, bench, new(i * .62f, .21f, 0), new(.09f, .42f, .4f), trim);
            Box("Bench foot " + i, bench, new(i * .62f, .03f, 0), new(.2f, .06f, .48f), trim);
            Box("Bench arm " + i, bench, new(i * .62f, .58f, -.06f), new(.07f, .28f, .12f), trim);
        }
    }

    static void Parasol(string name, Transform parent, Vector3 position, int lean)
    {
        var set = Group(name, parent); set.localPosition = position;
        Rounded("Table top", set, new(0, .73f, 0), 1.15f, 1.15f, .06f, .55f, trim);
        Primitive("Table column", PrimitiveType.Cylinder, set, new(0, .36f, 0), new(.09f, .36f, .09f), trim);
        Primitive("Table base", PrimitiveType.Cylinder, set, new(0, .035f, 0), new(.46f, .035f, .46f), trim);
        Primitive("Parasol pole", PrimitiveType.Cylinder, set, new(0, 1.28f, 0), new(.045f, 1.28f, .045f), teak);
        if (!Meshes.TryGetValue("Parasol canopy", out var shell))
        {
            var vertices = new List<Vector3> { new(0, .46f, 0) }; var indices = new List<int>();
            const int count = 24;
            for (int i = 0; i <= count; i++)
            {
                float a = i * Mathf.PI * 2 / count;
                float dip = i % 3 == 0 ? .04f : 0;
                vertices.Add(new Vector3(Mathf.Cos(a) * 1.5f, -dip, Mathf.Sin(a) * 1.5f));
                if (i > 0) indices.AddRange(new[] { 0, i + 1, i });
            }
            shell = SaveMesh("Parasol canopy", new Mesh { vertices = vertices.ToArray(), triangles = indices.ToArray() });
        }
        var top = MeshObject("Canopy", set, new(0, 2.1f, 0), shell, canopy);
        top.transform.localRotation = Quaternion.Euler(lean * 2.5f, 0, lean * 1.5f);
        Ball("Canopy finial", set, new(0, 2.6f, 0), Vector3.one * .1f, teak);
        for (int i = 0; i < 2; i++)
            Box("Chair " + i, set, new(i == 0 ? -.92f : .92f, .43f, i == 0 ? .16f : -.16f), new(.44f, .06f, .44f), teak);
    }

    static void Signpost(string name, Transform parent, Vector3 position)
    {
        var post = Group(name, parent); post.localPosition = position;
        Primitive("Post", PrimitiveType.Cylinder, post, new(0, 1.1f, 0), new(.09f, 1.1f, .09f), teak);
        Box("Post base", post, new(0, .07f, 0), new(.32f, .14f, .32f), paving);
        Ball("Post cap", post, new(0, 2.24f, 0), Vector3.one * .16f, teak);
        var upper = Board("Upper board", post, new(-.62f, 1.92f, 0), -14);
        WallText("Upper label", upper, "STUDIO", new(.06f, 0, -.04f), .028f, new Color(.98f, .99f, .96f));
        var lower = Board("Lower board", post, new(.64f, 1.5f, 0), 13);
        WallText("Lower label", lower, "1ST TEE", new(-.06f, 0, -.04f), .028f, new Color(.98f, .99f, .96f));
    }

    static Transform Board(string name, Transform parent, Vector3 position, float yaw)
    {
        var board = Group(name, parent); board.localPosition = position; board.localRotation = Quaternion.Euler(0, yaw, 0);
        Box("Board face", board, Vector3.zero, new(1.24f, .34f, .05f), accent);
        Box("Board edge", board, new(0, 0, .01f), new(1.3f, .4f, .04f), trim);
        return board;
    }

    static void Palm(string name, Transform parent, Vector3 position, float scale, int fronds, float lean)
    {
        var palm = Group(name, parent); palm.localPosition = position; palm.localScale = Vector3.one * scale;
        var bark = Material("Palm bark", "B99366", .15f);
        // Stacked, tapering collars give the trunk its lean and a little silhouette texture.
        const int rings = 13;
        var tip = Vector3.zero;
        for (int i = 0; i < rings; i++)
        {
            float t = i / (float)(rings - 1);
            float bend = Mathf.Sin(t * 1.35f) * lean * .035f;
            var at = new Vector3(bend, .28f + t * 3.5f, bend * .35f);
            float r = Mathf.Lerp(.23f, .14f, t);
            Primitive("Trunk collar " + i, PrimitiveType.Cylinder, palm, at, new(r, .17f, r), bark);
            tip = at;
        }
        Primitive("Crown boss", PrimitiveType.Cylinder, palm, tip + new Vector3(0, .16f, 0), new(.19f, .1f, .19f), bark);
        for (int i = 0; i < 3; i++)
            Ball("Coconut " + i, palm, tip + new Vector3(Mathf.Cos(i * 2.2f) * .19f, .02f, Mathf.Sin(i * 2.2f) * .19f), Vector3.one * .11f, Material("Coconut husk", "8B6A46", .2f));

        if (!Meshes.TryGetValue("Palm frond", out var blade))
        {
            var vertices = new List<Vector3>(); var indices = new List<int>();
            const int steps = 7;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float droop = -Mathf.Pow(t, 2.1f) * 1.35f;
                float half = Mathf.Sin(t * Mathf.PI * .92f) * .30f * (1 - t * .45f);
                var spine = new Vector3(0, droop, t * 2.6f);
                vertices.Add(spine + new Vector3(-half, 0, -.04f));
                vertices.Add(spine + new Vector3(0, half * .16f, 0));
                vertices.Add(spine + new Vector3(half, 0, -.04f));
                if (i == 0) continue;
                int k = i * 3;
                indices.AddRange(new[] { k, k - 3, k - 2, k, k - 2, k + 1, k + 1, k - 2, k - 1, k + 1, k - 1, k + 2 });
            }
            blade = SaveMesh("Palm frond", new Mesh { vertices = vertices.ToArray(), triangles = indices.ToArray() });
        }
        var jade = Material("Frond jade", "579B58"); var sage = Material("Frond sage", "7FB566");
        jade.SetFloat("_Cull", 0); sage.SetFloat("_Cull", 0);
        // Fronds hang off one sway pivot so MenuSway can breathe the whole crown at once.
        var crown = Group("Sway palm crown", palm); crown.localPosition = tip + new Vector3(0, .2f, 0);
        for (int i = 0; i < fronds; i++)
        {
            float a = i * 360f / fronds + lean;
            var frond = MeshObject("Frond " + i, crown, Vector3.zero, blade, i % 3 == 0 ? sage : jade);
            frond.transform.localRotation = Quaternion.Euler(-24 - i % 3 * 9, a, 0);
            frond.transform.localScale = Vector3.one * (.9f + i % 4 * .09f);
        }
    }

    static void Plant(string name, Transform parent, Vector3 position, float scale, int leaves)
    {
        var plant = Group(name, parent); plant.localPosition = position; plant.localScale = Vector3.one * scale;
        if (!Meshes.TryGetValue("Plaza planter", out var pot))
        {
            Vector2[] profile = { new(.21f, 0), new(.23f, .04f), new(.30f, .44f), new(.32f, .5f), new(.28f, .5f), new(.26f, .43f), new(.21f, .04f) };
            pot = Lathe("Plaza planter", profile, 28);
        }
        MeshObject("Planter", plant, Vector3.zero, pot, Material("Planter terracotta", "D8A886", .2f));
        Primitive("Soil", PrimitiveType.Cylinder, plant, new(0, .44f, 0), new(.5f, .012f, .5f), Material("Potting soil", "615849"));
        if (!Meshes.TryGetValue("Shrub leaf", out var blade))
        {
            Vector3[] vertices = { new(0, 0, 0), new(-.12f, .2f, .04f), new(0, .21f, -.01f), new(.12f, .2f, .04f), new(-.09f, .4f, .12f), new(0, .41f, .09f), new(.09f, .4f, .12f), new(0, .55f, .23f) };
            int[] triangles = { 0, 1, 2, 0, 2, 3, 1, 4, 5, 1, 5, 2, 2, 5, 6, 2, 6, 3, 4, 7, 5, 5, 7, 6 };
            blade = SaveMesh("Shrub leaf", new Mesh { vertices = vertices, triangles = triangles });
        }
        var jade = Material("Shrub jade", "4C8460"); var bloom = Material("Shrub bloom", "E98AA6");
        jade.SetFloat("_Cull", 0);
        for (int i = 0; i < leaves; i++)
        {
            float a = i * 137.5f * Mathf.Deg2Rad, height = .52f + (i % 4) * .15f;
            var end = new Vector3(Mathf.Sin(a) * .2f, height, Mathf.Cos(a) * .2f);
            var from = new Vector3(0, .44f, 0);
            var stem = Primitive("Leaf stem " + i, PrimitiveType.Cylinder, plant, (from + end) / 2, new(.014f, (end - from).magnitude / 2, .014f), jade);
            stem.transform.localRotation = Quaternion.FromToRotation(Vector3.up, end - from);
            var leaf = MeshObject("Leaf " + i, plant, end, blade, jade);
            leaf.transform.localRotation = Quaternion.Euler(22 + i % 4 * 11, i * 137.5f, 0);
            leaf.transform.localScale = Vector3.one * (.7f + i % 3 * .14f);
            if (i % 4 == 1) Ball("Bloom " + i, plant, end + new Vector3(0, .1f, 0), Vector3.one * .07f, bloom);
        }
    }

    // ---------------------------------------------------------------- viewpoint

    static void Anchors(Transform rail)
    {
        // The viewer stands here and never moves. Looking left finds the studio pavilion,
        // ahead the course over the balustrade, right the pro shop — the look input owns
        // the rotation, this only fixes where the eyes are.
        Anchor(rail, "Viewpoint", new(0, 1.62f, -6.4f), new(0, 1.35f, 8));
        // The menu hangs here as world-space geometry, square to the viewpoint, close enough to read and far
        // enough that the plaza still reads as a place behind it. It faces the same way the viewpoint does,
        // not back at it: a UI panel's readable side is the one its forward axis points away from.
        Anchor(rail, "MenuBoard", new(0, 1.55f, -2.9f), new(0, 1.5f, 8));
        Anchor(rail, "MiiStand", new(-4.6f, 0, .4f), new(-1f, 1f, -7f));
    }

    static void Anchor(Transform parent, string name, Vector3 position, Vector3 lookAt)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = Quaternion.LookRotation((lookAt - position).normalized, Vector3.up);
    }

    // ---------------------------------------------------------------- helpers

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
        if (old) { EditorUtility.CopySerialized(mesh, old); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(old); Meshes[name] = old; return old; }
        AssetDatabase.CreateAsset(mesh, Root + "/Meshes/" + name + ".asset"); Meshes[name] = mesh; return mesh;
    }
    static GameObject MeshObject(string name, Transform parent, Vector3 position, Mesh mesh, Material material)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false);
        go.transform.localPosition = position; go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = material; go.isStatic = true; return go;
    }

    // Soft-cornered slab — bench seats and table tops, where a hard cube reads as too sharp.
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
            var built = new Mesh(); built.SetVertices(vertices); built.SetTriangles(triangles, 0);
            mesh = SaveMesh(key, built);
        }
        return MeshObject(name, parent, position, mesh, material);
    }

    // Revolves a 2D profile (radius, height) into a closed solid — planters, balusters.
    static Mesh Lathe(string name, Vector2[] profile, int segments)
    {
        var vertices = new List<Vector3>(); var indices = new List<int>();
        for (int p = 0; p < profile.Length; p++)
        for (int i = 0; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2 / segments;
            vertices.Add(new Vector3(Mathf.Cos(a) * profile[p].x, profile[p].y, Mathf.Sin(a) * profile[p].x));
            if (p == 0 || i == 0) continue;
            int k = p * (segments + 1) + i;
            indices.AddRange(new[] { k, k - segments - 2, k - 1, k, k - segments - 1, k - segments - 2 });
        }
        return SaveMesh(name, new Mesh { vertices = vertices.ToArray(), triangles = indices.ToArray() });
    }

    // Hip roof: rectangular eaves rising to a ridge inset from both ends.
    static void Roof(string name, Transform parent, Vector3 position, float width, float depth, float height, float inset, Material material)
    {
        string key = System.FormattableString.Invariant($"Roof {width:0.00} {depth:0.00} {height:0.00} {inset:0.00}");
        if (!Meshes.TryGetValue(key, out var mesh))
        {
            Vector3 a = new(-width / 2, 0, -depth / 2), b = new(width / 2, 0, -depth / 2);
            Vector3 c = new(width / 2, 0, depth / 2), d = new(-width / 2, 0, depth / 2);
            Vector3 e = new(-width / 2 + inset, height, 0), f = new(width / 2 - inset, height, 0);
            var vertices = new[] { a, b, c, d, e, f };
            int[] triangles = { 0, 4, 5, 0, 5, 1, 2, 5, 4, 2, 4, 3, 0, 3, 4, 2, 1, 5, 0, 1, 2, 0, 2, 3 };
            mesh = SaveMesh(key, new Mesh { vertices = vertices, triangles = triangles });
        }
        MeshObject(name, parent, position, mesh, material);
    }

    static void WallText(string name, Transform parent, string text, Vector3 position, float size, Color color)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position;
        // TextMesh faces local -Z, which is the plaza's camera side.
        var label = go.AddComponent<TextMesh>(); label.text = text; label.fontSize = 72; label.characterSize = size;
        label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center; label.color = color;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        go.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
    }
}

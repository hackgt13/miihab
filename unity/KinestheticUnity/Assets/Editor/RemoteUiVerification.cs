using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kinesthetic;
using Kinesthetic.UI;
using Kinesthetic.UI.Remote;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// The remote UI library without a network: each screen is loaded into a runtime panel, mirrored into a
/// second panel through the wire form, mutated, diffed and patched, and a press is resolved and fired the
/// way the host does it. Every step is checked by re-serializing the replica and comparing trees.
///
/// Field changes and child changes are separate rounds on purpose: a changed child count replaces the
/// whole subtree, which would hide whether `set` ops — the common case at 10 Hz — apply correctly.
public static class RemoteUiVerification
{
    const string Settings = "Assets/Kinesthetic/Golf/GolfPanel.asset";
    const string Sheet = "Assets/Kinesthetic/Menu/MainMenu.uss";

    static readonly string[] Screens =
    {
        "Assets/Kinesthetic/Golf/Golf.uxml",
        "Assets/Kinesthetic/Rehab/RehabDock.uxml",
        "Assets/Kinesthetic/Rehab/RehabCrown.uxml",
        "Assets/Kinesthetic/Rehab/RehabFocus.uxml",
        "Assets/Kinesthetic/Rehab/RehabBrief.uxml",
        "Assets/Kinesthetic/Menu/Navigation.uxml",
        "Assets/Kinesthetic/Bowling/Bowling.uxml",
        "Assets/Kinesthetic/Menu/MainMenu.uxml",
        "Assets/Kinesthetic/Menu/Gallery.uxml",
        "Assets/Kinesthetic/Menu/Friends.uxml",
    };

    /// Stands in for the golf minimap: a live texture the registry cannot name, filled locally by name.
    sealed class StubImages : IReplicaImageSource
    {
        public RenderTexture texture;
        public Texture Resolve(string board, string elementName, string textureName) => texture;
    }

    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    [MenuItem("Kinesthetic/Remote UI/Verify mirror, diff and press")]
    public static string Run()
    {
        var lines = new List<string>();
        int failures = 0;
        void Pass(string m) { lines.Add("PASS: " + m); Debug.Log("PASS: " + m); }
        void Fail(string m) { failures++; lines.Add("FAIL: " + m); Debug.LogError("FAIL: " + m); }

        RemoteUiRegistries.Refresh();   // ids must resolve on both sides of the mirror
        var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(Settings);
        if (!settings) { Fail("no PanelSettings at " + Settings); return Summary(lines, failures); }

        try { Pass(VocabHash()); } catch (Exception e) { Fail("vocab hash: " + e.Message); }

        foreach (var path in Screens)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            try { Screen(path, name, settings, Pass); }
            catch (Exception e) { Fail($"{name}: {e.Message}"); }
        }

        try { Pass(LateBoard(settings)); } catch (Exception e) { Fail("late replica: " + e.Message); }
        return Summary(lines, failures);
    }

    // The hash must move when the table's meaning does: an attribute more, or children mirrored differently.
    static string VocabHash()
    {
        var table = new List<PanelWire.Entry>(PanelWire.Vocabulary);
        string same = PanelWire.ComputeVocabHash(table);
        Check(same == PanelWire.VocabHash && same.Length == 16, "an unaltered copy of the table hashes differently");
        int at = table.FindIndex(e => e.Id == "KChip");
        var chip = table[at];
        var attributes = new List<string>(chip.Attributes) { "extra" };
        table[at] = new PanelWire.Entry(chip.Id, chip.Type, chip.Make, chip.Kids, attributes.ToArray());
        string moreAttributes = PanelWire.ComputeVocabHash(table);
        table[at] = new PanelWire.Entry(chip.Id, chip.Type, chip.Make, PanelWire.Children.All, chip.Attributes);
        string otherKids = PanelWire.ComputeVocabHash(table);
        Check(moreAttributes != same, "an added attribute did not change the hash");
        Check(otherKids != same && otherKids != moreAttributes, "a different children rule did not change the hash");
        return $"vocab hash {same} changes with an entry's attributes and its children rule";
    }

    static string Summary(List<string> lines, int failures)
    {
        string summary = failures == 0 ? $"PASS: remote UI — {lines.Count} checks." : $"FAIL: remote UI — {failures} of {lines.Count} checks failed.";
        Debug.Log(summary + "\n" + string.Join("\n", lines));
        return summary;
    }

    static void Screen(string path, string name, PanelSettings settings, Action<string> pass)
    {
        var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
        Check(uxml, "missing " + path);
        var source = new GameObject("Remote UI source " + name) { hideFlags = HideFlags.HideAndDontSave };
        var replica = new GameObject("Remote UI replica " + name) { hideFlags = HideFlags.HideAndDontSave };
        var stub = new StubImages();
        RenderTexture liveSource = null;
        try
        {
            var sourceDoc = source.AddComponent<UIDocument>();
            sourceDoc.panelSettings = settings;
            sourceDoc.visualTreeAsset = uxml;
            var replicaDoc = replica.AddComponent<UIDocument>();
            replicaDoc.panelSettings = settings;
            var root = sourceDoc.rootVisualElement;
            var mirror = replicaDoc.rootVisualElement;
            Check(root?.panel != null && mirror?.panel != null, "documents did not get panels");
            liveSource = new RenderTexture(8, 8, 0) { name = "Live verification map" };
            stub.texture = new RenderTexture(8, 8, 0) { name = liveSource.name };
            PanelWire.ImageSource = stub;

            // Snapshot: serialize, cross JSON text, rebuild, re-serialize.
            var tree = PanelWire.Serialize(root);
            var wire = JObject.Parse(tree.ToString(Formatting.None));
            Equal(tree, wire, "tree through JSON text");
            PanelWire.Rebuild(mirror, wire, name);
            Equal(tree, PanelWire.Serialize(mirror), "snapshot");
            pass($"{name}: snapshot rebuilds equal ({Count(tree)} nodes, {tree.ToString(Formatting.None).Length} bytes)");

            // Fields only: every op must be a `set`.
            int fields = MutateFields(root);
            var current = Round(root, mirror, tree, name, "field mutations", expectReplace: false, out int setOps);
            pass($"{name}: {fields} field mutations → {setOps} set ops apply equal");

            // Children come and go: the parent is replaced.
            MutateChildren(root, liveSource);
            current = Round(root, mirror, current, name, "child mutations", expectReplace: true, out int replaceOps);
            pass($"{name}: children added → {replaceOps} ops with a replace apply equal");

            // Fields taken back, including attributes on the new children: `set` ops carrying nulls.
            RevertFields(root);
            current = Round(root, mirror, current, name, "field reverts", expectReplace: false, out int unsetOps);
            pass($"{name}: field reverts → {unsetOps} set ops apply equal");

            RevertChildren(root);
            Round(root, mirror, current, name, "child reverts", expectReplace: true, out int removeOps);
            pass($"{name}: children removed → {removeOps} ops with a replace apply equal");

            Presses(root, mirror, name, pass);
        }
        finally
        {
            PanelWire.ImageSource = null;
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(replica);
            if (liveSource) UnityEngine.Object.DestroyImmediate(liveSource);
            if (stub.texture) UnityEngine.Object.DestroyImmediate(stub.texture);
        }
    }

    /// Diff the source against `before`, cross JSON text, apply to the replica, compare. Returns the new tree.
    static JObject Round(VisualElement root, VisualElement mirror, JObject before, string name, string what, bool expectReplace, out int count)
    {
        var after = PanelWire.Serialize(root);
        var ops = PanelWire.Diff(before, after, name);
        Check(ops.Count > 0, what + " produced no ops");
        bool replaced = false;
        foreach (JObject op in ops) replaced |= (string)op["op"] == "replace";
        Check(replaced == expectReplace, expectReplace ? what + " produced no replace op" : what + " produced a replace op; set ops were not exercised");
        ops = JArray.Parse(ops.ToString(Formatting.None));
        PanelWire.Apply(mirror, ops, name);
        Equal(after, PanelWire.Serialize(mirror), what);
        Check(PanelWire.Diff(after, PanelWire.Serialize(root), name).Count == 0, "an unchanged tree produced ops");
        count = ops.Count;
        return after;
    }

    // What a live screen does to elements it keeps: text, classes, K attributes, inline styles, enabling,
    // picking, an image and a stylesheet.
    static int MutateFields(VisualElement root)
    {
        int n = 0;
        var label = root.Q<Label>(); if (label != null) { label.text = "Changed copy"; n++; }
        var buttons = root.Query<Button>().ToList();
        if (buttons.Count > 0) { buttons[0].AddToClassList("k-hot"); n++; }
        if (buttons.Count > 1) { buttons[1].SetEnabled(false); n++; }
        var hidden = root.Q(className: "hidden"); if (hidden != null) { hidden.RemoveFromClassList("hidden"); n++; }

        var kButton = root.Q<KButton>(); if (kButton != null) { kButton.tone = KButton.Tone.Primary; kButton.size = KButton.Size.Large; n++; }
        var readout = root.Q<KReadout>(); if (readout != null) { readout.value = "42"; readout.caption = ""; readout.tone = KReadout.Tone.Good; n++; }
        var arc = root.Q<KArc>(); if (arc != null) { arc.fraction = .5f; arc.form = KArc.Form.Segmented; arc.segments = 8; n++; }
        var meter = root.Q<KMeter>(); if (meter != null) { meter.filled = 3; meter.fraction = .25f; n++; }
        var text = root.Q<KText>(); if (text != null) { text.size = KText.Size.Title; text.tone = KText.Tone.Soft; n++; }
        var chip = root.Q<KChip>(); if (chip != null) { chip.text = "AirPods connected"; chip.state = KChip.State.Good; n++; }
        var tag = root.Q<KTag>(); if (tag != null) { tag.tone = KTag.Tone.Trouble; n++; }
        var eyebrow = root.Q<KEyebrow>(); if (eyebrow != null) { eyebrow.backdrop = KEyebrow.Backdrop.Dark; n++; }
        var surface = root.Q<KSurface>(); if (surface != null) { surface.tone = KSurface.Tone.Glass; surface.density = KSurface.Density.Flush; n++; }
        var scroll = root.Q<ScrollView>(); if (scroll != null) { scroll.mode = ScrollViewMode.Horizontal; scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden; n++; }
        var image = root.Q<Image>(); if (image != null) { image.image = Driver(); image.scaleMode = ScaleMode.ScaleToFit; n++; }

        var container = Container(root);
        var styled = buttons.Count > 0 ? buttons[0] : container;
        styled.style.left = 12; styled.style.top = Length.Percent(10); styled.style.width = Length.Percent(50); styled.style.height = Length.Auto();
        styled.style.opacity = .5f; styled.style.flexGrow = 1; styled.style.position = Position.Absolute;
        styled.style.visibility = Visibility.Hidden; styled.style.display = DisplayStyle.Flex;
        styled.style.backgroundColor = Palette.Cerulean50; styled.style.color = Palette.Sand00; styled.style.unityBackgroundImageTintColor = Palette.Jungle40;
        styled.style.translate = new Translate(4, Length.Percent(10), 0);
        styled.style.rotate = new Rotate(new Angle(15, AngleUnit.Degree));
        styled.style.scale = new Scale(new Vector3(1.5f, 1.5f, 1));
        n++;
        container.pickingMode = PickingMode.Ignore; n++;
        container.style.backgroundImage = new StyleBackground(Background.FromTexture2D(Driver())); n++;
        container.styleSheets.Add(MenuSheet()); n++;
        return n;
    }

    // Children that come and go: BowlingHud's frames, a checklist, and two images — one registered, one live.
    static void MutateChildren(VisualElement root, RenderTexture live)
    {
        var container = Container(root);
        for (int i = 0; i < 3; i++)
        {
            var frame = new VisualElement { name = "verify-frame-" + i }; frame.AddToClassList("frame");
            var number = new Label((i + 1).ToString()); number.AddToClassList("frame-number"); frame.Add(number);
            var marks = new Label(" "); marks.AddToClassList("frame-marks"); frame.Add(marks);
            var score = new Label("–"); score.AddToClassList("frame-score"); frame.Add(score);
            container.Add(frame);
        }
        var steps = new KSteps { numbering = KSteps.Numbering.Progress, name = "verify-steps" };
        steps.Add(new KStep("Camera", "Looking for you"));
        steps.Add(new KStep("AirPods", "Put them on"));
        steps.Step(0).Done = true;
        container.Add(steps);
        if (root.Q<Image>() == null) container.Add(new Image { name = "verify-image", image = Driver(), scaleMode = ScaleMode.ScaleToFit });
        container.Add(new Image { name = "live-map", image = live, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore });
    }

    static void RevertFields(VisualElement root)
    {
        var buttons = root.Query<Button>().ToList();
        var container = Container(root);
        var styled = buttons.Count > 0 ? buttons[0] : container;
        styled.style.left = StyleKeyword.Null; styled.style.opacity = StyleKeyword.Null; styled.style.scale = StyleKeyword.Null;
        styled.style.translate = StyleKeyword.Null; styled.style.visibility = StyleKeyword.Null; styled.style.backgroundColor = StyleKeyword.Null;
        if (buttons.Count > 0) buttons[0].RemoveFromClassList("k-hot");
        if (buttons.Count > 1) buttons[1].SetEnabled(true);
        var steps = root.Q<KSteps>("verify-steps");
        steps.Step(0).Done = false;
        steps.numbering = KSteps.Numbering.Number;
        var image = root.Q<Image>(); if (image != null && image.name != "live-map") image.image = null;
        container.style.backgroundImage = StyleKeyword.Null;
        container.pickingMode = PickingMode.Position;
        container.styleSheets.Remove(MenuSheet());
    }

    static void RevertChildren(VisualElement root)
    {
        root.Q("verify-frame-1").RemoveFromHierarchy();
        root.Q<KSteps>("verify-steps").Step(1).RemoveFromHierarchy();
        root.Q("live-map").RemoveFromHierarchy();
    }

    static VisualElement Container(VisualElement root)
    {
        var kids = PanelWire.LogicalChildren(root);
        Check(kids.Count > 0, "screen has no children");
        return kids[0];
    }

    static Texture2D Driver()
    {
        var driver = Resources.Load<Texture2D>("GolfHUD/Driver");
        Check(driver, "GolfHUD/Driver texture missing");
        return driver;
    }

    static StyleSheet MenuSheet()
    {
        var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(Sheet);
        Check(sheet, "missing " + Sheet);
        return sheet;
    }

    // A press names a board, a path and a button; the host resolves and fires it as the keyboard would.
    static void Presses(VisualElement root, VisualElement mirror, string name, Action<string> pass)
    {
        Button button = null;
        foreach (var candidate in root.Query<Button>().ToList())
            if (!string.IsNullOrEmpty(candidate.name) && candidate.enabledInHierarchy) button = candidate;
        if (button == null) { pass($"{name}: no named button to press"); return; }

        string path = PanelWire.PathOf(root, button);
        Check(path != null, $"'{button.name}' has no path");
        Check(RemoteUiHost.Resolve(root, path, button.name) == button, $"path '{path}' did not resolve to '{button.name}'");
        var twin = PanelWire.Resolve(mirror, path);
        Check(twin is Button && twin.name == button.name, "the replica's element at the same path is a different one");
        Check(PanelWire.PathOf(mirror, twin) == path, "the replica computes a different path for the same button");

        bool clicked = false, withInfo = false;
        button.clicked += () => clicked = true;
        button.clickable.clickedWithEventInfo += _ => withInfo = true;
        PanelWire.Click(button);
        Check(clicked && withInfo, "clicked handlers did not fire on a programmatic press");

        button.SetEnabled(false);
        Check(RemoteUiHost.Resolve(root, path, button.name) == null, "a disabled button was accepted");
        button.SetEnabled(true);
        button.parent.SetEnabled(false);
        Check(RemoteUiHost.Resolve(root, path, button.name) == null, "a button under a disabled parent was accepted");
        button.parent.SetEnabled(true);
        Check(RemoteUiHost.Resolve(root, path, "not-" + button.name) == null, "a renamed button was accepted");
        Check(RemoteUiHost.Resolve(root, "", button.name) == null, "a non-button path was accepted");
        pass($"{name}: press '{button.name}' at {path} resolves and fires clicked; disabled, shadowed and renamed refused");
    }

    // The Quest's scene follows the Mac's a second late, so a replica is normally enabled after the host
    // reported its board. The client is driven directly — no socket exists in edit mode — and must ask for a
    // snapshot once the board appears, fill it from that snapshot, re-ask on a rev gap, and apply after.
    static string LateBoard(PanelSettings settings)
    {
        const string id = "verify.late";
        var clientGo = new GameObject("Remote UI verification client") { hideFlags = HideFlags.HideAndDontSave };
        var boardGo = new GameObject("Remote UI late board") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var client = clientGo.AddComponent<RemoteUiClient>();   // no Awake in edit mode: no socket
            boardGo.SetActive(false);
            boardGo.AddComponent<UIDocument>().panelSettings = settings;
            var board = boardGo.AddComponent<RemoteBoard>();
            board.id = id; board.role = RemoteBoard.Role.Replica;

            var sample = new VisualElement();
            sample.Add(new KButton { name = "go", text = "Go" });
            var tree = PanelWire.Serialize(sample);
            JObject Envelope(string type, long rev) => new JObject { ["type"] = type, ["proto"] = PanelWire.Proto, ["vocab"] = PanelWire.VocabHash, ["rev"] = rev };
            string Snapshot(long rev) { var m = Envelope("ui.snapshot", rev); m["boards"] = new JArray { new JObject { ["id"] = id, ["tree"] = tree } }; return m.ToString(Formatting.None); }
            string Patch(long baseRev, long rev, params JObject[] ops) { var m = Envelope("ui.patch", rev); m["base"] = baseRev; m["ops"] = new JArray(ops); return m.ToString(Formatting.None); }

            client.Receive("{\"type\":\"ui.welcome\",\"client\":\"c1\"}");
            Check(client.ResyncWanted && client.ResyncRequests == 1, "welcome did not ask for a resync");
            client.Receive(Snapshot(1));
            Check(!client.ResyncWanted && client.Rev == 1 && client.HostBoards.Contains(id), "the snapshot's board was not remembered");

            boardGo.SetActive(true);
            Check(board.Root != null && board.Root.childCount == 0, "the late board did not come up empty");
            client.Poll();
            Check(client.ResyncWanted && client.ResyncRequests == 2, "a replica enabled after its board arrived did not ask for a resync");
            client.Receive(Snapshot(2));
            Check(!client.ResyncWanted && client.Rev == 2, "the answering snapshot was not taken");
            Equal(tree, PanelWire.Serialize(board.Root), "late board");
            client.Poll();
            Check(!client.ResyncWanted, "a filled board kept asking");

            client.Receive(Patch(7, 8));
            Check(client.ResyncWanted && client.Rev == 2, "a patch against the wrong base was not answered with a resync");
            client.Receive(Snapshot(9));
            var set = new JObject { ["op"] = "set", ["board"] = id, ["p"] = "0", ["f"] = new JObject { ["x"] = "Gone" } };
            client.Receive(Patch(9, 10, set));
            Check(client.Rev == 10 && board.Root.Q<Button>("go")?.text == "Gone", "a patch on the late board did not apply");
            client.Receive(Patch(10, 11, new JObject { ["op"] = "unboard", ["id"] = id }));
            Check(client.Rev == 11 && !client.HostBoards.Contains(id) && board.Root.childCount == 0, "unboard did not empty the board");
            client.Poll();
            Check(!client.ResyncWanted, "an unboarded replica asked for a resync");
            return "late replica: asks for a resync when enabled after its board, fills from the snapshot, re-asks on a rev gap, applies set and unboard";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(boardGo);
            UnityEngine.Object.DestroyImmediate(clientGo);
        }
    }

    static int Count(JObject node)
    {
        int n = 1;
        if (node["k"] is JArray kids) foreach (JObject kid in kids) n += Count(kid);
        return n;
    }

    static void Equal(JToken expected, JToken actual, string what)
    {
        if (JToken.DeepEquals(expected, actual)) return;
        throw new Exception($"{what}: trees differ at {FirstDifference(expected, actual, "$")}");
    }

    static string FirstDifference(JToken a, JToken b, string path)
    {
        if (a == null || b == null || a.Type != b.Type) return $"{path}: {Short(a)} vs {Short(b)}";
        switch (a)
        {
            case JObject oa:
                var ob = (JObject)b;
                foreach (var property in oa.Properties())
                    if (!JToken.DeepEquals(property.Value, ob[property.Name])) return FirstDifference(property.Value, ob[property.Name], path + "." + property.Name);
                foreach (var property in ob.Properties())
                    if (oa[property.Name] == null) return $"{path}.{property.Name}: missing vs {Short(property.Value)}";
                return path + ": ?";
            case JArray aa:
                var ab = (JArray)b;
                if (aa.Count != ab.Count) return $"{path}: {aa.Count} items vs {ab.Count}";
                for (int i = 0; i < aa.Count; i++)
                    if (!JToken.DeepEquals(aa[i], ab[i])) return FirstDifference(aa[i], ab[i], $"{path}[{i}]");
                return path + ": ?";
            default:
                return $"{path}: {Short(a)} vs {Short(b)}";
        }
    }

    static string Short(JToken token)
    {
        if (token == null) return "missing";
        string text = token.ToString(Formatting.None);
        return text.Length > 80 ? text.Substring(0, 80) + "…" : text;
    }
}

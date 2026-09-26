using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.UI.Remote
{
    /// The wire form of a board: how a visual tree becomes JSON on the Mac and a visual tree again on the
    /// headset. The vocabulary is a closed table, written out by hand, because IL2CPP strips whatever it
    /// cannot see a call to — a factory found by reflection or an attribute discovered at runtime works in
    /// the editor and returns null on the device. Every constructor and every attribute get/set here is an
    /// explicit call the linker keeps.
    ///
    /// Logical children. A node's `k` is the element's `Children()` — what a screen placed inside it —
    /// never the internal hierarchy of a composite. The rule per type:
    ///
    ///   * A type whose constructor builds children owns all of them (KChip's dot and label, KReadout's
    ///     figure and caption, KStep's badge and text). Its content is its attributes, so it is a leaf on
    ///     the wire and the replica's constructor rebuilds the same internals: `Children.None`.
    ///   * A type whose constructor builds nothing has only screen-placed children (VisualElement, KArc,
    ///     KSurface, KSteps, Label): all mirrored, `Children.All`.
    ///   * ScrollView redirects `Add` through its contentContainer, so `Children()` already skips the
    ///     viewport and scrollers: `Children.All`.
    ///   * Button holds screen children too — the gallery cards are Buttons full of labels — and builds an
    ///     internal Image and TextElement only once an `iconImage` is set. Those two are skipped by the
    ///     class Unity gives them: `Children.ButtonContent`.
    ///
    /// Paths index into these logical lists, so a press on the headset names the same element on the Mac.
    public static class PanelWire
    {
        public const int Proto = 1;

        /// Whether a type's `Children()` are the screen's (mirrored) or its own (rebuilt by its constructor).
        public enum Children { None, All, ButtonContent }

        /// One row of the vocabulary. `Read` writes every attribute into `a`; `Write` applies whichever are
        /// present. Both are explicit code, never a property lookup by name.
        public sealed class Entry
        {
            public readonly string Id;
            public readonly Type Type;
            public readonly Func<VisualElement> Make;
            public readonly Children Kids;
            public readonly string[] Attributes;
            public readonly Action<VisualElement, JObject> Read, Write;
            /// After a child was replaced under this element: KSteps renumbers, nothing else cares.
            public readonly Action<VisualElement> ChildrenChanged;

            public Entry(string id, Type type, Func<VisualElement> make, Children kids, string[] attributes = null,
                         Action<VisualElement, JObject> read = null, Action<VisualElement, JObject> write = null,
                         Action<VisualElement> childrenChanged = null)
            {
                Id = id; Type = type; Make = make; Kids = kids;
                Attributes = attributes ?? Array.Empty<string>();
                Read = read ?? ((_, __) => { }); Write = write ?? ((_, __) => { });
                ChildrenChanged = childrenChanged;
            }
        }

        /// The node keys a `set` op may carry, in the order they are applied. Classes go last on purpose: a
        /// K attribute setter moves its variant class to the end of the list, so applying `c` after `a`
        /// leaves the replica's class order exactly the host's.
        static readonly string[] Fields = { "n", "x", "d", "pi", "a", "s", "ss", "i", "c" };

        /// The inline style subset, in wire order.
        static readonly string[] StyleKeys =
        {
            "display", "visibility", "position", "left", "top", "right", "bottom", "width", "height", "opacity",
            "translate", "rotate", "scale", "flexGrow", "backgroundColor", "color", "unityBackgroundImageTintColor"
        };

        /// Fills a `{"rt": …}` image on the headset. Set by RemoteUiClient; null leaves such images empty.
        public static IReplicaImageSource ImageSource { get; set; }

        // ---- the table -------------------------------------------------------------------------------------

        static readonly Entry[] Table =
        {
            new Entry("VisualElement", typeof(VisualElement), () => new VisualElement(), Children.All),
            new Entry("Label", typeof(Label), () => new Label(), Children.All),
            new Entry("TextElement", typeof(TextElement), () => new TextElement(), Children.All),
            new Entry("Button", typeof(Button), () => new Button(), Children.ButtonContent),
            new Entry("Image", typeof(Image), () => new Image(), Children.None,
                new[] { "scaleMode" },
                (e, a) => { var i = (Image)e; a["scaleMode"] = i.scaleMode.ToString(); },
                (e, a) => { var i = (Image)e; if (E(a, "scaleMode", out ScaleMode mode)) i.scaleMode = mode; }),
            new Entry("ScrollView", typeof(ScrollView), () => new ScrollView(), Children.All,
                new[] { "mode", "horizontalScrollerVisibility", "verticalScrollerVisibility" },
                (e, a) =>
                {
                    var s = (ScrollView)e;
                    a["mode"] = s.mode.ToString();
                    a["horizontalScrollerVisibility"] = s.horizontalScrollerVisibility.ToString();
                    a["verticalScrollerVisibility"] = s.verticalScrollerVisibility.ToString();
                },
                (e, a) =>
                {
                    var s = (ScrollView)e;
                    if (E(a, "mode", out ScrollViewMode mode)) s.mode = mode;
                    if (E(a, "horizontalScrollerVisibility", out ScrollerVisibility h)) s.horizontalScrollerVisibility = h;
                    if (E(a, "verticalScrollerVisibility", out ScrollerVisibility v)) s.verticalScrollerVisibility = v;
                }),
            new Entry("KButton", typeof(KButton), () => new KButton(), Children.ButtonContent,
                new[] { "tone", "size", "shape" },
                (e, a) => { var b = (KButton)e; a["tone"] = b.tone.ToString(); a["size"] = b.size.ToString(); a["shape"] = b.shape.ToString(); },
                (e, a) =>
                {
                    var b = (KButton)e;
                    if (E(a, "tone", out KButton.Tone tone)) b.tone = tone;
                    if (E(a, "size", out KButton.Size size)) b.size = size;
                    if (E(a, "shape", out KButton.Shape shape)) b.shape = shape;
                }),
            new Entry("KEyebrow", typeof(KEyebrow), () => new KEyebrow(), Children.All,
                new[] { "backdrop" },
                (e, a) => a["backdrop"] = ((KEyebrow)e).backdrop.ToString(),
                (e, a) => { if (E(a, "backdrop", out KEyebrow.Backdrop b)) ((KEyebrow)e).backdrop = b; }),
            new Entry("KTag", typeof(KTag), () => new KTag(), Children.All,
                new[] { "tone" },
                (e, a) => a["tone"] = ((KTag)e).tone.ToString(),
                (e, a) => { if (E(a, "tone", out KTag.Tone t)) ((KTag)e).tone = t; }),
            new Entry("KText", typeof(KText), () => new KText(), Children.All,
                new[] { "size", "tone" },
                (e, a) => { var t = (KText)e; a["size"] = t.size.ToString(); a["tone"] = t.tone.ToString(); },
                (e, a) =>
                {
                    var t = (KText)e;
                    if (E(a, "size", out KText.Size size)) t.size = size;
                    if (E(a, "tone", out KText.Tone tone)) t.tone = tone;
                }),
            new Entry("KChip", typeof(KChip), () => new KChip(), Children.None,
                new[] { "text", "state" },
                (e, a) => { var c = (KChip)e; a["text"] = c.text ?? ""; a["state"] = c.state.ToString(); },
                (e, a) =>
                {
                    var c = (KChip)e;
                    if (S(a, "text", out var text)) c.text = text;
                    if (E(a, "state", out KChip.State state)) c.state = state;
                }),
            new Entry("KReadout", typeof(KReadout), () => new KReadout(), Children.None,
                new[] { "value", "caption", "size", "tone" },
                (e, a) =>
                {
                    var r = (KReadout)e;
                    a["value"] = r.value ?? ""; a["caption"] = r.caption ?? "";
                    a["size"] = r.size.ToString(); a["tone"] = r.tone.ToString();
                },
                (e, a) =>
                {
                    var r = (KReadout)e;
                    if (S(a, "value", out var value)) r.value = value;
                    if (S(a, "caption", out var caption)) r.caption = caption;
                    if (E(a, "size", out KReadout.Size size)) r.size = size;
                    if (E(a, "tone", out KReadout.Tone tone)) r.tone = tone;
                }),
            new Entry("KArc", typeof(KArc), () => new KArc(), Children.All,
                new[] { "form", "tone", "segments", "fraction" },
                (e, a) =>
                {
                    var arc = (KArc)e;
                    a["form"] = arc.form.ToString(); a["tone"] = arc.tone.ToString();
                    a["segments"] = arc.segments; a["fraction"] = Round(arc.fraction);
                },
                (e, a) =>
                {
                    var arc = (KArc)e;
                    if (E(a, "form", out KArc.Form form)) arc.form = form;
                    if (E(a, "tone", out KArc.Tone tone)) arc.tone = tone;
                    if (I(a, "segments", out int segments)) arc.segments = segments;
                    if (F(a, "fraction", out float fraction)) arc.fraction = fraction;
                }),
            new Entry("KMeter", typeof(KMeter), () => new KMeter(), Children.None,
                new[] { "form", "tone", "count", "filled", "fraction" },
                (e, a) =>
                {
                    var m = (KMeter)e;
                    a["form"] = m.form.ToString(); a["tone"] = m.tone.ToString();
                    a["count"] = m.count; a["filled"] = m.filled; a["fraction"] = Round(m.fraction);
                },
                (e, a) =>
                {
                    var m = (KMeter)e;
                    if (E(a, "form", out KMeter.Form form)) m.form = form;
                    if (E(a, "tone", out KMeter.Tone tone)) m.tone = tone;
                    if (I(a, "count", out int count)) m.count = count;
                    if (I(a, "filled", out int filled)) m.filled = filled;
                    if (F(a, "fraction", out float fraction)) m.fraction = fraction;
                }),
            // `activity` is deliberately not mirrored: it is a data source, not content. The host resolved
            // it against the catalog already, and the KStep children it produced are what crosses the wire.
            // Setting it on the replica would rebuild them from the headset's own catalog and double them.
            new Entry("KSteps", typeof(KSteps), () => new KSteps(), Children.All,
                new[] { "numbering" },
                (e, a) => a["numbering"] = ((KSteps)e).numbering.ToString(),
                (e, a) => { if (E(a, "numbering", out KSteps.Numbering n)) ((KSteps)e).numbering = n; },
                e => { var s = (KSteps)e; s.numbering = s.numbering; }),
            // `done` is not a UXML attribute but it is state the badge draws, so it rides with the row.
            new Entry("KStep", typeof(KStep), () => new KStep(), Children.None,
                new[] { "title", "copy", "done" },
                (e, a) => { var s = (KStep)e; a["title"] = s.title ?? ""; a["copy"] = s.copy ?? ""; a["done"] = s.Done; },
                (e, a) =>
                {
                    var s = (KStep)e;
                    if (S(a, "title", out var title)) s.title = title;
                    if (S(a, "copy", out var copy)) s.copy = copy;
                    if (B(a, "done", out bool done)) s.Done = done;
                }),
            new Entry("KSurface", typeof(KSurface), () => new KSurface(), Children.All,
                new[] { "tone", "density" },
                (e, a) => { var s = (KSurface)e; a["tone"] = s.tone.ToString(); a["density"] = s.density.ToString(); },
                (e, a) =>
                {
                    var s = (KSurface)e;
                    if (E(a, "tone", out KSurface.Tone tone)) s.tone = tone;
                    if (E(a, "density", out KSurface.Density density)) s.density = density;
                }),
        };

        static readonly Dictionary<Type, Entry> byType = new();
        static readonly Dictionary<string, Entry> byId = new();
        static readonly HashSet<Type> warnedTypes = new();
        static readonly Entry Fallback;

        static PanelWire()
        {
            foreach (var entry in Table) { byType[entry.Type] = entry; byId[entry.Id] = entry; }
            Fallback = byId["VisualElement"];
            VocabHash = ComputeVocabHash();
        }

        /// A stable digest of the table — type ids, their attribute lists and how their children are
        /// mirrored — plus the node and style keys. Both sides compute it from this same code; a mismatch
        /// means one side is running older code.
        public static string VocabHash { get; }

        /// The table itself, read-only, so the verification can hash a deliberately altered copy.
        public static IReadOnlyList<Entry> Vocabulary => Table;

        public static string ComputeVocabHash() => ComputeVocabHash(Table);

        public static string ComputeVocabHash(IEnumerable<Entry> table)
        {
            var text = new StringBuilder("proto=").Append(Proto).Append(';');
            foreach (var entry in table)
                text.Append(entry.Id).Append(':').Append(string.Join(",", entry.Attributes)).Append(':').Append(entry.Kids).Append(';');
            text.Append("fields=t,n,c,x,d,pi,a,s,ss,i,k;styles=").Append(string.Join(",", StyleKeys)).Append(';');
            // FNV-1a, 64-bit: the same on Mono and IL2CPP, unlike string.GetHashCode.
            ulong hash = 14695981039346656037;
            foreach (char c in text.ToString()) { hash ^= c; hash *= 1099511628211; }
            return hash.ToString("x16");
        }

        /// The row for an element: its exact type, or the nearest vocabulary base type, warned once.
        public static Entry EntryFor(VisualElement element)
        {
            var type = element.GetType();
            if (byType.TryGetValue(type, out var entry)) return entry;
            for (var t = type.BaseType; t != null; t = t.BaseType)
                if (byType.TryGetValue(t, out entry))
                {
                    if (warnedTypes.Add(type)) Debug.LogWarning($"Remote UI: {type.Name} is not in the vocabulary; mirrored as {entry.Id}.");
                    byType[type] = entry;
                    return entry;
                }
            return Fallback;
        }

        public static Entry EntryById(string id) => id != null && byId.TryGetValue(id, out var entry) ? entry : Fallback;

        // ---- logical children and paths --------------------------------------------------------------------

        public static List<VisualElement> LogicalChildren(VisualElement element)
        {
            var entry = EntryFor(element);
            var list = new List<VisualElement>();
            if (entry.Kids == Children.None) return list;
            bool button = entry.Kids == Children.ButtonContent;
            bool withIcon = button && element.ClassListContains(Button.iconUssClassName);
            bool skippedText = false;
            foreach (var child in element.Children())
            {
                if (button && child.ClassListContains(Button.imageUSSClassName)) continue;
                if (withIcon && !skippedText && child.GetType() == typeof(TextElement)) { skippedText = true; continue; }
                list.Add(child);
            }
            return list;
        }

        /// The element at a `/`-separated index path from `root`, or null. "" is the root.
        public static VisualElement Resolve(VisualElement root, string path)
        {
            var element = root;
            if (string.IsNullOrEmpty(path)) return element;
            foreach (var part in path.Split('/'))
            {
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int index)) return null;
                var kids = LogicalChildren(element);
                if (index >= kids.Count) return null;
                element = kids[index];
            }
            return element;
        }

        /// The path of `target` under `root` through logical children, or null when it is not reachable that
        /// way — an element inside a composite's internals has no path, and so cannot be pressed remotely.
        public static string PathOf(VisualElement root, VisualElement target)
        {
            if (root == target) return "";
            var kids = LogicalChildren(root);
            for (int i = 0; i < kids.Count; i++)
            {
                var below = PathOf(kids[i], target);
                if (below == null) continue;
                return below.Length == 0 ? i.ToString(CultureInfo.InvariantCulture) : i.ToString(CultureInfo.InvariantCulture) + "/" + below;
            }
            return null;
        }

        // ---- serialize -------------------------------------------------------------------------------------

        public static JObject Serialize(VisualElement element)
        {
            var entry = EntryFor(element);
            var node = new JObject { ["t"] = entry.Id };
            if (!string.IsNullOrEmpty(element.name)) node["n"] = element.name;
            var classes = new JArray();
            foreach (var c in element.GetClasses()) classes.Add(c);
            if (classes.Count > 0) node["c"] = classes;
            if (element is TextElement text) node["x"] = text.text ?? "";
            if (!element.enabledSelf) node["d"] = true;
            if (element.pickingMode == PickingMode.Ignore) node["pi"] = true;
            if (entry.Attributes.Length > 0) { var a = new JObject(); entry.Read(element, a); node["a"] = a; }
            var style = ReadStyle(element);
            if (style.Count > 0) node["s"] = style;
            var sheets = ReadSheets(element);
            if (sheets.Count > 0) node["ss"] = sheets;
            var image = ReadImage(element);
            if (image != null) node["i"] = image;
            var kids = LogicalChildren(element);
            if (kids.Count > 0)
            {
                var k = new JArray();
                foreach (var child in kids) k.Add(Serialize(child));
                node["k"] = k;
            }
            return node;
        }

        // ---- diff ------------------------------------------------------------------------------------------

        /// Ops that turn `oldNode` into `newNode`, appended to `ops`. A changed type or child count replaces
        /// the subtree; anything else is a `set` of the fields that differ, then the children in turn.
        public static void Diff(JObject oldNode, JObject newNode, string path, string board, JArray ops)
        {
            var oldKids = oldNode["k"] as JArray;
            var newKids = newNode["k"] as JArray;
            if ((string)oldNode["t"] != (string)newNode["t"] || (oldKids?.Count ?? 0) != (newKids?.Count ?? 0))
            {
                ops.Add(new JObject { ["op"] = "replace", ["board"] = board, ["p"] = path, ["node"] = newNode.DeepClone() });
                return;
            }
            JObject f = null;
            foreach (var key in Fields)
            {
                var before = oldNode[key];
                var after = newNode[key];
                if (JToken.DeepEquals(before, after)) continue;
                f ??= new JObject();
                f[key] = after?.DeepClone() ?? JValue.CreateNull();
            }
            if (f != null) ops.Add(new JObject { ["op"] = "set", ["board"] = board, ["p"] = path, ["f"] = f });
            if (newKids == null) return;
            for (int i = 0; i < newKids.Count; i++)
                Diff((JObject)oldKids[i], (JObject)newKids[i], path.Length == 0 ? i.ToString(CultureInfo.InvariantCulture) : path + "/" + i.ToString(CultureInfo.InvariantCulture), board, ops);
        }

        public static JArray Diff(JObject oldTree, JObject newTree, string board = null)
        {
            var ops = new JArray();
            Diff(oldTree, newTree, "", board, ops);
            return ops;
        }

        // ---- build and apply -------------------------------------------------------------------------------

        public static VisualElement Build(JObject node, string board = null)
        {
            var entry = EntryById((string)node["t"]);
            var element = entry.Make();
            ApplyFields(element, node, board, partial: false);
            AddChildren(element, node["k"] as JArray, board);
            entry.ChildrenChanged?.Invoke(element);
            return element;
        }

        /// Make an existing root — a UIDocument's, which cannot be swapped — match `node`.
        public static void Rebuild(VisualElement root, JObject node, string board = null)
        {
            ApplyFields(root, node, board, partial: false);
            root.Clear();
            AddChildren(root, node["k"] as JArray, board);
            EntryFor(root).ChildrenChanged?.Invoke(root);
        }

        static void AddChildren(VisualElement element, JArray kids, string board)
        {
            if (kids == null) return;
            foreach (var kid in kids) element.Add(Build((JObject)kid, board));
        }

        /// Apply `set` and `replace` ops to one board's root. Throws on a path that does not resolve, so the
        /// caller can fall back to a resync rather than drift.
        public static void Apply(VisualElement root, JArray ops, string board = null)
        {
            foreach (JObject op in ops)
            {
                string p = (string)op["p"] ?? "";
                switch ((string)op["op"])
                {
                    case "set":
                        var target = Resolve(root, p) ?? throw new InvalidOperationException($"set: no element at '{p}'");
                        ApplyFields(target, (JObject)op["f"], board, partial: true);
                        break;
                    case "replace":
                        var node = (JObject)op["node"];
                        if (p.Length == 0) { Rebuild(root, node, board); break; }
                        int slash = p.LastIndexOf('/');
                        var parent = Resolve(root, slash < 0 ? "" : p.Substring(0, slash)) ?? throw new InvalidOperationException($"replace: no parent for '{p}'");
                        int index = int.Parse(p.Substring(slash + 1), CultureInfo.InvariantCulture);
                        var kids = LogicalChildren(parent);
                        if (index >= kids.Count) throw new InvalidOperationException($"replace: no child at '{p}'");
                        // Insert where the old one stood in the real hierarchy: inside a ScrollView that is
                        // the content container, and under a Button it may sit after an icon internal.
                        var old = kids[index];
                        var container = old.hierarchy.parent;
                        int at = container.hierarchy.IndexOf(old);
                        old.RemoveFromHierarchy();
                        container.hierarchy.Insert(at, Build(node, board));
                        EntryFor(parent).ChildrenChanged?.Invoke(parent);
                        break;
                }
            }
        }

        /// Set node fields. Partial (a `set` op) touches only the keys present, null meaning unset; full
        /// (Build/Rebuild) treats an absent key as unset too.
        static void ApplyFields(VisualElement element, JObject f, string board, bool partial)
        {
            var entry = EntryFor(element);
            foreach (var key in Fields)
            {
                if (!Touch(f, key, partial, out var value)) continue;
                switch (key)
                {
                    case "n": element.name = value == null ? "" : (string)value; break;
                    case "x": if (element is TextElement text) text.text = value == null ? "" : (string)value; break;
                    case "d": element.SetEnabled(value == null || !(bool)value); break;
                    case "pi": element.pickingMode = value != null && (bool)value ? PickingMode.Ignore : PickingMode.Position; break;
                    case "a": if (value is JObject a) entry.Write(element, a); break;
                    case "s": WriteStyle(element, value as JObject); break;
                    case "ss": WriteSheets(element, value as JArray); break;
                    case "i": WriteImage(element, value as JObject, board); break;
                    case "c":
                        element.ClearClassList();
                        if (value is JArray classes) foreach (var c in classes) element.AddToClassList((string)c);
                        break;
                }
            }
        }

        static bool Touch(JObject f, string key, bool partial, out JToken value)
        {
            value = f?[key];
            if (value != null && value.Type == JTokenType.Null) value = null;
            return !partial || (f != null && f.ContainsKey(key));
        }

        // ---- press -----------------------------------------------------------------------------------------

        /// Press a Button the way a keyboard submit does. Button handles NavigationSubmitEvent by calling
        /// clickable.SimulateSingleClick, which runs the same Clickable.Invoke a pointer-up runs, so every
        /// `clicked` and `clickedWithEventInfo` handler fires — including the menu's, which is how its
        /// named actions are committed. The element must be in a panel: SendEvent dispatches through it.
        public static void Click(Button button)
        {
            using var evt = NavigationSubmitEvent.GetPooled();
            evt.target = button;
            button.SendEvent(evt);
        }

        // ---- inline style ----------------------------------------------------------------------------------

        static JObject ReadStyle(VisualElement element)
        {
            var s = element.style;
            var o = new JObject();
            if (s.display.keyword == StyleKeyword.Undefined) o["display"] = s.display.value.ToString();
            if (s.visibility.keyword == StyleKeyword.Undefined) o["visibility"] = s.visibility.value.ToString();
            if (s.position.keyword == StyleKeyword.Undefined) o["position"] = s.position.value.ToString();
            if (s.left.keyword == StyleKeyword.Undefined) o["left"] = LengthText(s.left.value);
            if (s.top.keyword == StyleKeyword.Undefined) o["top"] = LengthText(s.top.value);
            if (s.right.keyword == StyleKeyword.Undefined) o["right"] = LengthText(s.right.value);
            if (s.bottom.keyword == StyleKeyword.Undefined) o["bottom"] = LengthText(s.bottom.value);
            if (s.width.keyword == StyleKeyword.Undefined) o["width"] = LengthText(s.width.value);
            if (s.height.keyword == StyleKeyword.Undefined) o["height"] = LengthText(s.height.value);
            if (s.opacity.keyword == StyleKeyword.Undefined) o["opacity"] = Round(s.opacity.value);
            if (s.translate.keyword == StyleKeyword.Undefined)
            {
                var t = s.translate.value;
                o["translate"] = LengthText(t.x) + " " + LengthText(t.y) + " " + NumberText(t.z);
            }
            if (s.rotate.keyword == StyleKeyword.Undefined) o["rotate"] = AngleText(s.rotate.value.angle);
            if (s.scale.keyword == StyleKeyword.Undefined)
            {
                var v = s.scale.value.value;
                o["scale"] = NumberText(v.x) + " " + NumberText(v.y) + " " + NumberText(v.z);
            }
            if (s.flexGrow.keyword == StyleKeyword.Undefined) o["flexGrow"] = Round(s.flexGrow.value);
            if (s.backgroundColor.keyword == StyleKeyword.Undefined) o["backgroundColor"] = ColorText(s.backgroundColor.value);
            if (s.color.keyword == StyleKeyword.Undefined) o["color"] = ColorText(s.color.value);
            if (s.unityBackgroundImageTintColor.keyword == StyleKeyword.Undefined) o["unityBackgroundImageTintColor"] = ColorText(s.unityBackgroundImageTintColor.value);
            return o;
        }

        /// Every mirrored property is set or unset, so the replica never keeps a value the host dropped.
        static void WriteStyle(VisualElement element, JObject o)
        {
            var s = element.style;
            s.display = Has(o, "display", out var v) ? new StyleEnum<DisplayStyle>(ParseEnum<DisplayStyle>(v)) : new StyleEnum<DisplayStyle>(StyleKeyword.Null);
            s.visibility = Has(o, "visibility", out v) ? new StyleEnum<Visibility>(ParseEnum<Visibility>(v)) : new StyleEnum<Visibility>(StyleKeyword.Null);
            s.position = Has(o, "position", out v) ? new StyleEnum<Position>(ParseEnum<Position>(v)) : new StyleEnum<Position>(StyleKeyword.Null);
            s.left = Has(o, "left", out v) ? new StyleLength(ParseLength(v)) : new StyleLength(StyleKeyword.Null);
            s.top = Has(o, "top", out v) ? new StyleLength(ParseLength(v)) : new StyleLength(StyleKeyword.Null);
            s.right = Has(o, "right", out v) ? new StyleLength(ParseLength(v)) : new StyleLength(StyleKeyword.Null);
            s.bottom = Has(o, "bottom", out v) ? new StyleLength(ParseLength(v)) : new StyleLength(StyleKeyword.Null);
            s.width = Has(o, "width", out v) ? new StyleLength(ParseLength(v)) : new StyleLength(StyleKeyword.Null);
            s.height = Has(o, "height", out v) ? new StyleLength(ParseLength(v)) : new StyleLength(StyleKeyword.Null);
            s.opacity = Has(o, "opacity", out v) ? new StyleFloat(ParseNumber(v)) : new StyleFloat(StyleKeyword.Null);
            if (Has(o, "translate", out v))
            {
                var parts = v.Split(' ');
                s.translate = new StyleTranslate(new Translate(ParseLength(parts[0]), ParseLength(parts.Length > 1 ? parts[1] : "0px"), parts.Length > 2 ? ParseNumber(parts[2]) : 0));
            }
            else s.translate = new StyleTranslate(StyleKeyword.Null);
            s.rotate = Has(o, "rotate", out v) ? new StyleRotate(new Rotate(ParseAngle(v))) : new StyleRotate(StyleKeyword.Null);
            if (Has(o, "scale", out v))
            {
                var parts = v.Split(' ');
                s.scale = new StyleScale(new Scale(new Vector3(ParseNumber(parts[0]), parts.Length > 1 ? ParseNumber(parts[1]) : 1, parts.Length > 2 ? ParseNumber(parts[2]) : 1)));
            }
            else s.scale = new StyleScale(StyleKeyword.Null);
            s.flexGrow = Has(o, "flexGrow", out v) ? new StyleFloat(ParseNumber(v)) : new StyleFloat(StyleKeyword.Null);
            s.backgroundColor = Has(o, "backgroundColor", out v) ? new StyleColor(ParseColor(v)) : new StyleColor(StyleKeyword.Null);
            s.color = Has(o, "color", out v) ? new StyleColor(ParseColor(v)) : new StyleColor(StyleKeyword.Null);
            s.unityBackgroundImageTintColor = Has(o, "unityBackgroundImageTintColor", out v) ? new StyleColor(ParseColor(v)) : new StyleColor(StyleKeyword.Null);
        }

        static bool Has(JObject o, string key, out string value)
        {
            var token = o?[key];
            value = token != null && token.Type != JTokenType.Null ? token.ToString() : null;
            return value != null;
        }

        static string LengthText(Length length)
        {
            if (length.IsAuto()) return "auto";
            if (length.IsNone()) return "none";
            return NumberText(length.value) + (length.unit == LengthUnit.Percent ? "%" : "px");
        }

        static Length ParseLength(string text)
        {
            if (text == "auto") return Length.Auto();
            if (text == "none") return Length.None();
            if (text.EndsWith("%")) return Length.Percent(ParseNumber(text.Substring(0, text.Length - 1)));
            if (text.EndsWith("px")) return new Length(ParseNumber(text.Substring(0, text.Length - 2)), LengthUnit.Pixel);
            return new Length(ParseNumber(text), LengthUnit.Pixel);
        }

        static string AngleText(Angle angle)
        {
            string unit = angle.unit switch { AngleUnit.Gradian => "grad", AngleUnit.Radian => "rad", AngleUnit.Turn => "turn", _ => "deg" };
            return NumberText(angle.value) + unit;
        }

        static Angle ParseAngle(string text)
        {
            if (text.EndsWith("grad")) return new Angle(ParseNumber(text.Substring(0, text.Length - 4)), AngleUnit.Gradian);
            if (text.EndsWith("rad")) return new Angle(ParseNumber(text.Substring(0, text.Length - 3)), AngleUnit.Radian);
            if (text.EndsWith("turn")) return new Angle(ParseNumber(text.Substring(0, text.Length - 4)), AngleUnit.Turn);
            if (text.EndsWith("deg")) return new Angle(ParseNumber(text.Substring(0, text.Length - 3)), AngleUnit.Degree);
            return new Angle(ParseNumber(text), AngleUnit.Degree);
        }

        static string ColorText(Color color) => "#" + ColorUtility.ToHtmlStringRGBA(color);
        static Color ParseColor(string text) => ColorUtility.TryParseHtmlString(text, out var color) ? color : Color.clear;

        /// Numbers are rounded to four places and carried as doubles, so a value survives JSON parsing
        /// bit-for-bit and DeepEquals stays true across a round trip.
        static double Round(float value) => Math.Round(value, 4);
        static string NumberText(float value) => Round(value).ToString("0.####", CultureInfo.InvariantCulture);
        static float ParseNumber(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        static T ParseEnum<T>(string text) where T : struct => Enum.TryParse<T>(text, true, out var value) ? value : default;

        // ---- stylesheets -----------------------------------------------------------------------------------

        static JArray ReadSheets(VisualElement element)
        {
            var arr = new JArray();
            var set = element.styleSheets;
            for (int i = 0; i < set.count; i++)
            {
                string id = UiSheetRegistry.IdOf(set[i]);
                if (id == null || UiSheetRegistry.IsComponentSheet(id)) continue;
                arr.Add(id);
            }
            return arr;
        }

        /// The component's own sheets stay — its constructor attached them and the wire never carries
        /// them — and the screen's follow in wire order.
        static void WriteSheets(VisualElement element, JArray ids)
        {
            var set = element.styleSheets;
            var keep = new List<StyleSheet>();
            for (int i = 0; i < set.count; i++)
            {
                string id = UiSheetRegistry.IdOf(set[i], quiet: true);
                if (id == null || UiSheetRegistry.IsComponentSheet(id)) keep.Add(set[i]);
            }
            set.Clear();
            foreach (var sheet in keep) set.Add(sheet);
            if (ids == null) return;
            foreach (var id in ids)
            {
                var sheet = UiSheetRegistry.Resolve((string)id);
                if (sheet != null && !set.Contains(sheet)) set.Add(sheet);
            }
        }

        // ---- images ----------------------------------------------------------------------------------------

        static JObject ReadImage(VisualElement element)
        {
            UnityEngine.Object source = null;
            if (element is Image image)
                source = (UnityEngine.Object)image.image ?? (UnityEngine.Object)image.sprite ?? image.vectorImage;
            else if (element.style.backgroundImage.keyword == StyleKeyword.Undefined)
            {
                var bg = element.style.backgroundImage.value;
                source = (UnityEngine.Object)bg.texture ?? (UnityEngine.Object)bg.sprite ?? (UnityEngine.Object)bg.renderTexture ?? bg.vectorImage;
            }
            if (source == null) return null;
            string id = UiImageRegistry.IdOf(source);
            if (id != null) return new JObject { ["id"] = id };
            // Unregistered — a RenderTexture drawn live, like the golf minimap. The headset fills it locally.
            return new JObject { ["rt"] = element.name ?? "", ["tex"] = source.name ?? "" };
        }

        static void WriteImage(VisualElement element, JObject i, string board)
        {
            UnityEngine.Object source = null;
            if (i?["id"] != null) source = UiImageRegistry.Resolve((string)i["id"]);
            else if (i?["rt"] != null) source = ImageSource?.Resolve(board, (string)i["rt"], (string)i["tex"]);

            if (element is Image image)
            {
                switch (source)
                {
                    case Sprite sprite: image.sprite = sprite; break;
                    case VectorImage vector: image.vectorImage = vector; break;
                    case Texture texture: image.image = texture; break;
                    default: image.image = null; image.sprite = null; image.vectorImage = null; break;
                }
                return;
            }
            element.style.backgroundImage = source switch
            {
                Texture2D texture => new StyleBackground(Background.FromTexture2D(texture)),
                RenderTexture render => new StyleBackground(Background.FromRenderTexture(render)),
                Sprite sprite => new StyleBackground(Background.FromSprite(sprite)),
                VectorImage vector => new StyleBackground(Background.FromVectorImage(vector)),
                _ => new StyleBackground(StyleKeyword.Null),
            };
        }

        // ---- attribute helpers -----------------------------------------------------------------------------

        static bool E<T>(JObject a, string key, out T value) where T : struct
        {
            value = default;
            var token = a[key];
            return token != null && token.Type == JTokenType.String && Enum.TryParse((string)token, true, out value);
        }

        static bool S(JObject a, string key, out string value)
        {
            var token = a[key];
            value = token == null || token.Type == JTokenType.Null ? null : (string)token;
            return value != null;
        }

        static bool I(JObject a, string key, out int value)
        {
            var token = a[key];
            value = 0;
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)) return false;
            value = (int)token;
            return true;
        }

        static bool F(JObject a, string key, out float value)
        {
            var token = a[key];
            value = 0;
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)) return false;
            value = (float)token;
            return true;
        }

        static bool B(JObject a, string key, out bool value)
        {
            var token = a[key];
            value = false;
            if (token == null || token.Type != JTokenType.Boolean) return false;
            value = (bool)token;
            return true;
        }
    }
}

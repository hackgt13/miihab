using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.UI.Remote
{
    /// Every stylesheet a board may carry, by a stable id, so the headset can attach the sheet the Mac
    /// named. A sheet referenced only from UXML is not loadable at runtime by path — there is no
    /// AssetDatabase in a player and no Resources folder holds it — so this asset holds a reference to each
    /// one and both sides agree on the id: the asset path relative to Assets/, without the extension.
    ///
    /// Filled by Kinesthetic → Remote UI → Refresh registries; never edited by hand.
    public sealed class UiSheetRegistry : ScriptableObject
    {
        public const string ResourcePath = "RemoteUI/UiSheetRegistry";

        /// The components' own sheets. They are attached by constructors on both sides, so the wire skips
        /// them and the replica keeps whatever its constructor attached.
        public const string ComponentFolder = "Kinesthetic/UI/Resources/KinestheticUI/";

        public List<string> ids = new();
        public List<StyleSheet> sheets = new();

        static UiSheetRegistry loaded;
        static bool tried;
        static Dictionary<string, StyleSheet> byId;
        static Dictionary<StyleSheet, string> bySheet;
        static readonly HashSet<string> warned = new();

        public static UiSheetRegistry Instance
        {
            get
            {
                if (tried) return loaded;
                tried = true;
                loaded = Resources.Load<UiSheetRegistry>(ResourcePath);
                if (!loaded) Debug.LogWarning($"Remote UI: no sheet registry at Resources/{ResourcePath}. Run Kinesthetic → Remote UI → Refresh registries.");
                return loaded;
            }
        }

        /// Forget the cached asset and lookups: for the editor tool that just rewrote them.
        public static void Invalidate() { tried = false; loaded = null; byId = null; bySheet = null; }

        public static string IdForAssetPath(string assetPath)
        {
            const string prefix = "Assets/";
            string path = assetPath.StartsWith(prefix, StringComparison.Ordinal) ? assetPath.Substring(prefix.Length) : assetPath;
            int dot = path.LastIndexOf('.');
            int slash = path.LastIndexOf('/');
            return dot > slash ? path.Substring(0, dot) : path;
        }

        public static bool IsComponentSheet(string id) => id != null && id.StartsWith(ComponentFolder, StringComparison.Ordinal);

        /// The id of a sheet, or its object name (warned once) when it is not registered.
        public static string IdOf(StyleSheet sheet, bool quiet = false)
        {
            if (sheet == null) return null;
            Index();
            if (bySheet != null && bySheet.TryGetValue(sheet, out var id)) return id;
            if (!quiet && warned.Add(sheet.name)) Debug.LogWarning($"Remote UI: stylesheet '{sheet.name}' is not in the registry; sent by name.");
            return sheet.name;
        }

        public static StyleSheet Resolve(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            Index();
            if (byId != null && byId.TryGetValue(id, out var sheet) && sheet) return sheet;
            if (warned.Add(id)) Debug.LogWarning($"Remote UI: no stylesheet registered as '{id}'; the board will draw without it.");
            return null;
        }

        static void Index()
        {
            if (byId != null) return;
            var registry = Instance;
            byId = new Dictionary<string, StyleSheet>();
            bySheet = new Dictionary<StyleSheet, string>();
            if (!registry) return;
            for (int i = 0; i < registry.ids.Count && i < registry.sheets.Count; i++)
            {
                if (!registry.sheets[i] || string.IsNullOrEmpty(registry.ids[i])) continue;
                byId[registry.ids[i]] = registry.sheets[i];
                bySheet[registry.sheets[i]] = registry.ids[i];
            }
        }
    }
}

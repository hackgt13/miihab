using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kinesthetic.UI.Remote
{
    /// Every texture and sprite a board may show, by the same stable id the sheet registry uses (a sprite
    /// inside a sheet adds `#name`). What is not here — a RenderTexture drawn live — crosses the wire as a
    /// reference the headset fills from its own world instead.
    ///
    /// Filled by Kinesthetic → Remote UI → Refresh registries; never edited by hand.
    public sealed class UiImageRegistry : ScriptableObject
    {
        public const string ResourcePath = "RemoteUI/UiImageRegistry";

        public List<string> ids = new();
        public List<UnityEngine.Object> images = new();

        static UiImageRegistry loaded;
        static bool tried;
        static Dictionary<string, UnityEngine.Object> byId;
        static Dictionary<UnityEngine.Object, string> byImage;
        static readonly HashSet<string> warned = new();

        public static UiImageRegistry Instance
        {
            get
            {
                if (tried) return loaded;
                tried = true;
                loaded = Resources.Load<UiImageRegistry>(ResourcePath);
                if (!loaded) Debug.LogWarning($"Remote UI: no image registry at Resources/{ResourcePath}. Run Kinesthetic → Remote UI → Refresh registries.");
                return loaded;
            }
        }

        public static void Invalidate() { tried = false; loaded = null; byId = null; byImage = null; }

        /// The id of a registered image, or null: unregistered is a meaningful answer here, not a warning.
        public static string IdOf(UnityEngine.Object image)
        {
            if (image == null) return null;
            Index();
            return byImage != null && byImage.TryGetValue(image, out var id) ? id : null;
        }

        public static UnityEngine.Object Resolve(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            Index();
            if (byId != null && byId.TryGetValue(id, out var image) && image) return image;
            if (warned.Add(id)) Debug.LogWarning($"Remote UI: no image registered as '{id}'.");
            return null;
        }

        static void Index()
        {
            if (byId != null) return;
            var registry = Instance;
            byId = new Dictionary<string, UnityEngine.Object>();
            byImage = new Dictionary<UnityEngine.Object, string>();
            if (!registry) return;
            for (int i = 0; i < registry.ids.Count && i < registry.images.Count; i++)
            {
                if (!registry.images[i] || string.IsNullOrEmpty(registry.ids[i])) continue;
                byId[registry.ids[i]] = registry.images[i];
                byImage[registry.images[i]] = registry.ids[i];
            }
        }
    }

    /// Fills an image the wire could only name: `{"rt": elementName, "tex": textureName}` for a texture
    /// the registry does not hold. The golf minimap is the case — the headset draws it from world state.
    public interface IReplicaImageSource
    {
        Texture Resolve(string board, string elementName, string textureName);
    }
}

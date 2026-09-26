using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.UI
{
    /// How a component carries its look. Each one attaches its own stylesheet from Resources, so a KButton
    /// looks the same in the studio, on the course and inside a pane, whether or not that screen's UXML
    /// remembered to link anything. Colour still resolves from Palette.uss, which every screen links.
    ///
    /// A variant is a modifier class named for its enum value — `k-button--primary` — and a component
    /// holds exactly one value per axis. Enum names are unique across a component's axes, so clearing one
    /// axis never touches another.
    static class KStyles
    {
        const string Folder = "KinestheticUI/";
        static readonly Dictionary<string, StyleSheet> sheets = new Dictionary<string, StyleSheet>();

        public static void Attach(VisualElement element, string sheet)
        {
            if (!sheets.TryGetValue(sheet, out var loaded) || !loaded)
            {
                loaded = Resources.Load<StyleSheet>(Folder + sheet);
                if (!loaded) Debug.LogError($"UI stylesheet missing at Resources/{Folder}{sheet}.uss");
                sheets[sheet] = loaded;
            }
            if (loaded) element.styleSheets.Add(loaded);
        }

        public static void Variant<T>(VisualElement element, string block, T value) where T : struct, Enum
        {
            foreach (var name in Enum.GetNames(typeof(T))) element.RemoveFromClassList(Modifier(block, name));
            element.AddToClassList(Modifier(block, value.ToString()));
        }

        static string Modifier(string block, string name) => block + "--" + name.ToLowerInvariant();
    }
}

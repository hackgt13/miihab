using Kinesthetic;
using Kinesthetic.Shell;
using Kinesthetic.UI.Boards;
using Kinesthetic.UI.Remote;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// The board recipe, once, for every venue's scene setup: a world-space UIDocument on the shared board
/// panel, laid out at its station's density, pickable through a projection-only Pane, driven by a gaze
/// dwell and the mouse, and named for the wire so the headset can mirror it.
///
/// Density. A world-space panel lays its pixels out first and its transform maps them to metres, at 100
/// panel pixels per unit — measured from the collider UIDocument maintains (MainMenuSetup), the same
/// constant Pane scales by. So a board at a station lays out at `station.PixelsPerMetre` — 1000 /
/// distance — and scales by `PanelPixelsPerUnit / PixelsPerMetre` to come out `sizeMetres` wide in the
/// room. Handing metres straight to worldSpaceSize lays a panel out a pixel wide.
public static class BoardBuilder
{
    public const string PanelPath = "Assets/Kinesthetic/UI/Boards/BoardPanel.asset";
    public const float PanelPixelsPerUnit = 100f;
    const string Template = "Assets/Kinesthetic/Rehab/RehabPanel.asset";   // read for its theme; never written

    /// The one PanelSettings every board shares. Created from the patient app's panel so it inherits the
    /// theme stylesheet, then set world-space and constant pixel size: a world-space panel has no screen
    /// to scale against, and a board that scaled with the window would change density with it.
    public static PanelSettings Panel()
    {
        var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
        if (!panel)
        {
            var template = AssetDatabase.LoadAssetAtPath<PanelSettings>(Template);
            panel = template ? Object.Instantiate(template) : ScriptableObject.CreateInstance<PanelSettings>();
            panel.name = "BoardPanel";
            AssetDatabase.CreateAsset(panel, PanelPath);
        }
        panel.renderMode = PanelRenderMode.WorldSpace;
        panel.scaleMode = PanelScaleMode.ConstantPixelSize;
        panel.scale = 1;
        panel.sortingOrder = 0;
        EditorUtility.SetDirty(panel);
        return panel;
    }

    /// One board standing at `station`, `sizeMetres` in the room, showing `uxmlPath`, mirrored as `id`.
    public static UIDocument Build(SeatRig seat, string label, string id, string uxmlPath, Station station, Vector2 sizeMetres)
    {
        var panel = Panel();
        var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
        if (!uxml) throw new System.InvalidOperationException("Missing board layout: " + uxmlPath);

        var go = new GameObject(label, typeof(UIDocument), typeof(RemoteBoard), typeof(GazeDwell), typeof(PanePointerInput));
        var document = go.GetComponent<UIDocument>();
        document.panelSettings = panel;
        document.visualTreeAsset = uxml;
        document.worldSpaceSizeMode = WorldSpaceSizeMode.Fixed;
        float pixelsPerMetre = station.PixelsPerMetre;
        document.worldSpaceSize = sizeMetres * pixelsPerMetre;                       // panel pixels, not metres
        go.transform.localScale = Vector3.one * (PanelPixelsPerUnit / pixelsPerMetre);
        go.GetComponent<RemoteBoard>().id = id;
        // A projection-only Pane: no content, so it changes no size, no settings and no collider — the
        // collider UIDocument maintains stays the whole board, and both inputs pick through it.
        Kinesthetic.Panes.WorldPanelPick.MakePickable(go);
        seat.Stand(go.transform, station);
        return document;
    }

    /// Width in metres a built board comes out at, from what is serialized: the check the verification runs.
    public static Vector2 SizeMetres(UIDocument document) =>
        document.worldSpaceSize / PanelPixelsPerUnit * document.transform.localScale.x;

    /// Pixels per metre a built board lays out at, from what is serialized.
    public static float PixelsPerMetre(UIDocument document) => PanelPixelsPerUnit / document.transform.localScale.x;
}

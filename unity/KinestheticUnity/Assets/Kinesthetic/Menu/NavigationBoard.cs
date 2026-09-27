using Kinesthetic.Panes;
using Kinesthetic.Shell;
using Kinesthetic.UI.Boards;
using Kinesthetic.UI.Remote;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.Menu
{
    /// The navigation layer as a board standing in the world: the "Back to the menu?" dialog, the "How to
    /// play" card, the group lobby ("How would you like to play?") and the group room, on one surface that
    /// rides with the view (ViewFollow) a little nearer than the Focus station and outlives every scene load,
    /// the way the ActivityNavigation object it hangs on always has.
    ///
    /// It used to be a screen-space overlay. A screen-space panel is composited after the camera renders, so
    /// it never reaches a stereo eye, and it was no board on the wire, so pressing an activity on the headset
    /// opened a lobby only the Mac could see. As a board it is mirrored like any other (UI/Remote): the Mac
    /// hosts it, the headset shows the replica and sends presses back.
    ///
    /// Every other board is placed by a scene's setup and copied into the headset's scene, so the ids line
    /// up by construction. This one is the exception: it belongs to no scene, so both machines make it at
    /// runtime from the same recipe (`Dress`) — the Mac's copy is the navigation prefab MainMenuSetup builds,
    /// the headset's is `Replica`, made on load with no tree of its own, empty until the Mac's arrives.
    ///
    /// The layer's root ignores picking, and WorldPanelPick lets a ray through such a root where nothing is
    /// drawn, so the empty board in front of the view does not stop presses meant for the boards behind it;
    /// a shade or a card, once up, is picked and blocks as a modal should.
    public static class NavigationBoard
    {
        public const string Id = "nav.shell";

        /// Straight ahead, a little inside the Focus station, so it never stands in the same plane as a
        /// venue's own modal board and its type is read at nearly the same density.
        public static readonly Station Station = new("Navigation", 0, 0, 1.3f);

        /// Laid out this many times coarser than the station's density, so its type reads that many times larger
        /// at the same distance — the same figure as RehabSceneSetup.Magnify, after the headset showed every
        /// board tuned against the Mac's camera at half the size it needed.
        public const float Magnify = 2;

        /// Wide enough for the room card (880 px, laid out at the magnified density) with the shade around it:
        /// about 90 by 66 degrees of the view, the width of the plaza's facing pane.
        public static readonly Vector2 SizeMetres = new(2.6f, 1.7f);

        /// Make `go` the board: laid out at the station's density and scaled to `SizeMetres` (the same arithmetic
        /// as BoardBuilder), named for the wire, following the view, picked by the dwell and the pointer through
        /// a projection-only Pane, and its named buttons pressed through a BoardSet. Idempotent.
        public static UIDocument Dress(GameObject go, PanelSettings panel)
        {
            var document = go.GetComponent<UIDocument>();
            if (!document) document = go.AddComponent<UIDocument>();
            document.panelSettings = panel;
            document.worldSpaceSizeMode = WorldSpaceSizeMode.Fixed;
            float pixelsPerMetre = Station.PixelsPerMetre / Magnify;
            document.worldSpaceSize = SizeMetres * pixelsPerMetre;                       // panel pixels, not metres
            go.transform.localScale = Vector3.one * (Pane.PanelPixelsPerUnit / pixelsPerMetre);

            var board = go.GetComponent<RemoteBoard>();
            if (!board) board = go.AddComponent<RemoteBoard>();
            board.id = Id;

            var follow = go.GetComponent<ViewFollow>();
            if (!follow) follow = go.AddComponent<ViewFollow>();
            follow.station = Station;

            if (!go.GetComponent<GazeDwell>()) go.AddComponent<GazeDwell>();
            if (!go.GetComponent<PanePointerInput>()) go.AddComponent<PanePointerInput>();
            WorldPanelPick.MakePickable(go);

            // A BoardSet subscribes to its boards' inputs when it is enabled, so one added at runtime has to
            // be told about the board and enabled again to hear it.
            var set = go.GetComponent<BoardSet>();
            if (!set) set = go.AddComponent<BoardSet>();
            set.enabled = false;
            set.Adopt(document);
            set.enabled = true;
            return document;
        }

        /// The headset's copy: the same board with no tree, filled by the Mac over the wire. Its panel is read
        /// off the navigation prefab, which is never instantiated on a headset — the menu's logic stays on the Mac.
        public static GameObject Replica()
        {
            var prefab = Resources.Load<GameObject>("Menu/ActivityNavigation");   // what ActivityNavigation.Ensure loads
            var panel = prefab ? prefab.GetComponent<UIDocument>()?.panelSettings : null;
            if (!panel) { Debug.LogError("Navigation board: no panel on the navigation prefab; run Kinesthetic → Menu → Rebuild navigation prefab."); return null; }
            var go = new GameObject("Navigation board");
            Object.DontDestroyOnLoad(go);
            var document = Dress(go, panel);
            go.GetComponent<RemoteBoard>().role = RemoteBoard.Role.Replica;
            // A layer from the first frame: the Mac's root ignores picking and the wire carries that, but until its
            // tree arrives the empty root here would stop every ray meant for the boards behind it.
            var root = document.rootVisualElement;
            if (root != null) root.pickingMode = PickingMode.Ignore;
            return go;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Application.platform != RuntimePlatform.Android) return;
            Replica();
        }
    }
}

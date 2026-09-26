using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Kinesthetic.Shell
{
    /// The two arrows and the row of dots that frame the ring. Lives on its own `UIDocument` rather than
    /// inside each pane, for two reasons: every pane would otherwise have to carry a copy of the chrome in
    /// its own UXML, and — more importantly — chrome that rode the ring would turn away exactly when it was
    /// needed. This panel does not rotate. It is the one fixed thing in the scene.
    ///
    /// Gaze reaches it the same way it reaches anything else. `GazeDwell` raycasts from the centre of the
    /// view and takes the first collider it meets, so as long as this panel is wider than the windows and
    /// sits a little further back, the facing window answers everything in front of it and the arrows answer
    /// only past its edge. Put a `GazeDwell` on this object and wire `Committed` to `Press`.
    [RequireComponent(typeof(UIDocument))]
    public sealed class CarouselChrome : MonoBehaviour
    {
        public PaneCarousel carousel;

        VisualElement dots;
        Button previous, next;

        void Start() => Bind();

        /// A world-space panel never receives pointer events — UI Toolkit's `panel.Pick` returns null on one,
        /// which is why the board is driven by dwell rather than by clicks. So the arrows would answer a gaze
        /// and ignore a mouse, which is not what anyone sitting at a Mac expects. This is the pointer half:
        /// a ray from the camera through the cursor, resolved against this panel's own collider.
        ///
        /// The element maths is GazeDwell's, deliberately: a world-space panel lays its elements out in the
        /// panel's own units, centred and y-up, which is exactly the space the hit point lands in once it is
        /// put back into the collider's local space. Both paths end at Press, so neither can drift.
        void Clicked()
        {
            var mouse = Mouse.current;
            var cam = Camera.main;
            if (mouse == null || cam == null || !mouse.leftButton.wasPressedThisFrame) return;

            var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out var hit, 40f)) return;
            if (!hit.collider.transform.IsChildOf(transform)) return;

            var root = GetComponent<UIDocument>()?.rootVisualElement;
            if (root == null) return;
            var local = hit.collider.transform.InverseTransformPoint(hit.point);
            var point = new Vector2(local.x, local.y);
            foreach (var button in root.Query<Button>().ToList())
                if (!string.IsNullOrEmpty(button.name) && button.worldBound.Contains(point)) { Press(button.name); return; }
        }

        // The document rebuilds its tree when it is enabled, and the carousel may adopt its windows after
        // this runs, so keep trying until both ends exist. Same shape as MainMenuController.
        void Update()
        {
            if (previous == null || dots == null || dots.childCount == 0) Bind();
            Clicked();
        }

        void Bind()
        {
            var root = GetComponent<UIDocument>()?.rootVisualElement;
            if (root == null || carousel == null) return;

            if (previous == null)
            {
                previous = root.Q<Button>("carousel-prev");
                next = root.Q<Button>("carousel-next");
                if (previous == null || next == null) { previous = null; return; }
                previous.clicked += carousel.Previous;
                next.clicked += carousel.Next;
                carousel.Changed += _ => Mark();
                carousel.Settled += _ => Mark();
            }

            dots = root.Q("carousel-dots");
            if (dots == null || carousel.Slots.Count == 0) return;
            if (dots.childCount != carousel.Slots.Count)
            {
                // In the order they physically stand, not the order they were adopted — a dot row that does
                // not match the room is worse than no dot row, because it teaches the wrong map.
                dots.Clear();
                foreach (int i in carousel.LeftToRight)
                {
                    var slot = carousel.Slots[i];
                    var dot = new VisualElement { name = $"dot-{slot.id}" };
                    dot.AddToClassList("dot");
                    dot.tooltip = slot.title;
                    dots.Add(dot);
                }
            }
            Mark();
        }

        /// Which dot is lit. Driven from `Changed` rather than `Settled`, so it moves with the turn.
        void Mark()
        {
            if (dots == null) return;
            var order = carousel.LeftToRight;
            for (int i = 0; i < dots.childCount && i < order.Count; i++)
                dots[i].EnableInClassList("here", order[i] == carousel.CurrentIndex);

            // An arrow pointing at nothing is worse than no arrow: it invites a press and then appears broken.
            previous?.SetEnabled(carousel.CanStep(-1));
            next?.SetEnabled(carousel.CanStep(1));
        }

        /// Stand the chrome in a scene that already has a ring. One call, because wiring arrows should not
        /// be a paragraph in every setup script that wants them.
        ///
        /// Two numbers carry the whole trick. The panel is wider than the panes, so its edges stick out past
        /// whatever is facing; and it sits slightly further out than the ring's radius, so it is *behind*
        /// them. `GazeDwell` takes the first collider its ray meets, which makes that ordering the entire
        /// hit-testing rule: the facing pane wins everything in front of it, and the arrows answer only where
        /// there is no pane to answer first. Nothing needs to know about anything else.
        ///
        /// It hangs off the carousel's own transform rather than the ring inside it, so it does not turn.
        public static CarouselChrome Stand(PaneCarousel carousel, PanelSettings panel, VisualTreeAsset tree,
                                           float widthMetres = 5.4f, float behindPanes = .18f)
        {
            if (carousel == null || panel == null || tree == null) return null;

            var chrome = new GameObject("Carousel chrome", typeof(UIDocument), typeof(CarouselChrome));
            chrome.transform.SetParent(carousel.transform, false);
            chrome.transform.localPosition = Vector3.forward * (carousel.radius + behindPanes);
            chrome.transform.localRotation = Quaternion.identity;

            var document = chrome.GetComponent<UIDocument>();
            document.panelSettings = panel;
            document.visualTreeAsset = tree;
            document.worldSpaceSizeMode = WorldSpaceSizeMode.Fixed;
            document.worldSpaceSize = Reference;

            // Panel pixels are mapped to metres by the transform, exactly as MainMenuSetup does for the
            // board: the panel lays itself out first, and the scale carries it to the width we asked for.
            const float pixelsPerUnit = 100f;
            chrome.transform.localScale = Vector3.one * (widthMetres / (Reference.x / pixelsPerUnit));

            var component = chrome.GetComponent<CarouselChrome>();
            component.carousel = carousel;

            // The head path and the pointer path end at the same two lines, so they cannot drift apart.
            var gaze = chrome.AddComponent<GazeDwell>();
            gaze.Committed += component.Press;
            return component;
        }

        static readonly Vector2 Reference = new(1600, 900);

        /// Every way of pressing an arrow ends here: the button's own clicked event, the raycast above, and
        /// a dwell commit. That is three paths into two actions, and they are not mutually exclusive — a
        /// mouse click that UI Toolkit does deliver arrives as both a clicked event and a ray hit, which is
        /// one press turning the ring twice.
        ///
        /// Rather than trying to pick a single true input, the guard is on the outcome: the ring is already
        /// turning, so asking it to turn again is not a second instruction, it is the same one arriving by
        /// another road. It also stops a press being counted twice while the ring is mid-flight, which is
        /// exactly when an impatient second click lands.
        public void Press(string element)
        {
            if (carousel == null || carousel.IsTurning) return;
            if (element == "carousel-prev") carousel.Previous();
            else if (element == "carousel-next") carousel.Next();
        }
    }
}

using UnityEngine;
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

        // The document rebuilds its tree when it is enabled, and the carousel may adopt its windows after
        // this runs, so keep trying until both ends exist. Same shape as MainMenuController.
        void Update() { if (previous == null || dots == null || dots.childCount == 0) Bind(); }

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
            }

            dots = root.Q("carousel-dots");
            if (dots == null || carousel.Slots.Count == 0) return;
            if (dots.childCount != carousel.Slots.Count)
            {
                dots.Clear();
                foreach (var slot in carousel.Slots)
                {
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
            for (int i = 0; i < dots.childCount; i++)
                dots[i].EnableInClassList("here", i == carousel.CurrentIndex);
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

        /// What a dwell commits. Takes the element name `GazeDwell` reports, so the head path and the pointer
        /// path run the same two lines and cannot drift apart.
        public void Press(string element)
        {
            if (element == "carousel-prev") carousel?.Previous();
            else if (element == "carousel-next") carousel?.Next();
        }
    }
}

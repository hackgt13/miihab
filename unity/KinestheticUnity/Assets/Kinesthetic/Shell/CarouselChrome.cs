using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.Shell
{
    /// The two arrows and the row of dots that frame the ring. Lives on its own `UIDocument` rather than
    /// inside each window, for two reasons: every window would otherwise have to carry a copy of the chrome
    /// in its own UXML, and — more importantly — chrome that rode the ring would turn away exactly when it
    /// was needed. This panel does not rotate. It is the one fixed thing in the scene.
    ///
    /// Gaze reaches it the same way it reaches anything else. `GazeDwell` raycasts from the centre of the
    /// view and takes the first collider it meets, so as long as this panel is wider than the windows and
    /// sits a little further back, the facing window answers everything in front of it and the arrows answer
    /// only past its edge. Put a `GazeDwell` on this object and wire `Committed` to `Press`.
    [RequireComponent(typeof(UIDocument))]
    public sealed class CarouselChrome : MonoBehaviour
    {
        public WindowCarousel carousel;

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
            if (dots == null || carousel.Windows.Count == 0) return;
            if (dots.childCount != carousel.Windows.Count)
            {
                dots.Clear();
                foreach (var window in carousel.Windows)
                {
                    var dot = new VisualElement { name = $"dot-{window.id}" };
                    dot.AddToClassList("dot");
                    dot.tooltip = window.title;
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
                dots[i].EnableInClassList("here", carousel.Current != null && i == carousel.Current.Slot);
        }

        /// What a dwell commits. Takes the element name `GazeDwell` reports, so the head path and the pointer
        /// path run the same two lines and cannot drift apart.
        public void Press(string element)
        {
            if (element == "carousel-prev") carousel?.Previous();
            else if (element == "carousel-next") carousel?.Next();
        }
    }
}

using System.Collections.Generic;
using Kinesthetic.Shell;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.Panes
{
    /// Opens, closes and focuses panes. It holds no opinion about where they stand — that is
    /// PaneCarousel's ring, and this never touches a pane's transform.
    ///
    /// Per-scene and not DontDestroyOnLoad on purpose. Panes are world-space geometry anchored to
    /// a room, so carrying them across a scene load would leave one floating in the wrong venue.
    /// The persistent concerns a shell would own — audio, preferences, scene routing — already
    /// live in ActivityNavigation and are not restated here.
    ///
    /// Content is ticked only while its pane is the one being faced, which is what the carousel's
    /// Settled event exists for: a clip should not play, and a bridge should not be polled, from a
    /// pane that is edge-on and unreadable.
    ///
    /// Opening and closing go through the ring's Add and Remove rather than a full Adopt, so nobody
    /// standing in the ring moves and the person is never spun away from what they were reading.
    public sealed class PaneHost : MonoBehaviour
    {
        public static PaneHost Instance { get; private set; }

        [Header("Assigned by Kinesthetic -> Panes -> Create pane sandbox")]
        public PanelSettings panelSettings;
        public VisualTreeAsset paneTree;
        public PaneCarousel carousel;

        [Tooltip("How far the pointer reaches, in metres. Not layout — the ring owns distance.")]
        public float reach = 12;

        [Tooltip("A mobile GPU budget, not a UI opinion. The ring's own spacing decides what looks right.")]
        public int maxPanes = 4;

        readonly List<Pane> panes = new();
        PanePointer pointer;
        Pane dragging;
        float dragDistance;
        string facing;

        public IReadOnlyList<Pane> Panes => panes;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; }

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(this); return; }
            Instance = this;
            pointer = GetComponent<PanePointer>();
            if (!pointer) pointer = gameObject.AddComponent<PanePointer>();
        }

        void OnEnable()
        {
            if (!carousel) return;
            carousel.Settled += Settled;
            carousel.Leaving += Leaving;
        }

        void OnDisable()
        {
            if (!carousel) return;
            carousel.Settled -= Settled;
            carousel.Leaving -= Leaving;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Settled(PaneCarousel.Slot slot) { facing = slot.id; Focus(Find(slot.id)); }
        void Leaving(PaneCarousel.Slot slot)
        {
            var left = Find(slot.id);
            if (left) left.Ticking = false;           // edge-on and unreadable until the turn ends
            if (facing == slot.id) facing = null;
        }

        Pane Find(string id) => panes.Find(p => p.Id == id);

        /// Open a pane for `content` and stand it in the ring. Returns null when the budget is
        /// full, rather than quietly costing frames.
        public Pane Open(string id, IPaneContent content)
        {
            if (!carousel || content == null || string.IsNullOrEmpty(id) || panes.Count >= maxPanes) return null;
            if (Find(id)) return null;                       // ids are how the ring is addressed

            var go = new GameObject("Pane · " + content.Title);
            go.transform.SetParent(transform, false);         // Add reparents it into the ring
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = paneTree;
            var pane = go.AddComponent<Pane>();
            pane.Id = id;

            panes.Add(pane);
            pane.SetContent(content);

            carousel.Add(new PaneCarousel.Slot(id, content.Title, pane.transform));
            // Add deliberately announces nothing — opening a pane in the background should not move
            // the furniture — so the first one has to be adopted as the faced pane here.
            if (string.IsNullOrEmpty(facing)) facing = carousel.Current.id;
            Focus(Find(facing));                              // a pane opened behind the person does not tick
            return pane;
        }

        public void Forget(Pane pane)
        {
            if (!panes.Remove(pane)) return;
            if (dragging == pane) dragging = null;
            if (carousel) carousel.Remove(pane.Id);      // the ring closes the gap and keeps the view still
        }

        /// Focus and ticking follow the faced pane together: the one being read runs, the rest stand idle.
        public void Focus(Pane pane)
        {
            foreach (var p in panes) { p.SetFocused(p == pane); p.Ticking = p == pane; }
        }

        void Update()
        {
            if (pointer == null || !carousel || carousel.IsTurning) return;

            var element = WorldPanelPick.Under<VisualElement>(
                pointer.Ray, reach, Physics.DefaultRaycastLayers,
                out var hitPane, out _, out var normalised);

            if (dragging) { Drag(); return; }

            foreach (var pane in panes) pane.Highlight(pane == hitPane ? element : null);
            if (!hitPane) return;

            if (pointer.PressedThisFrame && hitPane.IsGrip(element))
            {
                // Dragging a pane off its slot is a deliberate exception to the ring owning
                // placement: the person moved it, so it stops being furniture until the ring restands it.
                dragging = hitPane;
                dragDistance = Vector3.Distance(pointer.Ray.origin, hitPane.transform.position);
                return;
            }

            if (Pane.Inside(normalised) && hitPane.Content is IPanePointerTarget target)
                target.OnPointer(normalised, pointer.Pressed);
        }

        void Drag()
        {
            if (!pointer.Pressed) { dragging = null; return; }
            dragging.transform.position = pointer.Ray.origin + pointer.Ray.direction * dragDistance;
        }
    }
}

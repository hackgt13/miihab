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
        void Leaving(PaneCarousel.Slot slot) { if (facing == slot.id) facing = null; }

        Pane Find(string id) => panes.Find(p => p.Id == id);

        /// Open a pane for `content` and stand it in the ring. Returns null when the budget is
        /// full, rather than quietly costing frames.
        public Pane Open(string id, IPaneContent content)
        {
            if (content == null || string.IsNullOrEmpty(id) || panes.Count >= maxPanes) return null;
            if (Find(id)) return null;                       // ids are how the ring is addressed

            var go = new GameObject("Pane · " + content.Title);
            go.transform.SetParent(transform, false);         // Adopt reparents it into the ring
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = paneTree;
            var pane = go.AddComponent<Pane>();
            pane.Id = id;

            panes.Add(pane);
            pane.Bind(content);
            Restand();
            return pane;
        }

        public void Forget(Pane pane)
        {
            if (!panes.Remove(pane)) return;
            if (dragging == pane) dragging = null;
            Restand();
        }

        /// Hand the whole list to the ring. Adopt rebuilds it by contract, and it also resets to
        /// slot 0 — so whatever was being faced is turned back to rather than yanked away. That
        /// restore is a visible turn, which is the open question with the carousel's owner: an
        /// Add(Slot) that appends without resetting would make this a no-op instead.
        void Restand()
        {
            if (!carousel) return;
            var slots = new PaneCarousel.Slot[panes.Count];
            for (int i = 0; i < panes.Count; i++)
                slots[i] = new PaneCarousel.Slot(panes[i].Id, panes[i].Content?.Title ?? panes[i].Id, panes[i].transform);

            string wasFacing = facing;
            carousel.Adopt(slots);
            if (!string.IsNullOrEmpty(wasFacing) && wasFacing != carousel.Current.id) carousel.Show(wasFacing);
        }

        public void Focus(Pane pane)
        {
            foreach (var p in panes) p.SetFocused(p == pane);
        }

        void Update()
        {
            // Only the pane being faced runs. Everything else is standing there, drawn but idle.
            var current = Find(facing);
            if (current) current.Tick();

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
                // placement: the person moved it, so it stops being furniture until Restand.
                dragging = hitPane;
                dragDistance = Vector3.Distance(pointer.Ray.origin, hitPane.transform.position);
                return;
            }

            if (hitPane.Content is IPanePointerTarget target) target.OnPointer(normalised, pointer.Pressed);
        }

        void Drag()
        {
            if (!pointer.Pressed) { dragging = null; return; }
            dragging.transform.position = pointer.Ray.origin + pointer.Ray.direction * dragDistance;
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.Panes
{
    /// What goes inside a pane. Deliberately one shape, not two: a texture-backed content
    /// (video, a small game, a render of anything) returns a tree containing an Image, the
    /// way GolfHud already wraps its minimap render texture. Adding a second `Texture Surface`
    /// member would double every code path in the host for no gain.
    ///
    /// Content never learns which pane it is in. No Pane property, no host reference — the
    /// moment content can reach its window, every content class grows shell plumbing and the
    /// coupling this interface exists to prevent is back.
    ///
    /// Queries are cached in Bind, following GolfHud rather than BowlingHud: the latter runs
    /// twelve root.Q lookups every frame.
    public interface IPaneContent : IDisposable
    {
        string Title { get; }

        /// Pane size in metres. The host may clamp it to fit its layout.
        Vector2 PreferredSize { get; }

        /// Build the tree under the pane's content area and cache element references.
        void Bind(VisualElement root);

        /// Driven by the host, so content needs no MonoBehaviour and stays unit-testable.
        void Tick();
    }

    /// Opt-in for content that needs the pointer itself rather than UI Toolkit's own events:
    /// a small game, a video scrub bar, anything drawing into a texture. `point` is normalised
    /// 0..1 across the content area with the origin at the bottom-left, which is what a Camera
    /// viewport and a RenderTexture both already expect.
    public interface IPanePointerTarget
    {
        void OnPointer(Vector2 point, bool pressed);
    }
}

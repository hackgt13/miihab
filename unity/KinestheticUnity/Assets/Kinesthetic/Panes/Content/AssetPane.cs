using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace Kinesthetic.Panes
{
    /// Where local files live. `local-data/` sits beside the Unity project, not inside Assets, so
    /// it is resolved rather than referenced. This is editor-correct; a built player has a
    /// different dataPath, and the honest fix then is to serve the files from the coordinator on
    /// 8766, which already streams static files with a mime map.
    public static class PaneAssets
    {
        public static string Root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../local-data"));
        public static string Resolve(string relative) =>
            Path.IsPathRooted(relative) ? relative : Path.Combine(Root, relative);
    }

    /// A local asset in a pane: a still, a page of text, or a clip.
    ///
    /// All three are one content type because they differ only in how they fill an element — the
    /// texture cases end in an Image exactly as GolfHud's minimap does, which is why IPaneContent
    /// needs no separate texture channel.
    public sealed class AssetPane : IPaneContent
    {
        public enum Kind { Image, Text, Video }

        readonly Kind kind;
        readonly string path;

        GameObject carrier;                // owns the VideoPlayer; created only for clips
        VideoPlayer video;
        RenderTexture target;
        Texture2D still;
        Label message;

        public string Title { get; }
        public Vector2 PreferredSize { get; }

        AssetPane(Kind kind, string path, string title, Vector2 size)
        {
            this.kind = kind; this.path = PaneAssets.Resolve(path);
            Title = title; PreferredSize = size;
        }

        public static AssetPane Image(string path, string title = null) =>
            new(Kind.Image, path, title ?? Path.GetFileName(path), new Vector2(1.1f, .72f));

        public static AssetPane Text(string path, string title = null) =>
            new(Kind.Text, path, title ?? Path.GetFileName(path), new Vector2(.86f, 1.12f));   // a page, not a screen

        public static AssetPane Video(string path, string title = null) =>
            new(Kind.Video, path, title ?? Path.GetFileName(path), new Vector2(1.28f, .72f));  // 16:9

        public void Bind(VisualElement root)
        {
            root.Clear();
            if (!File.Exists(path)) { Fail(root, "Not found: " + path); return; }

            switch (kind)
            {
                case Kind.Text:
                    var scroll = new ScrollView { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
                    scroll.style.flexGrow = 1;
                    var body = new Label(File.ReadAllText(path));
                    body.AddToClassList("pane-reading");
                    scroll.Add(body); root.Add(scroll);
                    break;

                case Kind.Image:
                    still = new Texture2D(2, 2);
                    if (!still.LoadImage(File.ReadAllBytes(path))) { Fail(root, "Unreadable image"); return; }
                    root.Add(Fill(new Image { image = still, scaleMode = ScaleMode.ScaleToFit }));
                    break;

                case Kind.Video:
                    // 720p is a deliberate cap. On a mobile GPU the decode is cheap and the
                    // per-frame texture upload is not, so the pane's pixel budget is the knob.
                    target = new RenderTexture(1280, 720, 0) { name = "Pane video · " + Title };
                    target.Create();
                    carrier = new GameObject("Pane video player · " + Title);
                    var speaker = carrier.AddComponent<AudioSource>();
                    speaker.spatialBlend = 0; speaker.playOnAwake = false;
                    video = carrier.AddComponent<VideoPlayer>();
                    video.playOnAwake = false; video.isLooping = true;
                    video.source = VideoSource.Url; video.url = "file://" + path;
                    video.renderMode = VideoRenderMode.RenderTexture; video.targetTexture = target;
                    video.audioOutputMode = VideoAudioOutputMode.AudioSource;
                    video.SetTargetAudioSource(0, speaker);
                    video.Play();
                    root.Add(Fill(new Image { image = target, scaleMode = ScaleMode.ScaleToFit }));
                    break;
            }
        }

        static VisualElement Fill(VisualElement element)
        {
            element.style.flexGrow = 1;
            element.pickingMode = PickingMode.Ignore;   // the pane's chrome owns interaction
            return element;
        }

        void Fail(VisualElement root, string why)
        {
            message = new Label(why);
            message.AddToClassList("pane-problem");
            root.Add(message);
        }

        public void Tick() { }                          // VideoPlayer and Image both self-drive

        public void Dispose()
        {
            if (video) video.Stop();
            if (carrier) Object.Destroy(carrier);
            if (target) { target.Release(); Object.Destroy(target); }
            if (still) Object.Destroy(still);
            carrier = null; video = null; target = null; still = null;
        }
    }
}

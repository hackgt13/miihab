using UnityEngine;

namespace Kinesthetic.Panes
{
    /// Opens a few panes so the sandbox scene proves the surface without needing any art.
    /// Content here is deliberately the three kinds that matter: a page, a clip, and a live stage.
    public sealed class PaneSandbox : MonoBehaviour
    {
        [Tooltip("Paths under local-data/, or absolute. Missing files show the problem in the pane.")]
        public string textPath = "panes/readme.txt";
        public string videoPath = "panes/clip.mp4";
        public bool openDemoGame = true;

        GameObject spinner, cursor;

        void Start()
        {
            var host = PaneHost.Instance;
            if (!host) { Debug.LogWarning("PaneSandbox needs a PaneHost in the scene."); return; }

            host.Open("page", AssetPane.Text(textPath, "A page"));
            host.Open("clip", AssetPane.Video(videoPath, "A clip"));
            if (openDemoGame) host.Open("stage", DemoGame());
        }

        /// The smallest thing that proves the stage: one shape that moves on its own, one that
        /// follows the pointer. If both work, video and a real game work for the same reasons.
        GamePane DemoGame()
        {
            GamePane pane = null;
            pane = new GamePane("A stage", new Vector2(1.05f, .7f), new Vector2Int(768, 512),
                onPointer: (point, pressed) =>
                {
                    if (!cursor) return;
                    // Normalised 0..1 from the bottom-left maps onto the stage camera's viewport.
                    var ray = pane.StageCamera.ViewportPointToRay(new Vector3(point.x, point.y, 0));
                    var plane = new Plane(Vector3.forward, pane.StageRoot.position);
                    if (plane.Raycast(ray, out float t)) cursor.transform.position = ray.GetPoint(t);
                    cursor.GetComponent<MeshRenderer>().material.color = pressed ? Palette.Coral40 : Palette.Jungle40;
                },
                onTick: p =>
                {
                    if (!spinner) return;
                    spinner.transform.Rotate(new Vector3(18, 34, 0) * Time.deltaTime, Space.Self);
                });
            return pane;
        }

        void Update()
        {
            // Spawn into the stage once it exists; Bind runs when the host opens the pane.
            var host = PaneHost.Instance;
            if (!host || spinner) return;
            foreach (var pane in host.Panes)
            {
                if (pane.Content is not GamePane game || game.StageRoot == null) continue;
                spinner = game.Spawn("Spinner", PrimitiveType.Cube);
                spinner.transform.localPosition = new Vector3(-.9f, .2f, 0);
                cursor = game.Spawn("Cursor", PrimitiveType.Sphere);
                cursor.transform.localScale = Vector3.one * .45f;
                var light = new GameObject("Stage light") { layer = GamePane.StageLayer };
                light.transform.SetParent(game.StageRoot, false);
                light.transform.rotation = Quaternion.Euler(38, 20, 0);
                var sun = light.AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 1.1f;
                sun.cullingMask = 1 << GamePane.StageLayer;     // do not light the venue from 10 km away
                break;
            }
        }
    }
}

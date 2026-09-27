using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Kinesthetic.Bowling
{
    public sealed class BowlingHud : MonoBehaviour
    {
        public BowlingGame game;
        VisualElement root;
        readonly Label[] marks = new Label[10], scores = new Label[10];
        readonly VisualElement[] frames = new VisualElement[10];
        Label total;
        void Start()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            var board = root.Q("scoreboard");
            for (int i = 0; i < 10; i++)
            {
                var frame = frames[i] = new VisualElement(); frame.AddToClassList("frame");
                var number = new Label((i + 1).ToString()); number.AddToClassList("frame-number"); frame.Add(number);
                marks[i] = new Label(" "); marks[i].AddToClassList("frame-marks"); frame.Add(marks[i]);
                scores[i] = new Label("–"); scores[i].AddToClassList("frame-score"); frame.Add(scores[i]); board.Add(frame);
            }
            var sum = new VisualElement(); sum.AddToClassList("total");
            var caption = new Label("TOTAL"); caption.AddToClassList("frame-number"); sum.Add(caption);
            total = new Label("0"); total.AddToClassList("total-score"); sum.Add(total); board.Add(sum);
            root.Q<Button>("calibrate").clicked += game.Recalibrate;
            root.Q<Button>("retry").clicked += game.Connect;
            root.Q<Button>("pause").clicked += () => game.SetPaused(true);
            root.Q<Button>("resume").clicked += () => game.SetPaused(false);
            root.Q<Button>("replay").clicked += game.RestartRound;
            foreach (string name in new[] { "setupBack", "pauseBack", "resultBack" }) root.Q<Button>(name).clicked += Back;
        }
        // The same way back as every other activity (ActivityNavigation), so the round is recorded and the headset
        // follows the Mac to the menu, rather than a scene load only the Mac knew about.
        static void Back() { Time.timeScale = 1; Kinesthetic.Menu.ActivityNavigation.Ensure().ReturnToMenuNow(); }
        public static string AimText(float degrees) => Mathf.Abs(degrees) < .25f ? "Straight" : $"{Mathf.Abs(degrees):0.0}° {(degrees < 0 ? "left" : "right")}";
        void Update()
        {
            if (root == null || !game) return;
            bool setup = game.Phase == "Setup";
            root.Q("setup").EnableInClassList("hidden", !setup || game.Paused);
            root.Q("paused").EnableInClassList("hidden", !game.Paused);
            root.Q("results").EnableInClassList("hidden", game.Phase != "Complete" || game.Paused);
            root.Q<Label>("device").text = game.MotionReady ? "AirPod connected" : "Connecting AirPods…";
            root.Q("device").EnableInClassList("ready", game.MotionReady);
            root.Q<Button>("calibrate").SetEnabled(game.CanRecalibrate);
            root.Q<Label>("setupStatus").text = !game.MotionReady ? game.Connection : game.Swing.Calibrated ? "Ready. Starting…" : "Hold still for a moment…";
            root.Q<Label>("cue").text = game.Cue;
            root.Q<Label>("frameLabel").text = game.Score.Complete ? "ROUND COMPLETE" : $"FRAME {game.Score.Frame + 1} · BALL {game.Score.BallNumber}";
            bool live = game.Phase == "Ready" || setup;
            root.Q<Label>("aim").text = AimText(live ? game.Swing.Aim : game.LastAim);
            root.Q<Label>("power").text = $"{Mathf.RoundToInt(100 * (live ? game.Swing.Power : game.LastPower))}%";
            for (int f = 0; f < 10; f++)
            {
                marks[f].text = game.Score.Marks(f); scores[f].text = game.Score.Cumulative(f)?.ToString() ?? "–";
                frames[f].EnableInClassList("active", f == game.Score.Frame && !game.Score.Complete);
            }
            total.text = game.Score.Total.ToString(); root.Q<Label>("finalScore").text = total.text;
        }
    }
}

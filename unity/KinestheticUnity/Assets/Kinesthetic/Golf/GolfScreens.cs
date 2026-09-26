using Kinesthetic.Menu;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.Golf
{
    public sealed class GolfScreens
    {
        readonly KinestheticGolf game;
        readonly VisualElement root, setup, results;
        readonly Button launch, play, setupButton;
        bool resultsShown;
        public bool SetupVisible => !setup.ClassListContains("hidden");

        public GolfScreens(VisualElement root, KinestheticGolf game)
        {
            this.root=root; this.game=game;
            setup=root.Q("golf-setup"); results=root.Q("golf-results");
            setupButton=root.Q<Button>("setup-open"); launch=root.Q<Button>("setup-connect"); play=root.Q<Button>("setup-play");
            setupButton.clicked+=OpenSetup;
            launch.clicked+=game.StartCapture;
            play.clicked+=CloseSetup;
            root.Q<Button>("setup-close").clicked+=CloseSetup;
            root.Q<Button>("round-replay").clicked+=()=> {
                game.RestartRound(); resultsShown=false; results.AddToClassList("hidden");
                OpenSetup();
            };
            root.Q<Button>("round-menu").clicked+=()=>ActivityNavigation.Ensure().OpenReturn();
            root.Q<Button>("sound-toggle").clicked+=()=>ActivityNavigation.Ensure().PlaySelect();
            OpenSetup();
        }
        void OpenSetup()
        {
            if(game.Phase=="Round complete")return;
            setup.RemoveFromClassList("hidden"); launch.Focus();
            ActivityNavigation.Ensure().PlaySelect();
        }
        void CloseSetup()
        {
            setup.AddToClassList("hidden"); setupButton.Focus();
            ActivityNavigation.Ensure().PlayBack();
        }
        void Step(string name, bool complete, string waiting, string ready)
        {
            var row=root.Q(name); row.EnableInClassList("step-ready",complete);
            row.Q<Label>(className:"step-state").text=complete?ready:waiting;
            row.Q<Label>(className:"step-badge").text=complete?"✓":name=="setup-camera"?"1":name=="setup-motion"?"2":"3";
        }
        public void Update()
        {
            bool ready=game.StrikePoseReady && game.MotionReady && game.ClubCalibrated;
            Step("setup-camera",game.StrikePoseReady,"Keep shoulders, elbows and both hands in view.","Both arms are in view.");
            Step("setup-motion",game.MotionReady,"Pair AirPods with this Mac; mount the reporting AirPod on the club.","Live club movement connected.");
            Step("setup-calibration",ready,"Rest the club at the mat. Hold still to calibrate automatically.","Club calibrated. You're ready to swing.");
            play.SetEnabled(ready); launch.SetEnabled(!game.StartingCapture);
            launch.text=game.StartingCapture?"Connecting…":game.CaptureRequested?"Reconnect devices":"Connect camera & AirPods";
            root.Q<Label>("setup-status").text=ready?"All set. Enjoy your round together.":game.CaptureRequested?game.CaptureStatus:"Connect your devices when you're ready.";
            root.Q<Label>("device-status").text=ready?"Ready to swing":!game.CaptureRequested?"Set up to play":!game.StrikePoseReady?"Camera needs a clear view":!game.MotionReady?"Waiting for AirPods":"Hold still to calibrate";
            root.Q("device-chip").EnableInClassList("connected",ready);
            setupButton.SetEnabled(game.Phase=="Address");
            if(game.Phase!="Round complete" || resultsShown)return;
            resultsShown=true; setup.AddToClassList("hidden"); results.RemoveFromClassList("hidden");
            root.Q<Label>("round-title").text=game.Strokes[0]==game.Strokes[1]?"A round shared. A tie earned.":"A round well played.";
            root.Q<Label>("round-you").text=game.Strokes[0].ToString();
            root.Q<Label>("round-friend").text=game.Strokes[1].ToString();
            root.Q<Label>("round-you-par").text=ScoreName(game.Strokes[0]);
            root.Q<Label>("round-friend-par").text=ScoreName(game.Strokes[1]);
            root.Q<Button>("round-replay").Focus(); ActivityNavigation.Ensure().PlaySelect();
        }
        static string ScoreName(int strokes)
        {
            int relative=strokes-4;
            return relative==0?"Even par":relative>0?$"+{relative} over par":$"{relative} under par";
        }
    }
}

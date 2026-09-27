using Kinesthetic.Menu;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.Golf
{
    public sealed class GolfScreens
    {
        readonly KinestheticGolf game;
        readonly VisualElement root, setup, results;
        readonly Button retry, close, setupButton;
        bool automaticStart;
        float readyAt = -1, openedAt;
        bool resultsShown;
        public bool SetupVisible => !setup.ClassListContains("hidden");

        public GolfScreens(VisualElement root, KinestheticGolf game)
        {
            this.root=root; this.game=game;
            setup=root.Q("golf-setup"); results=root.Q("golf-results");
            setupButton=root.Q<Button>("setup-open"); retry=root.Q<Button>("setup-retry"); close=root.Q<Button>("setup-close");
            setupButton.clicked+=()=>OpenSetup(false);
            retry.clicked+=()=>{openedAt=Time.unscaledTime; game.StartCapture();};
            close.clicked+=CloseSetup;
            root.Q<Button>("round-replay").clicked+=()=> {
                game.RestartRound(); resultsShown=false; results.AddToClassList("hidden");
                OpenSetup(true);
            };
            root.Q<Button>("round-menu").clicked+=()=>ActivityNavigation.Ensure().OpenReturn();
            OpenSetup(true);
        }
        void OpenSetup(bool autoStart)
        {
            if(game.Phase=="Round complete")return;
            automaticStart=autoStart; readyAt=-1; openedAt=Time.unscaledTime;
            close.EnableInClassList("hidden",autoStart);
            setup.RemoveFromClassList("hidden");
            if(!autoStart)close.Focus();
            ActivityNavigation.Ensure().PlaySelect();
        }
        void CloseSetup()
        {
            automaticStart=false; setup.AddToClassList("hidden"); setupButton.Focus();
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
            bool ready=game.MotionReady && game.ClubCalibrated;
            Step("setup-camera",game.MotionReady,"Hold your AirPods together on the club.","AirPods ready.");
            Step("setup-motion",game.ClubCalibrated,"Hold the club still at your hip.","Club ready.");
            Step("setup-calibration",ready,"Swing back, then through the ball.","Ready. Swing through the ball.");
            bool counting=ready && ActivityNavigation.Instance?.OverlayOpen!=true;
            if(!counting)readyAt=-1;
            else if(readyAt<0)readyAt=Time.unscaledTime;
            float countdown=readyAt<0?3:Mathf.Max(0,3-(Time.unscaledTime-readyAt));
            bool retryNeeded=false;   // nothing to retry: the AirPods connect on their own
            retry.EnableInClassList("hidden",!retryNeeded);
            root.Q<Label>("setup-status").text=automaticStart && counting?$"Starting in {Mathf.Max(1,Mathf.CeilToInt(countdown))}…":ready?"You're ready.":!game.MotionReady?"Waiting for AirPods…":"Hold still…";
            root.Q<Label>("device-status").text=ready?"Ready":!game.MotionReady?"AirPods connecting…":"Hold still…";
            if(SetupVisible && automaticStart && counting && countdown<=0)CloseSetup();
            root.Q("device-chip").EnableInClassList("connected",ready);
            setupButton.SetEnabled(game.Phase=="Address");
            if(game.Phase!="Round complete" || resultsShown)return;
            resultsShown=true; setup.AddToClassList("hidden"); results.RemoveFromClassList("hidden");
            root.Q<Label>("round-title").text=game.Strokes[0]==game.Strokes[1]?"A tie!":"Nice round!";
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

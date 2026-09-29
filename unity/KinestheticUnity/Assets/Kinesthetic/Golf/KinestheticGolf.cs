using System;
using System.IO;
using System.Collections;
using UnityEngine.Networking;
using UnityEngine;
using UnityEngine.UIElements;

using Kinesthetic.Activities;

namespace Kinesthetic.Golf
{
    public sealed class KinestheticGolf : MonoBehaviour, IActivity
    {
        public Rigidbody ball;
        public Camera spectator;
        public Transform tee, cup;
        public Transform[] players, clubs;
        public PoseRig[] rigs;
        public LineRenderer aimLine;
        public string bridge = "http://127.0.0.1:8766";
        public bool allowDeveloperShots;
        public int activePlayer;
        public int clubIndex;
        public float aimOffset;
        public string Phase { get; private set; } = "Address";
        public string Message { get; private set; } = "Starting camera and AirPod tracking…";
        public int[] Strokes { get; private set; } = new int[2];
        public bool[] Finished { get; private set; } = new bool[2];
        public int AcceptedShots { get; private set; }
        // Swings that qualified but whose clubhead path missed the virtual ball. Part of the dose:
        // "attempted 11, 8 counted" is the one figure comparable across every activity.
        public int[] Misses { get; private set; } = new int[2];
        // IActivity. Golf has no start button and no server-side session to close: a round begins
        // with the scene and ends when both players hole out.
        public string ActivityId => "golf.adaptive";
        /// <summary>Raised once when both players have holed out, carrying the recorded session id.</summary>
        public event Action<string> Completed;
        public bool IsRunning => !roundReported;
        public bool IsBusy => postingRound;
        bool postingRound;
        DateTime roundStartedUtc = DateTime.UtcNow;
        bool roundReported, roundSimulated;
        // Tracking loss in the round's record is the AirPod stream reconnecting: golf no longer uses the camera, whose
        // reconnects were counted here before, and the baseline is taken at the start of every round, the first too.
        int motionReconnectsAtRoundStart;
        int TrackingLossEvents => (SensorHub.Instance?.MotionReconnects ?? 0) - motionReconnectsAtRoundStart;
        // The camera is not part of golf any more: the club is the AirPod, held from the hip, and a swing that goes
        // through the ball hits it. A live camera still animates the arms when there is one; nothing waits on it.
        public Vector3 HudAim => AimDirection();
        public Vector3 GetLie(int index) => lies[index];
        public float HudPower => Phase=="Address" ? (IMUReady && swing.Calibrated ? SwingPower(new Vector3(latest.rotationRate[0],latest.rotationRate[1],latest.rotationRate[2]).magnitude)*PowerCap : 0) : lastShotPower;
        // Rehab unlocks golf (coordinator/dashboard.ts golfUnlock): the patient's swing reaches this share of a full
        // drive, rising with each rehab level. The friend is uncapped. Offline it stays 1, so golf is unchanged.
        float patientPowerCap=1; string unlockMessage;
        float PowerCap => activePlayer==0 ? patientPowerCap : 1;
        IEnumerator LoadUnlock()
        {
            using var request=UnityWebRequest.Get(bridge+"/api/golf-unlock");
            request.timeout=3;
            yield return request.SendWebRequest();
            if(request.result!=UnityWebRequest.Result.Success)yield break;
            try
            {
                var unlock=Newtonsoft.Json.Linq.JObject.Parse(request.downloadHandler.text);
                float cap=unlock["swingPowerCap"]?.ToObject<float>() ?? 1;
                if(PoseMath.Finite(cap))patientPowerCap=Mathf.Clamp(cap,.1f,1);
                unlockMessage=(string)unlock["message"];
            }
            catch(Exception){}
        }
        float lastShotPower;
        static double Now=>System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency;
        // Forgiving on purpose: a gentle rehab swing (4-6 rad/s) already sends the ball a good way, a firm one (9 rad/s)
        // is a full drive, and any swing that goes through the ball moves it at least a little. A full driver (25 m/s)
        // stops about 5 m from the cup on this hole.
        const float MinSwingRadPerSec=1f, FullSwingRadPerSec=9f, PowerFloor=.3f;
        public static float SwingPower(float radPerSec)=>Mathf.Lerp(PowerFloor,1,Mathf.InverseLerp(MinSwingRadPerSec,FullSwingRadPerSec,radPerSec));
        GolfHud hud;
        GolfScreens screens;
        public bool MotionReady => IMUReady;
        public bool ClubCalibrated => swing.Calibrated;
        public bool InterfaceOpen => screens?.SetupVisible == true || Kinesthetic.Menu.ActivityNavigation.Instance?.OverlayOpen == true;
        GroundAimGuide groundAim;
        readonly ClubSwingGate swing = new();
        readonly VirtualClubStrike strikeZone = new();
        bool pendingSpatial;
        float pendingSpeed;
        Vector3 Grip(int player)=>rigs[player].GolfGripCenter; // same point the rendered club attaches to
        AudioSource hitAudio;
        AudioClip hitClip;
        double motionContactAt;
        void ResetSwing(bool clearClub=true){swing.Reset();pendingSpatial=false;if(clearClub)strikeZone.Reset();}
        readonly Vector3[] lies = new Vector3[2];
        readonly Vector3[] rigRest = new Vector3[2];
        readonly GolfClubPresentation[] clubPresentation=new GolfClubPresentation[2];
        readonly string[] playerIds = {"patient", "friend"};
        readonly string[] names = {"You", "Your friend"};
        readonly string[] clubNames = {"Driver", "Iron", "Putter"};
        LivePoseClient pose => SensorHub.Instance?.Pose;
        GolfMotionClient motion => SensorHub.Instance?.Motion;
        PoseFrame poseFrame;
        ClubMotionPacket latest;
        long poseTicks, imuTicks, poseSequence=-1, imuSequence=-1;
        string poseSession, imuSession;
        float stillSince=-1, stationarySince=-1, shotAt;
        Quaternion lastAttitude;
        Vector3 cameraVelocity, lastSafeLie;
        Label heading, score, distance, guidance;
        Button setupButton;
        bool captureRequested;
        float readySince=-1, advanceAt=-1;

        string logPath;

        // Solo, the friend has no AirPods of their own (AGENTS.md: a second person's pair only exists in a group), so
        // they take their own shot after a moment instead of leaving the round waiting on a turn nobody can play.
        const float CompanionWaitSeconds=2.5f, GimmeMetres=2.2f;
        float turnStartedAt, friendMotionAt=-99;
        bool friendLive => Time.unscaledTime-friendMotionAt<2;
        void CompanionTurn()
        {
            if(activePlayer!=1 || Phase!="Address" || friendLive || InterfaceOpen) return;
            if(Time.unscaledTime-turnStartedAt<CompanionWaitSeconds) return;
            float distance=Vector3.ProjectOnPlane(cup.position-ball.position,Vector3.up).magnitude;
            // Roughly the stroke a steady friend would pick: most of the way there, a little short or long.
            float power=Mathf.Clamp01(clubIndex==2?distance/12f:distance/110f)*UnityEngine.Random.Range(.85f,1.1f);
            Launch(Mathf.Max(.15f,power),"companion",0);
        }

        void Start()
        {
            Application.runInBackground = true;

            for(int i=0;i<2;i++) { rigs[i].golfGrip=true; rigs[i].Initialize(); rigRest[i]=rigs[i].transform.localPosition; rigs[i].Apply(null); lies[i]=tee.position; }
            for(int i=0;i<clubs.Length;i++)clubPresentation[i]=new GolfClubPresentation(rigs[i],clubs[i]);
            logPath=Path.Combine(Application.persistentDataPath,"golf-shots.jsonl");
            SensorHub.Ensure();
            groundAim=new GameObject("Ground aim guidance").AddComponent<GroundAimGuide>();
            foreach(var rig in rigs)if(!rig.GetComponent<MiiIdleLife>())rig.gameObject.AddComponent<MiiIdleLife>();
            hitClip=Resources.Load<AudioClip>("GolfAudio/GolfHit");
            hitAudio=gameObject.AddComponent<AudioSource>();
            hitAudio.playOnAwake=false; hitAudio.spatialBlend=0;
            // Headsets render this host's state; they never run their own shot simulation.
            if(!GetComponent<GolfStatePublisher>())gameObject.AddComponent<GolfStatePublisher>();
            motionReconnectsAtRoundStart=SensorHub.Instance?.MotionReconnects ?? 0;
            BindUI(); BeginTurn(0); StartCapture(); StartCoroutine(LoadUnlock());
        }
        bool BindUI()
        {
            var root=GetComponent<UIDocument>().rootVisualElement;
            var button=root?.Q<Button>("setup-open");
            if(button==null)return false;
            if(button==setupButton)return true;
            hud?.Dispose();
            heading=root.Q<Label>("heading"); score=root.Q<Label>("score");
            distance=root.Q<Label>("distance"); guidance=root.Q<Label>("guidance");
            setupButton=button;
            root.Q<Button>("aim-left").clicked+=()=>Aim(-3);
            root.Q<Button>("aim-right").clicked+=()=>Aim(3);

            hud=new GolfHud(root,this);
            screens=new GolfScreens(root,this);
            return true;
        }
        // Gameplay presets for this course, not inferred physical club identity.
        public int RecommendedClub(Vector3 lie)
        {
            float distance=Vector3.ProjectOnPlane(cup.position-lie,Vector3.up).magnitude;
            bool difficultLie=false;
            if(Physics.Raycast(lie+Vector3.up*2,Vector3.down,out var hit,10,1<<0,QueryTriggerInteraction.Ignore))
                difficultLie=hit.collider.name.Contains("Rough") || hit.collider.name.Contains("004");
            if(difficultLie)return 1;
            if(distance<=8)return 2;
            return distance>=65?0:1;
        }
        void SelectClubForCurrentLie()
        {
            if(Phase!="Address")return;
            clubIndex=RecommendedClub(ball.position); ResetSwing();
            Message="Virtual "+clubNames[clubIndex]+" selected for this lie. Keep the same physical club; calibrate at address.";
        }
        void Aim(float delta) {if(Phase!="Address")return; aimOffset+=delta; ResetSwing();}
        Vector3 AimDirection()
        {
            var d=cup.position-ball.position; d.y=0;
            return Quaternion.Euler(0,aimOffset,0)*(d.sqrMagnitude>.001f?d.normalized:Vector3.forward);
        }
        bool IMUReady => motion?.connected==true && latest!=null && LivePoseClient.Fresh(imuTicks);
        public void Calibrate()
        {
            if(Phase!="Address")return;
            if(!IMUReady) {Message="Put your AirPods on the club.";return;}
            if(stationarySince<0 || Time.unscaledTime-stationarySince<.4f) {Message="Hold the club still for a moment.";return;}
            ResetSwing();
            if(poseFrame!=null && LivePoseClient.Fresh(poseTicks)) rigs[activePlayer].CalibrateCamera(poseFrame);
            rigs[activePlayer].Apply(LivePoseClient.Fresh(poseTicks)?poseFrame:null);rigs[activePlayer].ApplyGolfIdle();
            clubPresentation[activePlayer].FitAtAddress(ball.position-AimDirection()*.09f);
            var rotation=clubPresentation[activePlayer].AddressRotation;
            strikeZone.Calibrate(ball.position,rotation,lastAttitude,clubPresentation[activePlayer].ScaledShaft,.12f);
            strikeZone.Sample(Grip(activePlayer),lastAttitude,imuTicks/(double)System.Diagnostics.Stopwatch.Frequency);
            swing.Calibrate(lastAttitude,latest.sessionId,latest.sensorTime);   // the stream, not the bud: see ReadMotion
            Message="Ready. Swing back, then through the ball.";
        }
        public void BeginTurn(int index)
        {
            activePlayer=index; Phase="Address"; poseTicks=0;
            rigs[index].ResetTracking();
            latest=null; imuTicks=0; imuSequence=-1; imuSession=null;
            stationarySince=-1; stillSince=-1; ResetSwing(); aimOffset=0;
            ball.isKinematic=true; ball.position=lies[index]; ball.rotation=Quaternion.identity;
            SelectClubForCurrentLie();
            lastSafeLie=ball.position;
            var direction=AimDirection(); var side=Vector3.Cross(Vector3.up,direction);
            for(int i=0;i<2;i++)
            {
                var p=ball.position+side*(i==index?-1.05f:2.4f)+direction*(i==index?0:2.5f);
                players[i].position=FloorPoint(p);
                var facing=i==index?side:(ball.position-players[i].position).normalized;
                facing.y=0;
                players[i].rotation=Quaternion.LookRotation(-facing,Vector3.up);
                rigs[i].Apply(null);
                rigs[i].ApplyGolfIdle();
                if(!rigs[i].seated)GroundStandingAvatar(rigs[i],rigRest[i]);
                var clubTarget=i==index?ball.position:FloorPoint(players[i].position-players[i].forward*.7f)+Vector3.up*.055f;
                clubPresentation[i]?.FitAtAddress(clubTarget-direction*.09f);
            }
            Message=names[index]+"'s turn. Hold the club still, then swing through the ball.";
            turnStartedAt=Time.unscaledTime;
        }
        public void NextTurn()
        {
            if(Phase!="Settled" && Phase!="Holed")return;
            if(Finished[0] && Finished[1]) {Phase="Round complete"; Message="Back on the course. Together.";CompleteRound();return;}
            int nextPlayer=1-activePlayer;
            if(Finished[nextPlayer])nextPlayer=activePlayer;
            BeginTurn(nextPlayer);
        }
        public void RestartRound()
        {
            Strokes=new int[2]; Finished=new bool[2]; Misses=new int[2]; lies[0]=lies[1]=tee.position;
            roundStartedUtc=DateTime.UtcNow; roundReported=false; roundSimulated=false;
            motionReconnectsAtRoundStart=SensorHub.Instance?.MotionReconnects ?? 0;
            clubIndex=0; BeginTurn(0);
        }
        // One session record per round, in the same envelope an exercise session produces, POSTed to the
        // coordinator rather than written locally. golf-shots.jsonl stays as a per-shot debugging log, but
        // it lives in Application.persistentDataPath — a directory that differs between the Editor and a
        // built Player and that the coordinator cannot read, so it can never be the clinical record.
        void CompleteRound() => ReportRound(true);
        void ReportRound(bool completed)
        {
            if(roundReported)return;
            roundReported=true;
            string id=Guid.NewGuid().ToString();
            var subjects=new ActivitySubject[2];
            for(int i=0;i<2;i++)
                subjects[i]=new ActivitySubject {
                    SubjectId=playerIds[i], IsCompanion=i!=0,
                    // A swing that qualified but missed the virtual ball is still an attempt.
                    Attempted=Strokes[i]+Misses[i], Valid=Strokes[i],
                    MetricName="strokes", MetricValue=Strokes[i], MetricUnit="strokes",
                };
            Completed?.Invoke(id);
            postingRound=true;
            StartCoroutine(ActivityRecorder.Send(bridge, ActivityId, id, roundStartedUtc, subjects,
                TrackingLossEvents, "golf.round",
                new {strokes=Strokes, misses=Misses, acceptedShots=AcceptedShots, club=clubNames[clubIndex]},
                _=>postingRound=false, completed, roundSimulated));
        }
        /// <summary>Record a round left partway, then do not leave mid-POST or the round is lost.</summary>
        public IEnumerator RequestExit(Action<bool> succeeded)
        {
            // The swings the patient made still belong in their record, marked not completed.
            if (!roundReported && Strokes[0] + Misses[0] > 0) ReportRound(false);
            while (postingRound) yield return null;
            succeeded?.Invoke(true);
        }
        void Update()
        {
            if(!BindUI())return;
            if(captureRequested) ReadPose();
            ReadMotion();
            if(!IMUReady && swing.Calibrated && activePlayer==0) {ResetSwing();Message="AirPods paused. Hold the club still to pick up again.";}
            if(!LivePoseClient.Fresh(poseTicks))rigs[activePlayer].Apply(null);
            // The club went back and came through the ball: that is a hit. No camera, no mat, no virtual path to miss.
            if(pendingSpatial && Phase=="Address" && !InterfaceOpen)
            {
                pendingSpatial=false;
                Launch(SwingPower(pendingSpeed)*PowerCap,"airpod",pendingSpeed);
            }
            // A swing that finished while a screen was open (the setup countdown, the help card, the menu dialog) is
            // dropped — and the detector re-armed with it: ClubSwingGate ignores everything once it has fired, so
            // without the reset golf went deaf to every later swing.
            else if(pendingSpatial){pendingSpatial=false;ResetSwing(false);}
            CompanionTurn();
            if(Phase=="Flight")
            {
                if(ball.position.y<tee.position.y-45 || Time.time-shotAt>25)
                {Strokes[activePlayer]++;ball.position=lastSafeLie;Settle("Ball returned to the last lie · one penalty stroke.");}
                else if(Vector3.Distance(ball.position,cup.position)<.9f && ball.linearVelocity.magnitude<6f)
                {ball.isKinematic=true;ball.position=cup.position;Finished[activePlayer]=true;Phase="Holed";Message="Holed out! "+Strokes[activePlayer]+" strokes.";foreach(var r in rigs)r.GetComponent<MiiIdleLife>()?.Surprise(2f);Log("holed");}
                else if(Grounded() && ball.linearVelocity.magnitude<.18f)
                {if(stillSince<0)stillSince=Time.time;if(Time.time-stillSince>.8f){
                    // A ball resting near the cup is a gimme: counted in with one more stroke, as friends play it.
                    if(Vector3.ProjectOnPlane(cup.position-ball.position,Vector3.up).magnitude<GimmeMetres)
                    {Strokes[activePlayer]++;ball.isKinematic=true;ball.position=cup.position;Finished[activePlayer]=true;Phase="Holed";Message="Close enough — that's in! "+Strokes[activePlayer]+" strokes.";foreach(var r in rigs)r.GetComponent<MiiIdleLife>()?.Surprise(2f);Log("holed");}
                    else Settle("Shot complete. Continue to the next player.");}}
                else stillSince=-1;
            }
            UpdateAutomaticSetup();
            if(Phase=="Settled" || Phase=="Holed") {
                if(advanceAt<0)advanceAt=Time.unscaledTime+4;
                if(Time.unscaledTime>=advanceAt){advanceAt=-1;NextTurn();}
            } else advanceAt=-1;
            DrawPresentation(); UpdateUI();
        }
        void ReadPose()
        {
            if(pose!=null && pose.Take(out var text,out var ticks))
            {
                try
                {
                    var packet=PoseJson.Read<PoseEnvelope>(text);
                    if(packet?.type!="pose.frame" || packet.schemaVersion!="kinesthetic.session.v1") {poseTicks=0;return;}
                    if(poseSession!=packet.sessionId){poseSession=packet.sessionId;poseSequence=-1;ResetSwing();rigs[activePlayer].ResetTracking();}
                    if(packet.sequence<=poseSequence)return;
                    poseSequence=packet.sequence;poseTicks=ticks;poseFrame=packet.payload;
                    if(LivePoseClient.Fresh(ticks))rigs[activePlayer].Apply(poseFrame);
                }catch(Exception){poseTicks=0;}
            }
            if(pose?.Connected!=true)poseTicks=0;
        }
        void ReadMotion()
        {
            while(motion!=null && motion.Take(out var text,out var ticks))
            {
                try
                {
                    var p=PoseJson.Read<ClubMotionPacket>(text);
                    if(p?.playerId=="friend")friendMotionAt=Time.unscaledTime;
                    if(p?.playerId!=playerIds[activePlayer])continue;
                    if(p.type!="club.motion") {imuTicks=0;latest=null;ResetSwing();continue;}
                    if(!LivePoseClient.Fresh(ticks) || !Valid(p.quaternion,4) || !Valid(p.rotationRate,3) ||
                        double.IsNaN(p.sensorTime) || double.IsInfinity(p.sensorTime))continue;
                    if(imuSession!=p.sessionId){imuSession=p.sessionId;imuSequence=-1;latest=null;ResetSwing();}
                    if(p.sequence<=imuSequence || (latest!=null && p.sensorTime<=latest.sensorTime))continue;
                    var q=new Quaternion(p.quaternion[0],p.quaternion[1],p.quaternion[2],p.quaternion[3]);
                    if(Quaternion.Dot(q,q)<.5f || Quaternion.Dot(q,q)>1.5f)continue;
                    // A different bud within the same pair has its own frame, so calibration no longer holds. The fused
                    // stream switching pairs (golf both pairs in the grip) is re-based by the relay and keeps it: resetting
                    // there wiped calibration at the start of every swing, the moment the swinging pair took over.
                    bool budChanged=latest!=null && p.sourceId!=latest.sourceId && (string.IsNullOrEmpty(p.mac) || p.mac==latest.mac);
                    if(latest!=null && (budChanged || p.sensorTime-latest.sensorTime>.25)) {ResetSwing();stationarySince=-1;}
                    var rate=new Vector3(p.rotationRate[0],p.rotationRate[1],p.rotationRate[2]);
                    if(rate.magnitude<.15f) {if(stationarySince<0)stationarySince=Time.unscaledTime;}else stationarySince=-1;
                    latest=p;imuSequence=p.sequence;imuTicks=ticks;lastAttitude=q.normalized;
                    if(p.simulated && activePlayer==0)roundSimulated=true;
                    // Strike samples and the rendered club use the same corrected grip.
                    if(strikeZone.Calibrated)clubPresentation[activePlayer].Present(strikeZone.Rotation(lastAttitude));
                    if(Phase=="Address" && swing.Sample(lastAttitude,rate,p.sessionId,p.sensorTime,out var speed))
                    {
                        motionContactAt=ticks/(double)System.Diagnostics.Stopwatch.Frequency;
                        pendingSpatial=true;pendingSpeed=speed;
                    }
                } catch(Exception){imuTicks=0;ResetSwing();}
            }
        }
        static bool Valid(float[] a,int length)
        {if(a==null || a.Length!=length)return false;foreach(float x in a)if(!PoseMath.Finite(x))return false;return true;}
        public bool DeveloperShot(float power)
        {if(!allowDeveloperShots)return false;motionContactAt=0;return Launch(power,"developer-test",0);}
        bool Launch(float power,string source,float angularSpeed)
        {
            if(Phase!="Address" || !PoseMath.Finite(power))return false;
            power=Mathf.Clamp01(power);lastShotPower=power;
            var direction=AimDirection();
            float speed=clubIndex==0?Mathf.Lerp(10,25,power):clubIndex==1?Mathf.Lerp(5,23,power):Mathf.Lerp(.7f,9,power);
            float loft=(clubIndex==0?25:clubIndex==1?43:0)*Mathf.Deg2Rad;
            lastSafeLie=ball.position;
            ball.isKinematic=false;ball.linearVelocity=direction*(Mathf.Cos(loft)*speed)+Vector3.up*(Mathf.Sin(loft)*speed);
            ball.angularVelocity=Vector3.zero;
            Strokes[activePlayer]++;AcceptedShots++;Phase="Flight";shotAt=Time.time;stillSince=-1;
            if(hitClip) {
                hitAudio.pitch=clubIndex==2?1.3f:clubIndex==1?1.08f:1f;
                hitAudio.PlayOneShot(hitClip,Mathf.Lerp(.45f,.8f,power)*(clubIndex==2?.45f:1f));
            }
            Message=clubNames[clubIndex]+" · "+Mathf.RoundToInt(power*100)+"% virtual power";
            if(source=="airpod" && activePlayer==0 && patientPowerCap<1 && !string.IsNullOrEmpty(unlockMessage))Message+=" · "+unlockMessage;
            Log("shot",source,angularSpeed,power);ResetSwing(false);
            foreach(var r in rigs)r.GetComponent<MiiIdleLife>()?.Surprise(1.2f);   // both friends watch the ball go
            return true;
        }
        void Settle(string message)
        {
            ball.linearVelocity=Vector3.zero;ball.angularVelocity=Vector3.zero;ball.isKinematic=true;
            lies[activePlayer]=ball.position;Phase="Settled";Message=message;Log("settled");
        }
        bool Grounded()=>Physics.Raycast(ball.position+Vector3.up*.01f,Vector3.down,out _,.1f,1<<0,QueryTriggerInteraction.Ignore);
        public static Vector3 FloorPoint(Vector3 p)
        {return Physics.Raycast(p+Vector3.up*60,Vector3.down,out var hit,200,1<<0,QueryTriggerInteraction.Ignore)?hit.point:p-Vector3.up*.055f;}
        void FixedUpdate()
        {
            if(Phase!="Flight" || !Grounded())return;
            Physics.Raycast(ball.position+Vector3.up*.01f,Vector3.down,out var hit,.15f,1<<0,QueryTriggerInteraction.Ignore);
            float drag=hit.collider && hit.collider.name.Contains("Rough")?2.8f:hit.collider && hit.collider.name.Contains("004")?5f:.55f;
            var v=ball.linearVelocity;var flat=Vector3.MoveTowards(new Vector3(v.x,0,v.z),Vector3.zero,drag*Time.fixedDeltaTime);
            ball.linearVelocity=new Vector3(flat.x,v.y,flat.z);
        }
        // Absolute placement from the authored rest height: re-grounding every turn used to add
        // a 5.5 cm measurement error on top of the previous turn, sinking the friend each turn.
        static void GroundStandingAvatar(PoseRig rig,Vector3 restLocal)
        {
            rig.transform.localPosition=restLocal;
            float correction=float.NegativeInfinity;
            var mesh=new Mesh();
            foreach(var renderer in rig.avatar.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.BakeMesh(mesh,true); // scale baked in once; do not apply lossyScale again
                foreach(var vertex in mesh.vertices)
                {
                    var world=renderer.transform.position+renderer.transform.rotation*vertex;
                    if(world.y>rig.RightAnkle.position.y+.06f)continue;
                    var floor=FloorPoint(world);
                    correction=Mathf.Max(correction,floor.y-world.y);
                }
            }
            Destroy(mesh);
            if(!float.IsNegativeInfinity(correction))rig.transform.position+=Vector3.up*correction;
        }
        void DrawPresentation()
        {
            Vector3 aim=AimDirection();
            aimLine.enabled=false;
            groundAim.Show(ball.position,aim,Phase=="Address");
            for(int i=0;i<clubs.Length;i++)
                clubPresentation[i]?.Present(i==activePlayer && IMUReady && strikeZone.Calibrated
                    ?strikeZone.Rotation(lastAttitude):(Quaternion?)null);
            bool atAddress=Phase=="Address";
            var target=atAddress?Vector3.Lerp(players[activePlayer].position,ball.position,.65f):ball.position;
            float behind=atAddress?(activePlayer==0?4.2f:4.8f):9;
            float height=atAddress?(activePlayer==0?1.85f:2.3f):5;
            var desired=target-aim*behind+Vector3.up*height;
            spectator.transform.position=Vector3.SmoothDamp(spectator.transform.position,desired,ref cameraVelocity,.3f);
            spectator.transform.LookAt(target+aim*(atAddress?3:6)+Vector3.up*(atAddress?.9f:.7f));
        }
        void UpdateUI()
        {
            if(heading==null)return;
            heading.text=Phase=="Round complete"?"TOGETHER AGAIN":activePlayer==0?"YOUR TURN":"FRIEND’S TURN";
            score.text=$"YOU {Strokes[0]} — {Strokes[1]} FRIEND";
            distance.text=$"{Vector3.ProjectOnPlane(cup.position-ball.position,Vector3.up).magnitude*1.0936133f:0} yards to go";
            bool framesFresh=LivePoseClient.Fresh(poseTicks);
            guidance.text=Phase!="Address"?Message:activePlayer==1 && !friendLive?"Your friend is lining up…":
                !IMUReady?"Waiting for AirPods…":swing.Calibrated?"Swing back, then through the ball.":"Hold the club still.";
            hud?.Update();
            screens?.Update();
        }
        void Log(string kind,string source="unity",float rate=0,float power=0)
        {
            try {File.AppendAllText(logPath,Newtonsoft.Json.JsonConvert.SerializeObject(new {
                type=kind,utc=DateTime.UtcNow,playerId=playerIds[activePlayer],stroke=Strokes[activePlayer],
                input=source,club=clubNames[clubIndex],angularSpeedRadPerSec=rate,virtualPower=power,
                motionReceivedHostSeconds=kind=="shot"?(double?)motionContactAt:null,
                poseAgeMsAtDecision=kind=="shot" && poseTicks>0?(double?)((Now-poseTicks/(double)System.Diagnostics.Stopwatch.Frequency)*1000):null,
                virtualStrikeRadiusM=strikeZone.Calibrated?(float?)strikeZone.Radius:null,
                closestVirtualApproachM=float.IsInfinity(strikeZone.ClosestApproach)?null:(float?)strikeZone.ClosestApproach,
                contactMeaning="estimated virtual clubhead crossing; physical contact unverified",
                timingBasis="host arrival time; hardware latency not calibrated",
                x=ball.position.x,y=ball.position.y,z=ball.position.z})+"\n");}catch(Exception e){Debug.LogWarning(e.Message);}
        }
        void OnDestroy(){if(groundAim)Destroy(groundAim.gameObject);hud?.Dispose();}   // the hub owns both channels

        /// Starts listening for the club (golf's setup screen and its retry call this). The AirPods connect on their
        /// own through the sensor hub; there is no camera to launch.
        public void StartCapture()
        {
            if(Phase=="Round complete")RestartRound();
            captureRequested=true; readySince=-1; ResetSwing();
        }
        void UpdateAutomaticSetup()
        {
            // The club held still for a moment is the whole setup: it becomes "address" wherever it is.
            if(!captureRequested || Phase!="Address" || swing.Calibrated || !IMUReady ||
                stationarySince<0 || Time.unscaledTime-stationarySince<.4f) {readySince=-1;return;}
            if(readySince<0)readySince=Time.unscaledTime;
            if(Time.unscaledTime-readySince>=.6f) {Calibrate();readySince=-1;}
        }
    }
}

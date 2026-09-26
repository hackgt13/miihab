using System;
using System.IO;
using System.Collections;
using UnityEngine.Networking;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.Golf
{
    public sealed class KinestheticGolf : MonoBehaviour
    {
        public Rigidbody ball;
        public Camera spectator;
        public Transform tee, cup;
        public Transform[] players, clubs;
        public PoseRig[] rigs;
        public LineRenderer aimLine;
        public string poseUrl = "ws://127.0.0.1:8766/pose?role=viewer";
        public string motionUrl = "ws://127.0.0.1:8767/golf?role=viewer";
        public bool allowDeveloperShots;
        public bool enableSoundAssist = true;
        public int activePlayer;
        public int clubIndex;
        public bool ClubSelectedAutomatically { get; private set; }
        public float aimOffset;
        public string Phase { get; private set; } = "Address";
        public string Message { get; private set; } = "Starting camera and AirPod tracking…";
        public int[] Strokes { get; private set; } = new int[2];
        public bool[] Finished { get; private set; } = new bool[2];
        public int AcceptedShots { get; private set; }
        public bool PoseReady => LivePoseClient.Fresh(poseTicks) &&
            (rigs[activePlayer].RightArmTracked || rigs[activePlayer].LeftArmTracked);
        public Vector3 HudAim => AimDirection();
        public Vector3 GetLie(int index) => lies[index];
        public float HudPower => Phase=="Address" ? (IMUReady && swing.Calibrated ? SwingPower(new Vector3(latest.rotationRate[0],latest.rotationRate[1],latest.rotationRate[2]).magnitude) : 0) : lastShotPower;
        float lastShotPower;
        // Recorded AirPod swings peak around 10-16 rad/s, so the old 7 rad/s ceiling made nearly every swing full power.
        // A full driver (25 m/s) stops about 5 m from the cup on this hole; 26 m/s and faster left the course (measured with developer shots).
        const float MinSwingRadPerSec=2f, FullSwingRadPerSec=16f;
        public static float SwingPower(float radPerSec)=>Mathf.InverseLerp(MinSwingRadPerSec,FullSwingRadPerSec,radPerSec);
        GolfHud hud;
        GolfScreens screens;
        public bool CaptureRequested => captureRequested;
        public bool StartingCapture => startingCapture;
        public bool MotionReady => IMUReady;
        public bool ClubCalibrated => swing.Calibrated;
        public string CaptureStatus => captureStatus;
        public bool InterfaceOpen => screens?.SetupVisible == true || Kinesthetic.Menu.ActivityNavigation.Instance?.OverlayOpen == true;
        GroundAimGuide groundAim;
        readonly ClubSwingGate swing = new();
        readonly ContactCueGate contact = new();
        readonly VirtualClubStrike strikeZone = new();
        bool pendingSpatial;
        float pendingSpeed;
        public bool StrikePoseReady=>PoseReady && rigs[activePlayer].LeftArmTracked && rigs[activePlayer].RightArmTracked;
        Vector3 Grip(int player)=>rigs[player].GolfGripCenter; // same point the rendered club attaches to
        GolfImpactAudio impactAudio;
        AudioSource hitAudio;
        AudioClip hitClip;
        Label audioStatus;
        Button soundToggle;
        bool audioCorroborated;
        double? audioOffsetMs;
        double motionContactAt;
        void ResetSwing(bool clearClub=true){swing.Reset();contact.Reset();pendingSpatial=false;if(clearClub)strikeZone.Reset();}
        readonly Vector3[] lies = new Vector3[2];
        readonly Vector3[] rigRest = new Vector3[2];
        readonly GolfClubPresentation[] clubPresentation=new GolfClubPresentation[2];
        readonly string[] playerIds = {"patient", "friend"};
        readonly string[] names = {"You", "Your friend"};
        readonly string[] clubNames = {"Driver", "Iron", "Putter"};
        LivePoseClient pose;
        GolfMotionClient motion;
        PoseFrame poseFrame;
        ClubMotionPacket latest;
        long poseTicks, imuTicks, poseSequence=-1, imuSequence=-1;
        string poseSession, imuSession;
        float stillSince=-1, stationarySince=-1, shotAt, retryPoseAt;
        Quaternion lastAttitude;
        Vector3 cameraVelocity, lastSafeLie;
        Label heading, score, distance, guidance;
        Button cameraStart;
        bool captureRequested, startingCapture;
        float readySince=-1, advanceAt=-1;
        string captureStatus="Click the camera to start";
        [Serializable] class CaptureHealth { public string captureStatus; public bool sourceConnected; public float frameAgeMs; }

        string logPath;

        void Start()
        {
            Application.runInBackground = true;

            for(int i=0;i<2;i++) { rigs[i].Initialize(); rigRest[i]=rigs[i].transform.localPosition; rigs[i].Apply(null); lies[i]=tee.position; }
            for(int i=0;i<clubs.Length;i++)clubPresentation[i]=new GolfClubPresentation(rigs[i],clubs[i]);
            logPath=Path.Combine(Application.persistentDataPath,"golf-shots.jsonl");
            pose = new LivePoseClient(poseUrl);
            motion = new GolfMotionClient(motionUrl);
            groundAim=new GameObject("Ground aim guidance").AddComponent<GroundAimGuide>();
            foreach(var rig in rigs)if(!rig.GetComponent<MiiIdleLife>())rig.gameObject.AddComponent<MiiIdleLife>();
            impactAudio=gameObject.AddComponent<GolfImpactAudio>();
            hitClip=Resources.Load<AudioClip>("GolfAudio/GolfHit");
            hitAudio=gameObject.AddComponent<AudioSource>();
            hitAudio.playOnAwake=false; hitAudio.spatialBlend=0;
            impactAudio.Transient+=(at,peak)=>{if(Phase=="Address" && PoseReady && IMUReady && swing.Calibrated)contact.ObserveAudio(at);};
            // Headsets render this host's state; they never run their own shot simulation.
            if(!GetComponent<GolfStatePublisher>())gameObject.AddComponent<GolfStatePublisher>();
            BindUI(); BeginTurn(0);
        }
        bool BindUI()
        {
            var root=GetComponent<UIDocument>().rootVisualElement;
            var button=root?.Q<Button>("camera-start");
            if(button==null)return false;
            if(button==cameraStart)return true;
            hud?.Dispose();
            heading=root.Q<Label>("heading"); score=root.Q<Label>("score");
            distance=root.Q<Label>("distance"); guidance=root.Q<Label>("guidance");
            cameraStart=root.Q<Button>("camera-start");
            cameraStart.clicked+=StartCapture;
            root.Q<Button>("aim-left").clicked+=()=>Aim(-3);
            root.Q<Button>("aim-right").clicked+=()=>Aim(3);

            audioStatus=root.Q<Label>("audio-status");soundToggle=root.Q<Button>("sound-toggle");
            if(soundToggle!=null)soundToggle.clicked+=()=>{enableSoundAssist=!enableSoundAssist;ResetSwing();};
            hud=new GolfHud(root,this);
            screens=new GolfScreens(root,this);
            return true;
        }
        void ChangeClub(int delta)
        {
            if(Phase!="Address")return;
            clubIndex=(clubIndex+delta+3)%3; ClubSelectedAutomatically=false; ResetSwing();
            Message="Virtual "+clubNames[clubIndex]+" selected. Keep the same physical club; calibrate at address.";
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
            clubIndex=RecommendedClub(ball.position); ClubSelectedAutomatically=true; ResetSwing();
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
            if(!StrikePoseReady) {Message="Keep both wrists and elbows visible, then hold the club at the mat.";return;}
            if(!IMUReady) {Message="Pair your AirPods to this Mac and mount the reporting AirPod on the club.";return;}
            if(stationarySince<0 || Time.unscaledTime-stationarySince<.6f) {Message="Hold the club still at the mat for a moment.";return;}
            ResetSwing();
            rigs[activePlayer].CalibrateCamera(poseFrame);
            rigs[activePlayer].Apply(poseFrame);rigs[activePlayer].ApplyGolfIdle();
            clubPresentation[activePlayer].FitAtAddress(ball.position-AimDirection()*.09f);
            var rotation=clubPresentation[activePlayer].AddressRotation;
            strikeZone.Calibrate(ball.position,rotation,lastAttitude,clubPresentation[activePlayer].ScaledShaft,.12f);
            strikeZone.Sample(Grip(activePlayer),lastAttitude,imuTicks/(double)System.Diagnostics.Stopwatch.Frequency);
            swing.Calibrate(lastAttitude,latest.sourceId,latest.sensorTime);
            Message="Calibrated. Make a controlled backswing, then return through address.";
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
            Message=names[index]+"'s turn. Camera and AirPod connect automatically; hold the club still at the mat to calibrate.";
        }
        public void NextTurn()
        {
            if(Phase!="Settled" && Phase!="Holed")return;
            if(Finished[0] && Finished[1]) {Phase="Round complete"; Message="Back on the course. Together.";return;}
            int nextPlayer=1-activePlayer;
            if(Finished[nextPlayer])nextPlayer=activePlayer;
            BeginTurn(nextPlayer);
        }
        public void RestartRound()
        {
            Strokes=new int[2]; Finished=new bool[2]; lies[0]=lies[1]=tee.position;
            clubIndex=0; BeginTurn(0);
        }
        void Update()
        {
            if(!BindUI())return;
            if(captureRequested) ReadPose();
            impactAudio.Listen(enableSoundAssist && PoseReady);
            impactAudio.Poll();
            ReadMotion();
            if(!PoseReady || !IMUReady)
            {
                if(swing.Calibrated) {ResetSwing();Message="Tracking paused. Return to address and recalibrate.";}
            }
            if(!LivePoseClient.Fresh(poseTicks))rigs[activePlayer].Apply(null);
            if(!StrikePoseReady)
            {
                strikeZone.BreakTrace();
                if(contact.Pending){ResetSwing();Message="Tracking interrupted. Return to address and recalibrate.";}
            }
            if(pendingSpatial && Phase=="Address")
            {
                double now=GolfImpactAudio.Now;
                if(StrikePoseReady && IMUReady && strikeZone.CrossedNear(motionContactAt))
                {
                    pendingSpatial=false;
                    contact.Arm(motionContactAt,pendingSpeed,enableSoundAssist && impactAudio.Ready);
                }
                else if(now-motionContactAt>.12)
                {
                    Log("virtual-miss","pose+airpod",pendingSpeed);
                    ResetSwing();Message="Missed the virtual ball. Return to address and recalibrate.";
                }
            }
            if(contact.Pending && GolfImpactAudio.Now-motionContactAt>.25){ResetSwing();Message="Tracking delayed. Return to address and recalibrate.";}
            if(InterfaceOpen) { contact.Reset(); pendingSpatial=false; }
            if(!InterfaceOpen && Phase=="Address" && StrikePoseReady && IMUReady &&
                contact.Commit(GolfImpactAudio.Now,out var contactSpeed,out var heard,out var deltaMs))
            {
                audioCorroborated=heard;audioOffsetMs=heard?(double?)deltaMs:null;
                Launch(SwingPower(contactSpeed),heard?"airpod+audio":"airpod",contactSpeed);
            }
            if(Phase=="Flight")
            {
                if(ball.position.y<tee.position.y-45 || Time.time-shotAt>25)
                {Strokes[activePlayer]++;ball.position=lastSafeLie;Settle("Ball returned to the last lie · one penalty stroke.");}
                else if(Vector3.Distance(ball.position,cup.position)<.35f && ball.linearVelocity.magnitude<3f)
                {ball.isKinematic=true;ball.position=cup.position;Finished[activePlayer]=true;Phase="Holed";Message="Holed out! "+Strokes[activePlayer]+" strokes.";Log("holed");}
                else if(Grounded() && ball.linearVelocity.magnitude<.18f)
                {if(stillSince<0)stillSince=Time.time;if(Time.time-stillSince>.8f)Settle("Shot complete. Continue to the next player.");}
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
            if(pose?.Connected!=true && Time.unscaledTime>retryPoseAt)
            {pose?.Dispose();pose=new LivePoseClient(poseUrl);retryPoseAt=Time.unscaledTime+3;poseTicks=0;}
        }
        void ReadMotion()
        {
            while(motion!=null && motion.Take(out var text,out var ticks))
            {
                try
                {
                    var p=PoseJson.Read<ClubMotionPacket>(text);
                    if(p?.playerId!=playerIds[activePlayer])continue;
                    if(p.type!="club.motion") {imuTicks=0;latest=null;ResetSwing();continue;}
                    if(!LivePoseClient.Fresh(ticks) || !Valid(p.quaternion,4) || !Valid(p.rotationRate,3) ||
                        double.IsNaN(p.sensorTime) || double.IsInfinity(p.sensorTime))continue;
                    if(imuSession!=p.sessionId){imuSession=p.sessionId;imuSequence=-1;latest=null;ResetSwing();}
                    if(p.sequence<=imuSequence || (latest!=null && p.sensorTime<=latest.sensorTime))continue;
                    var q=new Quaternion(p.quaternion[0],p.quaternion[1],p.quaternion[2],p.quaternion[3]);
                    if(Quaternion.Dot(q,q)<.5f || Quaternion.Dot(q,q)>1.5f)continue;
                    if(latest!=null && (p.sourceId!=latest.sourceId || p.sensorTime-latest.sensorTime>.25)) {ResetSwing();stationarySince=-1;}
                    var rate=new Vector3(p.rotationRate[0],p.rotationRate[1],p.rotationRate[2]);
                    if(rate.magnitude<.15f) {if(stationarySince<0)stationarySince=Time.unscaledTime;}else stationarySince=-1;
                    latest=p;imuSequence=p.sequence;imuTicks=ticks;lastAttitude=q.normalized;
                    // Strike samples and the rendered club use the same corrected grip.
                    if(strikeZone.Calibrated)clubPresentation[activePlayer].Present(strikeZone.Rotation(lastAttitude));
                    if(Phase=="Address" && StrikePoseReady && strikeZone.Calibrated)
                        strikeZone.Sample(Grip(activePlayer),lastAttitude,ticks/(double)System.Diagnostics.Stopwatch.Frequency);
                    if(Phase=="Address" && PoseReady && swing.Sample(lastAttitude,rate,p.sourceId,p.sensorTime,out var speed))
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
        {if(!allowDeveloperShots)return false;audioCorroborated=false;audioOffsetMs=null;motionContactAt=0;return Launch(power,"developer-test",0);}
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
            Log("shot",source,angularSpeed,power);ResetSwing(false);return true;
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
            guidance.text=!captureRequested?"Click the camera to start":Phase!="Address"?Message:
                !framesFresh?captureStatus:!StrikePoseReady?"Camera on · keep shoulders, elbows and both hands in view":
                !IMUReady?"Camera on · waiting for AirPod motion":swing.Calibrated?"Ready to swing":
                readySince<0?"Hold the club still at the mat":$"Hold still · calibrating {Mathf.Max(1,Mathf.CeilToInt(2-(Time.unscaledTime-readySince)))}";
            cameraStart.EnableInClassList("ready",swing.Calibrated && StrikePoseReady && IMUReady);
            if(audioStatus!=null)audioStatus.text=enableSoundAssist && !PoseReady?"Sound assist waiting for camera":impactAudio.Status;
            if(soundToggle!=null)soundToggle.text=enableSoundAssist?"Sound assist: on":"Sound assist: off";
            hud?.Update();
            screens?.Update();
        }
        void Log(string kind,string source="unity",float rate=0,float power=0)
        {
            try {File.AppendAllText(logPath,Newtonsoft.Json.JsonConvert.SerializeObject(new {
                type=kind,utc=DateTime.UtcNow,playerId=playerIds[activePlayer],stroke=Strokes[activePlayer],
                input=source,club=clubNames[clubIndex],angularSpeedRadPerSec=rate,virtualPower=power,
                audioCorroborated=kind=="shot"?(bool?)audioCorroborated:null,
                audioOffsetMs=kind=="shot"?audioOffsetMs:null,
                motionReceivedHostSeconds=kind=="shot"?(double?)motionContactAt:null,
                poseAgeMsAtDecision=kind=="shot" && poseTicks>0?(double?)((GolfImpactAudio.Now-poseTicks/(double)System.Diagnostics.Stopwatch.Frequency)*1000):null,
                virtualStrikeRadiusM=strikeZone.Calibrated?(float?)strikeZone.Radius:null,
                closestVirtualApproachM=float.IsInfinity(strikeZone.ClosestApproach)?null:(float?)strikeZone.ClosestApproach,
                contactMeaning="estimated virtual clubhead crossing; physical contact unverified",
                timingBasis="host arrival time; audio block time estimated; hardware latency not calibrated",
                x=ball.position.x,y=ball.position.y,z=ball.position.z})+"\n");}catch(Exception e){Debug.LogWarning(e.Message);}
        }
        void OnDestroy(){if(impactAudio)impactAudio.StopCapture();if(groundAim)Destroy(groundAim.gameObject);hud?.Dispose();pose?.Dispose();motion?.Dispose();}

        public void StartCapture()
        {
            if(startingCapture)return;
            if(Phase=="Round complete")RestartRound();
            captureRequested=true; readySince=-1; ResetSwing();
            captureStatus="Starting camera and AirPod motion…";
            StartCoroutine(StartCaptureServices());
        }
        IEnumerator StartCaptureServices()
        {
            startingCapture=true;
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            var script=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../scripts/start_camera_session.sh"));
            if(File.Exists(script)) {
                System.Diagnostics.Process process=null;
                try {process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                    FileName="/bin/zsh", Arguments="\""+script+"\"", UseShellExecute=false, CreateNoWindow=true
                });} catch(Exception e){captureStatus="Could not start capture: "+e.Message;}
                if(process!=null) {
                    while(!process.HasExited)yield return null;
                    if(process.ExitCode!=0)captureStatus="Capture could not start · click camera to retry";
                    process.Dispose();
                }
            } else captureStatus="Capture launcher missing";
#else
            yield return null;   // capture services are launched only on the Mac host
#endif
            startingCapture=false;
            if(healthRoutine==null)healthRoutine=StartCoroutine(PollCaptureHealth());
        }
        Coroutine healthRoutine;
        IEnumerator PollCaptureHealth()
        {
            while(captureRequested) {
                using(var request=UnityWebRequest.Get("http://127.0.0.1:8766/health")) {
                    request.timeout=3;
                    yield return request.SendWebRequest();
                    if(request.result==UnityWebRequest.Result.Success) {
                        var state=JsonUtility.FromJson<CaptureHealth>(request.downloadHandler.text);
                        captureStatus=!state.sourceConnected?"Camera disconnected · click camera to reconnect":
                            state.captureStatus=="Camera streaming"?"Camera frames paused · click camera to reconnect":state.captureStatus;
                    } else captureStatus="Camera service unavailable · click camera to retry";
                }
                yield return new WaitForSecondsRealtime(1);
            }
            healthRoutine=null;
        }
        void UpdateAutomaticSetup()
        {
            if(!captureRequested || Phase!="Address" || swing.Calibrated || !StrikePoseReady || !IMUReady ||
                stationarySince<0 || Time.unscaledTime-stationarySince<.6f) {readySince=-1;return;}
            // Both hands must be together and below shoulders before fitting the club.
            var points=poseFrame?.imageLandmarks;
            if(points==null || points.Length<33 ||
                points[15].y<points[11].y || points[16].y<points[12].y ||
                Mathf.Abs(points[15].x-points[16].x)>.25f) {readySince=-1;return;}
            if(readySince<0)readySince=Time.unscaledTime;
            if(Time.unscaledTime-readySince>=2) {Calibrate();readySince=-1;}
        }
    }
}

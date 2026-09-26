using UnityEngine;

namespace Kinesthetic.Golf
{
    // Presentation-only two-hand attachment. The AirPod remains the source of
    // calibrated club orientation; the camera-only fallback is an approximation.
    public sealed class GolfClubPresentation
    {
        readonly PoseRig rig;
        readonly Transform club;
        public Vector3 GripAnchor {get;private set;}
        public Vector3 HeadAnchor {get;private set;}
        public Vector3 ScaledShaft=>Vector3.Scale(HeadAnchor-GripAnchor,club.localScale);
        public Quaternion AddressRotation {get;private set;}
        public Vector3 Head=>club.TransformPoint(HeadAnchor);
        Vector3 addressArm;
        Quaternion addressInRig;
        bool fitted;
        float addressHeadHeight;
        Vector3 smoothGrip;
        Quaternion smoothCameraRotation;
        Vector2 addressImageGrip;
        bool hasImageGrip;
        double lastPresentedPoseMs=-1;
        static float Weight(float dt,float hz)=>1-Mathf.Exp(-2*Mathf.PI*hz*dt);
        static bool ImageGrip(PoseFrame frame,out Vector2 grip)
        {
            grip=Vector2.zero;
            if(frame?.imageLandmarks==null || frame.imageLandmarks.Length<=16)return false;
            var left=frame.imageLandmarks[15];var right=frame.imageLandmarks[16];
            if(left==null || right==null || !PoseMath.Finite(left.x) || !PoseMath.Finite(right.x)
                || !PoseMath.Finite(left.y) || !PoseMath.Finite(right.y)
                || left.visibility<.5f || right.visibility<.5f)return false;
            grip=new Vector2((left.x+right.x)*.5f,(left.y+right.y)*.5f);
            return grip.x>=0 && grip.x<=1 && grip.y>=0 && grip.y<=1;
        }
        public GolfClubPresentation(PoseRig rig,Transform club)
        {
            this.rig=rig;this.club=club;
            GripAnchor=new Vector3(0,-.12f,0);HeadAnchor=new Vector3(-.17f,-.99f,0);
            foreach(var t in club.GetComponentsInChildren<Transform>()) {
                if(t.name=="GripAnchor")GripAnchor=club.InverseTransformPoint(t.position);
                if(t.name=="HeadAnchor")HeadAnchor=club.InverseTransformPoint(t.position);
            }
            // The prefab anchor is at the butt end. Put the hands on the rubber
            // grip itself so a short cap shows above the joined Mii hands.
            GripAnchor+=(HeadAnchor-GripAnchor).normalized*(rig.seated?.045f:.06f); // standing: stacked hands centred just below the butt
        }
        public void FitAtAddress(Vector3 headTarget)
        {
            addressHeadHeight=headTarget.y;
            rig.RestoreFreeArms();
            var shaft=HeadAnchor-GripAnchor;
            // Recompute the wrist axis as the shared grip settles. Do not repeat
            // this fitting during a tracked swing (the shaft must not telescope).
            for(int i=0;i<3;i++) {
                var rotation=Quaternion.FromToRotation(shaft.normalized,(headTarget-rig.GolfGripCenter).normalized);
                rig.ApplyGolfGrip(rotation*Vector3.down);
            }
            var toHead=headTarget-rig.GolfGripCenter;
            club.localScale=Vector3.one*(toHead.magnitude/shaft.magnitude);
            AddressRotation=Quaternion.FromToRotation(shaft.normalized,toHead.normalized);
            addressInRig=Quaternion.Inverse(rig.transform.rotation)*AddressRotation;
            addressArm=rig.transform.InverseTransformDirection(rig.GolfArmDirection).normalized;
            smoothGrip=rig.GolfGripCenter;
            smoothCameraRotation=AddressRotation;
            hasImageGrip=ImageGrip(rig.LastRawFrame,out addressImageGrip);
            lastPresentedPoseMs=rig.LastRawFrame?.sourceMediaTimeMs??-1;
            fitted=true;
            Attach(AddressRotation);
        }
        public void Present(Quaternion? sensorRotation=null)
        {
            if(!fitted)return;
            // Read the unconstrained, filtered pose once. The authored grip is
            // then applied to both arms together, so re-drawing cannot drift it.
            rig.RestoreFreeArms();
            var rawGrip=rig.GolfGripCenter;
            double sourceMs=rig.LastRawFrame?.sourceMediaTimeMs??-1;
            float dt=lastPresentedPoseMs>=0 && sourceMs>lastPresentedPoseMs
                ?Mathf.Clamp((float)(sourceMs-lastPresentedPoseMs)/1000,1f/120f,.1f)
                :Application.isPlaying?Mathf.Clamp(Time.unscaledDeltaTime,1f/120f,.05f):1f/15f;
            if(sourceMs>=0)lastPresentedPoseMs=sourceMs;
            float gripSpeed=Vector3.Distance(rawGrip,smoothGrip)/Mathf.Max(dt,.001f);
            smoothGrip=Vector3.Lerp(smoothGrip,rawGrip,Weight(dt,gripSpeed>.6f?14f:5f));

            Quaternion rotation;
            if(sensorRotation.HasValue)rotation=sensorRotation.Value;
            else {
                Quaternion target;
                if(!rig.seated && hasImageGrip && ImageGrip(rig.LastRawFrame,out var imageGrip)) {
                    // With one RGB camera the wrists give a reliable swing arc,
                    // but they do not determine the club's 3D roll. Keep the
                    // club in a stylized golf plane through the calibrated ball.
                    float side=imageGrip.x-addressImageGrip.x;
                    float lift=Mathf.Max(0,addressImageGrip.y-imageGrip.y);
                    float limit=side<0?158f:150f;
                    float swing=Mathf.Sign(side)*Mathf.Clamp(Mathf.Abs(side)*680f+lift*1400f,0,limit);
                    // Rotate within the inclined swing plane (address shaft + target line), not the
                    // camera's frontal plane: the club passes horizontal along the target line and
                    // finishes up and behind the shoulders instead of pointing at the viewer.
                    var addressHead=(AddressRotation*(HeadAnchor-GripAnchor)).normalized;
                    var plane=Vector3.Cross(addressHead,rig.transform.right).normalized;
                    if(Vector3.Dot(Vector3.Cross(plane,addressHead),Vector3.Cross(rig.transform.forward,addressHead))<0)plane=-plane;
                    target=Quaternion.AngleAxis(swing,plane.sqrMagnitude>.01f?plane:rig.transform.forward)*AddressRotation;
                } else {
                    var arm=rig.transform.InverseTransformDirection(rig.GolfArmDirection).normalized;
                    var delta=addressArm.sqrMagnitude>.01f && arm.sqrMagnitude>.01f
                        ?Quaternion.FromToRotation(addressArm,arm):Quaternion.identity;
                    target=rig.transform.rotation*delta*addressInRig;
                }
                float speed=Quaternion.Angle(smoothCameraRotation,target)/Mathf.Max(dt,.001f);
                float hz=speed>180?12f:speed>65?7f:2.4f;
                smoothCameraRotation=Quaternion.RotateTowards(smoothCameraRotation,
                    Quaternion.Slerp(smoothCameraRotation,target,Weight(dt,hz)),520*dt);
                rotation=smoothCameraRotation;
            }
            rig.ApplyGolfGrip(rotation*Vector3.down,smoothGrip);
            // The monocular preview can pitch the club through the ground.
            // This visual correction does not alter calibrated IMU motion.
            if(!sensorRotation.HasValue)for(int i=0;i<3;i++) {
                var shaft=rotation*ScaledShaft;
                float low=addressHeadHeight-rig.GolfGripCenter.y;
                if(shaft.y>=low-.0001f)break;
                float y=Mathf.Clamp(low,-shaft.magnitude,shaft.magnitude);
                var horizontal=Vector3.ProjectOnPlane(shaft,Vector3.up).normalized;
                if(horizontal.sqrMagnitude<.01f)horizontal=-rig.transform.forward;
                var corrected=horizontal*Mathf.Sqrt(Mathf.Max(0,shaft.sqrMagnitude-y*y))+Vector3.up*y;
                rotation=Quaternion.FromToRotation(shaft,corrected)*rotation;
                rig.ApplyGolfGrip(rotation*Vector3.down,smoothGrip);
            }
            Attach(rotation);
        }
        void Attach(Quaternion rotation)
        {
            club.rotation=rotation;
            club.position=rig.GolfGripCenter-club.TransformVector(GripAnchor);
        }
    }
}

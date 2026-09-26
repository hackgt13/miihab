using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Kinesthetic.Activities;

namespace Kinesthetic
{
    public class PoseRig : MonoBehaviour
    {
        public Transform avatar;
        public bool seated = true;
        public bool usePresentationSpace;
        [Tooltip("Golf only: untracked hands join the shared club grip. Off elsewhere, so rehab arms rest naturally.")]
        public bool golfGrip;
        public static float MiiHeadScale=.64f;
        readonly Dictionary<string, Transform> bones = new();
        readonly Dictionary<Transform, Quaternion> rest = new();
        bool initialized;
        bool mii;
        Quaternion cameraToBody=Quaternion.identity;
        Vector3 calibrationLeft;
        int calibrationSamples;
        double calibrationStart=-1, lastPoseTime=-1;
        readonly PosePresentationFilter presentationFilter=new();
        readonly Dictionary<Transform,Quaternion> rendered=new();
        readonly Dictionary<Transform,Quaternion> solved=new();
        readonly Dictionary<Transform,Quaternion> candidates=new();
        readonly Dictionary<Transform,float> candidateAge=new();
        public PoseFrame LastRawFrame {get;private set;}
        public PoseFrame LastFilteredFrame {get;private set;}
        public int RejectedJoints=>presentationFilter.RejectedThisFrame;
        public Quaternion RawChestRotation {get;private set;}
        public Quaternion FilteredChestRotation {get;private set;}
        float poseDelta=1f/30;
        bool finishingPose;

        Vector3 avatarRestPosition;
        AvatarContactConstraints contacts;
        readonly Quaternion[] freeArmPose=new Quaternion[4];
        static readonly string[] armNames={"bicep.L","forearm.L","bicep.R","forearm.R"};
        bool hasFreeArmPose;
        public bool GolfGripConnected {get;private set;}
        public Vector3 GolfGripCenter=>contacts!=null && !seated?contacts.GripCenter:(RightHand.position+LeftHand.position)*.5f;
        public Vector3 GolfArmDirection {get;private set;}
        public void RestoreFreeArms()
        {
            if(!hasFreeArmPose)return;
            for(int i=0;i<4;i++)Bone(armNames[i]).localRotation=freeArmPose[i];
        }
        public bool ApplyGolfGrip(Vector3 shaftDown,Vector3? gripCenter=null)
        {
            RestoreFreeArms();
            GolfGripConnected=contacts!=null && shaftDown.sqrMagnitude>.0001f && contacts.FitGolfGrip(shaftDown,gripCenter,seated?.038f:.05f,!seated);
            return GolfGripConnected;
        }
        public float ArmPenetration=>contacts?.ArmPenetration??0;
        public float ArmLegPenetration=>contacts?.ArmLegPenetration??0;
        public float FootAnchorError=>contacts?.FootAnchorError??0;
        public bool CameraAligned => calibrationSamples>0;
        public bool RightArmTracked { get; private set; }
        public bool LeftArmTracked { get; private set; }
        public Transform RightHand => Bone(mii ? "hand.R" : "Skeleton_arm_joint_R__3_");
        public Transform LeftHand => Bone(mii ? "hand.L" : "Skeleton_arm_joint_L__2_");
        public Transform RightUpperArm => Bone(mii ? "bicep.R" : "Skeleton_arm_joint_R");
        public Transform RightForearm => Bone(mii ? "forearm.R" : "Skeleton_arm_joint_R__2_");
        public Transform LeftUpperArm => Bone(mii ? "bicep.L" : "Skeleton_arm_joint_L__4_");
        public Transform LeftForearm => Bone(mii ? "forearm.L" : "Skeleton_arm_joint_L__3_");
        public Transform Hip => Bone(mii ? "hip" : "Skeleton_torso_joint_1");
        public Transform RightAnkle => Bone(mii ? "foot.R" : "leg_joint_R_3");

        public void Initialize()
        {
            if (initialized && bones.Count > 0) return;
            if (!avatar) throw new InvalidOperationException("Assign the imported articulated avatar.");
            foreach (var animation in avatar.GetComponentsInChildren<Animation>()) { animation.Stop(); animation.enabled = false; }
            foreach (var animator in avatar.GetComponentsInChildren<Animator>()) animator.enabled = false;
            foreach (var t in avatar.GetComponentsInChildren<Transform>())
            {
                bones[t.name] = t;
                rest[t] = t.localRotation;
            }
            mii = bones.ContainsKey("bicep.R");
            var required = mii ? new[] {"bicep.R","forearm.R","hand.R","bicep.L","forearm.L","hand.L","hip","thigh.R","calf.R","foot.R","thigh.L","calf.L","foot.L"} :
                new[] { "Skeleton_arm_joint_R", "Skeleton_arm_joint_R__2_", "Skeleton_arm_joint_R__3_",
                "Skeleton_arm_joint_L__4_", "Skeleton_arm_joint_L__3_", "Skeleton_arm_joint_L__2_" };
            foreach (var name in required)
                if (!bones.ContainsKey(name)) throw new InvalidOperationException("Missing articulated bone: " + name);
            // Smaller than the Mii Maker default: friendlier proportions and less direct Nintendo likeness.
            if(mii && Bone("head"))Bone("head").localScale=Vector3.one*MiiHeadScale;
            avatarRestPosition=avatar.localPosition;
            if(mii)contacts=new AvatarContactConstraints(transform,avatar,Hip,Bone("spine.001"),Bone("neck"),
                new[]{Bone("thigh.L"),Bone("thigh.R")},new[]{Bone("calf.L"),Bone("calf.R")},new[]{Bone("foot.L"),Bone("foot.R")},
                new[]{Bone("bicep.L"),Bone("bicep.R")},new[]{Bone("forearm.L"),Bone("forearm.R")},new[]{Bone("hand.L"),Bone("hand.R")});
            if(mii && !seated)contacts.handContactLocal=MittenCenters();
            initialized = true;
        }
        // The round Mii hand is centred ~10 cm past the wrist bone. Standing golf grips
        // put these palm centres (not the wrist bones) on the shaft.
        Vector3[] MittenCenters()
        {
            // Other skinned meshes (e.g. the head) list the hand bones without owning hand
            // geometry, so gather hand-weighted vertices across every skinned mesh.
            var result=new Vector3[2];var sums=new Vector3[2];var counts=new int[2];
            var mesh=new Mesh();
            foreach(var skin in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                if(!skin.sharedMesh)continue;
                skin.BakeMesh(mesh,true);
                var vertices=mesh.vertices;var weights=skin.sharedMesh.boneWeights;
                for(int side=0;side<2;side++) {
                    int index=Array.IndexOf(skin.bones,Bone(side==0?"hand.L":"hand.R"));
                    if(index<0)continue;
                    for(int i=0;i<vertices.Length && i<weights.Length;i++)
                        if(weights[i].boneIndex0==index && weights[i].weight0>.6f){sums[side]+=skin.transform.position+skin.transform.rotation*vertices[i];counts[side]++;}
                }
            }
            for(int side=0;side<2;side++)
                if(counts[side]>0)result[side]=Bone(side==0?"hand.L":"hand.R").InverseTransformPoint(sums[side]/counts[side]);
            if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);
            return result;
        }
        Transform Bone(string name) => bones.TryGetValue(name, out var bone) ? bone : null;
        public void ResetTracking()
        {
            contacts?.Reset();GolfGripConnected=false;hasFreeArmPose=false;
            cameraToBody=Quaternion.identity;calibrationLeft=Vector3.zero;calibrationSamples=0;
            calibrationStart=-1;lastPoseTime=-1;presentationFilter.Reset();rendered.Clear();solved.Clear();candidates.Clear();candidateAge.Clear();
        }
        public void CalibrateCamera(PoseFrame frame)
        {
            ResetTracking();UpdateCameraBasis(frame);
            // Explicit address calibration locks the chosen view immediately.
            calibrationStart=frame==null?-1:frame.sourceMediaTimeMs-1000;
        }
        void UpdateCameraBasis(PoseFrame frame)
        {
            if(!usePresentationSpace || !PoseMath.Direction(frame,24,23,out var left))return;
            left.y=0;if(left.sqrMagnitude<.01f)return;
            if(calibrationStart<0)calibrationStart=frame.sourceMediaTimeMs;
            if(calibrationSamples>0 && frame.sourceMediaTimeMs-calibrationStart>500)return;
            calibrationLeft+=left.normalized;calibrationSamples++;
            // The authored Mii faces local -Z and its anatomical left is local +X.
            cameraToBody=Quaternion.FromToRotation(calibrationLeft.normalized,Vector3.right);
        }
        PoseFrame PresentationFrame(PoseFrame frame)
        {
            if(!usePresentationSpace || frame==null)return frame;
            if(lastPoseTime>=0 && frame.sourceMediaTimeMs<lastPoseTime)ResetTracking();
            UpdateCameraBasis(frame);
            poseDelta=lastPoseTime<0?1f/30:Mathf.Clamp((float)(frame.sourceMediaTimeMs-lastPoseTime)/1000,.001f,.1f);
            lastPoseTime=frame.sourceMediaTimeMs;
            frame=presentationFilter.Process(frame);
            LastFilteredFrame=frame;
            if(frame.worldLandmarks==null)return frame;
            var points=new PosePoint[frame.worldLandmarks.Length];
            for(int i=0;i<points.Length;i++) {
                var p=frame.worldLandmarks[i];if(p==null)continue;
                var v=cameraToBody*new Vector3(p.x,-p.y,p.z);
                points[i]=new PosePoint{index=i,x=v.x,y=-v.y,z=v.z,visibility=p.visibility};
            }
            // Raw landmarks remain untouched for measurements and saved evidence.
            return new PoseFrame {subjectDetected=frame.subjectDetected,imageLandmarks=frame.imageLandmarks,worldLandmarks=points};
        }
        public void Apply(PoseFrame frame)
        {
            Initialize();
            LastRawFrame=frame;
            if(frame==null)poseDelta=Application.isPlaying?Mathf.Clamp(Time.unscaledDeltaTime,.001f,.1f):1f/30;
            // Solve from bind pose, then smooth the rendered result separately.
            foreach (var pair in rest) if (pair.Key) pair.Key.localRotation = pair.Value;
            avatar.localPosition=avatarRestPosition;
            frame=PresentationFrame(frame);
            DriveStandingHips(frame);
            DriveTorso(frame);
            ApplyAddressLean(frame);
            // Authored seated lower body, explicitly not a measurement of unseen legs.
            if (seated) {
            Aim(Bone(mii ? "thigh.R" : "leg_joint_R_1"), Bone(mii ? "calf.R" : "leg_joint_R_2"), Present(new Vector3(0, -.12f, -1)));
            Aim(Bone(mii ? "thigh.L" : "leg_joint_L_1"), Bone(mii ? "calf.L" : "leg_joint_L_2"), Present(new Vector3(0, -.12f, -1)));
            Aim(Bone(mii ? "calf.R" : "leg_joint_R_2"), Bone(mii ? "foot.R" : "leg_joint_R_3"), Vector3.down);
            Aim(Bone(mii ? "calf.L" : "leg_joint_L_2"), Bone(mii ? "foot.L" : "leg_joint_L_3"), Vector3.down);
            } else if (mii) {
                DriveLeg(frame, true); DriveLeg(frame, false);
            }

            RightArmTracked = DriveArm(frame, false);
            LeftArmTracked = DriveArm(frame, true);
            if(usePresentationSpace) {
                finishingPose=true;if(golfGrip)ApplyGolfIdle();else ApplyRestIdle();finishingPose=false;
                SmoothSolvedRotations();
                // Confidence is always from current accepted input, never a held presentation pose.
                RightArmTracked &= LastRawFrame!=null && presentationFilter.Accepted[12] && presentationFilter.Accepted[14] && presentationFilter.Accepted[16];
                LeftArmTracked &= LastRawFrame!=null && presentationFilter.Accepted[11] && presentationFilter.Accepted[13] && presentationFilter.Accepted[15];
            }
            if(mii && usePresentationSpace) {
                if(!seated)contacts.PlantFeet(frame,poseDelta);
                contacts.KeepArmsOutsideBody();
                // Next frame starts from the last collision-safe arm pose.
                foreach(var name in armNames)rendered[Bone(name)]=Bone(name).localRotation;
                for(int i=0;i<4;i++)freeArmPose[i]=Bone(armNames[i]).localRotation;
                hasFreeArmPose=true;
                GolfArmDirection=GolfGripCenter-(Bone("bicep.L").position+Bone("bicep.R").position)*.5f;
            }
        }

        void ConstrainCore(Transform bone)
        {
            var target=bone.localRotation;
            if(solved.TryGetValue(bone,out var prior) && Quaternion.Angle(prior,target)>65) {
                float age=candidates.TryGetValue(bone,out var candidate) && Quaternion.Angle(candidate,target)<25
                    ? candidateAge[bone]+poseDelta : 0;
                candidates[bone]=target;candidateAge[bone]=age;
                target=age<.15f?prior:Quaternion.RotateTowards(prior,target,180*poseDelta);
            } else {candidates.Remove(bone);candidateAge.Remove(bone);}
            // Apply this before solving children, so a rejected chest flip cannot
            // reappear as compensating 180-degree local arm rotations.
            bone.localRotation=target;
        }
        void SmoothSolvedRotations()
        {
            foreach(var name in new[]{"hip","spine.001","bicep.R","forearm.R","bicep.L","forearm.L","thigh.R","calf.R","thigh.L","calf.L"}) {
                if(seated && (name=="hip" || name.StartsWith("thigh") || name.StartsWith("calf")))continue;
                var bone=Bone(name);if(!bone)continue;
                var target=bone.localRotation;
                bool core=name=="hip" || name=="spine.001";
                solved[bone]=target;
                if(rendered.TryGetValue(bone,out var previous)) {
                    float alpha=1-Mathf.Exp(-2*Mathf.PI*(core?3.5f:6f)*poseDelta);
                    target=Quaternion.RotateTowards(previous,Quaternion.Slerp(previous,target,alpha),(core?240:720)*poseDelta);
                }
                bone.localRotation=target;rendered[bone]=target;
                if(name=="spine.001")FilteredChestRotation=target;
            }
        }
        Vector3 Present(Vector3 direction) => usePresentationSpace ? transform.TransformDirection(direction) : direction;
        void DriveStandingHips(PoseFrame frame)
        {
            if(!mii || seated || !usePresentationSpace || !PoseMath.Direction(frame,24,23,out var left))return;
            left.y=0;
            var current=Bone("thigh.L").position-Bone("thigh.R").position;current.y=0;
            if(left.sqrMagnitude<.01f || current.sqrMagnitude<1e-6f)return;
            Hip.rotation=Quaternion.FromToRotation(current,Present(left))*Hip.rotation;
            ConstrainCore(Hip);
        }
        void DriveTorso(PoseFrame frame)
        {
            if(!mii || !usePresentationSpace || !PoseMath.Visible(frame,11) || !PoseMath.Visible(frame,12) ||
               !PoseMath.Visible(frame,23) || !PoseMath.Visible(frame,24))return;
            var spine=Bone("spine.001");var neck=Bone("neck");if(!spine || !neck)return;
            var up=(PoseMath.Position(frame,11)+PoseMath.Position(frame,12)-PoseMath.Position(frame,23)-PoseMath.Position(frame,24))*.5f;
            var left=PoseMath.Position(frame,11)-PoseMath.Position(frame,12);
            var forward=Vector3.Cross(left,up);
            var currentUp=neck.position-spine.position;
            var currentLeft=Bone("bicep.L").position-Bone("bicep.R").position;
            if(up.sqrMagnitude<.01f || left.sqrMagnitude<.025f ||
                Vector3.Cross(left.normalized,up.normalized).sqrMagnitude<.16f) {
                if(solved.TryGetValue(spine,out var held))spine.localRotation=held;
                return;
            }
            var observed=Quaternion.LookRotation(Present(forward),Present(up));
            var authored=Quaternion.LookRotation(Vector3.Cross(currentLeft,currentUp),currentUp);
            spine.rotation=observed*Quaternion.Inverse(authored)*spine.rotation;
            RawChestRotation=spine.localRotation;ConstrainCore(spine);
        }
        void ApplyAddressLean(PoseFrame frame)
        {
            if(!mii || !usePresentationSpace)return;
            float weight=1;
            if(PoseMath.Visible(frame,11)&&PoseMath.Visible(frame,12)&&PoseMath.Visible(frame,15)&&PoseMath.Visible(frame,16)&&PoseMath.Visible(frame,23)&&PoseMath.Visible(frame,24)) {
                var shoulders=(PoseMath.Position(frame,11)+PoseMath.Position(frame,12))*.5f;
                var hips=(PoseMath.Position(frame,23)+PoseMath.Position(frame,24))*.5f;
                var wrists=(PoseMath.Position(frame,15)+PoseMath.Position(frame,16))*.5f;
                float height=(wrists.y-hips.y)/Mathf.Max(.15f,shoulders.y-hips.y);
                weight=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.25f,.8f,height));
            }
            var spine=Bone("spine.001");
            var up=(Bone("neck").position-spine.position).normalized;
            var left=Vector3.ProjectOnPlane(Bone("bicep.L").position-Bone("bicep.R").position,transform.up).normalized;
            var front=Vector3.Cross(transform.up,left).normalized;
            float lean=Mathf.Atan2(Vector3.Dot(up,front),Vector3.Dot(up,transform.up))*Mathf.Rad2Deg;
            float correction=Mathf.Clamp(12*weight-lean,0,12*weight);
            spine.rotation=Quaternion.AngleAxis(-correction,left)*spine.rotation;
        }
        // Outside golf: each untracked arm rests on its own side — seated hands on the thighs, standing arms at the sides.
        // Tracked flags stay false; this is presentation only, never a measurement.
        void ApplyRestIdle()
        {
            if(!mii)return;
            // The authored Mii faces local -Z and its anatomical left is local +X.
            Vector3 Rest(bool left)=>Hip.position+Present(seated?new Vector3(left?.17f:-.17f,.1f,-.24f):new Vector3(left?.2f:-.2f,-.22f,-.05f));
            if(!RightArmTracked)PlaceIdleHand(false,Rest(false));
            if(!LeftArmTracked)PlaceIdleHand(true,Rest(true));
        }
        public void ApplyGolfIdle()
        {
            if(!mii || (usePresentationSpace && !finishingPose))return;
            // Seated: hands rest forward over the lap. Standing: arms hang from the shoulders,
            // so the joined hands sit just above hip height, in front of the thighs.
            var grip=Hip.position+Present(seated?new Vector3(0,.25f,-.37f):new Vector3(0,.06f,-.24f));
            // In golf, the unseen hand follows the visible grip for presentation.
            // Its Tracked flag stays false; this never fabricates a measurement.
            if(!RightArmTracked)PlaceIdleHand(false,(LeftArmTracked?LeftHand.position:grip)-Vector3.up*.035f);
            if(!LeftArmTracked)PlaceIdleHand(true,(RightArmTracked?RightHand.position:grip)+Vector3.up*.035f);
        }
        void PlaceIdleHand(bool left,Vector3 target)
        {
            string side=left?".L":".R";
            var upper=Bone("bicep"+side);var lower=Bone("forearm"+side);var hand=Bone("hand"+side);
            float a=Vector3.Distance(upper.position,lower.position),b=Vector3.Distance(lower.position,hand.position);
            var delta=target-upper.position;
            float d=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+.001f,a+b-.001f);
            var direction=delta.normalized;
            var bend=Vector3.ProjectOnPlane(Present(new Vector3(left?.65f:-.65f,-1,0)),direction).normalized;
            float along=(a*a-b*b+d*d)/(2*d);
            var elbow=upper.position+direction*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            Aim(upper,lower,elbow-upper.position);
            Aim(lower,hand,upper.position+direction*d-lower.position);
        }
        /// Presents an IMU-measured movement (no camera): call after Apply(null), which leaves the authored seated
        /// rest pose. The posture's held segments go where the movement needs them (the upper arm out for 90/90, both
        /// legs straight to stand), then the measured segment is drawn at `deg` — the coordinator's angle, so what the
        /// patient sees is what is scored. One routine for every movement: the model says which joint, not this code.
        public void ApplyMovement(BodyModel model, bool left, float deg)
        {
            Initialize();
            if (!mii || model == null) return;
            string s = left ? ".L" : ".R";
            // Proximal holds first, so the moving segment starts from them; distal ones after, so they stay put.
            Hold(model, "legs", left); Hold(model, "upperArm", left); Hold(model, "thigh", left);
            var direction = BodyToWorld(model.At(deg), left);
            switch (model.Segment)
            {
                case "arm":
                    var clear = ClearOfTorso(direction, left, deg);
                    Aim(Bone("bicep" + s), Bone("forearm" + s), clear); Aim(Bone("forearm" + s), Bone("hand" + s), clear); break;
                case "forearm" when model.Roll:
                    var along = BodyToWorld(model.Rest, left);
                    Aim(Bone("forearm" + s), Bone("hand" + s), along);
                    Turn(Bone("forearm" + s), along, deg); break;
                case "forearm": Aim(Bone("forearm" + s), Bone("hand" + s), direction); break;
                case "thigh": Aim(Bone("thigh" + s), Bone("calf" + s), direction); break;
                case "shank": Aim(Bone("calf" + s), Bone("foot" + s), direction); break;
                case "leg": Aim(Bone("thigh" + s), Bone("calf" + s), direction); Aim(Bone("calf" + s), Bone("foot" + s), direction); break;
                // A squat: both legs, the thigh forward by half the knee angle and the shin back by the other half.
                case "knees":
                    foreach (var side in new[] { ".L", ".R" })
                    {
                        bool l = side == ".L";
                        Aim(Bone("thigh" + side), Bone("calf" + side), BodyToWorld(model.At(deg / 2), l));
                        Aim(Bone("calf" + side), Bone("foot" + side), BodyToWorld(model.At(-deg / 2), l));
                    }
                    break;
                // Aimed like a limb rather than turned from the authored pose, whose neck and spine lean about 12
                // degrees: the drawn angle has to be the measured one, measured from upright.
                case "head": Aim(Bone("neck"), Bone("head"), direction); break;
                case "trunk": Aim(Bone("spine.001"), Bone("neck"), direction); break;
            }
            Hold(model, "forearm", left); Hold(model, "shank", left);
        }

        /// Where the measured segment is drawn from, which way it points at `deg`, and how long it is: what the
        /// target band and the mirror's ghosts are laid along. Read after ApplyMovement, so the origin already sits
        /// where the held segments put it (the elbow of a 90/90 arm, the knee of a standing leg). For a roll the
        /// segment is a thumb standing off the hand.
        public bool MovementSegment(BodyModel model, bool left, float deg, out Vector3 origin, out Vector3 direction, out float length)
        {
            Initialize();
            origin = direction = default; length = 0;
            if (!mii || model == null) return false;
            string s = left ? ".L" : ".R";
            float Span(params string[] names) { float d = 0; for (int i = 1; i < names.Length; i++) d += Vector3.Distance(Bone(names[i - 1]).position, Bone(names[i]).position); return d; }
            (origin, length) = model.Segment switch
            {
                "arm" => (Bone("bicep" + s).position, Span("bicep" + s, "forearm" + s, "hand" + s)),
                "forearm" when model.Roll => (Bone("hand" + s).position, Span("forearm" + s, "hand" + s) * .6f),
                "forearm" => (Bone("forearm" + s).position, Span("forearm" + s, "hand" + s)),
                "thigh" => (Bone("thigh" + s).position, Span("thigh" + s, "calf" + s)),
                "shank" => (Bone("calf" + s).position, Span("calf" + s, "foot" + s)),
                "leg" => (Bone("thigh" + s).position, Span("thigh" + s, "calf" + s, "foot" + s)),
                "knees" => (Bone("thigh" + s).position, Span("thigh" + s, "calf" + s)),
                // The head bone sits at the base of the skull; the crown is about as far again.
                "head" => (Bone("neck").position, Span("neck", "head") * 2.2f),
                "trunk" => (Bone("spine.001").position, Span("spine.001", "neck")),
                _ => (Vector3.zero, 0f),
            };
            // A squat's ghost is the thigh, which carries half the knee angle.
            direction = BodyToWorld(model.At(model.Segment == "knees" ? deg / 2 : deg), left);
            return length > 0;
        }

        void Hold(BodyModel model, string key, bool left)
        {
            if (!model.Hold.TryGetValue(key, out var v)) return;
            var direction = BodyToWorld(v, left);
            foreach (bool side in key == "legs" ? new[] { false, true } : new[] { left })
            {
                string s = side ? ".L" : ".R";
                var d = key == "legs" ? BodyToWorld(v, side) : direction;
                switch (key)
                {
                    case "upperArm": Aim(Bone("bicep" + s), Bone("forearm" + s), ClearOfTorso(d, side, 0)); break;
                    case "forearm": Aim(Bone("forearm" + s), Bone("hand" + s), d); break;
                    case "thigh": Aim(Bone("thigh" + s), Bone("calf" + s), d); break;
                    case "shank": Aim(Bone("calf" + s), Bone("foot" + s), d); break;
                    case "legs": Aim(Bone("thigh" + s), Bone("calf" + s), d); Aim(Bone("calf" + s), Bone("foot" + s), d); break;
                }
            }
        }
        /// The patient's frame in the world: x out to the working side, y up, z forward. The authored Mii faces
        /// local -Z; its anatomical left is local +X.
        /// A Mii's shoulder joint sits inside its round body, so an arm hanging straight down from it is drawn through
        /// the torso. Near rest the arm swings out just enough to clear the body; the swing fades to nothing by 40
        /// degrees of elevation, so from there up — and at every target — the drawn angle is exactly the measured one.
        Vector3 ClearOfTorso(Vector3 direction, bool left, float deg)
        {
            float fade = Mathf.Clamp01(1 - deg / 40f);
            if (fade <= 0) return direction;
            return (direction + BodyToWorld(new Vector3(1, 0, 0), left) * (.32f * fade)).normalized;
        }
        Vector3 BodyToWorld(Vector3 v, bool left) =>
            (transform.TransformDirection(new Vector3(left ? 1 : -1, 0, 0)) * v.x + transform.TransformDirection(Vector3.up) * v.y
             + transform.TransformDirection(new Vector3(0, 0, -1)) * v.z).normalized;
        static void Turn(Transform joint, Vector3 axis, float deg)
        {
            if (joint && axis.sqrMagnitude > 1e-8f) joint.rotation = Quaternion.AngleAxis(deg, axis) * joint.rotation;
        }
        void DriveLeg(PoseFrame frame, bool left)
        {
            string side = left ? ".L" : ".R";
            if (PoseMath.Direction(frame, left ? 23 : 24, left ? 25 : 26, out var thigh))
                Aim(Bone("thigh" + side), Bone("calf" + side), Present(thigh));
            if (PoseMath.Direction(frame, left ? 25 : 26, left ? 27 : 28, out var calf))
                Aim(Bone("calf" + side), Bone("foot" + side), Present(calf));
        }
        bool DriveArm(PoseFrame frame, bool left)
        {
            int shoulder = left ? 11 : 12, elbow = left ? 13 : 14, wrist = left ? 15 : 16;
            var upper = Bone(mii ? (left ? "bicep.L" : "bicep.R") : (left ? "Skeleton_arm_joint_L__4_" : "Skeleton_arm_joint_R"));
            var forearm = Bone(mii ? (left ? "forearm.L" : "forearm.R") : (left ? "Skeleton_arm_joint_L__3_" : "Skeleton_arm_joint_R__2_"));
            var hand = Bone(mii ? (left ? "hand.L" : "hand.R") : (left ? "Skeleton_arm_joint_L__2_" : "Skeleton_arm_joint_R__3_"));
            bool upperValid = PoseMath.Direction(frame, shoulder, elbow, out var upperDirection);
            bool lowerValid = PoseMath.Direction(frame, elbow, wrist, out var lowerDirection);
            // Occlusion can collapse the estimated forearm to a few millimetres
            // even with a high visibility score. Do not animate that as a real arm.
            lowerValid &= upperValid && PoseMath.PlausibleArm(upperDirection,lowerDirection);
            if (!upperValid)
            {
                Aim(upper, forearm, Present(new Vector3(left ? .18f : -.18f, -1, 0)));
                Aim(forearm, hand, Present(new Vector3(0, -.8f, -.2f)));
            }
            if (upperValid) Aim(upper, forearm, Present(upperDirection));
            if (upperValid && lowerValid) Aim(forearm, hand, Present(lowerDirection));
            return upperValid && lowerValid;
        }
        // Port of Wheelgentic's aim()/finite-direction approach, extended to separate bones.
        // FromToRotation uses the actual imported bone axis instead of assuming local +Y.
        static void Aim(Transform joint, Transform child, Vector3 direction)
        {
            if (!joint || !child || !(direction.sqrMagnitude > 1e-8f)) return;
            var current = child.position - joint.position;
            if (!(current.sqrMagnitude > 1e-8f)) return;
            joint.rotation = Quaternion.FromToRotation(current, direction) * joint.rotation;
        }
    }
}

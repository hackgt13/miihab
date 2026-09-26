using UnityEngine;

namespace Kinesthetic
{
    // Presentation contacts: neither these planted feet nor collision-corrected arms
    // are substituted for the recorded patient landmarks.
    public sealed class AvatarContactConstraints
    {
        readonly Transform space,avatar,hip,spine,neck;
        readonly Transform[] thighs, knees, feet, upper, elbows, hands;
        readonly Vector3[] legStart=new Vector3[4],legEnd=new Vector3[4];
        readonly float[] legRadius=new float[4];
        void CacheLegs()
        {
            for(int i=0;i<2;i++) {
                legStart[i*2]=thighs[i].position;legEnd[i*2]=knees[i].position;
                legStart[i*2+1]=knees[i].position;legEnd[i*2+1]=feet[i].position;
                legRadius[i*2]=Vector3.Distance(legStart[i*2],legEnd[i*2])*.24f+.028f;
                legRadius[i*2+1]=Vector3.Distance(legStart[i*2+1],legEnd[i*2+1])*.18f+.025f;
            }
        }
        readonly Vector3[] footAnchor=new Vector3[2];
        readonly Quaternion[] footRotation=new Quaternion[2];
        readonly Vector3 avatarRest;
        readonly Vector3[] priorElbow=new Vector3[2],priorHand=new Vector3[2];
        readonly bool[] armHistory=new bool[2];
        Vector3 hipOffset, referenceSupport;
        bool supportSet;
        // Palm-centre offsets in each hand bone's space; zero means "use the wrist bone".
        public Vector3[] handContactLocal=new Vector3[2];
        Vector3 Contact(int i)=>hands[i].TransformPoint(handContactLocal[i]);
        public Vector3 GripCenter=>(Contact(0)+Contact(1))*.5f;
        static void AimPoint(Transform joint,Vector3 point,Vector3 direction)
        {if(direction.sqrMagnitude>.000001f)joint.rotation=Quaternion.FromToRotation(point-joint.position,direction)*joint.rotation;}
        public float ArmPenetration {get;private set;}
        public float ArmLegPenetration {get;private set;}
        public float FootAnchorError {get;private set;}
        public AvatarContactConstraints(Transform space,Transform avatar,Transform hip,Transform spine,Transform neck,
            Transform[] thighs,Transform[] knees,Transform[] feet,Transform[] upper,Transform[] elbows,Transform[] hands)
        {
            this.space=space;this.avatar=avatar;this.hip=hip;this.spine=spine;this.neck=neck;
            this.thighs=thighs;this.knees=knees;this.feet=feet;this.upper=upper;this.elbows=elbows;this.hands=hands;
            avatarRest=avatar.localPosition;
            for(int i=0;i<2;i++){footAnchor[i]=space.InverseTransformPoint(feet[i].position);footRotation[i]=Quaternion.Inverse(space.rotation)*feet[i].rotation;}
        }
        public void Reset(){hipOffset=Vector3.zero;supportSet=false;armHistory[0]=armHistory[1]=false;ArmPenetration=ArmLegPenetration=FootAnchorError=0;}
        public void PlantFeet(PoseFrame frame,float dt)
        {
            Vector3 desired=Vector3.zero;
            if(PoseMath.Visible(frame,23)&&PoseMath.Visible(frame,24)&&PoseMath.Visible(frame,27)&&PoseMath.Visible(frame,28)) {
                var support=(PoseMath.Position(frame,23)+PoseMath.Position(frame,24)-PoseMath.Position(frame,27)-PoseMath.Position(frame,28))*.5f;
                if(!supportSet){referenceSupport=support;supportSet=true;}
                float modelHeight=space.InverseTransformPoint(hip.position).y-(footAnchor[0].y+footAnchor[1].y)*.5f;
                float scale=modelHeight/Mathf.Max(.4f,referenceSupport.y);
                var delta=(support-referenceSupport)*scale;
                desired=new Vector3(Mathf.Clamp(delta.x,-.12f,.12f),Mathf.Clamp(delta.y,-.16f,0),Mathf.Clamp(delta.z,-.10f,.10f));
            }
            hipOffset=Vector3.Lerp(hipOffset,desired,1-Mathf.Exp(-12*dt));
            avatar.localPosition=avatarRest;
            avatar.position+=space.TransformVector(hipOffset);
            // Lower the pelvis enough for both legs to reach their planted contacts.
            float lower=0;
            for(int i=0;i<2;i++) {
                var target=space.TransformPoint(footAnchor[i]);
                float length=Vector3.Distance(thighs[i].position,knees[i].position)+Vector3.Distance(knees[i].position,feet[i].position);
                var delta=thighs[i].position-target;
                float horizontal=Vector3.ProjectOnPlane(delta,space.up).sqrMagnitude;
                float maxHeight=Mathf.Sqrt(Mathf.Max(.001f,length*length*.995f-horizontal));
                lower=Mathf.Max(lower,Vector3.Dot(delta,space.up)-maxHeight);
            }
            avatar.position-=space.up*Mathf.Max(0,lower);
            FootAnchorError=0;
            for(int i=0;i<2;i++) {
                var target=space.TransformPoint(footAnchor[i]);
                Solve(thighs[i],knees[i],feet[i],target,space.TransformDirection(new Vector3(i==0?.12f:-.12f,0,-1)));
                feet[i].rotation=space.rotation*footRotation[i];
                FootAnchorError=Mathf.Max(FootAnchorError,Vector3.Distance(feet[i].position,target));
            }
        }
        static Vector3 Closest(Vector3 p,Vector3 a,Vector3 b)
        {var axis=b-a;return a+axis*Mathf.Clamp01(Vector3.Dot(p-a,axis)/Mathf.Max(axis.sqrMagnitude,.00001f));}
        static float Depth(Vector3 p,Vector3 a,Vector3 b,float radius)=>Mathf.Max(0,radius-Vector3.Distance(p,Closest(p,a,b)));
        static float SegmentDepth(Vector3 x,Vector3 y,Vector3 a,Vector3 b,float r,float start=0)
        {
            // Exact closest distance between two segments: faster than per-point
            // sampling and cannot miss an intersection between samples.
            x=Vector3.Lerp(x,y,start);
            var u=y-x;var v=b-a;var w=x-a;
            float uu=Vector3.Dot(u,u),vv=Vector3.Dot(v,v),uv=Vector3.Dot(u,v);
            float uw=Vector3.Dot(u,w),vw=Vector3.Dot(v,w),s=0,t=0;
            if(uu<1e-8f)t=Mathf.Clamp01(vw/Mathf.Max(vv,1e-8f));
            else if(vv<1e-8f)s=Mathf.Clamp01(-uw/uu);
            else {
                float denominator=uu*vv-uv*uv;
                s=denominator>1e-8f?Mathf.Clamp01((uv*vw-uw*vv)/denominator):0;
                t=(uv*s+vw)/vv;
                if(t<0){t=0;s=Mathf.Clamp01(-uw/uu);}
                else if(t>1){t=1;s=Mathf.Clamp01((uv-uw)/uu);}
            }
            return Mathf.Max(0,r-(w+u*s-v*t).magnitude);
        }
        float LegDepth(Vector3 shoulder,Vector3 elbow,Vector3 hand)
        {
            float depth=0;
            for(int j=0;j<4;j++)
                depth=Mathf.Max(depth,Mathf.Max(SegmentDepth(shoulder,elbow,legStart[j],legEnd[j],legRadius[j]),SegmentDepth(elbow,hand,legStart[j],legEnd[j],legRadius[j])));
            return depth;
        }
        public void KeepArmsOutsideBody()
        {
            CacheLegs();
            var shoulders=(upper[0].position+upper[1].position)*.5f;
            var axis=(neck.position-hip.position).normalized;
            var a=hip.position+axis*.07f;var b=shoulders-axis*.025f;
            float radius=Vector3.Distance(upper[0].position,upper[1].position)*.37f+.025f;
            var front=Vector3.Cross(upper[0].position-upper[1].position,axis).normalized;
            if(Vector3.Dot(front,space.TransformDirection(Vector3.back))<0)front=-front;
            ArmPenetration=ArmLegPenetration=0;
            for(int i=0;i<2;i++) {
                var shoulder=upper[i].position;var originalElbow=elbows[i].position;var target=hands[i].position;
                float armA=Vector3.Distance(shoulder,originalElbow),armB=Vector3.Distance(originalElbow,target);
                if(SegmentDepth(shoulder,originalElbow,a,b,radius,.22f)<.001f && SegmentDepth(originalElbow,target,a,b,radius)<.001f && LegDepth(shoulder,originalElbow,target)<.001f){RememberArm(i);continue;}
                Vector3 best=originalElbow,bestTarget=target,originalHand=target;float score=float.MaxValue;
                for(int attempt=0;attempt<9;attempt++) {
                    var center=Closest(target,a,b);var away=target-center;
                    if(away.magnitude<radius+.012f)target=center+(away.sqrMagnitude>.001f?away.normalized:front)*(radius+.012f);
                    var delta=target-shoulder;float d=Mathf.Clamp(delta.magnitude,Mathf.Abs(armA-armB)+.001f,armA+armB-.001f);
                    var dir=delta.normalized;target=shoulder+dir*d;
                    float along=(armA*armA-armB*armB+d*d)/(2*d),height=Mathf.Sqrt(Mathf.Max(0,armA*armA-along*along));
                    var bend=Vector3.ProjectOnPlane(originalElbow-shoulder,dir).normalized;
                    if(bend.sqrMagnitude<.01f)bend=Vector3.ProjectOnPlane(front,dir).normalized;
                    for(int k=0;k<48;k++) {
                        var elbow=shoulder+dir*along+(Quaternion.AngleAxis(k*7.5f,dir)*bend)*height;
                        float depth=Mathf.Max(SegmentDepth(shoulder,elbow,a,b,radius,.22f),SegmentDepth(elbow,target,a,b,radius),LegDepth(shoulder,elbow,target));
                        float cost=Mathf.Max(0,depth-.0009f)*1000+Vector3.SqrMagnitude(elbow-originalElbow)*.3f+Vector3.SqrMagnitude(target-originalHand)*.2f
                            +(armHistory[i]?(Vector3.SqrMagnitude(elbow-space.TransformPoint(priorElbow[i]))+Vector3.SqrMagnitude(target-space.TransformPoint(priorHand[i])))*.7f:0);
                        if(cost<score){score=cost;best=elbow;bestTarget=target;}
                    }
                    target+=front*.045f+space.up*.012f+(shoulder-shoulders).normalized*.025f;
                }
                Aim(upper[i],elbows[i],best-shoulder);Aim(elbows[i],hands[i],bestTarget-elbows[i].position);
                ArmPenetration=Mathf.Max(ArmPenetration,Mathf.Max(SegmentDepth(shoulder,elbows[i].position,a,b,radius,.22f),SegmentDepth(elbows[i].position,hands[i].position,a,b,radius)));
                ArmLegPenetration=Mathf.Max(ArmLegPenetration,LegDepth(shoulder,elbows[i].position,hands[i].position));
                RememberArm(i);
            }
        }
        // Solve both arms together onto one shaft, using the same torso/leg
        // envelopes as the free-arm solve. Only the displayed rig is changed.
        public bool FitGolfGrip(Vector3 shaftDown,Vector3? gripCenter=null,float handGap=.038f,bool stacked=false)
        {
            CacheLegs();
            shaftDown=shaftDown.normalized;
            var shoulders=(upper[0].position+upper[1].position)*.5f;
            var axis=(neck.position-hip.position).normalized;
            var facing=Vector3.Cross(upper[0].position-upper[1].position,axis).normalized;
            if(Vector3.Dot(facing,space.TransformDirection(Vector3.back))<0)facing=-facing;
            // Untracked standing address: arms hang from the shoulders, so the joined hands sit
            // well below the chest and slightly forward (the shoulders→hands triangle).
            float reach=Vector3.Distance(upper[0].position,elbows[0].position)+Vector3.Distance(elbows[0].position,stacked?Contact(0):hands[0].position);
            var center=gripCenter??(stacked?shoulders-axis*reach*.86f+facing*reach*.42f:(hands[0].position+hands[1].position)*.5f);
            var torsoA=hip.position+axis*.07f;var torsoB=shoulders-axis*.025f;
            float radius=Vector3.Distance(upper[0].position,upper[1].position)*.37f+.025f;
            var front=Vector3.Cross(upper[0].position-upper[1].position,axis).normalized;
            if(Vector3.Dot(front,space.TransformDirection(Vector3.back))<0)front=-front;
            // Wii-style presentation: round hands meet side by side at one
            // handle height. Each elbow stays on its anatomical side.
            var lateral=Vector3.ProjectOnPlane(upper[0].position-upper[1].position,shaftDown).normalized;
            if(lateral.sqrMagnitude<.01f)lateral=space.right;
            var bestElbow=new Vector3[2];var bestHand=new Vector3[2];
            var chosenElbow=new Vector3[2];var chosenHand=new Vector3[2];
            float bestScore=float.PositiveInfinity;
            // Preserve the measured grip centre where possible, then search toward
            // a reachable point in front of the chest/lap.
            var safeCenter=shoulders+front*.25f-axis*.12f;
            for(int attempt=0;attempt<34;attempt++) {
                var candidate=attempt<9?Vector3.Lerp(center,safeCenter,attempt/8f):shoulders+front*(.16f+((attempt-9)%5)*.035f)+axis*(.06f-((attempt-9)/5)*.065f);
                float score=Vector3.SqrMagnitude(candidate-center)*2;bool reachable=true;
                for(int i=0;i<2;i++) {
                    var shoulder=upper[i].position;var original=elbows[i].position;
                    float a=Vector3.Distance(shoulder,original),b=Vector3.Distance(original,stacked?Contact(i):hands[i].position);
                    // Stacked (standing): both hands on the shaft axis, left hand on top
                    // (toward the butt) and right hand directly below it, touching.
                    // Side by side (seated): hands meet across the handle.
                    var target=stacked?candidate-shaftDown*(i==0?handGap*.5f:-handGap*.5f)
                        :candidate+lateral*(i==0?handGap*.5f:-handGap*.5f);
                    var delta=target-shoulder;float distance=delta.magnitude;
                    if(distance>=a+b-.0002f || distance<=Mathf.Abs(a-b)+.0002f){reachable=false;break;}
                    var direction=delta/distance;
                    float along=(a*a-b*b+distance*distance)/(2*distance),height=Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
                    var bend=Vector3.ProjectOnPlane(original-shoulder,direction).normalized;
                    if(bend.sqrMagnitude<.01f)bend=Vector3.ProjectOnPlane(front,direction).normalized;
                    float armScore=float.PositiveInfinity;Vector3 chosen=original;
                    for(int k=0;k<48;k++) {
                        var elbow=shoulder+direction*along+(Quaternion.AngleAxis(k*7.5f,direction)*bend)*height;
                        float depth=Mathf.Max(SegmentDepth(shoulder,elbow,torsoA,torsoB,radius,.22f),SegmentDepth(elbow,target,torsoA,torsoB,radius),LegDepth(shoulder,elbow,target));
                        if(depth>.001f)continue;
                        var outward=(shoulder-shoulders).normalized;
                        // Stacked address: upper arms hang down from the shoulders and a little
                        // forward, elbows stay on their own side, forming the shoulders→hands triangle.
                        var authored=stacked?shoulder+(-axis*.78f+front*.42f+outward*.3f).normalized*a
                            :shoulder+front*.095f+outward*.055f-axis*.07f;
                        float cost=Vector3.SqrMagnitude(elbow-original)*(stacked?.15f:.45f)+Vector3.SqrMagnitude(elbow-authored)*(stacked?1.2f:.55f);
                        // Never let an elbow drift inward across the chest.
                        if(stacked)cost+=Mathf.Pow(Mathf.Max(0,.03f-Vector3.Dot(elbow-shoulders,outward)),2)*400;
                        if(cost<armScore){armScore=cost;chosen=elbow;}
                    }
                    if(float.IsPositiveInfinity(armScore)){reachable=false;break;}
                    score+=armScore;chosenElbow[i]=chosen;chosenHand[i]=target;
                }
                if(reachable && score<bestScore) {
                    bestScore=score;
                    for(int i=0;i<2;i++){bestElbow[i]=chosenElbow[i];bestHand[i]=chosenHand[i];}
                    break; // candidates are ordered by proximity to the captured grip
                }
            }
            if(float.IsPositiveInfinity(bestScore))return false;
            ArmPenetration=ArmLegPenetration=0;
            for(int i=0;i<2;i++) {
                Aim(upper[i],elbows[i],bestElbow[i]-upper[i].position);
                if(stacked)AimPoint(elbows[i],Contact(i),bestHand[i]-elbows[i].position);
                else Aim(elbows[i],hands[i],bestHand[i]-elbows[i].position);
                ArmPenetration=Mathf.Max(ArmPenetration,SegmentDepth(upper[i].position,elbows[i].position,torsoA,torsoB,radius,.22f),SegmentDepth(elbows[i].position,hands[i].position,torsoA,torsoB,radius));
                ArmLegPenetration=Mathf.Max(ArmLegPenetration,LegDepth(upper[i].position,elbows[i].position,hands[i].position));
            }
            return true;
        }
        void RememberArm(int i){priorElbow[i]=space.InverseTransformPoint(elbows[i].position);priorHand[i]=space.InverseTransformPoint(hands[i].position);armHistory[i]=true;}
        static void Solve(Transform root,Transform middle,Transform end,Vector3 target,Vector3 pole)
        {
            float a=Vector3.Distance(root.position,middle.position),b=Vector3.Distance(middle.position,end.position);
            var direction=(target-root.position).normalized;
            float d=Mathf.Clamp(Vector3.Distance(root.position,target),Mathf.Abs(a-b)+.0001f,a+b-.0001f);
            var bend=Vector3.ProjectOnPlane(pole,direction).normalized;
            float along=(a*a-b*b+d*d)/(2*d);
            var joint=root.position+direction*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            Aim(root,middle,joint-root.position);Aim(middle,end,target-middle.position);
        }
        static void Aim(Transform joint,Transform child,Vector3 direction)
        {if(direction.sqrMagnitude>.000001f)joint.rotation=Quaternion.FromToRotation(child.position-joint.position,direction)*joint.rotation;}
    }
}

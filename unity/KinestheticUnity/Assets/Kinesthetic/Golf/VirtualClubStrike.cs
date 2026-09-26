using System;
using UnityEngine;

namespace Kinesthetic.Golf
{
    // A virtual collision proxy built from avatar grip + relative IMU orientation.
    // It does not observe the physical clubhead or establish real ball contact.
    public sealed class VirtualClubStrike
    {
        public bool Calibrated {get;private set;}
        public float Radius {get;private set;}
        public float ClosestApproach {get;private set;}=float.PositiveInfinity;
        public Vector3 Ball {get;private set;}
        Quaternion address,reference;
        Vector3 shaft,previous;
        double previousAt=-1,crossedAt=double.NegativeInfinity;
        bool departed;
        public void Reset(){Calibrated=false;BreakTrace();}
        public void BreakTrace(){previousAt=-1;crossedAt=double.NegativeInfinity;departed=false;ClosestApproach=float.PositiveInfinity;}
        public void Calibrate(Vector3 ball,Quaternion addressRotation,Quaternion sensorReference,Vector3 scaledShaft,float radius)
        {
            Reset();Ball=ball;address=addressRotation;reference=sensorReference;shaft=scaledShaft;Radius=radius;Calibrated=true;
        }
        public Quaternion Rotation(Quaternion sensor)=>address*(Quaternion.Inverse(reference)*sensor);
        public Vector3 Head(Vector3 grip,Quaternion sensor)=>grip+Rotation(sensor)*shaft;
        public bool CrossedNear(double candidateAt)=>Math.Abs(crossedAt-candidateAt)<=.12;
        public void Sample(Vector3 grip,Quaternion sensor,double time)
        {
            if(!Calibrated)return;
            var head=Head(grip,sensor);
            if(!PoseMath.Finite(head.x)||!PoseMath.Finite(head.y)||!PoseMath.Finite(head.z)||double.IsNaN(time)||double.IsInfinity(time)){BreakTrace();return;}
            if(previousAt>=0 && time<=previousAt)return;
            if(previousAt>=0 && (time-previousAt>.15 || Vector3.Distance(previous,head)>.5f))BreakTrace();
            if(previousAt<0){previous=head;previousAt=time;return;}
            // Ignore address and the outward departure. Only a subsequent return counts.
            if(!departed)
            {
                if(Vector3.Distance(head,Ball)>Radius*1.5f)departed=true;
                previous=head;previousAt=time;return;
            }
            var segment=head-previous;
            float t=segment.sqrMagnitude>1e-8f?Mathf.Clamp01(Vector3.Dot(Ball-previous,segment)/segment.sqrMagnitude):0;
            float distance=Vector3.Distance(previous+segment*t,Ball);
            ClosestApproach=Mathf.Min(ClosestApproach,distance);
            if(segment.sqrMagnitude>1e-8f && distance<=Radius)crossedAt=previousAt+(time-previousAt)*t;
            previous=head;previousAt=time;
        }
    }
}

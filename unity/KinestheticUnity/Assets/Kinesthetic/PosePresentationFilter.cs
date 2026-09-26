using System;
using UnityEngine;

namespace Kinesthetic
{
    // Presentation only. Raw recordings and clinical measurements never pass through this filter.
    public sealed class PosePresentationFilter
    {
        readonly Vector3[] value=new Vector3[33], acceptedRaw=new Vector3[33], velocity=new Vector3[33];
        readonly double[] acceptedAt=new double[33];
        readonly bool[] initialized=new bool[33];
        public readonly bool[] Accepted=new bool[33];
        double previousTime=-1;
        public int RejectedThisFrame {get;private set;}
        public void Reset(){Array.Clear(initialized,0,33);Array.Clear(Accepted,0,33);previousTime=-1;}
        static float Alpha(float cutoff,float dt)=>1-Mathf.Exp(-2*Mathf.PI*cutoff*dt);
        public PoseFrame Process(PoseFrame input)
        {
            Array.Clear(Accepted,0,33);RejectedThisFrame=0;
            if(input?.worldLandmarks==null || input.imageLandmarks==null)return input;
            double now=input.sourceMediaTimeMs*.001;
            if(previousTime>=0 && (now<previousTime || now-previousTime>.3))Reset();
            float dt=previousTime<0?1f/30:Mathf.Clamp((float)(now-previousTime),.001f,.1f);previousTime=now;
            var world=new PosePoint[input.worldLandmarks.Length];var image=new PosePoint[input.imageLandmarks.Length];
            for(int i=0;i<Math.Min(33,world.Length);i++) {
                var p=input.worldLandmarks[i];var im=input.imageLandmarks[i];
                if(p==null || im==null)continue;
                var raw=new Vector3(p.x,p.y,p.z);
                bool valid=PoseMath.Visible(input,i);
                // Time-scaled gross jump rejection; fast coherent swings remain admissible.
                bool core=i==11 || i==12 || i==23 || i==24;
                float speedLimit=core?3f:10f;
                if(valid && initialized[i] && now-acceptedAt[i]<.18 &&
                    Vector3.Distance(raw,acceptedRaw[i])>.055f+speedLimit*dt)valid=false;
                if(valid) {
                    if(!initialized[i] || now-acceptedAt[i]>.18) {value[i]=raw;velocity[i]=Vector3.zero;}
                    else {
                        var derivative=(raw-acceptedRaw[i])/Mathf.Max(dt,.001f);
                        velocity[i]=Vector3.Lerp(velocity[i],derivative,Alpha(1.5f,dt));
                        float cutoff=1.8f+.55f*velocity[i].magnitude;
                        value[i]=Vector3.Lerp(value[i],raw,Alpha(cutoff,dt));
                    }
                    acceptedRaw[i]=raw;acceptedAt[i]=now;initialized[i]=true;Accepted[i]=true;
                } else RejectedThisFrame++;
                bool held=initialized[i] && now-acceptedAt[i]<=.18;
                var v=held?value[i]:raw;
                world[i]=new PosePoint{index=i,x=v.x,y=v.y,z=v.z,visibility=held?1:0};
                // Held positions may animate briefly; Accepted stays false for shot readiness.
                image[i]=new PosePoint{index=i,x=held?.5f:im.x,y=held?.5f:im.y,z=im.z,visibility=held?1:0};
            }
            return new PoseFrame{frameID=input.frameID,source=input.source,sourceMediaTimeMs=input.sourceMediaTimeMs,
                subjectDetected=input.subjectDetected,imageLandmarks=image,worldLandmarks=world};
        }
    }
}

using System;
using Kinesthetic;
using UnityEngine;

public static class PoseFilterVerification
{
    public static string Run()
    {
        var f=new PoseFrame{subjectDetected=true,imageLandmarks=new PosePoint[33],worldLandmarks=new PosePoint[33]};
        for(int i=0;i<33;i++) {f.imageLandmarks[i]=new PosePoint{x=.5f,y=.5f,visibility=1};f.worldLandmarks[i]=new PosePoint{x=i*.01f,y=0,z=0,visibility=1};}
        var filter=new PosePresentationFilter();var a=filter.Process(f);
        f.sourceMediaTimeMs=33;f.worldLandmarks[15].z=1;
        var b=filter.Process(f);
        if(filter.Accepted[15] || Mathf.Abs(b.worldLandmarks[15].z)>.001)throw new Exception("Wrist teleport reached presentation");
        if(f.worldLandmarks[15].z!=1)throw new Exception("Raw data changed");
        f.sourceMediaTimeMs=66;f.worldLandmarks[15].z=.02f;
        var c=filter.Process(f);
        if(!filter.Accepted[15] || c.worldLandmarks[15].z<=0 || c.worldLandmarks[15].z>=.02f)throw new Exception("Ordinary motion did not recover smoothly");
        f.sourceMediaTimeMs=100;f.imageLandmarks[15].visibility=0;
        var d=filter.Process(f);
        if(filter.Accepted[15] || !PoseMath.Visible(d,15))throw new Exception("Short dropout must hold presentation without claiming fresh input");
        f.sourceMediaTimeMs=290;var e=filter.Process(f);
        if(PoseMath.Visible(e,15))throw new Exception("Stale pose was held indefinitely");
        f.sourceMediaTimeMs=0;f.imageLandmarks[15].visibility=1;
        filter.Process(f);if(!filter.Accepted[15])throw new Exception("Replay seek failed to reset filter");
        return "PASS: teleport rejected, normal motion recovered, raw unchanged, short hold not counted as fresh, stale hold expires, backward seek resets";
    }
}

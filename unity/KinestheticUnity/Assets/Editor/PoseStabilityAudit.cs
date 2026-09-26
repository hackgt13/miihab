using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Kinesthetic;
using Kinesthetic.Golf;
using Newtonsoft.Json;

public static class PoseStabilityAudit
{
    public static string Run(string source,int player,string output)
    {
        var rig=UnityEngine.Object.FindAnyObjectByType<KinestheticGolf>().rigs[player];rig.Initialize();rig.ResetTracking();rig.Apply(null);
        var capture=PoseJson.Read<PoseCapture>(File.ReadAllText(source));
        var names=new[]{"spine.001","hip","bicep.R","forearm.R","bicep.L","forearm.L"};
        var bones=names.Select(n=>rig.avatar.GetComponentsInChildren<Transform>().First(t=>t.name==n)).ToArray();
        var previous=bones.Select(t=>t.localRotation).ToArray();
        var steps=names.Select(_=>new List<float>()).ToArray();
        var trace=new List<object>();
        bool lastLeft=false,lastRight=false;int transitions=0;
        foreach(var frame in capture.frames) {
            rig.Apply(frame);rig.ApplyGolfIdle();
            if(frame.frameID>0)for(int i=0;i<bones.Length;i++)steps[i].Add(Quaternion.Angle(previous[i],bones[i].localRotation));
            if(lastLeft!=rig.LeftArmTracked || lastRight!=rig.RightArmTracked)transitions++;
            lastLeft=rig.LeftArmTracked;lastRight=rig.RightArmTracked;
            previous=bones.Select(t=>t.localRotation).ToArray();
            trace.Add(new {timeMs=frame.sourceMediaTimeMs,raw=frame.worldLandmarks,image=frame.imageLandmarks,
                filtered=rig.LastFilteredFrame?.worldLandmarks,rejected=rig.RejectedJoints,
                rawChest=new[]{rig.RawChestRotation.x,rig.RawChestRotation.y,rig.RawChestRotation.z,rig.RawChestRotation.w},
                rotations=previous.Select(q=>new[]{q.x,q.y,q.z,q.w}),left=lastLeft,right=lastRight});
        }
        var stats=names.Select((n,i)=>new {bone=n,maxStepDeg=steps[i].Max(),p95StepDeg=steps[i].OrderBy(x=>x).ElementAt((int)(steps[i].Count*.95)),stepsOver45=steps[i].Count(x=>x>45)}).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output,JsonConvert.SerializeObject(new {source,player,frames=capture.frames.Length,transitions,stats,trace}));
        rig.ResetTracking();rig.Apply(null);rig.ApplyGolfIdle();
        return JsonConvert.SerializeObject(new{player,frames=capture.frames.Length,transitions,stats});
    }
}

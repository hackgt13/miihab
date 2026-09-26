using System;
using System.IO;
using System.Linq;
using Kinesthetic;
using Kinesthetic.Golf;
using UnityEngine;
using Newtonsoft.Json;

public static class AvatarContactVerification
{
    public static string Verify(string capturePath,int player)
    {
        var rig=UnityEngine.Object.FindAnyObjectByType<KinestheticGolf>().rigs[player];rig.Initialize();rig.ResetTracking();rig.Apply(null);
        var capture=PoseJson.Read<PoseCapture>(File.ReadAllText(capturePath));var original=JsonConvert.SerializeObject(capture);
        var all=rig.avatar.GetComponentsInChildren<Transform>();
        var feet=all.Where(t=>t.name=="foot.L"||t.name=="foot.R").ToArray();var anchors=feet.Select(t=>t.position).ToArray();
        var rotations=feet.Select(t=>t.rotation).ToArray();
        float drift=0,soleTilt=0,penetration=0,legPenetration=0,hipTravel=0;
        var firstHip=rig.Hip.position;
        int worstFrame=0;
        foreach(var frame in capture.frames) {
            rig.Apply(frame);rig.ApplyGolfIdle();
            if(rig.ArmPenetration>penetration){penetration=rig.ArmPenetration;worstFrame=frame.frameID;}
            legPenetration=Mathf.Max(legPenetration,rig.ArmLegPenetration);
            if(!rig.seated)for(int i=0;i<feet.Length;i++) {
                drift=Mathf.Max(drift,Vector3.Distance(anchors[i],feet[i].position));
                soleTilt=Mathf.Max(soleTilt,Quaternion.Angle(rotations[i],feet[i].rotation));
            }
            hipTravel=Mathf.Max(hipTravel,Vector3.Distance(firstHip,rig.Hip.position));
        }
        if(original!=JsonConvert.SerializeObject(capture))throw new Exception("Raw recording mutated");
        if(drift>.001f || soleTilt>.1f || penetration>.002f || legPenetration>.002f)throw new Exception($"Contact constraints failed: torso={penetration}, legs={legPenetration}, feet={drift}");
        rig.ResetTracking();rig.Apply(null);
        return JsonConvert.SerializeObject(new{player=player+1,frames=capture.frames.Length,maxFootAnchorDriftM=drift,maxSoleRotationDeg=soleTilt,maxArmTorsoPenetrationM=penetration,maxArmLegPenetrationM=legPenetration,worstFrame,hipTravelM=hipTravel,rawUnchanged=true});
    }
}

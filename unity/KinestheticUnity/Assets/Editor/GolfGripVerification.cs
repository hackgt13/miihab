using System;
using System.IO;
using Kinesthetic;
using Kinesthetic.Golf;
using UnityEngine;
using Newtonsoft.Json;
public static class GolfGripVerification
{
    public static string Verify(string path,int player)
    {
        var game=UnityEngine.Object.FindAnyObjectByType<KinestheticGolf>();
        var rig=game.rigs[player];rig.Initialize();rig.ResetTracking();rig.Apply(null);
        var club=game.clubs[player];var oldPosition=club.position;var oldRotation=club.rotation;var oldScale=club.localScale;
        var capture=PoseJson.Read<PoseCapture>(File.ReadAllText(path));string raw=JsonConvert.SerializeObject(capture);
        try {
            var view=new GolfClubPresentation(rig,club);
            var target=rig.Hip.position+rig.transform.TransformDirection(new Vector3(0,-.4f,-.85f));
            view.FitAtAddress(target);view.Present();
            float addressError=Vector3.Distance(view.Head,target),shaftLength=view.ScaledShaft.magnitude;
            float gripError=0,body=0,legs=0,scaleError=0;int disconnected=0;Vector3 initial=rig.Hip.position;
            foreach(var frame in capture.frames) {
                rig.Apply(frame);view.Present();
                if(!rig.GolfGripConnected)disconnected++;
                gripError=Mathf.Max(gripError,Mathf.Abs(Vector3.Distance(rig.LeftHand.position,rig.RightHand.position)-(rig.seated?.038f:.12f)));
                body=Mathf.Max(body,rig.ArmPenetration);legs=Mathf.Max(legs,rig.ArmLegPenetration);
                scaleError=Mathf.Max(scaleError,Mathf.Abs(view.ScaledShaft.magnitude-shaftLength));
            }
            var sensor=Quaternion.Euler(20,35,10);
            var strike=new VirtualClubStrike();strike.Calibrate(target,view.AddressRotation,Quaternion.identity,view.ScaledShaft,.12f);
            view.Present(strike.Rotation(sensor));
            float sensorHeadError=Vector3.Distance(view.Head,strike.Head(rig.GolfGripCenter,sensor));
            float sensorRotationError=Quaternion.Angle(club.rotation,strike.Rotation(sensor));
            var report=JsonConvert.SerializeObject(new{player,frames=capture.frames.Length,disconnected,addressError,gripError,body,legs,scaleError,sensorHeadError,sensorRotationError,rawUnchanged=raw==JsonConvert.SerializeObject(capture)});
            if(sensorHeadError>.0001f || sensorRotationError>.01f || disconnected>0 || addressError>.001f || gripError>.001f || body>.002f || legs>.002f || scaleError>.0001f || raw!=JsonConvert.SerializeObject(capture))throw new Exception(report);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path),$"p{player+1}-grip-verification.json"),report);return report;
        }finally {rig.ResetTracking();rig.Apply(null);club.SetPositionAndRotation(oldPosition,oldRotation);club.localScale=oldScale;}
    }
}

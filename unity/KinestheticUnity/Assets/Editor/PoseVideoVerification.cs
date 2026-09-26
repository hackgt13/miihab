using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Kinesthetic;
using Kinesthetic.Golf;
using UnityEngine;
using Newtonsoft.Json;

// Offline checks against the actual game rigs. Never saves the scene or modifies source data.
public static class PoseVideoVerification
{
    public static string Verify(string capturePath,int player)
    {
        var rig=UnityEngine.Object.FindAnyObjectByType<KinestheticGolf>().rigs[player];rig.Initialize();
        var capture=PoseJson.Read<PoseCapture>(File.ReadAllText(capturePath));
        string raw=JsonConvert.SerializeObject(capture);
        var bones=rig.avatar.GetComponentsInChildren<Transform>();
        var feet=bones.Where(t=>t.name=="foot.L" || t.name=="foot.R").ToArray();
        var lower=bones.Where(t=>new[]{"hip","thigh.L","thigh.R","calf.L","calf.R","foot.L","foot.R"}.Contains(t.name)).ToArray();
        rig.ResetTracking();rig.Apply(null);
        var positions=lower.Select(t=>t.position).ToArray();
        float baseline=feet.Min(t=>rig.transform.InverseTransformPoint(t.position).y);
        float legError=0,footError=0;int left=0,right=0;
        try
        {
            foreach(var frame in capture.frames)
            {
                rig.Apply(frame);rig.ApplyGolfIdle();
                if(rig.LeftArmTracked)left++;if(rig.RightArmTracked)right++;
                foreach(var bone in bones)
                    if(!PoseMath.Finite(bone.rotation.x) || !PoseMath.Finite(bone.position.x))throw new Exception("Non-finite bone: "+bone.name);
                if(rig.seated)for(int i=0;i<lower.Length;i++)legError=Mathf.Max(legError,Vector3.Distance(positions[i],lower[i].position));
                else footError=Mathf.Max(footError,Mathf.Abs(feet.Min(t=>rig.transform.InverseTransformPoint(t.position).y)-baseline));
            }
            if(legError>.0001f || footError>.0001f)throw new Exception("Seat/ground anchoring failed.");
            if(raw!=JsonConvert.SerializeObject(capture))throw new Exception("Source landmarks mutated.");
            var sample=capture.frames.First(f=>PoseMath.Visible(f,23)&&PoseMath.Visible(f,24)&&PoseMath.Visible(f,15));
            rig.CalibrateCamera(sample);rig.Apply(sample);
            var original=bones.Select(t=>t.localRotation).ToArray();
            var rotated=PoseJson.Read<PoseFrame>(JsonConvert.SerializeObject(sample));
            foreach(var p in rotated.worldLandmarks)
            {
                var v=Quaternion.Euler(0,90,0)*new Vector3(p.x,-p.y,p.z);p.x=v.x;p.y=-v.y;p.z=v.z;
            }
            rig.CalibrateCamera(rotated);rig.Apply(rotated);
            float yawError=0;
            for(int i=0;i<bones.Length;i++)yawError=Mathf.Max(yawError,Quaternion.Angle(original[i],bones[i].localRotation));
            if(yawError>.1f)throw new Exception("Camera yaw invariance failed: "+yawError);
            rig.Apply(null);if(rig.LeftArmTracked||rig.RightArmTracked)throw new Exception("Dropout retained tracked flags.");
            return JsonConvert.SerializeObject(new {player=player+1,frames=capture.frames.Length,leftArmFrames=left,rightArmFrames=right,rawUnchanged=true,seatedLegMaxErrorM=legError,standingFootMaxErrorM=footError,cameraYawMaxErrorDeg=yawError,dropoutClearsTracking=true});
        }
        finally{rig.ResetTracking();rig.Apply(null);rig.ApplyGolfIdle();}
    }
    public static string Render(string capturePath,int player,string directory,bool sequence=false,int startFrame=0,int frameCount=100000,bool withClub=false,float viewX=2.2f)
    {
        var game=UnityEngine.Object.FindAnyObjectByType<KinestheticGolf>();
        var rig=game.rigs[player];rig.Initialize();rig.ResetTracking();rig.Apply(null);
        var capture=PoseJson.Read<PoseCapture>(File.ReadAllText(capturePath));
        GameObject visualClub=null,visualBall=null;GolfClubPresentation clubView=null;
        if(withClub) {
            visualClub=UnityEngine.Object.Instantiate(game.clubs[player].gameObject,game.players[player]);
            visualClub.name="Replay club";
            clubView=new GolfClubPresentation(rig,visualClub.transform);
        }
        var transforms=game.players[player].GetComponentsInChildren<Transform>();
        var layers=transforms.Select(t=>t.gameObject.layer).ToArray();
        foreach(var t in transforms)t.gameObject.layer=31;
        var go=new GameObject("Temporary pose verification camera");var camera=go.AddComponent<Camera>();
        camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.82f,.86f,.9f);
        camera.orthographic=true;camera.orthographicSize=1.2f;
        var target=rig.Hip.position+Vector3.up*.36f;
        camera.transform.position=target+rig.transform.TransformDirection(new Vector3(viewX,1.0f,-3));camera.transform.LookAt(target);
        var rt=new RenderTexture(720,800,24);camera.targetTexture=rt;
        var texture=new Texture2D(720,800,TextureFormat.RGB24,false);
        // A visible contact plane makes grounded feet inspectable in offline renders.
        float floorY=float.PositiveInfinity;var groundMesh=new Mesh();
        foreach(var skin in rig.avatar.GetComponentsInChildren<SkinnedMeshRenderer>()) {
            skin.BakeMesh(groundMesh);
            foreach(var vertex in groundMesh.vertices)floorY=Mathf.Min(floorY,skin.transform.TransformPoint(vertex).y);
        }
        foreach(var t in transforms) {
            if(visualClub && t.IsChildOf(visualClub.transform))continue;
            var meshFilter=t.GetComponent<MeshFilter>();
            if(!meshFilter || !meshFilter.sharedMesh)continue;
            foreach(var vertex in meshFilter.sharedMesh.vertices)floorY=Mathf.Min(floorY,t.TransformPoint(vertex).y);
        }
        UnityEngine.Object.DestroyImmediate(groundMesh);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="Replay contact floor";floor.layer=31;
        floor.transform.position=new Vector3(rig.Hip.position.x,floorY-.003f,rig.Hip.position.z);floor.transform.localScale=Vector3.one*.45f;
        var floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMaterial.SetColor("_BaseColor",new Color(.68f,.73f,.77f));
        floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
        if(withClub) {
            var ballPosition=new Vector3(rig.Hip.position.x,floorY+.055f,rig.Hip.position.z)+rig.transform.TransformDirection(Vector3.back)*.85f;
            visualBall=GameObject.CreatePrimitive(PrimitiveType.Sphere);visualBall.name="Replay ball";visualBall.layer=31;
            visualBall.transform.position=ballPosition;visualBall.transform.localScale=Vector3.one*.11f;
            rig.Apply(capture.frames[0]);clubView.FitAtAddress(ballPosition-rig.transform.right*.09f);
        }
        var previousRT=RenderTexture.active;
        Directory.CreateDirectory(directory);
        try
        {
            int sequenceFrames=Mathf.FloorToInt((float)(capture.frames.Last().sourceMediaTimeMs/1000)*15)+1;
            var seconds=sequence?Enumerable.Range(0,sequenceFrames).Select(i=>i/15f).ToArray():new[]{2f,5f,8f,11f,14f};
            rig.ResetTracking();rig.Apply(null);int next=0,index=startFrame;
            foreach(float second in seconds.Skip(startFrame).Take(frameCount))
            {
                while(next<capture.frames.Length && capture.frames[next].sourceMediaTimeMs<=second*1000)rig.Apply(capture.frames[next++]);
                rig.ApplyGolfIdle();clubView?.Present();
                // Bake explicitly: synchronous editor rendering may otherwise show stale skinning matrices.
                var skins=rig.avatar.GetComponentsInChildren<SkinnedMeshRenderer>();
                var enabled=skins.Select(s=>s.enabled).ToArray();var baked=new List<GameObject>();
                var staticParts=transforms.Select(t=>t.GetComponent<MeshRenderer>()).Where(r=>r && r.enabled && r.gameObject.activeInHierarchy && r.GetComponent<MeshFilter>()).ToArray();
                var copies=new List<GameObject>();
                try
                {
                    // The resident drawer caches static layers. Duplicate accessories
                    // as temporary dynamic renderers so the isolated camera sees them.
                    foreach(var source in staticParts)
                    {
                        var part=new GameObject("Pose verification accessory");part.layer=31;copies.Add(part);
                        part.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);part.transform.localScale=source.transform.lossyScale;
                        part.AddComponent<MeshFilter>().sharedMesh=source.GetComponent<MeshFilter>().sharedMesh;
                        part.AddComponent<MeshRenderer>().sharedMaterials=source.sharedMaterials;source.enabled=false;
                    }
                    foreach(var skin in skins.Where(s=>s.enabled))
                    {
                        var mesh=new Mesh();skin.BakeMesh(mesh);
                        var part=new GameObject("Pose verification baked mesh");part.layer=31;baked.Add(part);
                        part.transform.SetPositionAndRotation(skin.transform.position,skin.transform.rotation);part.transform.localScale=skin.transform.lossyScale;
                        part.AddComponent<MeshFilter>().sharedMesh=mesh;part.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;skin.enabled=false;
                    }
                    camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,720,800),0,0);texture.Apply();
                    string name=sequence?$"frame-{index++:0000}.png":$"p{player+1}-{second:00}.png";
                    File.WriteAllBytes(Path.Combine(directory,name),texture.EncodeToPNG());
                }
                finally
                {
                    for(int i=0;i<skins.Length;i++)skins[i].enabled=enabled[i];
                    foreach(var source in staticParts)source.enabled=true;
                    foreach(var part in copies)UnityEngine.Object.DestroyImmediate(part);
                    foreach(var part in baked){UnityEngine.Object.DestroyImmediate(part.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(part);}
                }
            }
            return "Rendered "+seconds.Length+" frames from actual "+(rig.seated?"seated":"standing")+" game rig.";
        }
        finally
        {
            rig.ResetTracking();rig.Apply(null);rig.ApplyGolfIdle();
            for(int i=0;i<transforms.Length;i++)transforms[i].gameObject.layer=layers[i];
            camera.targetTexture=null;RenderTexture.active=previousRT;
            if(visualClub)UnityEngine.Object.DestroyImmediate(visualClub);if(visualBall)UnityEngine.Object.DestroyImmediate(visualBall);
            UnityEngine.Object.DestroyImmediate(floor);UnityEngine.Object.DestroyImmediate(floorMaterial);
            UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(go);
        }
    }
}

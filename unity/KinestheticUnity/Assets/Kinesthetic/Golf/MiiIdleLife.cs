using System.Collections.Generic;
using UnityEngine;

namespace Kinesthetic.Golf
{
    // Authored presentation animation; it never supplies motion measurements.
    public sealed class MiiIdleLife : MonoBehaviour
    {
        PoseRig rig;
        Transform chest,neck;
        Vector3 chestScale;
        Quaternion neckRotation;
        readonly List<SkinnedMeshRenderer> eyes=new();
        readonly List<Mesh> eyeMeshes=new();
        readonly List<int> blinkIndices=new();
        float nextBlink,blinkStart=-10,phase;
        public float BlinkWeight {get;private set;}
        public int EyeCount=>eyes.Count;
        void Start()
        {
            rig=GetComponent<PoseRig>();phase=rig.seated?0:2.1f;
            foreach(var t in rig.avatar.GetComponentsInChildren<Transform>())
            {if(t.name=="spine.002")chest=t;if(t.name=="neck")neck=t;}
            if(chest)chestScale=chest.localScale;
            if(neck)neckRotation=neck.localRotation;
            foreach(var renderer in rig.avatar.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if(renderer.name!="Mii_eye.L" && renderer.name!="Mii_eye.R")continue;
                var mesh=Instantiate(renderer.sharedMesh);mesh.name+=" Idle blink";
                var positions=mesh.vertices;var offsets=new Vector3[positions.Length];
                float center=mesh.bounds.center.y;
                for(int i=0;i<positions.Length;i++)offsets[i]=new Vector3(0,(center-positions[i].y)*.96f,0);
                int index=mesh.blendShapeCount;
                mesh.AddBlendShapeFrame("IdleBlink",100,offsets,null,null);
                renderer.sharedMesh=mesh;eyes.Add(renderer);eyeMeshes.Add(mesh);blinkIndices.Add(index);
            }
            nextBlink=Time.time+2+phase;
        }
        void LateUpdate()
        {
            if(Time.time>=nextBlink){blinkStart=Time.time;nextBlink=Time.time+Random.Range(3.2f,5.8f);}
            float t=(Time.time-blinkStart)/.18f;
            BlinkWeight=t>=0 && t<=1?Mathf.Sin(t*Mathf.PI)*100:0;
            for(int i=0;i<eyes.Count;i++)eyes[i].SetBlendShapeWeight(blinkIndices[i],BlinkWeight);
            float breath=(!rig.RightArmTracked && !rig.LeftArmTracked)?Mathf.Sin(Time.time*1.55f+phase):0;
            if(chest)chest.localScale=Vector3.Scale(chestScale,new Vector3(1+.006f*breath,1+.014f*breath,1+.008f*breath));
            if(neck)neck.localRotation=neckRotation*Quaternion.Euler(.45f*breath,0,0);
        }
        void OnDestroy(){foreach(var mesh in eyeMeshes)if(mesh)Destroy(mesh);}
    }
}

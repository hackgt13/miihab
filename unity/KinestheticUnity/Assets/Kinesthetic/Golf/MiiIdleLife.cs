using UnityEngine;

namespace Kinesthetic.Golf
{
    // Authored presentation animation; it never supplies motion measurements.
    // Eyes are simple drawn shapes: the patient has O O (blinks ⌒ ⌒), the friend ^ ^ (blinks — —).
    // Mouths: patient ᴗ, friend ⌣; both become o during effort (arm held high, fast hands) or surprise.
    public sealed class MiiIdleLife : MonoBehaviour
    {
        public enum EyeStyle { Auto, Round, Happy }
        public EyeStyle eyeStyle = EyeStyle.Auto;     // Auto: seated patient → Round, standing friend → Happy
        PoseRig rig;
        Transform chest,neck;
        Vector3 chestScale;
        Quaternion neckRotation;
        Material[] eyeMaterials=System.Array.Empty<Material>();
        Texture2D open,closed,smile,oh;
        Material[] mouthMaterials=System.Array.Empty<Material>();
        Transform shoulderL,shoulderR,elbowL,elbowR;
        Vector3 lastHandL,lastHandR;float ohUntil=-1,lastT=-1;
        public bool MouthOpen {get;private set;}
        /// Astonishment (a launched ball, a hole-in): open mouth for a moment.
        public void Surprise(float seconds=1.2f){ohUntil=Mathf.Max(ohUntil,Time.time+seconds);}
        float nextBlink,blinkStart=-10,phase;
        public float BlinkWeight {get;private set;}
        public int EyeCount=>eyeMaterials.Length;
        void Start()
        {
            rig=GetComponent<PoseRig>();phase=rig.seated?0:2.1f;
            foreach(var t in rig.avatar.GetComponentsInChildren<Transform>())
            {if(t.name=="spine.002")chest=t;if(t.name=="neck")neck=t;}
            if(chest)chestScale=chest.localScale;
            if(neck)neckRotation=neck.localRotation;
            var style=eyeStyle==EyeStyle.Auto?(rig.seated?EyeStyle.Round:EyeStyle.Happy):eyeStyle;
            string prefix=style==EyeStyle.Round?"patient":"friend";
            open=Resources.Load<Texture2D>("MiiEyes/"+prefix+"_open");closed=Resources.Load<Texture2D>("MiiEyes/"+prefix+"_blink");
            smile=Resources.Load<Texture2D>("MiiEyes/"+prefix+"_mouth");oh=Resources.Load<Texture2D>("MiiEyes/"+prefix+"_mouth_o");
            var mouths=new System.Collections.Generic.List<Material>();
            foreach(var renderer in rig.avatar.GetComponentsInChildren<SkinnedMeshRenderer>())if(renderer.name=="Mii_mouth")mouths.Add(renderer.material);
            mouthMaterials=mouths.ToArray();
            foreach(var t in rig.avatar.GetComponentsInChildren<Transform>())
            {if(t.name=="bicep.L")shoulderL=t;if(t.name=="bicep.R")shoulderR=t;if(t.name=="forearm.L")elbowL=t;if(t.name=="forearm.R")elbowR=t;}
            var list=new System.Collections.Generic.List<Material>();
            foreach(var renderer in rig.avatar.GetComponentsInChildren<SkinnedMeshRenderer>())
                if(renderer.name=="Mii_eye.L" || renderer.name=="Mii_eye.R")list.Add(renderer.material);
            eyeMaterials=list.ToArray();
            SetEyes(open);SetTexture(mouthMaterials,smile);
            nextBlink=Time.time+2+phase;
        }
        static void SetTexture(Material[] materials,Texture2D texture)
        {
            if(!texture)return;
            foreach(var m in materials){if(m.HasProperty("baseColorTexture"))m.SetTexture("baseColorTexture",texture);if(m.HasProperty("_BaseMap"))m.SetTexture("_BaseMap",texture);}
        }
        // Effort: a tracked arm held well away from the body (e.g. near the top of a raise) or hands moving fast (a swing).
        bool Effort()
        {
            float dt=Time.time-lastT;bool effort=false;
            var down=-(rig.avatar.up);
            foreach(var (tracked,shoulder,elbow) in new[]{(rig.LeftArmTracked,shoulderL,elbowL),(rig.RightArmTracked,shoulderR,elbowR)})
                if(tracked && shoulder && elbow && Vector3.Angle(elbow.position-shoulder.position,Vector3.down)>62)effort=true;
            if(lastT>0 && dt>0 && dt<.2f)
            {
                float speed=Mathf.Max((rig.LeftHand.position-lastHandL).magnitude,(rig.RightHand.position-lastHandR).magnitude)/dt;
                if((rig.LeftArmTracked||rig.RightArmTracked) && speed>1.6f)effort=true;
            }
            lastHandL=rig.LeftHand.position;lastHandR=rig.RightHand.position;lastT=Time.time;
            return effort;
        }
        void SetEyes(Texture2D texture)
        {
            if(!texture)return;
            foreach(var m in eyeMaterials){if(m.HasProperty("baseColorTexture"))m.SetTexture("baseColorTexture",texture);if(m.HasProperty("_BaseMap"))m.SetTexture("_BaseMap",texture);}
        }
        void LateUpdate()
        {
            if(Time.time>=nextBlink){blinkStart=Time.time;nextBlink=Time.time+Random.Range(3.2f,5.8f);}
            bool blinking=Time.time-blinkStart<.16f;
            BlinkWeight=blinking?100:0;
            SetEyes(blinking?closed:open);
            if(Effort())ohUntil=Mathf.Max(ohUntil,Time.time+.35f);   // hold briefly so it never flickers
            MouthOpen=Time.time<ohUntil;
            SetTexture(mouthMaterials,MouthOpen?oh:smile);
            float breath=(!rig.RightArmTracked && !rig.LeftArmTracked)?Mathf.Sin(Time.time*1.55f+phase):0;
            if(chest)chest.localScale=Vector3.Scale(chestScale,new Vector3(1+.006f*breath,1+.014f*breath,1+.008f*breath));
            if(neck)neck.localRotation=neckRotation*Quaternion.Euler(.45f*breath,0,0);
        }
        void OnDestroy(){foreach(var m in eyeMaterials)if(m)Destroy(m);foreach(var m in mouthMaterials)if(m)Destroy(m);}
    }
}

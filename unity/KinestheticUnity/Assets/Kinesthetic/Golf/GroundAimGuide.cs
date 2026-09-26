using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kinesthetic.Golf
{
    public sealed class GroundAimGuide : MonoBehaviour
    {
        Mesh mesh;
        Material material;
        MeshRenderer meshRenderer;
        readonly List<Vector3> vertices=new();
        readonly List<int> triangles=new();
        Vector3 previousBall, previousAim;
        bool dirty=true;
        void Awake()
        {
            mesh=new Mesh{name="Terrain-following aim dashes"};mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            meshRenderer=gameObject.AddComponent<MeshRenderer>();
            material=new Material(Shader.Find("Universal Render Pipeline/Unlit")){name="Translucent aim guidance",renderQueue=3000};
            material.SetColor("_BaseColor",new Color(1,.94f,.61f,.48f));
            material.SetFloat("_Surface",1);material.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);material.SetFloat("_ZWrite",0);
            material.SetFloat("_Cull",0);material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            meshRenderer.sharedMaterial=material;meshRenderer.shadowCastingMode=ShadowCastingMode.Off;meshRenderer.receiveShadows=false;
        }
        public void Show(Vector3 ball,Vector3 aim,bool visible)
        {
            meshRenderer.enabled=visible;
            if(!visible){dirty=true;return;}
            if(!dirty && (ball-previousBall).sqrMagnitude<.00001f && (aim-previousAim).sqrMagnitude<.00001f)return;
            dirty=false;previousBall=ball;previousAim=aim;vertices.Clear();triangles.Clear();
            var side=Vector3.Cross(Vector3.up,aim).normalized;
            for(float d=.3f;d<12;d+=.9f)
            {
                var a=ball+aim*d;var b=ball+aim*(d+.48f);
                Quad(a-side*.055f,a+side*.055f,b+side*.055f,b-side*.055f);
            }
            // Direction arrow, deliberately not a simulated landing location.
            Triangle(ball+aim*13,ball+aim*11.8f+side*.48f,ball+aim*11.8f-side*.48f);
            dirty=vertices.Count==0;
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
        }
        bool Ground(Vector3 p,out Vector3 point)
        {
            // Imported course has downward-wound faces in this region. Scope
            // backface hits to this visual query; leave physics settings intact.
            bool previous=Physics.queriesHitBackfaces;
            try
            {
                Physics.queriesHitBackfaces=true;
                if(Physics.Raycast(p+Vector3.up*60,Vector3.down,out var hit,200,1<<0,QueryTriggerInteraction.Ignore))
                {point=hit.point+Vector3.up*.025f;return true;}
                point=default;return false;
            }
            finally{Physics.queriesHitBackfaces=previous;}
        }
        void Triangle(Vector3 a,Vector3 b,Vector3 c)
        {
            if(!Ground(a,out a)||!Ground(b,out b)||!Ground(c,out c))return;
            int n=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);
            triangles.Add(n);triangles.Add(n+1);triangles.Add(n+2);
        }
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d){Triangle(a,b,c);Triangle(a,c,d);}
        void OnDestroy(){if(mesh)Destroy(mesh);if(material)Destroy(material);}
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic.Golf
{
    // A separate, render-only overhead view of the course. Markers use the same
    // projection as its camera, so shot motion and the map cannot drift apart.
    public sealed class GolfHud : IDisposable
    {
        readonly KinestheticGolf game;
        readonly VisualElement root, map, meter;
        readonly Label stroke, lie, power;
        readonly List<Material> materials = new();
        readonly Vector3 offset = new(10000,0,10000);
        GameObject mapWorld;
        Camera mapCamera;
        RenderTexture mapTexture;
        public GolfHud(VisualElement root, KinestheticGolf game)
        {
            this.root=root; this.game=game;
            map=root.Q("course-map"); meter=root.Q("power-meter");
            stroke=root.Q<Label>("stroke");lie=root.Q<Label>("lie");power=root.Q<Label>("power-value");
            var driverArtwork=root.Q<Image>("driver-artwork");
            driverArtwork.image=Resources.Load<Texture2D>("GolfHUD/Driver");
            driverArtwork.scaleMode=ScaleMode.ScaleToFit;
            BuildMap();
            meter.generateVisualContent+=DrawPower;
        }
        void BuildMap()
        {
            var course=GameObject.Find("Purchased golf course");
            if(!course)return;
            mapWorld=new GameObject("Golf HUD map (render only)");
            var cameraObject=new GameObject("Course map camera");cameraObject.transform.SetParent(mapWorld.transform);
            mapCamera=cameraObject.AddComponent<Camera>();
            mapCamera.orthographic=true;mapCamera.clearFlags=CameraClearFlags.SolidColor;
            mapCamera.backgroundColor=new Color(.12f,.37f,.22f);mapCamera.cullingMask=1<<31;
            mapCamera.nearClipPlane=.1f;mapCamera.farClipPlane=600;
            mapCamera.allowHDR=false;mapCamera.allowMSAA=false;mapCamera.depth=-20;
            Vector3 forward=game.cup.position-game.tee.position;forward.y=0;forward.Normalize();
            Vector3 right=Vector3.Cross(forward,Vector3.up).normalized;
            // LookRotation's local right is cross(up,forward): with downward
            // camera forward and course-forward up, it equals this map right.
            float minX=float.MaxValue,maxX=float.MinValue,minZ=float.MaxValue,maxZ=float.MinValue;
            foreach(var mesh in course.GetComponentsInChildren<MeshFilter>())
            {
                if(!mesh.name.StartsWith("Export_Course"))continue;
                var clone=new GameObject(mesh.name);clone.layer=31;clone.transform.SetParent(mapWorld.transform);
                clone.transform.SetPositionAndRotation(mesh.transform.position+offset,mesh.transform.rotation);
                clone.transform.localScale=mesh.transform.lossyScale;
                clone.AddComponent<MeshFilter>().sharedMesh=mesh.sharedMesh;
                var renderer=clone.AddComponent<MeshRenderer>();
                var mat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                Color color=mesh.name.Contains("004")?new Color(.91f,.85f,.57f):mesh.name.Contains("Rough")?new Color(.22f,.49f,.27f):new Color(.53f,.78f,.31f);
                mat.SetColor("_BaseColor",color);materials.Add(mat);
                var source=mesh.GetComponent<MeshRenderer>();
                var slots=new Material[Math.Max(1,source.sharedMaterials.Length)];Array.Fill(slots,mat);renderer.sharedMaterials=slots;
                // Fairway/bunkers determine the crop; surrounding rough can be huge.
                if(mesh.name.Contains("Rough"))continue;
                foreach(var v in mesh.sharedMesh.vertices)
                {
                    var world=mesh.transform.TransformPoint(v);
                    float x=Vector3.Dot(world,right),z=Vector3.Dot(world,forward);
                    minX=Mathf.Min(minX,x);maxX=Mathf.Max(maxX,x);minZ=Mathf.Min(minZ,z);maxZ=Mathf.Max(maxZ,z);
                }
            }
            if(minX==float.MaxValue){minX=-70;maxX=70;minZ=-70;maxZ=70;}
            float aspect=204f/333f;
            mapCamera.orthographicSize=Mathf.Max((maxZ-minZ)*.5f+5,((maxX-minX)*.5f+5)/aspect);
            var center=right*((minX+maxX)*.5f)+forward*((minZ+maxZ)*.5f);
            mapCamera.transform.SetPositionAndRotation(center+offset+Vector3.up*250,Quaternion.LookRotation(Vector3.down,forward));
            mapTexture=new RenderTexture(408,666,16,RenderTextureFormat.ARGB32){name="Live course minimap"};
            mapTexture.Create();mapCamera.targetTexture=mapTexture;
            var background=new Image{image=mapTexture,scaleMode=ScaleMode.StretchToFill,pickingMode=PickingMode.Ignore};
            background.style.position=Position.Absolute;background.style.left=0;background.style.top=0;background.style.right=0;background.style.bottom=0;
            map.Add(background);
            // Overlay must be a child after the image, otherwise its geometry
            // would render behind the image in UI Toolkit's draw order.
            var overlay=new VisualElement{pickingMode=PickingMode.Ignore};
            overlay.style.position=Position.Absolute;overlay.style.left=0;overlay.style.top=0;overlay.style.right=0;overlay.style.bottom=0;
            overlay.generateVisualContent+=DrawMap;map.Add(overlay);
        }
        Vector2 Project(Vector3 position)
        {
            var p=mapCamera.WorldToViewportPoint(position+offset);
            return new Vector2(p.x*map.contentRect.width,(1-p.y)*map.contentRect.height);
        }
        static void Line(Painter2D p,Vector2 a,Vector2 b,Color color,float width)
        {p.strokeColor=color;p.lineWidth=width;p.BeginPath();p.MoveTo(a);p.LineTo(b);p.Stroke();}
        static void Rect(Painter2D p,float x,float y,float w,float h,Color color)
        {p.fillColor=color;p.BeginPath();p.MoveTo(new(x,y));p.LineTo(new(x+w,y));p.LineTo(new(x+w,y+h));p.LineTo(new(x,y+h));p.ClosePath();p.Fill();}
        static void Dot(Painter2D p,Vector2 at,float radius,Color color)
        {p.fillColor=color;p.BeginPath();p.Arc(at,radius,Angle.Degrees(0),Angle.Degrees(360));p.ClosePath();p.Fill();}
        void DrawMap(MeshGenerationContext ctx)
        {
            if(!mapCamera || map.contentRect.width<1)return;
            var p=ctx.painter2D;
            var b=Project(game.ball.position); var flag=Project(game.cup.position);
            if(game.Phase=="Address")
            {
                var target=Project(game.ball.position+game.HudAim*35);
                for(int i=0;i<8;i++)Line(p,Vector2.Lerp(b,target,i/8f),Vector2.Lerp(b,target,(i+.5f)/8f),new Color(1,1,1,.8f),1.5f);
            }
            int other=1-game.activePlayer;
            var friend=Project(game.GetLie(other));
            Dot(p,friend,5,new Color(.07f,.15f,.23f));Dot(p,friend,3,other==0?Color.white:new Color(1,.78f,.28f));
            Line(p,flag+new Vector2(0,1),flag+new Vector2(0,-19),Color.white,2);
            p.fillColor=new Color(1,.23f,.22f);p.BeginPath();p.MoveTo(flag+new Vector2(0,-20));p.LineTo(flag+new Vector2(14,-15));p.LineTo(flag+new Vector2(0,-10));p.ClosePath();p.Fill();
            Dot(p,b,7,new Color(.08f,.23f,.34f));Dot(p,b,4.5f,game.activePlayer==0?Color.white:new Color(1,.78f,.28f));
        }
        void DrawPower(MeshGenerationContext ctx)
        {
            var p=ctx.painter2D;float h=meter.contentRect.height-8;
            Rect(p,17,2,24,h+4,new Color(.23f,.25f,.13f));Rect(p,19,4,20,h,new Color(1,.87f,.36f));
            Rect(p,22,7,14,h-6,new Color(.13f,.22f,.20f));
            float fill=(h-6)*game.HudPower;
            Rect(p,22,h+1-fill,14,fill,game.HudPower>.9f?new Color(1,.27f,.20f):new Color(.22f,.71f,1));
            for(int i=0;i<=4;i++){float y=7+(h-6)*i/4;Line(p,new(13,y),new(20,y),Color.white,2);Line(p,new(39,y),new(44,y),Color.white,2);}
            var yPower=h+1-fill;Line(p,new(6,yPower),new(16,yPower),new Color(1,.95f,.44f),4);
        }
        public void Update()
        {
            stroke.text=game.Phase=="Holed"?"Holed out!":game.Phase=="Round complete"?"Round complete":"Stroke "+(game.Strokes[game.activePlayer]+(game.Phase=="Address"?1:0));
            lie.text=game.Phase=="Flight"?"Ball in flight":Surface();
            power.text=Mathf.RoundToInt(game.HudPower*100)+"%";
            meter.MarkDirtyRepaint();map.MarkDirtyRepaint();
            foreach(var child in map.Children())child.MarkDirtyRepaint();
        }
        string Surface()
        {
            if(Vector3.Distance(game.ball.position,game.tee.position)<1)return "Tee";
            if(Physics.Raycast(game.ball.position+Vector3.up*2,Vector3.down,out var hit,10,1<<0))
            {if(hit.collider.name.Contains("004"))return "Bunker";if(hit.collider.name.Contains("Rough"))return "Rough";return "Fairway";}
            return "To the pin";
        }
        public void Dispose()
        {
            if(mapCamera)mapCamera.targetTexture=null;
            if(mapTexture){mapTexture.Release();UnityEngine.Object.Destroy(mapTexture);}
            if(mapWorld)UnityEngine.Object.Destroy(mapWorld);
            foreach(var material in materials)UnityEngine.Object.Destroy(material);
        }
    }
}

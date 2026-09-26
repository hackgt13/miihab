using System;
using System.Linq;
using Kinesthetic;
using Kinesthetic.Golf;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class AdaptiveGolfSceneSetup
{
    const string Root="Assets/Kinesthetic/Golf";
    public const string ScenePath=Root+"/AdaptiveGolf.unity";
    [MenuItem("Kinesthetic/Golf/Create adaptive course scene")]
    public static string Create()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode before assembling the course.");
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Kinesthetic/Art/PurchasedGolf/PurchasedCourse.glb");
        if(!source)throw new InvalidOperationException("Purchased course is not imported yet.");
        // Save any open work before changing scenes.
        foreach(var scene in Enumerable.Range(0,UnityEngine.SceneManagement.SceneManager.sceneCount).Select(UnityEngine.SceneManagement.SceneManager.GetSceneAt))
            if(scene.isDirty && !string.IsNullOrEmpty(scene.path))EditorSceneManager.SaveScene(scene);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var course=(GameObject)PrefabUtility.InstantiatePrefab(source);
        course.name="Purchased golf course";course.transform.localScale=Vector3.one*.28f;
        foreach(var filter in course.GetComponentsInChildren<MeshFilter>())
        {
            if(filter.name.StartsWith("Export_Course"))
            {
                var col=filter.gameObject.AddComponent<MeshCollider>();col.sharedMesh=filter.sharedMesh;
                col.sharedMaterial=new PhysicsMaterial("Course contact") {dynamicFriction=.45f,staticFriction=.5f,bounciness=.12f};
            }
        }
        foreach(var t in course.GetComponentsInChildren<Transform>())
            if(t.name.StartsWith("Export_Tee Marker"))t.gameObject.SetActive(false);
        Physics.SyncTransforms();
        Transform Find(string name)=>course.GetComponentsInChildren<Transform>().First(t=>t.name==name);
        var tee=new GameObject("Physical mat / virtual address").transform;
        tee.position=KinestheticGolf.FloorPoint(Find("TeeAnchor").position)+Vector3.up*.06f;
        var cup=new GameObject("Cup capture center").transform;
        cup.position=KinestheticGolf.FloorPoint(Find("CupAnchor").position)+Vector3.up*.04f;
        var camera=new GameObject("Golf spectator").AddComponent<Camera>();camera.tag="MainCamera";
        camera.transform.position=Find("SourceCameraAnchor").position;
        var direction=(cup.position-tee.position).normalized;
        camera.transform.LookAt(tee.position+direction*8+Vector3.up);
        camera.fieldOfView=55;camera.nearClipPlane=.05f;camera.farClipPlane=600;
        camera.clearFlags=CameraClearFlags.Skybox;camera.gameObject.AddComponent<AudioListener>();
        var sky=new Material(Shader.Find("Skybox/Procedural"));sky.SetColor("_SkyTint",new Color(.48f,.66f,.85f));
        sky.SetFloat("_AtmosphereThickness",.6f);sky.SetFloat("_Exposure",1.1f);
        AssetDatabase.CreateAsset(sky,UniqueAsset("CourseSky.mat"));RenderSettings.skybox=sky;
        var sun=new GameObject("Course sun").AddComponent<Light>();sun.type=LightType.Directional;
        sun.intensity=1.3f;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(48,-32,0);
        RenderSettings.sun=sun;RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.67f,.72f,.78f);
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=new Color(.64f,.8f,.9f);
        RenderSettings.fogStartDistance=180;RenderSettings.fogEndDistance=440;
        var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);go.name="Authoritative golf ball";
        go.layer=2;go.transform.position=tee.position;go.transform.localScale=Vector3.one*.11f;
        go.GetComponent<Renderer>().sharedMaterial=MakeMaterial("GolfBall",Color.white);
        go.GetComponent<SphereCollider>().sharedMaterial=new PhysicsMaterial("Ball contact") {bounciness=.2f,dynamicFriction=.35f,staticFriction=.4f};
        var ball=go.AddComponent<Rigidbody>();ball.mass=.0459f;ball.isKinematic=true;
        ball.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;ball.interpolation=RigidbodyInterpolation.Interpolate;
        ball.linearDamping=.025f;ball.angularDamping=.1f;
        var game=new GameObject("Kinesthetic adaptive golf").AddComponent<KinestheticGolf>();
        game.ball=ball;game.tee=tee;game.cup=cup;game.spectator=camera;
        game.players=new Transform[2];game.rigs=new PoseRig[2];game.clubs=new Transform[2];
        for(int i=0;i<2;i++)
        {
            var slot=new GameObject(i==0?"Patient and wheelchair":"Standing friend").transform;
            var actor=new GameObject("Body pose").transform;actor.SetParent(slot,false);
            string path=i==0?"Assets/Kinesthetic/Art/Mii/KinestheticMii.glb":"Assets/Kinesthetic/Art/Mii/Friend/StandingFriendMii.glb";
            var avatar=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),actor);
            avatar.transform.localRotation=Quaternion.Euler(0,180,0);
            var head=avatar.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name=="head");if(head)head.localScale*=.78f;
            foreach(var a in avatar.GetComponentsInChildren<Animation>()){a.playAutomatically=false;a.enabled=false;}
            foreach(var a in avatar.GetComponentsInChildren<Animator>())a.enabled=false;
            var renders=avatar.GetComponentsInChildren<Renderer>();var bounds=renders[0].bounds;
            foreach(var r in renders)bounds.Encapsulate(r.bounds);
            avatar.transform.localScale*=1.75f/bounds.size.y;
            foreach(var r in avatar.GetComponentsInChildren<SkinnedMeshRenderer>())r.updateWhenOffscreen=true;
            var rig=actor.gameObject.AddComponent<PoseRig>();rig.avatar=avatar.transform;rig.seated=i==0;rig.usePresentationSpace=true;rig.golfGrip=true;
            rig.Initialize();rig.Apply(null);
            actor.position+=Vector3.up*((i==0?.12f:.08f)-rig.RightAnkle.position.y);
            if(i==0)
            {
                var chair=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Kinesthetic/Art/BlenderProps/MiiWheelchair.glb"),slot);
                chair.transform.localRotation=Quaternion.Euler(0,180,0);
            }
            var club=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Kinesthetic/Art/WiiSportsResort/ResortDriver.prefab"));
            club.name=i==0?"Patient tracked club":"Friend tracked club";
            game.players[i]=slot;game.rigs[i]=rig;game.clubs[i]=club.transform;
        }
        var arrow=new GameObject("Aim direction").AddComponent<LineRenderer>();arrow.positionCount=2;
        arrow.startWidth=.04f;arrow.endWidth=.015f;arrow.sharedMaterial=MakeMaterial("Aim",new Color(.98f,.94f,.58f));game.aimLine=arrow;arrow.enabled=false;
        var panel=AssetDatabase.LoadAssetAtPath<PanelSettings>(Root+"/GolfPanel.asset");
        if(!panel){panel=ScriptableObject.CreateInstance<PanelSettings>();AssetDatabase.CreateAsset(panel,Root+"/GolfPanel.asset");}
        panel.scaleMode=PanelScaleMode.ScaleWithScreenSize;panel.referenceResolution=new Vector2Int(1400,900);
        var doc=game.gameObject.AddComponent<UIDocument>();doc.panelSettings=panel;
        doc.visualTreeAsset=AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Root+"/Golf.uxml");
        for(int i=0;i<2;i++)
        {
            game.players[i].position=KinestheticGolf.FloorPoint(tee.position+Vector3.Cross(Vector3.up,direction)*(i==0?-1.05f:2.4f));
            game.players[i].rotation=Quaternion.LookRotation(-Vector3.Cross(Vector3.up,direction));
            game.rigs[i].Apply(null);
            game.clubs[i].position=game.rigs[i].RightHand.position;
        }
        game.allowDeveloperShots=false;
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),ScenePath);
        var builds=EditorBuildSettings.scenes.ToList();if(!builds.Any(s=>s.path==ScenePath))builds.Add(new EditorBuildSettingsScene(ScenePath,true));
        EditorBuildSettings.scenes=builds.ToArray();AssetDatabase.SaveAssets();
        return $"Purchased course ready. Tee={tee.position}, cup={cup.position}, distance={Vector3.Distance(tee.position,cup.position):0.0}m. MotionProof preserved.";
    }
    static string UniqueAsset(string name)
    {var path=Root+"/"+name;var old=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);if(old)AssetDatabase.DeleteAsset(path);return path;}
    static Material MakeMaterial(string name,Color color)
    {
        var path=Root+"/Adaptive-"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
        m.color=color;m.SetFloat("_Smoothness",.1f);return m;
    }
}

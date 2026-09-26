using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Kinesthetic.Golf;

public static class ResortClubSetup
{
    const string Folder="Assets/Kinesthetic/Art/WiiSportsResort";
    static Mesh MeshOf(Renderer r)=>r is SkinnedMeshRenderer skinned?skinned.sharedMesh:r.GetComponent<MeshFilter>().sharedMesh;
    public static string Install()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop play before installing the club.");
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/WS2_glf_club_WD1_R.dae");
        var unfinished=GameObject.Find("Wii Sports Resort driver");if(unfinished)UnityEngine.Object.DestroyImmediate(unfinished);
        var root=new GameObject("Wii Sports Resort driver");
        var model=(GameObject)PrefabUtility.InstantiatePrefab(source,root.transform);
        var headMesh=model.GetComponentsInChildren<Renderer>().First(f=>f.name=="polygon0");
        var gripMesh=model.GetComponentsInChildren<Renderer>().First(f=>f.name=="polygon1");
        var grip=gripMesh.transform.TransformPoint(MeshOf(gripMesh).bounds.center);
        var headPoints=MeshOf(headMesh).vertices.Select(v=>headMesh.transform.TransformPoint(v)).ToArray();
        float top=headPoints.Max(v=>v.y);
        var head=Vector3.zero;int count=0;
        foreach(var point in headPoints)if(point.y>top-1.8f){head+=point;count++;}
        head/=count;
        float scale=.9f/Vector3.Distance(grip,head);
        model.transform.localRotation=Quaternion.Euler(0,0,180);
        model.transform.localScale=Vector3.one*scale;
        model.transform.localPosition=-(model.transform.localRotation*grip*scale);
        var gripAnchor=new GameObject("GripAnchor").transform;gripAnchor.SetParent(root.transform,false);
        var headAnchor=new GameObject("HeadAnchor").transform;headAnchor.SetParent(root.transform,false);
        headAnchor.localPosition=model.transform.TransformPoint(head);
        foreach(var renderer in model.GetComponentsInChildren<Renderer>())
        {
            var materials=renderer.sharedMaterials;
            for(int i=0;i<materials.Length;i++)
            {
                var sourceMat=materials[i];string path=Folder+"/"+sourceMat.name+"-URP.mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
                material.SetTexture("_BaseMap",sourceMat.mainTexture);material.SetColor("_BaseColor",Color.white);
                material.SetFloat("_Smoothness",.35f);material.SetFloat("_Metallic",0);
                materials[i]=material;
            }
            renderer.sharedMaterials=materials;
        }
        // The source puts its metal shaft and driver head in polygon0.
        // Split its triangles into material slots while retaining every source vertex.
        var original=MeshOf(headMesh);
        var split=UnityEngine.Object.Instantiate(original);
        split.name="Resort driver head and steel shaft";
        var steelTriangles=new List<int>();var headTriangles=new List<int>();
        var vertices=original.vertices;var sourceTriangles=original.GetTriangles(0);
        for(int i=0;i<sourceTriangles.Length;i+=3)
        {
            float height=(vertices[sourceTriangles[i]].y+vertices[sourceTriangles[i+1]].y+vertices[sourceTriangles[i+2]].y)/3;
            var destination=height<6.25f?steelTriangles:headTriangles;
            destination.Add(sourceTriangles[i]);destination.Add(sourceTriangles[i+1]);destination.Add(sourceTriangles[i+2]);
        }
        split.subMeshCount=2;
        split.SetTriangles(steelTriangles,0);
        split.SetTriangles(headTriangles,1);
        var splitPath=Folder+"/ResortDriverHeadShaft.asset";
        if(AssetDatabase.LoadAssetAtPath<Mesh>(splitPath))AssetDatabase.DeleteAsset(splitPath);
        AssetDatabase.CreateAsset(split,splitPath);
        ((SkinnedMeshRenderer)headMesh).sharedMesh=split;
        var steelPath=Folder+"/ResortDriverSteel-URP.mat";
        var steel=AssetDatabase.LoadAssetAtPath<Material>(steelPath);
        if(!steel){steel=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(steel,steelPath);}
        steel.SetTexture("_BaseMap",Texture2D.whiteTexture);
        steel.SetColor("_BaseColor",new Color(.67f,.73f,.79f));
        steel.SetFloat("_Metallic",.38f);steel.SetFloat("_Smoothness",.72f);
        var driverHead=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/M_DriveHead-URP.mat");
        driverHead.SetTexture("_BaseMap",null);
        driverHead.SetColor("_BaseColor",new Color(.35f,.39f,.44f));
        driverHead.SetFloat("_Metallic",.2f);driverHead.SetFloat("_Smoothness",.48f);
        headMesh.sharedMaterials=new[]{steel,driverHead};
        var prefab=PrefabUtility.SaveAsPrefabAsset(root,Folder+"/ResortDriver.prefab");
        UnityEngine.Object.DestroyImmediate(root);
        var game=UnityEngine.Object.FindAnyObjectByType<KinestheticGolf>();
        for(int i=0;i<game.clubs.Length;i++)
        {
            var old=game.clubs[i];var replacement=(GameObject)PrefabUtility.InstantiatePrefab(prefab);
            replacement.name=i==0?"Patient Wii Resort driver":"Friend Wii Resort driver";
            replacement.transform.SetPositionAndRotation(old.position,old.rotation);
            game.clubs[i]=replacement.transform;UnityEngine.Object.DestroyImmediate(old.gameObject);
        }
        EditorUtility.SetDirty(game);EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
        EditorSceneManager.SaveScene(game.gameObject.scene);AssetDatabase.SaveAssets();
        return "Installed source Wii Sports Resort WD1_R driver for both players, with original textures and normalized grip/head anchors.";
    }
}

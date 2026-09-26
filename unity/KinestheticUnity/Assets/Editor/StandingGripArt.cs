using System.Collections.Generic;
using System.Linq;
using Kinesthetic.Golf;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StandingGripArt
{
    const string MaterialPath="Assets/Kinesthetic/Art/StandingGripHands.mat";
    const string MeshPath="Assets/Kinesthetic/Art/StandingGripBody.asset";
    public static string Build()
    {
        var game=Object.FindAnyObjectByType<KinestheticGolf>();
        var rig=game.rigs[1];rig.Initialize();
        var skins=rig.avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var face=skins.First(r=>r.name=="Mii_head");
        var body=skins.First(r=>r.name=="MiiBody");
        var skin=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if(!skin) {
            skin=new Material(face.sharedMaterial){name="StandingGripHands"};
            AssetDatabase.CreateAsset(skin,MaterialPath);
        }
        // The imported mitten geometry is in the shirt submesh. Split its
        // hand-weighted triangles so the actual moving geometry has skin color.
        var source=body.sharedMesh;
        if(source.subMeshCount==2) {
            var weights=source.boneWeights;
            int left=System.Array.FindIndex(body.bones,b=>b.name=="hand.L");
            int right=System.Array.FindIndex(body.bones,b=>b.name=="hand.R");
            var shirt=new List<int>();var hands=new List<int>();var triangles=source.GetTriangles(0);
            for(int i=0;i<triangles.Length;i+=3) {
                int count=0;
                for(int j=0;j<3;j++) {
                    var weight=weights[triangles[i+j]];
                    if((weight.boneIndex0==left||weight.boneIndex0==right)&&weight.weight0>.5f)count++;
                }
                var target=count==3?hands:shirt;
                target.Add(triangles[i]);target.Add(triangles[i+1]);target.Add(triangles[i+2]);
            }
            var mesh=Object.Instantiate(source);mesh.name="StandingGripBody";mesh.subMeshCount=3;
            mesh.SetTriangles(shirt,0);mesh.SetTriangles(source.GetTriangles(1),1);mesh.SetTriangles(hands,2);
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if(existing){EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;}
            else AssetDatabase.CreateAsset(mesh,MeshPath);
            body.sharedMesh=mesh;
            var materials=body.sharedMaterials;body.sharedMaterials=new[]{materials[0],materials[1],skin};
        }
        foreach(var name in new[]{"hand.L","hand.R"}) {
            var bone=body.bones.First(b=>b.name==name);
            var overlay=bone.Find("Visible golf hand");if(overlay)Object.DestroyImmediate(overlay.gameObject);
        }
        EditorUtility.SetDirty(body);
        EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        return "Standing Mii's skinned hand geometry now has skin color and holds the club grip.";
    }
}

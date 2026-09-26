using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Linq;

public static class RepairWheelchairMaterials
{
    public static string Apply()
    {
        var chair=Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).First(t=>t.name=="MiiWheelchair");
        const string folder="Assets/Kinesthetic/Art/BlenderProps/ChairMaterials";
        System.IO.Directory.CreateDirectory(folder);
        int count=0;
        foreach(var renderer in chair.GetComponentsInChildren<MeshRenderer>()) {
            var materials=renderer.sharedMaterials;
            for(int i=0;i<materials.Length;i++) {
                string name=materials[i].name.Replace(" (Instance)","");
                string path=folder+"/"+name+".mat";
                var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(!mat) {mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
                bool steel=name.Contains("steel");
                Color color=steel?new Color(.50f,.55f,.58f):name.Contains("frame")?new Color(.15f,.17f,.19f):new Color(.06f,.07f,.08f);
                mat.SetColor("_BaseColor",color); mat.SetFloat("_Metallic",steel?.35f:.05f);
                mat.SetFloat("_Smoothness",steel?.36f:.15f);
                EditorUtility.SetDirty(mat); materials[i]=mat;
            }
            renderer.sharedMaterials=materials;EditorUtility.SetDirty(renderer);count++;
        }
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(chair.gameObject.scene);
        EditorSceneManager.SaveScene(chair.gameObject.scene);
        return "Repaired black/steel materials on "+count+" wheelchair parts";
    }
}

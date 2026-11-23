using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using BeachTennis.Presentation.Players;
namespace BeachTennis.EditorTools {
public static class XBotSetup {
const string Model="Assets/_Project/Art/Characters/XBot/XBot.fbx";
const string Prefab="Assets/_Project/Art/Characters/XBot/XBot.prefab";
public static void Apply() {
var importer=(ModelImporter)AssetImporter.GetAtPath(Model);
importer.animationType=ModelImporterAnimationType.Human;
importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
importer.materialImportMode=ModelImporterMaterialImportMode.None;
// ~49k triangles per character: mesh LODs keep 4 players inside the mobile budget (measured 410k -> 91k).
importer.generateMeshLods=true;
importer.maximumMeshLod=-1;
importer.SaveAndReimport();
var root=new GameObject("XBot");
var mesh=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Model));
mesh.transform.SetParent(root.transform,false);
var a=mesh.GetComponent<Animator>();
if(a==null || a.avatar==null || !a.avatar.isValid || !a.isHuman) { Object.DestroyImmediate(root); throw new System.Exception("Invalid humanoid avatar"); }
a.applyRootMotion=false; a.enabled=false;
var rs=mesh.GetComponentsInChildren<Renderer>();
if(rs.Length==0) throw new System.Exception("No mesh in FBX");
var b=rs[0].bounds; foreach(var r in rs) b.Encapsulate(r.bounds);
float scale=1.8f/b.size.y;
mesh.transform.localScale*=scale;
mesh.transform.localPosition-=new Vector3(b.center.x,b.min.y,b.center.z)*scale;
Relax(a,HumanBodyBones.LeftUpperArm,60); Relax(a,HumanBodyBones.RightUpperArm,-60);
PrefabUtility.SaveAsPrefabAsset(root,Prefab); Object.DestroyImmediate(root);
EditorSceneManager.SaveOpenScenes();
var setup=EditorSceneManager.GetSceneManagerSetup();
foreach(var path in new[]{GrayboxSceneBuilder.MatchScenePath,GrayboxSceneBuilder.TrainingScenePath,GrayboxSceneBuilder.SandboxScenePath}) {
var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
foreach(var go in scene.GetRootGameObjects()) {
foreach(var view in go.GetComponentsInChildren<PlayerView>(true)) {
var so=new SerializedObject(view); var body=(Transform)so.FindProperty("body").objectReferenceValue;
var old=body.Find("Body"); Attach(body,old!=null?old.GetComponent<Renderer>().sharedMaterial:null);
if(old!=null) old.gameObject.SetActive(false);
}
if(go.name=="Players (scale reference)") {
var children=go.transform.Cast<Transform>().ToArray();
foreach(var capsule in children) {
var r=capsule.GetComponent<Renderer>(); if(r==null) continue;
var target=go.transform.Find(capsule.name+"_Character");
if(target==null) { target=new GameObject(capsule.name+"_Character").transform; target.SetParent(go.transform,false); }
target.position=capsule.position-Vector3.up*0.9f;
if(capsule.name.Contains("_N")) target.rotation=Quaternion.Euler(0,180,0);
Attach(target,r.sharedMaterial); r.enabled=false;
}
}
}
EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
}
EditorSceneManager.RestoreSceneManagerSetup(setup); AssetDatabase.SaveAssets();
Debug.Log("[XBot] Valid humanoid, 1.80m. Updated Match, Training, Sandbox. Sports animations pending.");
}
static void Relax(Animator a,HumanBodyBones bone,float angle) { var t=a.GetBoneTransform(bone); if(t!=null) t.rotation=Quaternion.AngleAxis(angle,Vector3.forward)*t.rotation; }
public static void Attach(Transform body,Material material) {
var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Prefab); if(prefab==null) return;
var existing=body.Find("XBot");
if(existing!=null) { existing.localPosition=Vector3.zero; existing.localRotation=Quaternion.identity; existing.localScale=Vector3.one; return; }
var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,body); go.name="XBot"; go.transform.localPosition=Vector3.zero; go.transform.localRotation=Quaternion.identity; go.transform.localScale=Vector3.one;
if(material!=null) foreach(var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterials=Enumerable.Repeat(material,r.sharedMaterials.Length).ToArray();
}
}}


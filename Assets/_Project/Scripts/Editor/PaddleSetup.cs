using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using BeachTennis.Presentation.Players;

namespace BeachTennis.EditorTools
{
    public static class PaddleSetup
    {
        const string Dir = "Assets/_Project/Art/Equipment/ArionPaddle";
        public const string PrefabPath = Dir + "/ArionPaddle.prefab";

        [MenuItem("Beach Tennis/Setup/Apply Arion Paddle")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Exit Play Mode first.");
            AssetDatabase.Refresh();
            string modelPath = Dir + "/ArionPaddle.fbx";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var sourceMaterial = source.GetComponentInChildren<Renderer>().sharedMaterial;
            string materialPath = Dir + "/ArionPaddle.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor", sourceMaterial != null ? sourceMaterial.color : Color.gray);
                material.SetFloat("_Smoothness", 0.35f);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            // The Meshy paddle is ~190k triangles: import-time mesh LODs let the renderer pick a light
            // level by screen size (measured: 4 paddles drop from ~1.5M to ~13k drawn triangles).
            importer.generateMeshLods = true;
            importer.maximumMeshLod = -1;
            importer.SaveAndReimport();

            var root = new GameObject("ArionPaddle");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
                model.transform.SetParent(root.transform, false);
                // Handle at -Y in the supplied FBX. Canonical grip: blade extends along +X.
                model.transform.localRotation = Quaternion.Euler(0, 0, -90) * model.transform.localRotation;
                var renderers = model.GetComponentsInChildren<Renderer>();
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                float scale = 0.50f / bounds.size.x;
                model.transform.localScale *= scale;
                model.transform.localPosition -= new Vector3(bounds.min.x * scale + 0.055f, bounds.center.y * scale, bounds.center.z * scale);
                foreach (var renderer in renderers)
                {
                    var slots = new Material[renderer.sharedMaterials.Length];
                    for (int i = 0; i < slots.Length; i++) slots[i] = material;
                    renderer.sharedMaterials = slots;
                }
                var center = new GameObject("ContactCenter").transform;
                center.SetParent(root.transform, false);
                center.localPosition = new Vector3(0.32f, 0, 0);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }

            EditorSceneManager.SaveOpenScenes();
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (string path in new[] { GrayboxSceneBuilder.MatchScenePath, GrayboxSceneBuilder.TrainingScenePath })
                {
                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    foreach (var go in scene.GetRootGameObjects())
                        foreach (var view in go.GetComponentsInChildren<PlayerView>(true))
                        {
                            var so = new SerializedObject(view);
                            Attach((Transform)so.FindProperty("racketPivot").objectReferenceValue);
                        }
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
            AssetDatabase.SaveAssets();
            Debug.Log("[Paddle] Supplied generate.fbx installed; all previous paddles removed from player rigs.");
        }

        public static void Attach(Transform pivot)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null || pivot == null) throw new System.InvalidOperationException("Arion paddle prefab or grip pivot missing.");
            // Only paddle visuals live below this pivot. Recreate to avoid obsolete prefab references.
            for (int i = pivot.childCount - 1; i >= 0; i--) Object.DestroyImmediate(pivot.GetChild(i).gameObject);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, pivot);
            instance.name = "ArionPaddle";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
        }
    }
}


using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BeachTennis.EditorTools
{
    /// <summary>
    /// Builds the four quality tiers from docs/01 §4.5 (Low / Medium / High / Ultra), each with its own
    /// URP asset, and maps platforms to a default tier. Re-running overwrites the tier assets with the
    /// values below, so this file is the single source of truth for per-tier rendering settings.
    /// Runtime tier selection per device comes later (QualityManager, Phase 8).
    /// </summary>
    public static class QualityTierSetup
    {
        private const string Dir = "Assets/_Project/Settings/Rendering";
        private const string TemplateAsset = Dir + "/Mobile_RPAsset.asset";
        private const string TemplateRenderer = Dir + "/Mobile_Renderer.asset";
        private const string RendererForward = Dir + "/URP_Renderer_Forward.asset";
        private const string RendererForwardPlus = Dir + "/URP_Renderer_ForwardPlus.asset";

        // URP RenderingMode enum values as serialized.
        private const int ModeForward = 0;
        private const int ModeForwardPlus = 2;

        private struct Tier
        {
            public string Name;
            public bool ForwardPlus;
            public float RenderScale;
            public int Msaa;
            public bool Hdr;
            public float ShadowDistance;
            public int Cascades;
            public int ShadowResolution;
            public bool SoftShadows;
            public int SoftShadowQuality; // 1 low, 2 medium, 3 high
            public float LodBias;
            public int SkinWeights;       // 2 or 4 bones per vertex
        }

        private static readonly Tier[] Tiers =
        {
            new Tier { Name = "Low",    ForwardPlus = false, RenderScale = 0.8f, Msaa = 1, Hdr = false, ShadowDistance = 25, Cascades = 1, ShadowResolution = 1024, SoftShadows = false, SoftShadowQuality = 1, LodBias = 0.7f, SkinWeights = 2 },
            new Tier { Name = "Medium", ForwardPlus = true,  RenderScale = 0.9f, Msaa = 2, Hdr = true,  ShadowDistance = 30, Cascades = 1, ShadowResolution = 2048, SoftShadows = false, SoftShadowQuality = 1, LodBias = 1.0f, SkinWeights = 4 },
            new Tier { Name = "High",   ForwardPlus = true,  RenderScale = 1.0f, Msaa = 4, Hdr = true,  ShadowDistance = 35, Cascades = 2, ShadowResolution = 2048, SoftShadows = true,  SoftShadowQuality = 1, LodBias = 1.5f, SkinWeights = 4 },
            new Tier { Name = "Ultra",  ForwardPlus = true,  RenderScale = 1.0f, Msaa = 4, Hdr = true,  ShadowDistance = 40, Cascades = 2, ShadowResolution = 4096, SoftShadows = true,  SoftShadowQuality = 2, LodBias = 2.0f, SkinWeights = 4 },
        };

        private const int DefaultMobileTier = 1;  // Medium
        private const int DefaultDesktopTier = 3; // Ultra (Editor / PC)

        [MenuItem("Beach Tennis/Setup/Build Quality Tiers")]
        public static void Build()
        {
            var forward = GetOrCopyRenderer(RendererForward, ModeForward);
            var forwardPlus = GetOrCopyRenderer(RendererForwardPlus, ModeForwardPlus);

            var assets = new List<UniversalRenderPipelineAsset>();
            foreach (var tier in Tiers)
                assets.Add(BuildAsset(tier, tier.ForwardPlus ? forwardPlus : forward));

            ConfigureQualitySettings(assets);
            GraphicsSettings.defaultRenderPipeline = assets[DefaultMobileTier];

            AssetDatabase.SaveAssets();
            Debug.Log("[BeachTennis] Quality tiers built: " + string.Join(", ", QualitySettings.names));
        }

        private static ScriptableRendererData GetOrCopyRenderer(string path, int renderingMode)
        {
            if (AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path) == null)
                AssetDatabase.CopyAsset(TemplateRenderer, path);

            var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
            var so = new SerializedObject(data);
            so.FindProperty("m_RenderingMode").intValue = renderingMode;
            so.ApplyModifiedPropertiesWithoutUndo();
            return data;
        }

        private static UniversalRenderPipelineAsset BuildAsset(Tier tier, ScriptableRendererData renderer)
        {
            string path = $"{Dir}/URP_{tier.Name}.asset";
            if (AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path) == null)
                AssetDatabase.CopyAsset(TemplateAsset, path);

            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            var so = new SerializedObject(asset);

            var list = so.FindProperty("m_RendererDataList");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            so.FindProperty("m_DefaultRendererIndex").intValue = 0;

            so.FindProperty("m_RenderScale").floatValue = tier.RenderScale;
            so.FindProperty("m_MSAA").intValue = tier.Msaa;
            so.FindProperty("m_SupportsHDR").boolValue = tier.Hdr;
            so.FindProperty("m_ShadowDistance").floatValue = tier.ShadowDistance;
            so.FindProperty("m_ShadowCascadeCount").intValue = tier.Cascades;
            so.FindProperty("m_MainLightShadowmapResolution").intValue = tier.ShadowResolution;
            so.FindProperty("m_SoftShadowsSupported").boolValue = tier.SoftShadows;
            so.FindProperty("m_SoftShadowQuality").intValue = tier.SoftShadowQuality;

            // Shared policy for every tier (docs/01 §4).
            so.FindProperty("m_UseSRPBatcher").boolValue = true;
            so.FindProperty("m_SupportsDynamicBatching").boolValue = false;
            so.FindProperty("m_RequireDepthTexture").boolValue = false;  // enable only when an effect needs it
            so.FindProperty("m_RequireOpaqueTexture").boolValue = false;
            so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
            so.FindProperty("m_UseAdaptivePerformance").boolValue = true;
            so.FindProperty("m_SupportsTerrainHoles").boolValue = false; // no terrain on a beach court

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static void ConfigureQualitySettings(List<UniversalRenderPipelineAsset> assets)
        {
            var qualityAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0];
            var so = new SerializedObject(qualityAsset);
            var levels = so.FindProperty("m_QualitySettings");

            // Keep the first template level as the base for all tiers, then size the array.
            while (levels.arraySize > 1) levels.DeleteArrayElementAtIndex(levels.arraySize - 1);
            while (levels.arraySize < Tiers.Length) levels.InsertArrayElementAtIndex(0);

            for (int i = 0; i < Tiers.Length; i++)
            {
                var level = levels.GetArrayElementAtIndex(i);
                level.FindPropertyRelative("name").stringValue = Tiers[i].Name;
                level.FindPropertyRelative("customRenderPipeline").objectReferenceValue = assets[i];
                level.FindPropertyRelative("vSyncCount").intValue = 0;
                level.FindPropertyRelative("lodBias").floatValue = Tiers[i].LodBias;
                level.FindPropertyRelative("skinWeights").intValue = Tiers[i].SkinWeights;
                level.FindPropertyRelative("realtimeReflectionProbes").boolValue = false;
            }

            var perPlatform = so.FindProperty("m_PerPlatformDefaultQuality");
            for (int i = 0; i < perPlatform.arraySize; i++)
            {
                var entry = perPlatform.GetArrayElementAtIndex(i);
                string platform = entry.FindPropertyRelative("first").stringValue;
                bool mobile = platform == "Android" || platform == "iPhone" || platform == "tvOS";
                entry.FindPropertyRelative("second").intValue = mobile ? DefaultMobileTier : DefaultDesktopTier;
            }

            so.FindProperty("m_CurrentQuality").intValue = DefaultMobileTier;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}

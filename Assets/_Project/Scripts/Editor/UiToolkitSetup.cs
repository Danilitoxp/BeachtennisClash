using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BeachTennis.EditorTools
{
    /// <summary>
    /// Creates the UI Toolkit runtime assets: a theme that imports Unity's default runtime theme, and
    /// PanelSettings (1920×1080 reference, matched on height like the uGUI HUD) under Resources so the
    /// game can load them without scene wiring. Re-runnable.
    /// </summary>
    public static class UiToolkitSetup
    {
        public const string ThemePath = "Assets/_Project/Settings/UI/RuntimeTheme.tss";
        public const string PanelSettingsPath = "Assets/_Project/Resources/UI/PanelSettings.asset";

        [MenuItem("Beach Tennis/Setup/UI Toolkit Assets")]
        public static void Apply()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ThemePath));
            Directory.CreateDirectory(Path.GetDirectoryName(PanelSettingsPath));

            if (!File.Exists(ThemePath))
            {
                File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(ThemePath);
            }
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, PanelSettingsPath);
            }
            panel.themeStyleSheet = theme;
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1080, 1920); // portrait
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0f;                                         // scale by width
            panel.sortingOrder = 100;
            EditorUtility.SetDirty(panel);
            AssetDatabase.SaveAssets();
            Debug.Log("[BeachTennis] UI Toolkit theme and PanelSettings ready.");
        }
    }
}

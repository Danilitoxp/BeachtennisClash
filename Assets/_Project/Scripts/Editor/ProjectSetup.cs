using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace BeachTennis.EditorTools
{
    /// <summary>
    /// Applies the project-wide Player Settings decided in docs/01 §2.10, so they are reproducible
    /// and reviewable in code instead of living only in clicks inside the Inspector.
    /// Safe to run any number of times.
    /// </summary>
    public static class ProjectSetup
    {
        public const string AndroidPackageName = "com.beachtennis.game";

        [MenuItem("Beach Tennis/Setup/Apply Player Settings")]
        public static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "BeachTennis";
            PlayerSettings.productName = "Beach Tennis 3D";

            // Rendering basics
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.gpuSkinning = true;
            PlayerSettings.enableFrameTimingStats = true; // GPU time in the performance HUD
            PlayerSettings.gcIncremental = true;

            // Landscape only
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            ApplyAndroid();

            AssetDatabase.SaveAssets();
            Debug.Log("[BeachTennis] Player Settings applied.");
        }

        private static void ApplyAndroid()
        {
            var android = NamedBuildTarget.Android;

            PlayerSettings.SetApplicationIdentifier(android, AndroidPackageName);
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Low);

            // ARM64 only. Unity 6.3 deprecated x86_64 for Android (Magic Leap only); emulators run ARM64
            // builds through ARM translation (Android Studio Android 11+ images, BlueStacks 5).
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            // Vulkan first; GLES3 fallback mainly for emulators whose Vulkan support is unreliable.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });

            PlayerSettings.Android.optimizedFramePacing = true;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
        }
    }
}

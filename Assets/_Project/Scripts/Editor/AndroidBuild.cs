using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace BeachTennis.EditorTools
{
    /// <summary>
    /// Reproducible Android builds, usable from the menu or headless:
    ///   unity build &lt;project&gt; --target Android --execute-method BeachTennis.EditorTools.AndroidBuild.BuildDevelopmentApk
    /// </summary>
    public static class AndroidBuild
    {
        public const string OutputDir = "Builds/Android";

        /// <summary>Real devices: Vulkan first (project default).</summary>
        [MenuItem("Beach Tennis/Build/Android Development APK (Device)")]
        public static void BuildDevelopmentApk()
        {
            ProjectSetup.ApplyPlayerSettings();
            Build("BeachTennis-dev.apk");
        }

        /// <summary>
        /// Emulators (BlueStacks / Android Studio): OpenGL ES 3 only, min API 28.
        /// - The Android Studio emulator's Vulkan driver crashes in vkSetDebugUtilsObjectNameEXT when an
        ///   ARM64 development build runs through ARM translation.
        /// - BlueStacks' standard instance (Pie64) is Android 9 / API 28.
        /// Player Settings are restored to the device configuration afterwards.
        /// </summary>
        [MenuItem("Beach Tennis/Build/Android Development APK (Emulator, GLES3)")]
        public static void BuildEmulatorApk()
        {
            ProjectSetup.ApplyPlayerSettings();
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel28;
            try
            {
                Build("BeachTennis-emulator.apk");
            }
            finally
            {
                ProjectSetup.ApplyPlayerSettings();
            }
        }

        private static void Build(string fileName)
        {
            EditorUserBuildSettings.buildAppBundle = false;

            Directory.CreateDirectory(OutputDir);
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Path.Combine(OutputDir, fileName),
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development | BuildOptions.ConnectWithProfiler
            };

            if (options.scenes.Length == 0)
                throw new InvalidOperationException("No scenes in Build Settings. Run Beach Tennis/Setup/Build Graybox Scenes first.");

            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[BeachTennis] Android build {summary.result}: {summary.outputPath} " +
                      $"({summary.totalSize / (1024f * 1024f):0.0} MB, {summary.totalTime.TotalSeconds:0}s)");

            if (summary.result != BuildResult.Succeeded && Application.isBatchMode)
                EditorApplication.Exit(1);
        }
    }
}

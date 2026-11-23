using UnityEngine;

namespace BeachTennis.Game
{
    public enum FrameRateMode
    {
        /// <summary>60 FPS: default, best balance of smoothness, heat and battery.</summary>
        Standard60 = 60,
        /// <summary>120 FPS where the display supports it, otherwise falls back to 60.</summary>
        High120 = 120,
        /// <summary>Menus and idle screens: saves battery.</summary>
        Saver30 = 30
    }

    /// <summary>
    /// Owns the render frame-rate policy. Mobile ignores vSyncCount, so the cap is targetFrameRate;
    /// Android frame pacing (Optimized Frame Pacing, enabled in Player Settings) keeps frames evenly spaced.
    /// </summary>
    public static class FrameRateController
    {
        private const string PrefKey = "bt.frameRateMode";

        public static FrameRateMode Mode { get; private set; } = FrameRateMode.Standard60;

        /// <summary>Highest refresh rate the current display reports, rounded.</summary>
        public static int DisplayRefreshRate
        {
            get
            {
                double hz = Screen.currentResolution.refreshRateRatio.value;
                return hz > 1 ? Mathf.RoundToInt((float)hz) : 60;
            }
        }

        public static bool Supports120 => DisplayRefreshRate >= 119;

        public static void LoadAndApply()
        {
            var saved = (FrameRateMode)PlayerPrefs.GetInt(PrefKey, (int)FrameRateMode.Standard60);
            Apply(saved, persist: false);
        }

        public static void Apply(FrameRateMode mode, bool persist = true)
        {
            if (mode == FrameRateMode.High120 && !Supports120)
                mode = FrameRateMode.Standard60;

            Mode = mode;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = (int)mode;

            if (persist)
            {
                PlayerPrefs.SetInt(PrefKey, (int)mode);
                PlayerPrefs.Save();
            }
        }
    }
}

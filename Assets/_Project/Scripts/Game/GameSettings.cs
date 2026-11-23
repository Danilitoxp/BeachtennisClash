using BeachTennis.AI;
using BeachTennis.Simulation.Rules;
using UnityEngine;

namespace BeachTennis.Game
{
    /// <summary>
    /// Player preferences, persisted with PlayerPrefs (small, local, per-device): difficulty, visual aids,
    /// graphics quality, frame rate and event category. Applied once at boot and whenever changed.
    /// </summary>
    public static class GameSettings
    {
        private const string KeyDifficulty = "bt.difficulty";
        private const string KeyTrajectory = "bt.trajectoryAid";
        private const string KeyQuality = "bt.quality";
        private const string KeyHighFps = "bt.highFps";
        private const string KeyCategory = "bt.category";
        private const string KeyTimeOfDay = "bt.timeOfDay";

        /// <summary>0 = day, 1 = sunset, 2 = night (Presentation.Environment.TimeOfDay).</summary>
        public static int TimeOfDay
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(KeyTimeOfDay, 0), 0, 2);
            set { PlayerPrefs.SetInt(KeyTimeOfDay, value); PlayerPrefs.Save(); }
        }

        public static Difficulty Difficulty
        {
            get => (Difficulty)PlayerPrefs.GetInt(KeyDifficulty, (int)Difficulty.Medium);
            set { PlayerPrefs.SetInt(KeyDifficulty, (int)value); PlayerPrefs.Save(); }
        }

        public static bool TrajectoryAid
        {
            get => PlayerPrefs.GetInt(KeyTrajectory, 1) == 1;
            set { PlayerPrefs.SetInt(KeyTrajectory, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Quality level index (Low/Medium/High/Ultra); -1 = keep the platform default.</summary>
        public static int Quality
        {
            get => PlayerPrefs.GetInt(KeyQuality, -1);
            set { PlayerPrefs.SetInt(KeyQuality, value); PlayerPrefs.Save(); ApplyQuality(); }
        }

        public static bool HighFrameRate
        {
            get => PlayerPrefs.GetInt(KeyHighFps, 0) == 1;
            set { PlayerPrefs.SetInt(KeyHighFps, value ? 1 : 0); PlayerPrefs.Save(); ApplyFrameRate(); }
        }

        /// <summary>ITF category: decides net height (1.70 / 1.80 m) and prohibited zone (3 / 6 m).</summary>
        public static EventCategory Category
        {
            get => (EventCategory)PlayerPrefs.GetInt(KeyCategory, (int)EventCategory.Women);
            set { PlayerPrefs.SetInt(KeyCategory, (int)value); PlayerPrefs.Save(); }
        }

        public static void ApplyAll()
        {
            ApplyQuality();
            ApplyFrameRate();
        }

        private static void ApplyQuality()
        {
            int q = Quality;
            if (q >= 0 && q < QualitySettings.count && q != QualitySettings.GetQualityLevel())
                QualitySettings.SetQualityLevel(q, true);
        }

        private static void ApplyFrameRate() =>
            FrameRateController.Apply(HighFrameRate ? FrameRateMode.High120 : FrameRateMode.Standard60, persist: false);
    }
}

using BeachTennis.Core;
using BeachTennis.Presentation.Diagnostics;
using UnityEngine;

namespace BeachTennis.Game
{
    /// <summary>
    /// Composition root. Created automatically before the first scene loads, so any scene can be
    /// played directly in the Editor and still get the same services as a full boot.
    /// All cross-system wiring happens here: no singletons scattered through gameplay code.
    /// </summary>
    public sealed class AppRoot : MonoBehaviour
    {
        public static AppRoot Instance { get; private set; }

        public SimulationRunner Simulation { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;

            var go = new GameObject("[App]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<AppRoot>();
            Instance.Compose();
        }

        private void Compose()
        {
            FrameRateController.LoadAndApply();
            GameSettings.ApplyAll(); // saved quality level and 60/120 FPS choice

            Simulation = gameObject.AddComponent<SimulationRunner>();
            Simulation.Initialize(SimulationClock.DefaultTickRate);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var hud = gameObject.AddComponent<PerformanceHud>();
            hud.Bind(Simulation.Clock);
#endif
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}

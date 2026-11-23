using BeachTennis.Core;
using UnityEngine;

namespace BeachTennis.Game
{
    /// <summary>
    /// The single place where Unity's frame loop drives the fixed-rate simulation.
    /// Runs before every other script so presentation always reads this frame's simulated state.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class SimulationRunner : MonoBehaviour
    {
        public SimulationClock Clock { get; private set; }
        public SimulationScheduler Scheduler { get; private set; }

        /// <summary>Paused simulations keep rendering but stop advancing (menus, pause screen).</summary>
        public bool Paused { get; set; }

        public void Initialize(int tickRate)
        {
            Clock = new SimulationClock(tickRate);
            Scheduler = new SimulationScheduler();
        }

        private void Update()
        {
            if (Clock == null || Paused) return;
            // Time.deltaTime (not unscaled) so slow-motion replays can use Time.timeScale later.
            Clock.Advance(Time.deltaTime, Scheduler);
        }
    }
}

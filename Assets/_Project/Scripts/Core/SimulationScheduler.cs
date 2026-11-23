using System;

namespace BeachTennis.Core
{
    /// <summary>
    /// Runs simulation systems in a fixed, explicit order every tick.
    /// One ordered list instead of scattered Update() calls: deterministic order, no per-object overhead.
    /// Registration happens at setup time; ticking never allocates.
    /// </summary>
    public sealed class SimulationScheduler : ISimulationTickable
    {
        private ISimulationTickable[] _systems = new ISimulationTickable[8];
        private int _count;

        public int Count => _count;

        public void Register(ISimulationTickable system)
        {
            if (system == null) throw new ArgumentNullException(nameof(system));
            if (_count == _systems.Length) Array.Resize(ref _systems, _systems.Length * 2);
            _systems[_count++] = system;
        }

        public bool Unregister(ISimulationTickable system)
        {
            for (int i = 0; i < _count; i++)
            {
                if (!ReferenceEquals(_systems[i], system)) continue;
                // Preserve order: shift remaining systems left.
                Array.Copy(_systems, i + 1, _systems, i, _count - i - 1);
                _systems[--_count] = null;
                return true;
            }
            return false;
        }

        public void Tick(long tick, float deltaTime)
        {
            for (int i = 0; i < _count; i++)
                _systems[i].Tick(tick, deltaTime);
        }
    }
}

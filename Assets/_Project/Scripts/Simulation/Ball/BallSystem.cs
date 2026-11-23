using BeachTennis.Core;

namespace BeachTennis.Simulation.Ball
{
    /// <summary>
    /// The live ball inside the fixed-tick simulation. Keeps the previous and current state so the
    /// presentation can interpolate, and collects events for rules and visuals.
    /// </summary>
    public sealed class BallSystem : ISimulationTickable
    {
        public BallPhysicsConfig Config;
        public CourtDimensions Court;

        public BallState State;
        public BallState PreviousState;
        public readonly BallEventBuffer Events = new BallEventBuffer();

        /// <summary>Incremented on every launch/teleport so views know not to interpolate across it.</summary>
        public int LaunchVersion { get; private set; }

        public BallSystem(in BallPhysicsConfig config, in CourtDimensions court)
        {
            Config = config;
            Court = court;
            State = new BallState { Mode = BallMode.Inactive };
            PreviousState = State;
        }

        public void Launch(Vec3 position, Vec3 velocity, Vec3 spin)
        {
            State = BallState.Launch(position, velocity, spin);
            PreviousState = State;
            LaunchVersion++;
        }

        public void Deactivate()
        {
            State.Mode = BallMode.Inactive;
            State.Velocity = Vec3.Zero;
            PreviousState = State;
        }

        public void Tick(long tick, float deltaTime)
        {
            PreviousState = State;
            BallPhysics.Step(ref State, Config, Court, deltaTime, Events, tick);
        }
    }
}

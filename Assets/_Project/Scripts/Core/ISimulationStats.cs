namespace BeachTennis.Core
{
    /// <summary>Read-only view of the simulation clock, for diagnostics and presentation.</summary>
    public interface ISimulationStats
    {
        int TickRate { get; }
        long TickCount { get; }
        int LastFrameTicks { get; }
        double DroppedTime { get; }
        float Alpha { get; }
    }
}

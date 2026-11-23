using BeachTennis.Simulation.Ball;
using UnityEngine;

namespace BeachTennis.Game.Config
{
    /// <summary>
    /// Inspector-editable wrapper for <see cref="BallPhysicsConfig"/>. Systems read it every frame,
    /// so values can be tuned live in Play Mode (remember: Play Mode changes to assets persist).
    /// </summary>
    [CreateAssetMenu(menuName = "Beach Tennis/Ball Physics Settings", fileName = "BallPhysics")]
    public sealed class BallPhysicsSettings : ScriptableObject
    {
        public BallPhysicsConfig Config = BallPhysicsConfig.Default;

        [ContextMenu("Reset To Defaults")]
        private void ResetToDefaults() => Config = BallPhysicsConfig.Default;
    }
}

using BeachTennis.Simulation.Players;
using UnityEngine;

namespace BeachTennis.Game.Config
{
    /// <summary>Inspector-editable <see cref="PlayerConfig"/>; read every frame for live tuning in Play Mode.</summary>
    [CreateAssetMenu(menuName = "Beach Tennis/Player Settings", fileName = "Player")]
    public sealed class PlayerSettings : ScriptableObject
    {
        public PlayerConfig Config = PlayerConfig.Default;

        [ContextMenu("Reset To Defaults")]
        private void ResetToDefaults() => Config = PlayerConfig.Default;
    }
}

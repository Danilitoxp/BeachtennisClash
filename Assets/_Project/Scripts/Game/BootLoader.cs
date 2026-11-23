using UnityEngine;
using UnityEngine.SceneManagement;

namespace BeachTennis.Game
{
    /// <summary>
    /// Lives in the Boot scene (build index 0). Services are already composed by <see cref="AppRoot"/>;
    /// this only decides which scene to show first. Later it will load menus and Addressables content.
    /// </summary>
    public sealed class BootLoader : MonoBehaviour
    {
        [SerializeField] private string firstScene = "Match_Graybox";

        private void Start()
        {
            SceneManager.LoadSceneAsync(firstScene, LoadSceneMode.Single);
        }
    }
}

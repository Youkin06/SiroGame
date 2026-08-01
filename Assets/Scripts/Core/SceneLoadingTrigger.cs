using UnityEngine;

/// <summary>
/// UI ButtonのOn ClickからTileLoadingScreenを呼び出すための小さな中継コンポーネント。
/// </summary>
public sealed class SceneLoadingTrigger : MonoBehaviour
{
    [SerializeField] private string _sceneName = "SampleScene";

    public void LoadConfiguredScene()
    {
        if (TileLoadingScreen.Instance == null)
        {
            Debug.LogError(
                "TileLoadingScreenが見つかりません。最初に開くシーンへ1つ配置してください。",
                this
            );
            return;
        }

        TileLoadingScreen.Instance.LoadScene(_sceneName);
    }
}

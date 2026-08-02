using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Playerが入った時、Build Settings上の次のシーンへ
/// ステージクリア演出付きで遷移する。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class StageClearZone : MonoBehaviour
{
    private const string TitleSceneName = "TitleScene";

    private bool _hasTriggered;

    private void Awake()
    {
        Collider clearCollider = GetComponent<Collider>();
        if (!clearCollider.isTrigger)
        {
            Debug.LogError(
                "StageClearZoneのColliderはIs Triggerを有効にしてください。",
                this
            );
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_hasTriggered || other == null)
        {
            return;
        }

        PlayerMove player = other.GetComponentInParent<PlayerMove>();
        if (player == null || player.IsDead)
        {
            return;
        }

        TileLoadingScreen loadingScreen = TileLoadingScreen.Instance;
        WorldModeManager worldModeManager = WorldModeManager.Instance;
        if (loadingScreen == null || worldModeManager == null)
        {
            Debug.LogError(
                "ステージクリアにはTileLoadingScreenとWorldModeManagerが必要です。",
                this
            );
            return;
        }

        int nextBuildIndex = SceneManager.GetActiveScene().buildIndex + 1;
        if (nextBuildIndex < 0)
        {
            Debug.LogError(
                "現在のステージがBuild Settingsに登録されていません。",
                this
            );
            return;
        }

        _hasTriggered = true;
        if (nextBuildIndex >= SceneManager.sceneCountInBuildSettings)
        {
            GameClearTimeTracker.CompleteRun();
            loadingScreen.LoadFinalResultScene(
                TitleSceneName,
                worldModeManager.KuroElapsedTime
            );
            return;
        }

        loadingScreen.LoadStageClearScene(
            nextBuildIndex,
            worldModeManager.KuroElapsedTime
        );
    }
}

using UnityEngine;

/// <summary>
/// ステージをまたいで引き継ぐ、クリア済みステージ分のクロ累計時間。
/// 現在プレイ中のステージ分は、クリアが確定するまで加算しない。
/// </summary>
public static class KuroTimeProgress
{
    public static float CompletedStageTime { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnGameStart()
    {
        Reset();
    }

    public static void Reset()
    {
        CompletedStageTime = 0f;
    }

    public static void CommitStageTotal(float totalKuroTime)
    {
        CompletedStageTime = Mathf.Max(
            CompletedStageTime,
            Mathf.Max(0f, totalKuroTime)
        );
    }
}

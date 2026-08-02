using UnityEngine;

public enum GameResultRank
{
    Clear,
    GrayNotCleared,
    BlackNotCleared
}

public readonly struct GameResultSnapshot
{
    public float TotalKuroTime { get; }
    public float GaugeRatio { get; }
    public GameResultRank Rank { get; }
    public float ClearTimeSeconds { get; }
    public bool HasClearTime { get; }

    public GameResultSnapshot(
        float totalKuroTime,
        float gaugeRatio,
        GameResultRank rank,
        float clearTimeSeconds,
        bool hasClearTime
    )
    {
        TotalKuroTime = totalKuroTime;
        GaugeRatio = gaugeRatio;
        Rank = rank;
        ClearTimeSeconds = clearTimeSeconds;
        HasClearTime = hasClearTime;
    }
}

/// <summary>
/// 最終ステージからTitleSceneへ、1回だけリザルトを受け渡す。
/// </summary>
public static class GameResultState
{
    private const float ClearUpperBound = 0.2f;
    private const float BlackLowerBound = 0.8f;

    private static bool _hasPendingResult;
    private static GameResultSnapshot _pendingResult;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnGameStart()
    {
        Reset();
    }

    public static void Prepare(float totalKuroTime, float maximumKuroTime)
    {
        float safeTotal = Mathf.Max(0f, totalKuroTime);
        float safeMaximum = Mathf.Max(0.01f, maximumKuroTime);
        float ratio = Mathf.Clamp01(safeTotal / safeMaximum);
        bool hasClearTime =
            GameClearTimeTracker.TryGetCompletedTime(out float clearTime);
        _pendingResult = new GameResultSnapshot(
            safeTotal,
            ratio,
            EvaluateRank(ratio),
            clearTime,
            hasClearTime
        );
        _hasPendingResult = true;
    }

    public static bool TryConsume(out GameResultSnapshot result)
    {
        if (!_hasPendingResult)
        {
            result = default;
            return false;
        }

        result = _pendingResult;
        _pendingResult = default;
        _hasPendingResult = false;
        return true;
    }

    public static GameResultRank EvaluateRank(float gaugeRatio)
    {
        float ratio = Mathf.Clamp01(gaugeRatio);
        if (ratio < ClearUpperBound)
        {
            return GameResultRank.Clear;
        }

        return ratio < BlackLowerBound
            ? GameResultRank.GrayNotCleared
            : GameResultRank.BlackNotCleared;
    }

    public static void Reset()
    {
        _hasPendingResult = false;
        _pendingResult = default;
    }
}

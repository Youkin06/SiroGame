using UnityEngine;

/// <summary>
/// 「はじめから」の決定から最終ゴール到達までの実時間を計測する。
/// シーンに依存しないため、ロード・リトライ・ステージ遷移中も継続する。
/// </summary>
public static class GameClearTimeTracker
{
    private static double _startedAt;
    private static float _completedTime;
    private static bool _isRunning;
    private static bool _hasCompletedTime;

    public static bool IsRunning => _isRunning;

    public static float ElapsedTime
    {
        get
        {
            if (_isRunning)
            {
                return Mathf.Max(
                    0f,
                    (float)(Time.realtimeSinceStartupAsDouble - _startedAt)
                );
            }

            return _hasCompletedTime ? _completedTime : 0f;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnGameStart()
    {
        Reset();
    }

    public static void StartNewRun()
    {
        _startedAt = Time.realtimeSinceStartupAsDouble;
        _completedTime = 0f;
        _hasCompletedTime = false;
        _isRunning = true;
    }

    public static bool CompleteRun()
    {
        if (!_isRunning)
        {
            return false;
        }

        _completedTime = Mathf.Max(
            0f,
            (float)(Time.realtimeSinceStartupAsDouble - _startedAt)
        );
        _hasCompletedTime = true;
        _isRunning = false;
        return true;
    }

    public static bool TryGetCompletedTime(out float clearTime)
    {
        clearTime = _hasCompletedTime ? _completedTime : 0f;
        return _hasCompletedTime;
    }

    public static void Reset()
    {
        _startedAt = 0d;
        _completedTime = 0f;
        _isRunning = false;
        _hasCompletedTime = false;
    }
}

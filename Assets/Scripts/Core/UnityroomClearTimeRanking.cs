using UnityEngine;
using unityroom.Api;

/// <summary>
/// Clear時のクリアタイムをunityroomの昇順ランキングへ送信する。
/// </summary>
public static class UnityroomClearTimeRanking
{
    private const float Milliseconds = 1000f;

    public static bool Submit(int boardNo, float clearTimeSeconds)
    {
        if (boardNo < 1 ||
            float.IsNaN(clearTimeSeconds) ||
            float.IsInfinity(clearTimeSeconds) ||
            clearTimeSeconds < 0f)
        {
            Debug.LogError(
                $"unityroomへ送信できないランキング値です。" +
                $" BoardNo={boardNo} Time={clearTimeSeconds}"
            );
            return false;
        }

        IUnityroomApiClient client = UnityroomApiClient.Instance;
        if (client == null)
        {
            return false;
        }

        float roundedTime =
            Mathf.Round(clearTimeSeconds * Milliseconds) / Milliseconds;
        client.SendScore(
            boardNo,
            roundedTime,
            ScoreboardWriteMode.HighScoreAsc
        );
        return true;
    }
}

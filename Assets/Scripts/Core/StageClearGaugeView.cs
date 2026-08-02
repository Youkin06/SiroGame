using System.Collections;
using UnityEngine;

/// <summary>
/// ステージクリア時のクロ累計ゲージ表示。
/// GageFillの横幅だけを左端から変更し、GageFrameは常に手前へ表示する。
/// </summary>
[DisallowMultipleComponent]
public sealed class StageClearGaugeView : MonoBehaviour
{
    private const string FillObjectName = "GageFill";
    private const string FrameObjectName = "GageFrame";

    [Header("Gauge Animation")]
    [SerializeField, Min(0f)] private float _displayDuration = 3f;
    [SerializeField, Min(0.01f)] private float _maximumKuroTime = 60f;

    private RectTransform _fillRectTransform;
    private Transform _frameTransform;
    private float _fullWidth;

    private void Awake()
    {
        CacheChildren();
        SetFillAmount(0f);
    }

    public IEnumerator Play(float fromKuroTime, float toKuroTime)
    {
        gameObject.SetActive(true);
        if (!CacheChildren())
        {
            yield break;
        }

        float maximumTime = Mathf.Max(0.01f, _maximumKuroTime);
        float safeFrom = Mathf.Clamp01(fromKuroTime / maximumTime);
        float safeTo = Mathf.Clamp01(toKuroTime / maximumTime);
        float duration = Mathf.Max(0f, _displayDuration);
        SetFillAmount(safeFrom);

        if (duration <= 0f)
        {
            SetFillAmount(safeTo);
            gameObject.SetActive(false);
            yield break;
        }

        float startedAt = Time.unscaledTime;
        while (Time.unscaledTime - startedAt < duration)
        {
            float progress = Mathf.Clamp01(
                (Time.unscaledTime - startedAt) / duration
            );
            float easedProgress = progress * progress * (3f - 2f * progress);
            SetFillAmount(Mathf.Lerp(safeFrom, safeTo, easedProgress));
            yield return null;
        }

        SetFillAmount(safeTo);
        gameObject.SetActive(false);
    }

    public void HideImmediate()
    {
        gameObject.SetActive(false);
    }

    private bool CacheChildren()
    {
        if (_fillRectTransform != null && _frameTransform != null)
        {
            return true;
        }

        Transform fill = transform.Find(FillObjectName);
        _frameTransform = transform.Find(FrameObjectName);
        _fillRectTransform = fill as RectTransform;

        if (_fillRectTransform == null || _frameTransform == null)
        {
            Debug.LogError(
                $"StageClearGaugeには{FillObjectName}と{FrameObjectName}が必要です。",
                this
            );
            return false;
        }

        _fullWidth = Mathf.Max(0f, _fillRectTransform.sizeDelta.x);
        _frameTransform.SetAsLastSibling();
        return true;
    }

    private void SetFillAmount(float amount)
    {
        if (!CacheChildren())
        {
            return;
        }

        Vector2 sizeDelta = _fillRectTransform.sizeDelta;
        sizeDelta.x = _fullWidth * Mathf.Clamp01(amount);
        _fillRectTransform.sizeDelta = sizeDelta;
    }
}

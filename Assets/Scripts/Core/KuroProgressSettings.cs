using UnityEngine;

/// <summary>
/// クロ累計時間による色レベル、ゲージ、リザルト判定の共有設定。
/// Resources内のKuroProgressSettings.assetを全シーンから共通利用する。
/// </summary>
[CreateAssetMenu(
    fileName = "KuroProgressSettings",
    menuName = "SiroGame/Kuro Progress Settings"
)]
public sealed class KuroProgressSettings : ScriptableObject
{
    private const string ResourcePath = "KuroProgressSettings";

    public const float DefaultLevel1EndTime = 5f;
    public const float DefaultLevel2EndTime = 10f;
    public const float DefaultLevel3EndTime = 15f;
    public const float DefaultMaximumKuroTime = 20f;
    public const float DefaultClearUpperRatio = 0.2f;
    public const float DefaultBlackLowerRatio = 0.8f;

    [Header("Color Level Times")]
    [InspectorName("Level 1 終了時間（秒）")]
    [SerializeField, Min(0f)] private float _level1EndTime = DefaultLevel1EndTime;
    [InspectorName("Level 2 終了時間（秒）")]
    [SerializeField, Min(0f)] private float _level2EndTime = DefaultLevel2EndTime;
    [InspectorName("Level 3 終了時間（秒）")]
    [SerializeField, Min(0f)] private float _level3EndTime = DefaultLevel3EndTime;
    [Tooltip("Level 5（真っ黒）の開始時間であり、ゲージが満タンになる時間です。")]
    [InspectorName("Level 4 終了・ゲージ満タン時間（秒）")]
    [SerializeField, Min(0.01f)]
    private float _maximumKuroTime = DefaultMaximumKuroTime;

    [Header("Result Thresholds")]
    [Tooltip("この割合未満をClear（白）として判定します。")]
    [InspectorName("Clear 上限割合")]
    [SerializeField, Range(0f, 1f)]
    private float _clearUpperRatio = DefaultClearUpperRatio;
    [Tooltip("この割合以上をBlack Not Clearedとして判定します。")]
    [InspectorName("Black 開始割合")]
    [SerializeField, Range(0f, 1f)]
    private float _blackLowerRatio = DefaultBlackLowerRatio;

    private static KuroProgressSettings _current;
    private static bool _loadAttempted;

    public float Level1EndTime => Mathf.Max(0f, _level1EndTime);
    public float Level2EndTime => Mathf.Max(Level1EndTime, _level2EndTime);
    public float Level3EndTime => Mathf.Max(Level2EndTime, _level3EndTime);
    public float MaximumKuroTime =>
        Mathf.Max(Mathf.Max(0.01f, Level3EndTime), _maximumKuroTime);
    public float ClearUpperRatio => Mathf.Clamp01(_clearUpperRatio);
    public float BlackLowerRatio =>
        Mathf.Clamp(_blackLowerRatio, ClearUpperRatio, 1f);

    public static KuroProgressSettings Current
    {
        get
        {
            if (!_loadAttempted)
            {
                _current = Resources.Load<KuroProgressSettings>(ResourcePath);
                _loadAttempted = true;

                if (_current == null)
                {
                    Debug.LogError(
                        "Resources/KuroProgressSettings.assetが見つかりません。"
                    );
                }
            }

            return _current;
        }
    }

    public static float SharedMaximumKuroTime =>
        Current != null ? Current.MaximumKuroTime : DefaultMaximumKuroTime;

    public static int GetSharedColorLevel(float kuroElapsedTime)
    {
        KuroProgressSettings settings = Current;
        float level1 = settings != null
            ? settings.Level1EndTime
            : DefaultLevel1EndTime;
        float level2 = settings != null
            ? settings.Level2EndTime
            : DefaultLevel2EndTime;
        float level3 = settings != null
            ? settings.Level3EndTime
            : DefaultLevel3EndTime;
        float maximum = settings != null
            ? settings.MaximumKuroTime
            : DefaultMaximumKuroTime;
        float elapsedTime = Mathf.Max(0f, kuroElapsedTime);

        if (elapsedTime < level2)
        {
            return elapsedTime < level1 ? 1 : 2;
        }

        if (elapsedTime < level3)
        {
            return 3;
        }

        return elapsedTime < maximum ? 4 : 5;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeCache()
    {
        _current = null;
        _loadAttempted = false;
    }

    private void OnValidate()
    {
        _level1EndTime = Mathf.Max(0f, _level1EndTime);
        _level2EndTime = Mathf.Max(_level1EndTime, _level2EndTime);
        _level3EndTime = Mathf.Max(_level2EndTime, _level3EndTime);
        _maximumKuroTime = Mathf.Max(
            Mathf.Max(0.01f, _level3EndTime),
            _maximumKuroTime
        );
        _clearUpperRatio = Mathf.Clamp01(_clearUpperRatio);
        _blackLowerRatio = Mathf.Clamp(
            _blackLowerRatio,
            _clearUpperRatio,
            1f
        );
    }
}

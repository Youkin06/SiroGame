using System;
using UnityEngine;

/// <summary>
/// 白／黒のワールドルールを一元管理する。
/// 見張りやギミックは <see cref="ModeChanged"/> を購読して状態を切り替える。
/// </summary>
[DefaultExecutionOrder(-100)]
public class WorldModeManager : MonoBehaviour
{
    public static WorldModeManager Instance { get; private set; }

    public WorldMode CurrentMode { get; private set; }

    /// <summary>変更後のワールドモードを通知する。</summary>
    public event Action<WorldMode> ModeChanged;

    [SerializeField] private WorldMode _initialMode = WorldMode.Shiro;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        CurrentMode = _initialMode;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void ToggleMode()
    {
        SetMode(CurrentMode == WorldMode.Shiro ? WorldMode.Kuro : WorldMode.Shiro);
    }

    public void SetMode(WorldMode nextMode)
    {
        if (CurrentMode == nextMode)
        {
            return;
        }

        CurrentMode = nextMode;
        ModeChanged?.Invoke(CurrentMode);
    }
}

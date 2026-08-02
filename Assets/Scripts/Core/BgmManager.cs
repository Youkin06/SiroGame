using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// TitleSceneから永続化し、タイトル・ゲーム・リザルトBGMを管理する。
/// 同じゲームBGMが指定されたステージ間では再生位置を維持する。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-900)]
public sealed class BgmManager : MonoBehaviour
{
    private const string TitleSceneName = "TitleScene";

    public static BgmManager Instance { get; private set; }

    [Header("BGM Clips")]
    [SerializeField] private AudioClip _titleBgm;
    [SerializeField] private AudioClip _gameplayBgm;
    [SerializeField] private AudioClip _shiroResultBgm;
    [SerializeField] private AudioClip _grayBlackResultBgm;

    [Header("Playback")]
    [SerializeField, Range(0f, 1f)] private float _volume = 1f;
    [SerializeField, Min(0f)] private float _transitionDuration = 1f;

    private AudioSource _firstSource;
    private AudioSource _secondSource;
    private AudioClip _requestedClip;
    private Coroutine _transitionCoroutine;

    public AudioClip CurrentClip => _requestedClip;
    public bool IsTransitioning => _transitionCoroutine != null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        AudioSource[] sources = GetComponents<AudioSource>();
        if (sources.Length < 2)
        {
            Debug.LogError(
                "BgmManagerにはクロスフェード用AudioSourceが2つ必要です。",
                this
            );
            enabled = false;
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        _firstSource = sources[0];
        _secondSource = sources[1];
        ConfigureSource(_firstSource);
        ConfigureSource(_secondSource);
        SceneManager.sceneLoaded += OnSceneLoaded;

        if (SceneManager.GetActiveScene().name != TitleSceneName)
        {
            PlayGameplayBgm();
        }
    }

    private void OnDestroy()
    {
        if (Instance != this)
        {
            return;
        }

        SceneManager.sceneLoaded -= OnSceneLoaded;
        Instance = null;
    }

    public void PlayTitleBgm()
    {
        PlayBgm(_titleBgm, "Title BGM");
    }

    public void PlayGameplayBgm()
    {
        PlayBgm(_gameplayBgm, "Gameplay BGM");
    }

    public void PlayResultBgm(GameResultRank rank)
    {
        bool isShiroResult = rank == GameResultRank.Clear;
        PlayBgm(
            isShiroResult ? _shiroResultBgm : _grayBlackResultBgm,
            isShiroResult ? "Shiro Result BGM" : "Gray/Black Result BGM"
        );
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != TitleSceneName)
        {
            PlayGameplayBgm();
        }
    }

    private void PlayBgm(AudioClip clip, string settingName)
    {
        if (clip == null)
        {
            Debug.LogWarning($"BgmManagerの{settingName}が未設定です。", this);
            return;
        }

        if (_requestedClip == clip)
        {
            return;
        }

        if (clip.loadState == AudioDataLoadState.Unloaded)
        {
            clip.LoadAudioData();
        }

        _requestedClip = clip;
        if (_transitionCoroutine != null)
        {
            StopCoroutine(_transitionCoroutine);
        }

        _transitionCoroutine = StartCoroutine(CrossFadeTo(clip));
    }

    private IEnumerator CrossFadeTo(AudioClip nextClip)
    {
        AudioSource targetSource = SelectTargetSource(nextClip);
        AudioSource fadingOutSource = targetSource == _firstSource
            ? _secondSource
            : _firstSource;

        if (targetSource.clip != nextClip || !targetSource.isPlaying)
        {
            targetSource.Stop();
            targetSource.clip = nextClip;
            targetSource.volume = 0f;
            targetSource.Play();
        }

        float targetStartVolume = targetSource.volume;
        float fadingOutStartVolume = fadingOutSource.isPlaying
            ? fadingOutSource.volume
            : 0f;
        float duration = Mathf.Max(0f, _transitionDuration);

        if (duration <= 0f)
        {
            targetSource.volume = _volume;
            StopSource(fadingOutSource);
            _transitionCoroutine = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float easedProgress = progress * progress * (3f - 2f * progress);
            targetSource.volume = Mathf.Lerp(
                targetStartVolume,
                _volume,
                easedProgress
            );
            fadingOutSource.volume = Mathf.Lerp(
                fadingOutStartVolume,
                0f,
                easedProgress
            );
            yield return null;
        }

        targetSource.volume = _volume;
        StopSource(fadingOutSource);
        _transitionCoroutine = null;
    }

    private AudioSource SelectTargetSource(AudioClip nextClip)
    {
        if (_firstSource.clip == nextClip && _firstSource.isPlaying)
        {
            return _firstSource;
        }

        if (_secondSource.clip == nextClip && _secondSource.isPlaying)
        {
            return _secondSource;
        }

        if (!_firstSource.isPlaying)
        {
            return _firstSource;
        }

        if (!_secondSource.isPlaying)
        {
            return _secondSource;
        }

        return _firstSource.volume <= _secondSource.volume
            ? _firstSource
            : _secondSource;
    }

    private static void ConfigureSource(AudioSource source)
    {
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.volume = 0f;
    }

    private static void StopSource(AudioSource source)
    {
        source.Stop();
        source.clip = null;
        source.volume = 0f;
    }
}

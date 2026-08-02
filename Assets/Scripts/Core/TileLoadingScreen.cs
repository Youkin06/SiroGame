using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 左上を起点に、マンハッタン距離が同じタイルを同時に反転させる
/// フルスクリーンのシーン遷移用ローディング画面。
/// 最初に開くシーンのGameObjectへ1つだけアタッチして使用する。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-1000)]
public sealed class TileLoadingScreen : MonoBehaviour
{
    private const string CanvasPrefabResourcePath = "UI/TileLoadingCanvas";

    public static TileLoadingScreen Instance { get; private set; }

    [Header("Grid")]
    [SerializeField, Min(1)] private int _columns = 16;
    [SerializeField, Min(1)] private int _rows = 9;
    [SerializeField, Min(0f)] private float _tileGap = 1f;

    [Header("Animation")]
    [SerializeField, Min(0.01f)] private float _flipDuration = 0.2f;
    [SerializeField, Min(0f)] private float _waveDelay = 0.045f;
    [SerializeField, Min(0f)] private float _minimumVisibleTime = 0.6f;
    [SerializeField] private Color _frontColor = Color.white;
    [SerializeField] private Color _backColor = Color.black;

    private readonly List<LoadingTile> _tiles = new();
    private Canvas _canvas;
    private CanvasGroup _canvasGroup;
    private RectTransform _tileRoot;
    private StageClearGaugeView _stageClearGauge;
    private Coroutine _loadCoroutine;
    private bool _isWhite;

    public bool IsLoading => _loadCoroutine != null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        if (!CreateUi())
        {
            enabled = false;
            return;
        }

        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (_loadCoroutine != null || WorldModeManager.Instance == null)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
        {
            RetryCurrentStage();
        }
    }

    /// <summary>
    /// クリア済みステージ分のクロ累計は維持し、現在のステージだけを再読込する。
    /// </summary>
    public void RetryCurrentStage()
    {
        if (_loadCoroutine != null)
        {
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.buildIndex < 0)
        {
            Debug.LogError(
                $"シーン '{activeScene.name}' がBuild Settingsにありません。" +
                "リトライするにはシーンを登録してください。",
                this
            );
            return;
        }

        LoadScene(activeScene.buildIndex);
    }

    /// <summary>Build Settingsに登録済みのシーン名を指定して遷移する。</summary>
    public void LoadScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError("読み込むシーン名が空です。", this);
            return;
        }

        if (_loadCoroutine != null)
        {
            return;
        }

        _loadCoroutine = StartCoroutine(LoadSceneRoutine(sceneName, false, 0f));
    }

    /// <summary>
    /// タイトルから新しくゲームを開始する。ステージ間のクロ累計をリセットする。
    /// </summary>
    public void LoadNewGameScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError("読み込むシーン名が空です。", this);
            return;
        }

        if (_loadCoroutine != null)
        {
            return;
        }

        KuroTimeProgress.Reset();
        GameResultState.Reset();
        _loadCoroutine = StartCoroutine(
            LoadSceneRoutine(sceneName, false, 0f, false)
        );
    }

    /// <summary>Build Settingsに登録済みのシーン番号を指定して遷移する。</summary>
    public void LoadScene(int buildIndex)
    {
        if (_loadCoroutine != null)
        {
            return;
        }

        _loadCoroutine = StartCoroutine(LoadSceneRoutine(buildIndex, false, 0f));
    }

    /// <summary>
    /// ステージクリア用。前回の累計位置から今回の累計位置までゲージを伸ばし、
    /// Build Settings上の次ステージへ遷移する。
    /// </summary>
    public void LoadStageClearScene(int buildIndex, float totalKuroElapsedTime)
    {
        if (_loadCoroutine != null)
        {
            return;
        }

        _loadCoroutine = StartCoroutine(
            LoadSceneRoutine(buildIndex, true, totalKuroElapsedTime)
        );
    }

    /// <summary>
    /// 最終ステージ用。ゲージ表示後にTitleSceneへ戻し、リザルトを受け渡す。
    /// </summary>
    public void LoadFinalResultScene(
        string titleSceneName,
        float totalKuroElapsedTime
    )
    {
        if (string.IsNullOrWhiteSpace(titleSceneName))
        {
            Debug.LogError("戻り先のTitleScene名が空です。", this);
            return;
        }

        if (_loadCoroutine != null)
        {
            return;
        }

        _loadCoroutine = StartCoroutine(
            LoadSceneRoutine(
                titleSceneName,
                true,
                totalKuroElapsedTime,
                true
            )
        );
    }

    private IEnumerator LoadSceneRoutine(
        string sceneName,
        bool showStageClearGauge,
        float totalKuroElapsedTime,
        bool prepareFinalResult = false
    )
    {
        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
        if (operation == null)
        {
            Debug.LogError($"シーン '{sceneName}' を非同期ロードできません。Build Settingsを確認してください。", this);
            _loadCoroutine = null;
            yield break;
        }

        yield return RunTransition(
            operation,
            showStageClearGauge,
            totalKuroElapsedTime,
            prepareFinalResult
        );
    }

    private IEnumerator LoadSceneRoutine(
        int buildIndex,
        bool showStageClearGauge,
        float totalKuroElapsedTime
    )
    {
        AsyncOperation operation = SceneManager.LoadSceneAsync(buildIndex);
        if (operation == null)
        {
            Debug.LogError($"シーン番号 {buildIndex} を非同期ロードできません。Build Settingsを確認してください。", this);
            _loadCoroutine = null;
            yield break;
        }

        yield return RunTransition(
            operation,
            showStageClearGauge,
            totalKuroElapsedTime,
            false
        );
    }

    private IEnumerator RunTransition(
        AsyncOperation operation,
        bool showStageClearGauge,
        float totalKuroElapsedTime,
        bool prepareFinalResult
    )
    {
        SetVisible(true);
        SetTilesHidden();
        operation.allowSceneActivation = false;

        float startedAt = Time.unscaledTime;
        yield return FlipAllTiles(true);

        float previousKuroTime = KuroTimeProgress.CompletedStageTime;
        float nextKuroTime = Mathf.Max(
            previousKuroTime,
            Mathf.Max(0f, totalKuroElapsedTime)
        );

        if (showStageClearGauge)
        {
            yield return _stageClearGauge.Play(
                previousKuroTime,
                nextKuroTime
            );
        }

        while (operation.progress < 0.9f ||
               Time.unscaledTime - startedAt < _minimumVisibleTime)
        {
            yield return null;
        }

        if (showStageClearGauge)
        {
            KuroTimeProgress.CommitStageTotal(nextKuroTime);
        }

        if (prepareFinalResult)
        {
            GameResultState.Prepare(
                nextKuroTime,
                _stageClearGauge.MaximumKuroTime
            );
        }

        operation.allowSceneActivation = true;
        while (!operation.isDone)
        {
            yield return null;
        }

        yield return null;
        yield return FlipAllTiles(false);
        SetVisible(false);
        _loadCoroutine = null;
    }

    private IEnumerator FlipAllTiles(bool makeWhite)
    {
        if (_isWhite == makeWhite)
        {
            yield break;
        }

        int maximumDistance = _columns + _rows - 2;
        for (int distance = 0; distance <= maximumDistance; distance++)
        {
            foreach (LoadingTile tile in _tiles)
            {
                if (tile.DistanceFromTopLeft == distance)
                {
                    if (makeWhite)
                    {
                        // 波が到達するまでタイルは透明のままにし、
                        // 到達した瞬間に黒い表面を表示してから反転させる。
                        tile.Image.color = _frontColor;
                    }

                    StartCoroutine(FlipTile(tile, makeWhite));
                }
            }

            if (_waveDelay > 0f)
            {
                yield return new WaitForSecondsRealtime(_waveDelay);
            }
        }

        float remainingTime = Mathf.Max(0f, _flipDuration - _waveDelay);
        if (remainingTime > 0f)
        {
            yield return new WaitForSecondsRealtime(remainingTime);
        }

        _isWhite = makeWhite;
    }

    private IEnumerator FlipTile(LoadingTile tile, bool makeWhite)
    {
        float startedAt = Time.unscaledTime;
        float halfDuration = Mathf.Max(0.005f, _flipDuration * 0.5f);
        Color fromColor = makeWhite ? _frontColor : _backColor;
        Color toColor = makeWhite ? _backColor : _frontColor;

        while (Time.unscaledTime - startedAt < _flipDuration)
        {
            float elapsed = Time.unscaledTime - startedAt;
            float normalized = Mathf.Clamp01(elapsed / _flipDuration);
            float angle = makeWhite
                ? normalized * 180f
                : (1f - normalized) * 180f;
            tile.RectTransform.localRotation = Quaternion.Euler(0f, angle, 0f);

            if (elapsed < halfDuration)
            {
                tile.Image.color = fromColor;
            }
            else if (!makeWhite)
            {
                // 裏面の白から表面の黒へ戻った後は、黒いタイル自体を
                // 透明にして、左上から新しいゲーム画面を見せていく。
                float fadeProgress = Mathf.InverseLerp(
                    halfDuration,
                    _flipDuration,
                    elapsed
                );
                Color fadingFront = _frontColor;
                fadingFront.a *= 1f - fadeProgress;
                tile.Image.color = fadingFront;
            }
            else
            {
                tile.Image.color = toColor;
            }

            yield return null;
        }

        tile.RectTransform.localRotation = Quaternion.Euler(
            0f,
            makeWhite ? 180f : 0f,
            0f
        );
        tile.Image.color = makeWhite ? toColor : Transparent(_frontColor);
    }

    private bool CreateUi()
    {
        _canvas = GetComponentInChildren<Canvas>(true);
        if (_canvas == null)
        {
            GameObject canvasPrefab = Resources.Load<GameObject>(
                CanvasPrefabResourcePath
            );
            if (canvasPrefab == null)
            {
                Debug.LogError(
                    $"Resources/{CanvasPrefabResourcePath}.prefabが見つかりません。",
                    this
                );
                return false;
            }

            GameObject canvasObject = Instantiate(canvasPrefab, transform, false);
            _canvas = canvasObject.GetComponent<Canvas>();
        }

        if (_canvas == null)
        {
            Debug.LogError(
                "TileLoadingCanvas PrefabにCanvasがありません。",
                this
            );
            return false;
        }

        _canvasGroup = _canvas.GetComponent<CanvasGroup>();
        _tileRoot = _canvas.transform.Find("Tiles") as RectTransform;
        _stageClearGauge = _canvas.GetComponentInChildren<StageClearGaugeView>(true);
        if (_canvasGroup == null || _tileRoot == null || _stageClearGauge == null)
        {
            Debug.LogError(
                "TileLoadingCanvasにはCanvasGroup、Tiles、StageClearGaugeが必要です。",
                this
            );
            return false;
        }

        BuildTiles();
        _stageClearGauge.HideImmediate();
        return true;
    }

    private void BuildTiles()
    {
        foreach (Transform child in _tileRoot)
        {
            Destroy(child.gameObject);
        }

        _tiles.Clear();
        _isWhite = false;

        float horizontalInset = _tileGap * 0.5f;
        float verticalInset = _tileGap * 0.5f;

        for (int row = 0; row < _rows; row++)
        {
            for (int column = 0; column < _columns; column++)
            {
                GameObject tileObject = new($"Tile_{column}_{row}", typeof(RectTransform), typeof(Image));
                tileObject.transform.SetParent(_tileRoot, false);

                RectTransform rectTransform = tileObject.GetComponent<RectTransform>();
                rectTransform.anchorMin = new Vector2(
                    (float)column / _columns,
                    1f - (float)(row + 1) / _rows
                );
                rectTransform.anchorMax = new Vector2(
                    (float)(column + 1) / _columns,
                    1f - (float)row / _rows
                );
                rectTransform.offsetMin = new Vector2(horizontalInset, verticalInset);
                rectTransform.offsetMax = new Vector2(-horizontalInset, -verticalInset);

                Image image = tileObject.GetComponent<Image>();
                image.color = Transparent(_frontColor);
                image.raycastTarget = false;
                _tiles.Add(new LoadingTile(rectTransform, image, column + row));
            }
        }
    }

    private void SetVisible(bool visible)
    {
        // Screen Space OverlayのUIはURPのFull Screen Passより後に描画される。
        // 演出後もCanvasを有効のまま残さず、ドット／アウトラインの最終出力を
        // 確実にそのまま画面へ出す。
        _canvas.enabled = visible;

        if (!visible)
        {
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;
            return;
        }

        _canvasGroup.alpha = visible ? 1f : 0f;
        _canvasGroup.blocksRaycasts = visible;
        _canvasGroup.interactable = visible;
    }

    private void SetTilesHidden()
    {
        _isWhite = false;
        _stageClearGauge.HideImmediate();

        foreach (LoadingTile tile in _tiles)
        {
            tile.RectTransform.localRotation = Quaternion.identity;
            tile.Image.color = Transparent(_frontColor);
        }
    }

    private static Color Transparent(Color color)
    {
        color.a = 0f;
        return color;
    }

    private sealed class LoadingTile
    {
        public RectTransform RectTransform { get; }
        public Image Image { get; }
        public int DistanceFromTopLeft { get; }

        public LoadingTile(RectTransform rectTransform, Image image, int distanceFromTopLeft)
        {
            RectTransform = rectTransform;
            Image = image;
            DistanceFromTopLeft = distanceFromTopLeft;
        }
    }
}

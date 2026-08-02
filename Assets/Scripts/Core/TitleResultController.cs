using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 初回Title表示と、最終ステージ後のリザルト表示を切り替える。
/// Scene内の参照はHierarchy名から取得し、Inspector参照は持たない。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class TitleResultController : MonoBehaviour
{
    private static readonly int BaseColorHash = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorHash = Shader.PropertyToID("_Color");

    [Header("Result Animation")]
    [SerializeField, Min(0.01f)] private float _characterInterval = 0.1f;
    [SerializeField, Min(0.01f)] private float _minimumPartDuration = 0.4f;
    [SerializeField, Min(0f)] private float _partInterval = 0.2f;
    [SerializeField, Min(0f)] private float _resultImageFadeDuration = 1f;
    [SerializeField, Min(0f)] private float _menuRevealDelay = 2f;

    [Header("Clear Colors")]
    [SerializeField] private Color _clearPlayerColor = Color.white;
    [SerializeField] private Color _clearBackgroundColor =
        new(0.84f, 0.84f, 0.84f, 1f);

    [Header("Gray Not Cleared Colors")]
    [SerializeField] private Color _grayPlayerColor =
        new(0.5f, 0.5f, 0.5f, 1f);
    [SerializeField] private Color _grayBackgroundColor =
        new(0.08f, 0.08f, 0.08f, 1f);

    [Header("Black Not Cleared Colors")]
    [SerializeField] private Color _blackPlayerColor = Color.black;
    [SerializeField] private Color _blackBackgroundColor =
        new(0.08f, 0.08f, 0.08f, 1f);

    private readonly List<ResultVisual> _resultVisuals = new();
    private readonly List<PlayerMaterialBinding> _playerMaterials = new();
    private MaterialPropertyBlock _propertyBlock;
    private TitleMenuController _titleMenu;
    private Camera _camera;
    private GameResultSnapshot _result;
    private ResultVisual _selectedVisual;
    private bool _isResultScreen;

    public bool IsPlayingResult { get; private set; }

    private void Awake()
    {
        _propertyBlock = new MaterialPropertyBlock();
        _titleMenu = FindFirstObjectByType<TitleMenuController>();
        _camera = Camera.main;

        GameObject titleCanvasObject = GameObject.Find("TitleCanvas");
        GameObject playerObject = GameObject.Find("Player");
        if (_titleMenu == null || _camera == null || titleCanvasObject == null ||
            playerObject == null || !CacheResultVisuals(titleCanvasObject.transform))
        {
            Debug.LogError(
                "TitleResultControllerに必要なTitleCanvas、Player、Main Camera、" +
                "TitleMenuController、リザルトUIが見つかりません。",
                this
            );
            enabled = false;
            return;
        }

        CachePlayerMaterials(
            playerObject.GetComponentsInChildren<Renderer>(true)
        );
        if (_playerMaterials.Count == 0)
        {
            Debug.LogError("TitleSceneのPlayerに色設定可能なRendererがありません。", this);
            enabled = false;
            return;
        }

        _isResultScreen = GameResultState.TryConsume(out _result);
        HideAllResultVisuals();

        if (!_isResultScreen)
        {
            ShowInitialTitle();
            return;
        }

        _selectedVisual = GetVisual(_result.Rank);
        PrepareResultVisual(_selectedVisual);
        _titleMenu.SetMenuVisible(false);
        ApplyResultColors(_result.Rank);
    }

    private IEnumerator Start()
    {
        if (!enabled || !_isResultScreen)
        {
            yield break;
        }

        TileLoadingScreen loadingScreen = TileLoadingScreen.Instance;
        while (loadingScreen != null && loadingScreen.IsLoading)
        {
            yield return null;
        }

        IsPlayingResult = true;
        foreach (TMP_Text textPart in _selectedVisual.TextParts)
        {
            yield return RevealTextPart(textPart);
            if (_partInterval > 0f)
            {
                yield return new WaitForSecondsRealtime(_partInterval);
            }
        }

        yield return FadeInResultImage(_selectedVisual.ResultImage);

        if (_menuRevealDelay > 0f)
        {
            yield return new WaitForSecondsRealtime(_menuRevealDelay);
        }

        _titleMenu.SetMenuVisible(true);
        IsPlayingResult = false;
    }

    private bool CacheResultVisuals(Transform titleCanvas)
    {
        _resultVisuals.Clear();
        return TryAddVisual(
                   titleCanvas,
                   GameResultRank.Clear,
                   "Clear_Title",
                   "SIRO",
                   "Clear"
               ) &&
               TryAddVisual(
                   titleCanvas,
                   GameResultRank.GrayNotCleared,
                   "NotClear_Gray",
                   "SIRO",
                   "NotCleared_Gray"
               ) &&
               TryAddVisual(
                   titleCanvas,
                   GameResultRank.BlackNotCleared,
                   "NotClear_Black",
                   "KURO",
                   "NotCleared_Black"
               );
    }

    private bool TryAddVisual(
        Transform titleCanvas,
        GameResultRank rank,
        string sentenceName,
        string keywordName,
        string resultImageName
    )
    {
        Transform sentence = titleCanvas.Find(sentenceName);
        Transform resultImageTransform = titleCanvas.Find(resultImageName);
        if (sentence == null || resultImageTransform == null)
        {
            return false;
        }

        TMP_Text first = GetText(sentence, "anata");
        TMP_Text keyword = GetText(sentence, keywordName);
        TMP_Text last = GetText(sentence, "desu");
        Image resultImage = resultImageTransform.GetComponent<Image>();
        if (first == null || keyword == null || last == null || resultImage == null)
        {
            return false;
        }

        _resultVisuals.Add(new ResultVisual(
            rank,
            sentence.gameObject,
            new[] { first, keyword, last },
            resultImage
        ));
        return true;
    }

    private void ShowInitialTitle()
    {
        ResultVisual clearVisual = GetVisual(GameResultRank.Clear);
        clearVisual.SentenceRoot.SetActive(true);
        foreach (TMP_Text textPart in clearVisual.TextParts)
        {
            SetTextAlpha(textPart, 1f);
            textPart.maxVisibleCharacters = int.MaxValue;
        }

        clearVisual.ResultImage.gameObject.SetActive(false);
        _titleMenu.SetMenuVisible(true);
        ApplyResultColors(GameResultRank.Clear);
    }

    private void PrepareResultVisual(ResultVisual visual)
    {
        visual.SentenceRoot.SetActive(true);
        foreach (TMP_Text textPart in visual.TextParts)
        {
            textPart.ForceMeshUpdate();
            textPart.maxVisibleCharacters = 0;
            SetTextAlpha(textPart, 0f);
        }

        visual.ResultImage.gameObject.SetActive(false);
    }

    private IEnumerator RevealTextPart(TMP_Text textPart)
    {
        textPart.ForceMeshUpdate();
        int characterCount = Mathf.Max(1, textPart.textInfo.characterCount);
        float duration = Mathf.Max(
            _minimumPartDuration,
            characterCount * _characterInterval
        );
        float startedAt = Time.unscaledTime;

        while (Time.unscaledTime - startedAt < duration)
        {
            float progress = Mathf.Clamp01(
                (Time.unscaledTime - startedAt) / duration
            );
            textPart.maxVisibleCharacters = Mathf.Clamp(
                Mathf.CeilToInt(progress * characterCount),
                0,
                characterCount
            );
            SetTextAlpha(textPart, SmoothStep01(progress));
            yield return null;
        }

        textPart.maxVisibleCharacters = int.MaxValue;
        SetTextAlpha(textPart, 1f);
    }

    private IEnumerator FadeInResultImage(Image resultImage)
    {
        resultImage.gameObject.SetActive(true);
        SetImageAlpha(resultImage, 0f);
        float duration = Mathf.Max(0f, _resultImageFadeDuration);
        if (duration <= 0f)
        {
            SetImageAlpha(resultImage, 1f);
            yield break;
        }

        float startedAt = Time.unscaledTime;
        while (Time.unscaledTime - startedAt < duration)
        {
            float progress = Mathf.Clamp01(
                (Time.unscaledTime - startedAt) / duration
            );
            SetImageAlpha(resultImage, SmoothStep01(progress));
            yield return null;
        }

        SetImageAlpha(resultImage, 1f);
    }

    private void HideAllResultVisuals()
    {
        foreach (ResultVisual visual in _resultVisuals)
        {
            visual.SentenceRoot.SetActive(false);
            visual.ResultImage.gameObject.SetActive(false);
        }
    }

    private ResultVisual GetVisual(GameResultRank rank)
    {
        foreach (ResultVisual visual in _resultVisuals)
        {
            if (visual.Rank == rank)
            {
                return visual;
            }
        }

        return null;
    }

    private void CachePlayerMaterials(Renderer[] renderers)
    {
        foreach (Renderer playerRenderer in renderers)
        {
            Material[] materials = playerRenderer.sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material == null)
                {
                    continue;
                }

                bool hasBaseColor = material.HasProperty(BaseColorHash);
                bool hasColor = material.HasProperty(ColorHash);
                if (!hasBaseColor && !hasColor)
                {
                    continue;
                }

                _playerMaterials.Add(new PlayerMaterialBinding(
                    playerRenderer,
                    materialIndex,
                    playerRenderer.gameObject.name == "eye",
                    hasBaseColor,
                    hasColor
                ));
            }
        }
    }

    private void ApplyResultColors(GameResultRank rank)
    {
        Color playerColor;
        Color backgroundColor;
        switch (rank)
        {
            case GameResultRank.GrayNotCleared:
                playerColor = _grayPlayerColor;
                backgroundColor = _grayBackgroundColor;
                break;
            case GameResultRank.BlackNotCleared:
                playerColor = _blackPlayerColor;
                backgroundColor = _blackBackgroundColor;
                break;
            default:
                playerColor = _clearPlayerColor;
                backgroundColor = _clearBackgroundColor;
                break;
        }

        _camera.backgroundColor = backgroundColor;
        Color eyeColor = rank == GameResultRank.BlackNotCleared
            ? Color.white
            : Color.black;

        foreach (PlayerMaterialBinding binding in _playerMaterials)
        {
            if (binding.Renderer == null)
            {
                continue;
            }

            binding.Renderer.GetPropertyBlock(
                _propertyBlock,
                binding.MaterialIndex
            );
            Color color = binding.IsEye ? eyeColor : playerColor;
            if (binding.HasBaseColor)
            {
                _propertyBlock.SetColor(BaseColorHash, color);
            }

            if (binding.HasColor)
            {
                _propertyBlock.SetColor(ColorHash, color);
            }

            binding.Renderer.SetPropertyBlock(
                _propertyBlock,
                binding.MaterialIndex
            );
            _propertyBlock.Clear();
        }

        ApplyUiColors(
            GetVisual(rank),
            playerColor,
            rank == GameResultRank.Clear ? Color.black : Color.white
        );
    }

    private static void ApplyUiColors(
        ResultVisual visual,
        Color keywordColor,
        Color contrastColor
    )
    {
        SetTextRgb(visual.TextParts[0], contrastColor);
        SetTextRgb(visual.TextParts[1], keywordColor);
        SetTextRgb(visual.TextParts[2], contrastColor);
        SetImageRgb(visual.ResultImage, contrastColor);
    }

    private static TMP_Text GetText(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        return child != null ? child.GetComponent<TMP_Text>() : null;
    }

    private static void SetTextAlpha(TMP_Text text, float alpha)
    {
        Color color = text.color;
        color.a = Mathf.Clamp01(alpha);
        text.color = color;
    }

    private static void SetTextRgb(TMP_Text text, Color rgb)
    {
        Color color = text.color;
        color.r = rgb.r;
        color.g = rgb.g;
        color.b = rgb.b;
        text.color = color;
    }

    private static void SetImageAlpha(Image image, float alpha)
    {
        Color color = image.color;
        color.a = Mathf.Clamp01(alpha);
        image.color = color;
    }

    private static void SetImageRgb(Image image, Color rgb)
    {
        Color color = image.color;
        color.r = rgb.r;
        color.g = rgb.g;
        color.b = rgb.b;
        image.color = color;
    }

    private static float SmoothStep01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private sealed class ResultVisual
    {
        public GameResultRank Rank { get; }
        public GameObject SentenceRoot { get; }
        public TMP_Text[] TextParts { get; }
        public Image ResultImage { get; }

        public ResultVisual(
            GameResultRank rank,
            GameObject sentenceRoot,
            TMP_Text[] textParts,
            Image resultImage
        )
        {
            Rank = rank;
            SentenceRoot = sentenceRoot;
            TextParts = textParts;
            ResultImage = resultImage;
        }
    }

    private sealed class PlayerMaterialBinding
    {
        public Renderer Renderer { get; }
        public int MaterialIndex { get; }
        public bool IsEye { get; }
        public bool HasBaseColor { get; }
        public bool HasColor { get; }

        public PlayerMaterialBinding(
            Renderer renderer,
            int materialIndex,
            bool isEye,
            bool hasBaseColor,
            bool hasColor
        )
        {
            Renderer = renderer;
            MaterialIndex = materialIndex;
            IsEye = isEye;
            HasBaseColor = hasBaseColor;
            HasColor = hasColor;
        }
    }
}

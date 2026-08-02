using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// WorldModeの見た目を、Playerを起点に広がる円形ワイプで切り替える。
/// Camera、Player、Rendererは既存シーンから取得し、自動生成は行わない。
/// </summary>
[DefaultExecutionOrder(-90)]
public sealed class WorldModeVisualTransition : MonoBehaviour
{
    private static readonly int VisualEnabledHash =
        Shader.PropertyToID("_WorldModeVisualEnabled");
    private static readonly int OriginHash =
        Shader.PropertyToID("_WorldModeTransitionOrigin");
    private static readonly int ProgressHash =
        Shader.PropertyToID("_WorldModeTransitionProgress");
    private static readonly int FromModeHash =
        Shader.PropertyToID("_WorldModeTransitionFrom");
    private static readonly int ToModeHash =
        Shader.PropertyToID("_WorldModeTransitionTo");
    private static readonly int FeatherHash =
        Shader.PropertyToID("_WorldModeTransitionFeather");
    private static readonly int ShiroBackgroundHash =
        Shader.PropertyToID("_WorldModeShiroBackground");
    private static readonly int KuroBackgroundHash =
        Shader.PropertyToID("_WorldModeKuroBackground");
    private static readonly int BaseColorHash = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorHash = Shader.PropertyToID("_Color");

    [SerializeField, Min(0.01f)] private float _transitionDuration = 0.6f;
    [SerializeField, Range(0.001f, 0.2f)] private float _edgeSoftness = 0.04f;
    [Header("Kuro Colors")]
    [FormerlySerializedAs("_kuroBackground")]
    [InspectorName("Kuro Player Color")]
    [SerializeField] private Color _kuroPlayerColor = Color.black;
    [InspectorName("Kuro Background Color")]
    [SerializeField] private Color _kuroBackgroundColor = Color.black;

    [Header("Shiro Player Color Levels")]
    [InspectorName("Level 1 Color（真っ白）")]
    [SerializeField] private Color _level1Color = Color.white;
    [InspectorName("Level 2 Color（薄い灰色）")]
    [SerializeField] private Color _level2Color = new(0.75f, 0.75f, 0.75f, 1f);
    [InspectorName("Level 3 Color（灰色）")]
    [SerializeField] private Color _level3Color = new(0.5f, 0.5f, 0.5f, 1f);
    [InspectorName("Level 4 Color（濃い灰色）")]
    [SerializeField] private Color _level4Color = new(0.25f, 0.25f, 0.25f, 1f);
    [InspectorName("Level 5 Color（真っ黒）")]
    [SerializeField] private Color _level5Color = Color.black;

    [Header("Shiro Background Color Levels")]
    [InspectorName("Level 1 Background Color")]
    [SerializeField] private Color _level1BackgroundColor = Color.white;
    [InspectorName("Level 2 Background Color")]
    [SerializeField] private Color _level2BackgroundColor =
        new(0.75f, 0.75f, 0.75f, 1f);
    [InspectorName("Level 3 Background Color")]
    [SerializeField] private Color _level3BackgroundColor =
        new(0.5f, 0.5f, 0.5f, 1f);
    [InspectorName("Level 4 Background Color")]
    [SerializeField] private Color _level4BackgroundColor =
        new(0.25f, 0.25f, 0.25f, 1f);
    [InspectorName("Level 5 Background Color")]
    [SerializeField] private Color _level5BackgroundColor = Color.black;

    public bool IsTransitioning { get; private set; }
    public float TransitionProgress => _progress;
    public Vector2 TransitionOrigin => _origin;
    public Color CurrentShiroPlayerColor { get; private set; } = Color.white;
    public Color CurrentShiroBackgroundColor { get; private set; } = Color.white;
    public int CurrentShiroColorLevel { get; private set; } = 1;

    private readonly List<MaterialColorBinding> _playerMaterials = new();
    private MaterialPropertyBlock _propertyBlock;
    private WorldModeManager _worldModeManager;
    private Transform _player;
    private Camera _camera;
    private Vector2 _origin = new(0.5f, 0.5f);
    private float _progress = 1f;
    private float _targetProgress = 1f;
    private float _fromMode;
    private float _toMode;
    private float _settledMode;

    private void Awake()
    {
        _propertyBlock = new MaterialPropertyBlock();
        _worldModeManager = GetComponent<WorldModeManager>();

        PlayerMove playerMove = FindFirstObjectByType<PlayerMove>();
        _player = playerMove != null ? playerMove.transform : null;

        _camera = Camera.main;
        if (_camera == null)
        {
            _camera = FindFirstObjectByType<Camera>();
        }

        if (_player != null)
        {
            CachePlayerMaterials(_player.GetComponentsInChildren<Renderer>(true));
        }
    }

    private void Start()
    {
        if (!HasRequiredReferences())
        {
            enabled = false;
            return;
        }

        _settledMode = ModeToFloat(_worldModeManager.CurrentMode);
        _fromMode = _settledMode;
        _toMode = _settledMode;
        _progress = 1f;
        _targetProgress = 1f;
        IsTransitioning = false;
        UpdateCurrentShiroAppearance();

        _worldModeManager.ModeChanged += OnModeChanged;
        ApplyVisualState();
    }

    private void Update()
    {
        if (!IsTransitioning)
        {
            return;
        }

        float duration = Mathf.Max(0.01f, _transitionDuration);
        _progress = Mathf.MoveTowards(
            _progress,
            _targetProgress,
            Time.unscaledDeltaTime / duration
        );
        ApplyVisualState();

        if (!Mathf.Approximately(_progress, _targetProgress))
        {
            return;
        }

        _settledMode = _targetProgress >= 0.5f ? _toMode : _fromMode;
        _fromMode = _settledMode;
        _toMode = _settledMode;
        _progress = 1f;
        _targetProgress = 1f;
        IsTransitioning = false;
        ApplyVisualState();
    }

    private void OnDisable()
    {
        if (_worldModeManager != null)
        {
            _worldModeManager.ModeChanged -= OnModeChanged;
        }

        Shader.SetGlobalFloat(VisualEnabledHash, 0f);
        RestorePlayerColors();
    }

    private void OnModeChanged(WorldMode nextMode)
    {
        float nextModeValue = ModeToFloat(nextMode);

        if (nextMode == WorldMode.Shiro)
        {
            // クロ時間は累積値を使い、シロへ戻るたびに現在の段階を確定する。
            UpdateCurrentShiroAppearance();
        }

        if (IsTransitioning)
        {
            // 同じ円と起点を維持し、現在位置から正確に逆再生する。
            _targetProgress = Mathf.Approximately(nextModeValue, _toMode) ? 1f : 0f;
            return;
        }

        if (Mathf.Approximately(nextModeValue, _settledMode))
        {
            return;
        }

        CaptureTransitionOrigin();
        _fromMode = _settledMode;
        _toMode = nextModeValue;
        _progress = 0f;
        _targetProgress = 1f;
        IsTransitioning = true;
        ApplyVisualState();
    }

    private void CaptureTransitionOrigin()
    {
        Vector3 viewportPosition = _camera.WorldToViewportPoint(_player.position);
        _origin = new Vector2(
            Mathf.Clamp01(viewportPosition.x),
            Mathf.Clamp01(viewportPosition.y)
        );
    }

    private void ApplyVisualState()
    {
        float feather = Mathf.Max(0.001f, _edgeSoftness);
        Shader.SetGlobalFloat(VisualEnabledHash, 1f);
        Shader.SetGlobalVector(OriginHash, new Vector4(_origin.x, _origin.y, 0f, 0f));
        Shader.SetGlobalFloat(ProgressHash, _progress);
        Shader.SetGlobalFloat(FromModeHash, _fromMode);
        Shader.SetGlobalFloat(ToModeHash, _toMode);
        Shader.SetGlobalFloat(FeatherHash, feather);
        Shader.SetGlobalColor(ShiroBackgroundHash, CurrentShiroBackgroundColor);
        Shader.SetGlobalColor(KuroBackgroundHash, _kuroBackgroundColor);

        float playerMode = GetModeAtTransitionCenter(feather);
        ApplyPlayerMode(playerMode, CurrentShiroPlayerColor);
    }

    private float GetModeAtTransitionCenter(float feather)
    {
        if (!IsTransitioning)
        {
            return _settledMode;
        }

        float aspect = Mathf.Max(0.01f, _camera.aspect);
        Vector2 farthestCorner = new(
            Mathf.Max(_origin.x, 1f - _origin.x) * aspect,
            Mathf.Max(_origin.y, 1f - _origin.y)
        );
        float maximumRadius = farthestCorner.magnitude;
        float easedProgress = SmoothStep01(_progress);
        float radius = Mathf.Lerp(
            -feather,
            maximumRadius + feather,
            easedProgress
        );
        float wave = 1f - SmoothStep(radius - feather, radius + feather, 0f);
        return Mathf.Lerp(_fromMode, _toMode, wave);
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

                bool isEye = playerRenderer.gameObject.name == "eye";

                _playerMaterials.Add(new MaterialColorBinding(
                    playerRenderer,
                    materialIndex,
                    isEye,
                    hasBaseColor,
                    hasBaseColor ? material.GetColor(BaseColorHash) : Color.white,
                    hasColor,
                    hasColor ? material.GetColor(ColorHash) : Color.white
                ));
            }
        }
    }

    private void ApplyPlayerMode(float modeAmount, Color shiroColor)
    {
        if (_propertyBlock == null)
        {
            return;
        }

        float amount = Mathf.Clamp01(modeAmount);

        foreach (MaterialColorBinding binding in _playerMaterials)
        {
            if (binding.Renderer == null)
            {
                continue;
            }

            binding.Renderer.GetPropertyBlock(_propertyBlock, binding.MaterialIndex);

            if (binding.HasBaseColor)
            {
                Color shiroBaseColor = GetShiroMaterialColor(
                    binding.BaseColor,
                    shiroColor,
                    binding.IsEye
                );
                Color kuroBaseColor = GetKuroMaterialColor(
                    binding.BaseColor,
                    binding.IsEye
                );
                _propertyBlock.SetColor(
                    BaseColorHash,
                    Color.Lerp(shiroBaseColor, kuroBaseColor, amount)
                );
            }

            if (binding.HasColor)
            {
                Color shiroMaterialColor = GetShiroMaterialColor(
                    binding.Color,
                    shiroColor,
                    binding.IsEye
                );
                Color kuroMaterialColor = GetKuroMaterialColor(
                    binding.Color,
                    binding.IsEye
                );
                _propertyBlock.SetColor(
                    ColorHash,
                    Color.Lerp(shiroMaterialColor, kuroMaterialColor, amount)
                );
            }

            binding.Renderer.SetPropertyBlock(_propertyBlock, binding.MaterialIndex);
            _propertyBlock.Clear();
        }
    }

    /// <summary>
    /// クロ状態だった累積時間に対応する、シロ状態のPlayer色を返す。
    /// 境界値は次のLevelとして扱う（例: 10秒ちょうどはLevel 2）。
    /// </summary>
    public Color GetShiroPlayerColor(float kuroElapsedTime)
    {
        switch (GetShiroColorLevel(kuroElapsedTime))
        {
            case 1:
                return _level1Color;
            case 2:
                return _level2Color;
            case 3:
                return _level3Color;
            case 4:
                return _level4Color;
            default:
                return _level5Color;
        }
    }

    public int GetShiroColorLevel(float kuroElapsedTime)
    {
        return KuroProgressSettings.GetSharedColorLevel(kuroElapsedTime);
    }

    public Color GetShiroBackgroundColor(float kuroElapsedTime)
    {
        switch (GetShiroColorLevel(kuroElapsedTime))
        {
            case 1:
                return _level1BackgroundColor;
            case 2:
                return _level2BackgroundColor;
            case 3:
                return _level3BackgroundColor;
            case 4:
                return _level4BackgroundColor;
            default:
                return _level5BackgroundColor;
        }
    }

    private void UpdateCurrentShiroAppearance()
    {
        float elapsedTime = _worldModeManager.KuroElapsedTime;
        CurrentShiroColorLevel = GetShiroColorLevel(elapsedTime);
        CurrentShiroPlayerColor = GetShiroPlayerColor(elapsedTime);
        CurrentShiroBackgroundColor = GetShiroBackgroundColor(elapsedTime);
    }

    private bool HasRequiredReferences()
    {
        if (_worldModeManager != null && _player != null && _camera != null &&
            _playerMaterials.Count > 0)
        {
            return true;
        }

        Debug.LogError(
            "WorldModeVisualTransitionの必要な参照が見つかりません。" +
            "同じGameObjectのWorldModeManager、Player、Main Camera、" +
            "Playerモデルの色プロパティ付きRendererを確認してください。",
            this
        );
        return false;
    }

    private static float ModeToFloat(WorldMode mode)
    {
        return mode == WorldMode.Kuro ? 1f : 0f;
    }

    private static float SmoothStep01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float range = Mathf.Max(0.000001f, edge1 - edge0);
        return SmoothStep01((value - edge0) / range);
    }

    private static Color Invert(Color color)
    {
        return new Color(
            1f - Mathf.Clamp01(color.r),
            1f - Mathf.Clamp01(color.g),
            1f - Mathf.Clamp01(color.b),
            color.a
        );
    }

    private Color GetShiroMaterialColor(Color original, Color bodyColor, bool isEye)
    {
        if (isEye)
        {
            return CurrentShiroColorLevel >= 4
                ? WithOriginalAlpha(Color.white, original)
                : original;
        }

        return WithOriginalAlpha(bodyColor, original);
    }

    private Color GetKuroMaterialColor(Color original, bool isEye)
    {
        return isEye
            ? Invert(original)
            : WithOriginalAlpha(_kuroPlayerColor, original);
    }

    private void RestorePlayerColors()
    {
        if (_propertyBlock == null)
        {
            return;
        }

        foreach (MaterialColorBinding binding in _playerMaterials)
        {
            if (binding.Renderer == null)
            {
                continue;
            }

            binding.Renderer.GetPropertyBlock(_propertyBlock, binding.MaterialIndex);
            if (binding.HasBaseColor)
            {
                _propertyBlock.SetColor(BaseColorHash, binding.BaseColor);
            }

            if (binding.HasColor)
            {
                _propertyBlock.SetColor(ColorHash, binding.Color);
            }

            binding.Renderer.SetPropertyBlock(_propertyBlock, binding.MaterialIndex);
            _propertyBlock.Clear();
        }
    }

    private static Color WithOriginalAlpha(Color color, Color original)
    {
        color.a = original.a;
        return color;
    }

    private sealed class MaterialColorBinding
    {
        public Renderer Renderer { get; }
        public int MaterialIndex { get; }
        public bool IsEye { get; }
        public bool HasBaseColor { get; }
        public Color BaseColor { get; }
        public bool HasColor { get; }
        public Color Color { get; }

        public MaterialColorBinding(
            Renderer renderer,
            int materialIndex,
            bool isEye,
            bool hasBaseColor,
            Color baseColor,
            bool hasColor,
            Color color
        )
        {
            Renderer = renderer;
            MaterialIndex = materialIndex;
            IsEye = isEye;
            HasBaseColor = hasBaseColor;
            BaseColor = baseColor;
            HasColor = hasColor;
            Color = color;
        }
    }
}

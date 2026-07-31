using System.Collections.Generic;
using UnityEngine;

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
    [SerializeField] private Color _shiroBackground = Color.black;
    [SerializeField] private Color _kuroBackground = Color.white;

    public bool IsTransitioning { get; private set; }
    public float TransitionProgress => _progress;
    public Vector2 TransitionOrigin => _origin;

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
        ApplyPlayerMode(0f);
    }

    private void OnModeChanged(WorldMode nextMode)
    {
        float nextModeValue = ModeToFloat(nextMode);

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
        Shader.SetGlobalColor(ShiroBackgroundHash, _shiroBackground);
        Shader.SetGlobalColor(KuroBackgroundHash, _kuroBackground);

        float playerMode = GetModeAtTransitionCenter(feather);
        ApplyPlayerMode(playerMode);
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

                _playerMaterials.Add(new MaterialColorBinding(
                    playerRenderer,
                    materialIndex,
                    hasBaseColor,
                    hasBaseColor ? material.GetColor(BaseColorHash) : Color.white,
                    hasColor,
                    hasColor ? material.GetColor(ColorHash) : Color.white
                ));
            }
        }
    }

    private void ApplyPlayerMode(float modeAmount)
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
                _propertyBlock.SetColor(
                    BaseColorHash,
                    Color.Lerp(binding.BaseColor, Invert(binding.BaseColor), amount)
                );
            }

            if (binding.HasColor)
            {
                _propertyBlock.SetColor(
                    ColorHash,
                    Color.Lerp(binding.Color, Invert(binding.Color), amount)
                );
            }

            binding.Renderer.SetPropertyBlock(_propertyBlock, binding.MaterialIndex);
            _propertyBlock.Clear();
        }
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

    private sealed class MaterialColorBinding
    {
        public Renderer Renderer { get; }
        public int MaterialIndex { get; }
        public bool HasBaseColor { get; }
        public Color BaseColor { get; }
        public bool HasColor { get; }
        public Color Color { get; }

        public MaterialColorBinding(
            Renderer renderer,
            int materialIndex,
            bool hasBaseColor,
            Color baseColor,
            bool hasColor,
            Color color
        )
        {
            Renderer = renderer;
            MaterialIndex = materialIndex;
            HasBaseColor = hasBaseColor;
            BaseColor = baseColor;
            HasColor = hasColor;
            Color = color;
        }
    }
}

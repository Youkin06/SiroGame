using UnityEngine;

/// <summary>
/// Inspectorで指定されたDoorButtonがすべて押された時にDoorを開き、
/// 子のdoorOpenへ渦ポータルの表示率を適用する。
/// AnimatorとRendererはAwakeで取得し、コンポーネントは自動生成しない。
/// </summary>
public sealed class DoorGoalPortal : MonoBehaviour
{
    private static readonly int OpenAnimationHash = Animator.StringToHash("open");
    private static readonly int CloseAnimationHash = Animator.StringToHash("close");
    private static readonly int RevealPropertyId = Shader.PropertyToID("_Reveal");

    private const string PortalObjectName = "doorOpen";
    private const string OpenClipName = "open_door";
    private const string PortalShaderName = "SiroGame/Goal Portal Spiral";
    private const float DefaultRevealDuration = 1f;

    [Header("Open Conditions")]
    [Tooltip("有効にすると、Required Buttonsに関係なく最初から開きます。")]
    [SerializeField] private bool _openOnStart;
    [Tooltip("ここへ設定したDoorButtonがすべて押されると開きます。")]
    [SerializeField] private DoorButton[] _requiredButtons = new DoorButton[0];

    private Animator _animator;
    private Renderer _portalRenderer;
    private MaterialPropertyBlock _propertyBlock;
    private float _reveal;
    private float _revealDuration = DefaultRevealDuration;
    private bool _isOpen;

    public float Reveal => _reveal;
    public bool IsOpen => _isOpen;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _portalRenderer = FindPortalRenderer();

        if (_animator == null || _portalRenderer == null)
        {
            Debug.LogError(
                "DoorGoalPortalの必要な参照が見つかりません。" +
                "DoorのAnimatorと、子のdoorOpen Rendererを確認してください。",
                this
            );
            enabled = false;
            return;
        }

        Material portalMaterial = _portalRenderer.sharedMaterial;
        if (portalMaterial == null ||
            portalMaterial.shader == null ||
            portalMaterial.shader.name != PortalShaderName)
        {
            Debug.LogError(
                "doorOpenにGoalPortalSpiralマテリアルが設定されていません。",
                _portalRenderer
            );
            enabled = false;
            return;
        }

        _revealDuration = FindOpenClipDuration();
        _propertyBlock = new MaterialPropertyBlock();
        _isOpen = EvaluateOpenCondition();
        ApplyDoorAnimationParameters(_isOpen);
        _reveal = _isOpen ? 1f : 0f;
        ApplyReveal();
    }

    private void Update()
    {
        bool shouldOpen = EvaluateOpenCondition();
        if (shouldOpen != _isOpen)
        {
            _isOpen = shouldOpen;
            ApplyDoorAnimationParameters(_isOpen);
        }

        float targetReveal = _isOpen ? 1f : 0f;
        float speed = 1f / Mathf.Max(0.01f, _revealDuration);
        float nextReveal = Mathf.MoveTowards(
            _reveal,
            targetReveal,
            speed * Time.deltaTime
        );

        if (Mathf.Approximately(nextReveal, _reveal))
        {
            return;
        }

        _reveal = nextReveal;
        ApplyReveal();
    }

    private bool EvaluateOpenCondition()
    {
        if (_openOnStart)
        {
            return true;
        }

        if (_requiredButtons == null || _requiredButtons.Length == 0)
        {
            return false;
        }

        foreach (DoorButton requiredButton in _requiredButtons)
        {
            if (requiredButton == null || !requiredButton.IsPressed)
            {
                return false;
            }
        }

        return true;
    }

    private void ApplyDoorAnimationParameters(bool isOpen)
    {
        _animator.SetBool(OpenAnimationHash, isOpen);
        _animator.SetBool(CloseAnimationHash, !isOpen);
    }

    private void OnDisable()
    {
        if (_portalRenderer == null || _propertyBlock == null)
        {
            return;
        }

        _reveal = 0f;
        ApplyReveal();
    }

    private Renderer FindPortalRenderer()
    {
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child != transform && child.name == PortalObjectName)
            {
                return child.GetComponent<Renderer>();
            }
        }

        return null;
    }

    private float FindOpenClipDuration()
    {
        RuntimeAnimatorController controller = _animator.runtimeAnimatorController;
        if (controller == null)
        {
            return DefaultRevealDuration;
        }

        foreach (AnimationClip clip in controller.animationClips)
        {
            if (clip != null && clip.name == OpenClipName)
            {
                return Mathf.Max(0.01f, clip.length);
            }
        }

        return DefaultRevealDuration;
    }

    private void ApplyReveal()
    {
        _portalRenderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetFloat(RevealPropertyId, _reveal);
        _portalRenderer.SetPropertyBlock(_propertyBlock);
    }
}

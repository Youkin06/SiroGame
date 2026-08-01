using UnityEngine;

/// <summary>
/// Doorの開閉に合わせて、子のdoorOpenへ渦ポータルの表示率を適用する。
/// 参照は既存のシーン階層からAwakeで取得し、コンポーネントは自動生成しない。
/// </summary>
public sealed class DoorGoalPortal : MonoBehaviour
{
    private static readonly int OpenAnimationHash = Animator.StringToHash("open");
    private static readonly int RevealPropertyId = Shader.PropertyToID("_Reveal");

    private const string PortalObjectName = "doorOpen";
    private const string OpenClipName = "open_door";
    private const string PortalShaderName = "SiroGame/Goal Portal Spiral";
    private const float DefaultRevealDuration = 1f;

    private Animator _animator;
    private Renderer _portalRenderer;
    private MaterialPropertyBlock _propertyBlock;
    private float _reveal;
    private float _revealDuration = DefaultRevealDuration;

    public float Reveal => _reveal;

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
        _reveal = _animator.GetBool(OpenAnimationHash) ? 1f : 0f;
        ApplyReveal();
    }

    private void Update()
    {
        float targetReveal = _animator.GetBool(OpenAnimationHash) ? 1f : 0f;
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

using UnityEngine;

/// <summary>
/// PlayerまたはEnemyが押し板の上にいる間だけ、ボタンを沈めてDoorを開く。
/// 参照は既存のシーン階層からAwakeで取得し、コンポーネントは自動生成しない。
/// </summary>
public sealed class DoorButton : MonoBehaviour
{
    private static readonly int OpenAnimationHash = Animator.StringToHash("open");
    private static readonly int CloseAnimationHash = Animator.StringToHash("close");
    private const string PressPlateName = "button";
    private const string DoorName = "door";
    private const int OverlapCapacity = 32;

    [SerializeField] private float _pressSpeed = 2f;
    [SerializeField] private float _releaseSpeed = 2f;
    [SerializeField] private float _detectionHeight = 0.4f;
    [SerializeField] private float _horizontalInset = 0.05f;

    public bool IsPressed { get; private set; }

    private readonly Collider[] _overlapResults = new Collider[OverlapCapacity];
    private Transform _pressPlate;
    private BoxCollider _pressPlateCollider;
    private Animator _doorAnimator;
    private float _releasedLocalY;

    /// <summary>
    /// 指定されたColliderが、このボタンの押し板かどうかを返す。
    /// Falling中のEnemyが押し板へ着地したことを判定するために使用する。
    /// </summary>
    public bool IsPressPlateCollider(Collider candidate)
    {
        return candidate != null && candidate == _pressPlateCollider;
    }

    private void Awake()
    {
        ResolvePressPlate();
        ResolveDoorAnimator();

        if (_pressPlate == null || _pressPlateCollider == null || _doorAnimator == null)
        {
            Debug.LogError(
                "DoorButtonの必要な参照が見つかりません。" +
                "子のbuttonとBoxCollider、シーン内のdoor Animatorを確認してください。",
                this
            );
            enabled = false;
            return;
        }

        _releasedLocalY = _pressPlate.localPosition.y;
        ApplyDoorAnimationParameters(false);
    }

    private void FixedUpdate()
    {
        bool shouldBePressed = HasPresserOnTop();
        if (shouldBePressed != IsPressed)
        {
            IsPressed = shouldBePressed;
            ApplyDoorAnimationParameters(IsPressed);
        }

        float targetY = IsPressed ? 0f : _releasedLocalY;
        float speed = IsPressed ? _pressSpeed : _releaseSpeed;
        Vector3 localPosition = _pressPlate.localPosition;
        localPosition.y = Mathf.MoveTowards(
            localPosition.y,
            targetY,
            Mathf.Max(0f, speed) * Time.fixedDeltaTime
        );
        _pressPlate.localPosition = localPosition;
    }

    private void OnDisable()
    {
        if (_doorAnimator != null)
        {
            ApplyDoorAnimationParameters(false);
        }
    }

    private void ApplyDoorAnimationParameters(bool isPressed)
    {
        _doorAnimator.SetBool(OpenAnimationHash, isPressed);
        _doorAnimator.SetBool(CloseAnimationHash, !isPressed);
    }

    private void ResolvePressPlate()
    {
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child == transform || child.name != PressPlateName)
            {
                continue;
            }

            BoxCollider boxCollider = child.GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                continue;
            }

            _pressPlate = child;
            _pressPlateCollider = boxCollider;
            return;
        }
    }

    private void ResolveDoorAnimator()
    {
        Animator[] animators = FindObjectsByType<Animator>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (Animator animator in animators)
        {
            if (animator.gameObject.name == DoorName)
            {
                _doorAnimator = animator;
                return;
            }
        }
    }

    private bool HasPresserOnTop()
    {
        Vector3 lossyScale = _pressPlate.lossyScale;
        Vector3 scaledHalfSize = new Vector3(
            Mathf.Abs(_pressPlateCollider.size.x * lossyScale.x) * 0.5f,
            Mathf.Abs(_pressPlateCollider.size.y * lossyScale.y) * 0.5f,
            Mathf.Abs(_pressPlateCollider.size.z * lossyScale.z) * 0.5f
        );
        float detectionHeight = Mathf.Max(0.05f, _detectionHeight);
        float worldInsetX = Mathf.Min(
            scaledHalfSize.x - 0.01f,
            Mathf.Max(0f, _horizontalInset * Mathf.Abs(lossyScale.x))
        );
        float worldInsetZ = Mathf.Min(
            scaledHalfSize.z - 0.01f,
            Mathf.Max(0f, _horizontalInset * Mathf.Abs(lossyScale.z))
        );
        Vector3 up = _pressPlate.up;
        Vector3 plateCenter = _pressPlate.TransformPoint(_pressPlateCollider.center);
        Vector3 detectionCenter = plateCenter + up * (
            scaledHalfSize.y + detectionHeight * 0.5f - 0.02f
        );
        Vector3 detectionHalfSize = new Vector3(
            Mathf.Max(0.01f, scaledHalfSize.x - worldInsetX),
            detectionHeight * 0.5f + 0.02f,
            Mathf.Max(0.01f, scaledHalfSize.z - worldInsetZ)
        );

        int hitCount = Physics.OverlapBoxNonAlloc(
            detectionCenter,
            detectionHalfSize,
            _overlapResults,
            _pressPlate.rotation,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < hitCount; i++)
        {
            Collider candidate = _overlapResults[i];
            if (candidate == null)
            {
                continue;
            }

            Transform presser = GetPresserRoot(candidate);
            if (presser != null && IsCenterAbovePlate(presser))
            {
                return true;
            }
        }

        return false;
    }

    private static Transform GetPresserRoot(Collider candidate)
    {
        PlayerMove player = candidate.GetComponentInParent<PlayerMove>();
        if (player != null)
        {
            return player.transform;
        }

        EnemyController enemy = candidate.GetComponentInParent<EnemyController>();
        return enemy != null ? enemy.transform : null;
    }

    private bool IsCenterAbovePlate(Transform presser)
    {
        Vector3 localPosition = _pressPlate.InverseTransformPoint(presser.position);
        Vector3 center = _pressPlateCollider.center;
        Vector3 halfSize = _pressPlateCollider.size * 0.5f;
        float inset = Mathf.Max(0f, _horizontalInset);

        return Mathf.Abs(localPosition.x - center.x) <=
                   Mathf.Max(0.01f, halfSize.x - inset) &&
               Mathf.Abs(localPosition.z - center.z) <=
                   Mathf.Max(0.01f, halfSize.z - inset) &&
               localPosition.y > center.y;
    }
}

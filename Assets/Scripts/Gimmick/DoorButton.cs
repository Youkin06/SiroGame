using UnityEngine;

/// <summary>
/// Player、Enemy、または物理的に動く箱が押し板の上にいる間だけ、
/// ボタンを沈め、現在の押下状態を公開する。
/// 参照は既存のシーン階層からAwakeで取得し、コンポーネントは自動生成しない。
/// </summary>
public sealed class DoorButton : MonoBehaviour
{
    private const string PressPlateName = "button";
    private const int OverlapCapacity = 32;

    [SerializeField] private float _pressSpeed = 2f;
    [SerializeField] private float _releaseSpeed = 2f;
    [SerializeField, Min(0f)] private float _pressDepth = 0.2f;
    [SerializeField] private float _detectionHeight = 0.4f;
    [SerializeField] private float _horizontalInset = 0.05f;

    public bool IsPressed { get; private set; }

    private readonly Collider[] _overlapResults = new Collider[OverlapCapacity];
    private Transform _pressPlate;
    private BoxCollider _pressPlateCollider;
    private float _releasedLocalY;
    private float _pressedLocalY;

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

        if (_pressPlate == null || _pressPlateCollider == null)
        {
            Debug.LogError(
                "DoorButtonの必要な参照が見つかりません。" +
                "子のbuttonとBoxColliderを確認してください。",
                this
            );
            enabled = false;
            return;
        }

        _releasedLocalY = _pressPlate.localPosition.y;
        _pressedLocalY = _releasedLocalY - Mathf.Max(0f, _pressDepth);
    }

    private void FixedUpdate()
    {
        bool shouldBePressed = HasPresserOnTop();
        if (shouldBePressed != IsPressed)
        {
            IsPressed = shouldBePressed;
        }

        float targetY = IsPressed ? _pressedLocalY : _releasedLocalY;
        float speed = IsPressed ? _pressSpeed : _releaseSpeed;
        Vector3 localPosition = _pressPlate.localPosition;
        localPosition.y = Mathf.MoveTowards(
            localPosition.y,
            targetY,
            Mathf.Max(0f, speed) * Time.fixedDeltaTime
        );
        _pressPlate.localPosition = localPosition;
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
        if (enemy != null)
        {
            return enemy.transform;
        }

        // WoodenBoxのようなDynamic Rigidbodyを持つ物理オブジェクトも押下対象にする。
        // 名前やTagには依存しないため、同じ構成の箱を増やしても追加設定は不要。
        Rigidbody rigidbody = candidate.attachedRigidbody;
        return rigidbody != null && !rigidbody.isKinematic
            ? rigidbody.transform
            : null;
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

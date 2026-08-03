using UnityEngine;

/// <summary>
/// Player、Enemy、または物理的に動く箱が押し板の上にいる間だけ、
/// ボタンを沈め、現在の押下状態を公開する。
/// 参照は既存のシーン階層からAwakeで取得し、コンポーネントは自動生成しない。
/// </summary>
public sealed class DoorButton : MonoBehaviour
{
    private const string PressPlateName = "button";
    private const int OverlapCapacity = 64;
    private const float MinimumSensorHeight = 0.05f;
    private const float SensorBottomOverlap = 0.05f;

    [SerializeField] private float _pressSpeed = 2f;
    [SerializeField] private float _releaseSpeed = 2f;
    [SerializeField, Min(0f)] private float _pressDepth = 0.2f;
    [Tooltip("押し板の元の上面から上へ伸ばす、固定検出範囲の高さです。")]
    [SerializeField, Min(MinimumSensorHeight)] private float _sensorHeight = 0.8f;
    [Tooltip("押し板の外周へ追加する判定の余裕です。")]
    [SerializeField, Min(0f)] private float _horizontalTolerance = 0.05f;
    [Tooltip("物理挙動で一瞬だけ判定から外れた時に、押下を維持する時間です。")]
    [SerializeField, Min(0f)] private float _releaseGraceTime = 0.1f;

    public bool IsPressed { get; private set; }

    private readonly Collider[] _overlapResults = new Collider[OverlapCapacity];
    private Transform _pressPlate;
    private BoxCollider _pressPlateCollider;
    private float _releasedLocalY;
    private float _pressedLocalY;
    private float _lastDetectedFixedTime = float.NegativeInfinity;

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
        bool detected = HasPresserOnTop();
        if (detected)
        {
            _lastDetectedFixedTime = Time.fixedTime;
        }

        bool shouldBePressed = detected ||
            Time.fixedTime - _lastDetectedFixedTime <=
            Mathf.Max(0f, _releaseGraceTime);
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
        float detectionHeight = Mathf.Max(MinimumSensorHeight, _sensorHeight);
        float worldToleranceX = Mathf.Max(
            0f,
            _horizontalTolerance * Mathf.Abs(lossyScale.x)
        );
        float worldToleranceZ = Mathf.Max(
            0f,
            _horizontalTolerance * Mathf.Abs(lossyScale.z)
        );
        Vector3 up = _pressPlate.up;
        Vector3 plateCenter = _pressPlate.TransformPoint(_pressPlateCollider.center);
        Vector3 releasedPositionOffset = GetReleasedPositionOffset();
        Vector3 releasedPlateCenter = plateCenter + releasedPositionOffset;
        Vector3 releasedPlateTop = releasedPlateCenter + up * scaledHalfSize.y;
        Vector3 detectionCenter = releasedPlateTop + up * (
            detectionHeight * 0.5f - SensorBottomOverlap * 0.5f
        );
        Vector3 detectionHalfSize = new Vector3(
            scaledHalfSize.x + worldToleranceX,
            detectionHeight * 0.5f + SensorBottomOverlap * 0.5f,
            scaledHalfSize.z + worldToleranceZ
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

            if (candidate.transform.IsChildOf(transform))
            {
                continue;
            }

            if (IsValidPresser(candidate) &&
                ReachesAbovePlate(candidate, releasedPlateTop, up))
            {
                return true;
            }
        }

        return false;
    }

    private Vector3 GetReleasedPositionOffset()
    {
        float localYOffset = _releasedLocalY - _pressPlate.localPosition.y;
        Transform parent = _pressPlate.parent;
        return parent != null
            ? parent.TransformVector(Vector3.up * localYOffset)
            : Vector3.up * localYOffset;
    }

    private static bool IsValidPresser(Collider candidate)
    {
        PlayerMove player = candidate.GetComponentInParent<PlayerMove>();
        if (player != null)
        {
            return true;
        }

        EnemyController enemy = candidate.GetComponentInParent<EnemyController>();
        if (enemy != null)
        {
            // Falling／DeadもColliderを維持するため、穴の底でも押下対象になる。
            return true;
        }

        // WoodenBoxのようなDynamic Rigidbodyを持つ物理オブジェクトも押下対象にする。
        // 名前やTagには依存しないため、同じ構成の箱を増やしても追加設定は不要。
        Rigidbody rigidbody = candidate.attachedRigidbody;
        return rigidbody != null && !rigidbody.isKinematic;
    }

    private static bool ReachesAbovePlate(
        Collider candidate,
        Vector3 plateTop,
        Vector3 up
    )
    {
        Bounds bounds = candidate.bounds;
        Vector3 extents = bounds.extents;
        float projectedExtent =
            Mathf.Abs(up.x) * extents.x +
            Mathf.Abs(up.y) * extents.y +
            Mathf.Abs(up.z) * extents.z;
        float candidateTop = Vector3.Dot(bounds.center, up) + projectedExtent;
        float plateTopPosition = Vector3.Dot(plateTop, up);

        return candidateTop >= plateTopPosition - SensorBottomOverlap;
    }
}

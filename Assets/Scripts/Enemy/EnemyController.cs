using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

public enum EnemyState
{
    Idle,
    Chase,
    Falling,
    Dead
}

/// <summary>
/// 追跡中はNavMeshで経路探索し、Kinematic Rigidbodyで移動する見張り。
/// Holeへ入った後だけNavMeshを離れ、Dynamic Rigidbodyで落下する。
/// </summary>
public class EnemyController : MonoBehaviour
{
    private static readonly int WalkAnimationHash = Animator.StringToHash("walk");
    private static readonly int FallingAnimationHash = Animator.StringToHash("falling");
    private static readonly int DeadAnimationHash = Animator.StringToHash("dead");

    [Header("Chase")]
    [SerializeField] private float _moveSpeed = 3.5f;
    [SerializeField] private float _acceleration = 20f;
    [SerializeField] private float _rotationSpeed = 360f;
    [SerializeField] private float _stoppingDistance = 0.1f;
    [SerializeField] private float _destinationRefreshInterval = 0.1f;
    [SerializeField] private float _destinationMoveThreshold = 0.1f;

    [Header("Falling")]
    [FormerlySerializedAs("_fallForwardSpeed")]
    [SerializeField] private float _fallCenteringSpeed = 2f;
    [SerializeField] private float _fallDownwardSpeed = 4f;

    public EnemyState CurrentState { get; private set; } = EnemyState.Idle;

    private WorldModeManager _worldModeManager;
    private Transform _player;
    private NavMeshAgent _navMeshAgent;
    private Rigidbody _rigidbody;
    private Animator _animator;
    private Collider[] _bodyColliders;
    private Vector3 _holeCenter;
    private Vector3 _navigationVelocity;
    private Vector3 _previousNavigationPosition;
    private Vector3 _lastDestination = new(float.PositiveInfinity, 0f, 0f);
    private float _nextDestinationRefreshTime;
    private float _activeFallCenteringSpeed;

    private void Awake()
    {
        _worldModeManager = FindFirstObjectByType<WorldModeManager>();

        PlayerMove playerMove = FindFirstObjectByType<PlayerMove>();
        _player = playerMove != null ? playerMove.transform : null;

        // Agentは敵本体のPivotではなく、足元に置いた子オブジェクトから取得する。
        // これにより、Rigidbody本体の高さを変えずにNavMeshへ正しく接地できる。
        _navMeshAgent = GetComponentInChildren<NavMeshAgent>();
        _rigidbody = GetComponent<Rigidbody>();
        _animator = GetComponentInChildren<Animator>();
        _bodyColliders = GetComponentsInChildren<Collider>();
    }

    private void Start()
    {
        if (!HasRequiredReferences())
        {
            enabled = false;
            return;
        }

        ConfigureNavigation();
        ConfigureAnimation();

        if (!_navMeshAgent.enabled || !_navMeshAgent.isOnNavMesh)
        {
            Debug.LogError(
                "Enemyの子NavMeshAgentがNavMesh上にありません。" +
                "Agent用の子オブジェクトが敵の足元にあるか、" +
                "NavMeshSurfaceの生成とベイクを確認してください。",
                this
            );
            enabled = false;
            return;
        }

        _rigidbody.isKinematic = true;
        _rigidbody.useGravity = false;
        _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        _rigidbody.constraints |=
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;

        _previousNavigationPosition = _rigidbody.position;

        _worldModeManager.ModeChanged += OnModeChanged;
        OnModeChanged(_worldModeManager.CurrentMode);
    }

    private void OnDisable()
    {
        if (_worldModeManager != null)
        {
            _worldModeManager.ModeChanged -= OnModeChanged;
        }
    }

    private void Update()
    {
        if (CurrentState != EnemyState.Idle && CurrentState != EnemyState.Chase)
        {
            return;
        }

        if (!_navMeshAgent.enabled || !_navMeshAgent.isOnNavMesh)
        {
            _navigationVelocity = Vector3.zero;
            UpdateAnimationState();
            return;
        }

        SyncAgentToRigidbody();

        if (CurrentState == EnemyState.Chase)
        {
            RefreshDestination(false);
            _navigationVelocity = _navMeshAgent.desiredVelocity;
            _navigationVelocity.y = 0f;
        }
        else
        {
            _navigationVelocity = Vector3.zero;
        }

        UpdateAnimationState();
    }

    private void FixedUpdate()
    {
        switch (CurrentState)
        {
            case EnemyState.Idle:
            case EnemyState.Chase:
                UpdateNavigationMovement();
                break;

            case EnemyState.Falling:
                UpdateFalling();
                break;
        }
    }

    /// <summary>
    /// 現在位置を変えず、穴の中心へ寄りながら落下状態へ切り替える。
    /// </summary>
    public void EnterFalling(Vector3 holeCenter)
    {
        if (CurrentState != EnemyState.Idle && CurrentState != EnemyState.Chase)
        {
            return;
        }

        Vector3 inheritedVelocity = _navigationVelocity;
        _holeCenter = holeCenter;
        _activeFallCenteringSpeed = Mathf.Max(
            _fallCenteringSpeed,
            new Vector2(inheritedVelocity.x, inheritedVelocity.z).magnitude
        );

        DisableNavigation();

        _rigidbody.isKinematic = false;
        _rigidbody.useGravity = true;
        _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rigidbody.linearVelocity = inheritedVelocity;
        _rigidbody.angularVelocity = Vector3.zero;

        CurrentState = EnemyState.Falling;
        UpdateAnimationState();
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryDieOnLanding(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        TryDieOnLanding(collision);
    }

    public void Die()
    {
        if (CurrentState == EnemyState.Dead)
        {
            return;
        }

        _rigidbody.linearVelocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;
        _rigidbody.isKinematic = true;

        CurrentState = EnemyState.Dead;
        UpdateAnimationState();
    }

    private void ConfigureNavigation()
    {
        _navMeshAgent.speed = _moveSpeed;
        _navMeshAgent.acceleration = _acceleration;
        _navMeshAgent.angularSpeed = _rotationSpeed;
        _navMeshAgent.stoppingDistance = _stoppingDistance;
        _navMeshAgent.autoBraking = true;
        _navMeshAgent.autoRepath = true;
        _navMeshAgent.autoTraverseOffMeshLink = false;
        _navMeshAgent.updatePosition = false;
        _navMeshAgent.updateRotation = false;
    }

    private void ConfigureAnimation()
    {
        _animator.applyRootMotion = false;
        _animator.speed = 1f;
        _animator.SetBool(WalkAnimationHash, false);
        _animator.SetBool(FallingAnimationHash, false);
        _animator.SetBool(DeadAnimationHash, false);
    }

    private void UpdateAnimationState()
    {
        bool isDead = CurrentState == EnemyState.Dead;
        bool isFalling = CurrentState == EnemyState.Falling;
        bool isWalking = CurrentState == EnemyState.Chase &&
            _navigationVelocity.sqrMagnitude > 0.0001f;

        _animator.speed = 1f;
        _animator.SetBool(WalkAnimationHash, isWalking && !isDead);
        _animator.SetBool(FallingAnimationHash, isFalling && !isDead);
        _animator.SetBool(DeadAnimationHash, isDead);
    }

    private void UpdateNavigationMovement()
    {
        Vector3 currentPosition = _rigidbody.position;

        if (HoleFallTrigger.TryFindCrossedHole(
                _previousNavigationPosition,
                currentPosition,
                out Vector3 holeCenter
            ))
        {
            EnterFalling(holeCenter);
            return;
        }

        _previousNavigationPosition = currentPosition;

        if (CurrentState != EnemyState.Chase || _navigationVelocity.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Vector3 nextPosition = currentPosition +
            _navigationVelocity * Time.fixedDeltaTime;
        _rigidbody.MovePosition(nextPosition);

        Quaternion targetRotation = Quaternion.LookRotation(
            _navigationVelocity.normalized,
            Vector3.up
        );
        _rigidbody.MoveRotation(Quaternion.RotateTowards(
            _rigidbody.rotation,
            targetRotation,
            _rotationSpeed * Time.fixedDeltaTime
        ));
    }

    private void UpdateFalling()
    {
        Vector3 toHoleCenter = _holeCenter - _rigidbody.position;
        toHoleCenter.y = 0f;

        Vector3 horizontalVelocity = Vector3.zero;
        if (toHoleCenter.sqrMagnitude > 0.0001f)
        {
            // 1回の物理更新で中心を通り越さない速度に制限する。
            float maxSpeedWithoutOvershoot =
                toHoleCenter.magnitude / Time.fixedDeltaTime;
            float speed = Mathf.Min(
                _activeFallCenteringSpeed,
                maxSpeedWithoutOvershoot
            );
            horizontalVelocity = toHoleCenter.normalized * speed;
        }

        Vector3 velocity = _rigidbody.linearVelocity;
        velocity.x = horizontalVelocity.x;
        velocity.z = horizontalVelocity.z;

        // 縁の床にまだ乗っている間は、強い下向き速度による摩擦を発生させない。
        // 穴の中心へ入ってから落下速度を与える。
        if (toHoleCenter.sqrMagnitude <= 0.0025f)
        {
            velocity.y = Mathf.Min(velocity.y, -_fallDownwardSpeed);
        }
        else
        {
            velocity.y = Mathf.Min(velocity.y, 0f);
        }

        _rigidbody.linearVelocity = velocity;
        _rigidbody.angularVelocity = Vector3.zero;
    }

    private void RefreshDestination(bool force)
    {
        if (!force && Time.time < _nextDestinationRefreshTime)
        {
            return;
        }

        _nextDestinationRefreshTime = Time.time + _destinationRefreshInterval;
        Vector3 destination = _player.position;

        if (!force &&
            (destination - _lastDestination).sqrMagnitude <
            _destinationMoveThreshold * _destinationMoveThreshold)
        {
            return;
        }

        if (_navMeshAgent.SetDestination(destination))
        {
            _lastDestination = destination;
        }
    }

    private void SyncAgentToRigidbody()
    {
        Vector3 agentPosition = _navMeshAgent.nextPosition;
        Vector3 bodyPosition = _rigidbody.position;
        agentPosition.x = bodyPosition.x;
        agentPosition.z = bodyPosition.z;
        _navMeshAgent.nextPosition = agentPosition;
    }

    private void DisableNavigation()
    {
        if (!_navMeshAgent.enabled)
        {
            return;
        }

        if (_navMeshAgent.isOnNavMesh)
        {
            _navMeshAgent.isStopped = true;
            _navMeshAgent.ResetPath();
        }

        _navMeshAgent.updatePosition = false;
        _navMeshAgent.updateRotation = false;
        _navMeshAgent.enabled = false;
    }

    private void TryDieOnLanding(Collision collision)
    {
        if (CurrentState != EnemyState.Falling)
        {
            return;
        }

        foreach (ContactPoint contact in collision.contacts)
        {
            bool isFloor = contact.normal.y >= 0.5f;
            bool isBelowHole = contact.point.y < _holeCenter.y - 0.01f;

            if (isFloor && isBelowHole)
            {
                Die();
                return;
            }
        }
    }

    private void OnModeChanged(WorldMode mode)
    {
        if (CurrentState == EnemyState.Falling || CurrentState == EnemyState.Dead)
        {
            return;
        }

        CurrentState = mode == WorldMode.Kuro
            ? EnemyState.Chase
            : EnemyState.Idle;

        bool shouldChase = CurrentState == EnemyState.Chase;
        _navMeshAgent.isStopped = !shouldChase;
        _navigationVelocity = Vector3.zero;
        _previousNavigationPosition = _rigidbody.position;
        UpdateAnimationState();

        if (shouldChase)
        {
            SyncAgentToRigidbody();
            RefreshDestination(true);
        }
    }

    private bool HasRequiredReferences()
    {
        if (_worldModeManager != null && _player != null &&
            _navMeshAgent != null && _rigidbody != null &&
            _animator != null &&
            _bodyColliders != null &&
            _bodyColliders.Length > 0)
        {
            return true;
        }

        Debug.LogError(
            "EnemyController の必要な参照が見つかりません。" +
            "WorldModeManager、Player、敵の子NavMeshAgent、" +
            "敵モデルのAnimator、敵本体のRigidbodyとColliderを確認してください。",
            this
        );
        return false;
    }
}

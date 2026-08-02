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
    [SerializeField, Min(0.05f)] private float _footstepInterval = 0.36f;

    [Header("Falling")]
    [FormerlySerializedAs("_fallForwardSpeed")]
    [SerializeField] private float _fallCenteringSpeed = 2f;
    [SerializeField] private float _fallDownwardSpeed = 4f;
    [SerializeField] private float _deathCenterTolerance = 0.02f;
    [SerializeField] private float _deathRotationTolerance = 2f;
    [SerializeField, Range(0.5f, 1f)] private float _deathVisualScale = 0.85f;

    public EnemyState CurrentState { get; private set; } = EnemyState.Idle;

    private WorldModeManager _worldModeManager;
    private PlayerMove _playerMove;
    private Transform _player;
    private NavMeshAgent _navMeshAgent;
    private Rigidbody _rigidbody;
    private Animator _animator;
    private AudioSource _footstepAudioSource;
    private Vector3 _visualInitialScale;
    private Collider[] _bodyColliders;
    private Vector3 _holeCenter;
    private Vector3 _navigationVelocity;
    private Vector3 _previousNavigationPosition;
    private Vector3 _lastDestination = new(float.PositiveInfinity, 0f, 0f);
    private float _nextDestinationRefreshTime;
    private float _activeFallCenteringSpeed;
    private Vector3 _deathFacingDirection;
    private bool _hasReachedDeathZone;
    private bool _hasLandedBelowHole;
    private float _nextFootstepTime;
    private bool _wasWalkingForFootsteps;

    private void Awake()
    {
        _worldModeManager = FindFirstObjectByType<WorldModeManager>();

        _playerMove = FindFirstObjectByType<PlayerMove>();
        _player = _playerMove != null ? _playerMove.transform : null;

        // Agentは敵本体のPivotではなく、足元に置いた子オブジェクトから取得する。
        // これにより、Rigidbody本体の高さを変えずにNavMeshへ正しく接地できる。
        _navMeshAgent = GetComponentInChildren<NavMeshAgent>();
        _rigidbody = GetComponent<Rigidbody>();
        _animator = GetComponentInChildren<Animator>();
        _footstepAudioSource = GetComponent<AudioSource>();
        _visualInitialScale = _animator != null
            ? _animator.transform.localScale
            : Vector3.one;
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
        ConfigureFootstepAudio();

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
        _playerMove.Died += OnPlayerDied;
        OnModeChanged(_worldModeManager.CurrentMode);
    }

    private void OnDisable()
    {
        StopFootstepAudio();

        if (_worldModeManager != null)
        {
            _worldModeManager.ModeChanged -= OnModeChanged;
        }

        if (_playerMove != null)
        {
            _playerMove.Died -= OnPlayerDied;
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

        if (CurrentState == EnemyState.Idle)
        {
            _navMeshAgent.isStopped = true;
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
        EnterFalling(new HoleFallArea(holeCenter, Vector2.one * 0.5f));
    }

    public void EnterFalling(HoleFallArea holeArea)
    {
        if (CurrentState != EnemyState.Idle && CurrentState != EnemyState.Chase)
        {
            return;
        }

        Vector3 inheritedVelocity = _navigationVelocity;
        _holeCenter = holeArea.Center;
        _deathFacingDirection = holeArea.GetNearestDiagonal(transform.forward);
        _hasReachedDeathZone = false;
        _hasLandedBelowHole = false;
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

    public void NotifyEnteredDeathZone()
    {
        if (CurrentState == EnemyState.Falling)
        {
            _hasReachedDeathZone = true;
        }
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

        DisableNavigation();
        _rigidbody.linearVelocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;
        _rigidbody.isKinematic = false;
        _rigidbody.useGravity = true;
        _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rigidbody.constraints |= RigidbodyConstraints.FreezeRotation;
        _rigidbody.WakeUp();

        CurrentState = EnemyState.Dead;
        _animator.transform.localScale = _visualInitialScale * _deathVisualScale;
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
        _animator.transform.localScale = _visualInitialScale;
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
        UpdateFootstepAudio(isWalking && !isFalling && !isDead);
    }

    private void ConfigureFootstepAudio()
    {
        if (_footstepAudioSource == null)
        {
            Debug.LogError(
                "Enemyに足音用AudioSourceがありません。" +
                "Enemy直下へAudioSourceを追加してください。",
                this
            );
            return;
        }

        if (_footstepAudioSource.clip == null)
        {
            Debug.LogError(
                "EnemyのAudioSourceにfootstep06が設定されていません。",
                this
            );
        }
        else if (_footstepAudioSource.clip.loadState == AudioDataLoadState.Unloaded)
        {
            _footstepAudioSource.clip.LoadAudioData();
        }

        _footstepAudioSource.playOnAwake = false;
        _footstepAudioSource.loop = false;
        _footstepAudioSource.spatialBlend = 1f;
    }

    private void UpdateFootstepAudio(bool isWalking)
    {
        if (_footstepAudioSource == null ||
            _footstepAudioSource.clip == null)
        {
            return;
        }

        if (!isWalking)
        {
            StopFootstepAudio();
            return;
        }

        if (!_wasWalkingForFootsteps)
        {
            _wasWalkingForFootsteps = true;
            _nextFootstepTime = Time.time;
        }

        if (Time.time < _nextFootstepTime)
        {
            return;
        }

        _footstepAudioSource.PlayOneShot(_footstepAudioSource.clip);
        _nextFootstepTime = Time.time + Mathf.Max(0.05f, _footstepInterval);
    }

    private void StopFootstepAudio()
    {
        _wasWalkingForFootsteps = false;
        _nextFootstepTime = 0f;
        if (_footstepAudioSource != null && _footstepAudioSource.isPlaying)
        {
            _footstepAudioSource.Stop();
        }
    }

    private void UpdateNavigationMovement()
    {
        Vector3 currentPosition = _rigidbody.position;

        if (HoleFallTrigger.TryFindCrossedHole(
                _previousNavigationPosition,
                currentPosition,
                out HoleFallArea holeArea
            ))
        {
            EnterFalling(holeArea);
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
        if (_hasLandedBelowHole)
        {
            velocity.y = Mathf.Min(velocity.y, 0f);
        }
        else if (toHoleCenter.sqrMagnitude <= 0.0025f)
        {
            velocity.y = Mathf.Min(velocity.y, -_fallDownwardSpeed);
        }
        else
        {
            velocity.y = Mathf.Min(velocity.y, 0f);
        }

        _rigidbody.linearVelocity = velocity;
        _rigidbody.angularVelocity = Vector3.zero;

        Quaternion targetRotation = Quaternion.LookRotation(
            _deathFacingDirection,
            Vector3.up
        );
        Quaternion nextRotation = Quaternion.RotateTowards(
            _rigidbody.rotation,
            targetRotation,
            _rotationSpeed * Time.fixedDeltaTime
        );
        _rigidbody.MoveRotation(nextRotation);

        bool isCentered = toHoleCenter.sqrMagnitude <=
            _deathCenterTolerance * _deathCenterTolerance;
        bool isAligned = Quaternion.Angle(nextRotation, targetRotation) <=
            _deathRotationTolerance;

        // 底床がある穴は着地を優先する。床がない穴ではDeathZoneを
        // フォールバックとして使い、無限落下を防ぐ。
        bool canDie = _hasLandedBelowHole || _hasReachedDeathZone;
        if (canDie && isCentered && isAligned)
        {
            Die();
        }
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
            Collider landedCollider = contact.otherCollider;
            DoorButton doorButton = landedCollider != null
                ? landedCollider.GetComponentInParent<DoorButton>()
                : null;
            bool isDoorButtonPlate = doorButton != null &&
                                     doorButton.IsPressPlateCollider(landedCollider);

            if (isFloor && (isBelowHole || isDoorButtonPlate))
            {
                _hasLandedBelowHole = true;
            }
        }
    }

    private void OnModeChanged(WorldMode mode)
    {
        if (CurrentState == EnemyState.Falling || CurrentState == EnemyState.Dead)
        {
            return;
        }

        if (_playerMove != null && _playerMove.IsDead)
        {
            StopChasing();
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

    private void OnPlayerDied(PlayerMove player)
    {
        StopChasing();
    }

    private void StopChasing()
    {
        if (CurrentState != EnemyState.Idle && CurrentState != EnemyState.Chase)
        {
            return;
        }

        CurrentState = EnemyState.Idle;
        _navigationVelocity = Vector3.zero;
        _lastDestination = new Vector3(float.PositiveInfinity, 0f, 0f);

        if (_navMeshAgent != null && _navMeshAgent.enabled)
        {
            _navMeshAgent.isStopped = true;
            if (_navMeshAgent.isOnNavMesh)
            {
                _navMeshAgent.ResetPath();
            }
        }

        UpdateAnimationState();
    }

    private bool HasRequiredReferences()
    {
        if (_worldModeManager != null && _playerMove != null && _player != null &&
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

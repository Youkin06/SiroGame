using UnityEngine;
using UnityEngine.AI;

public enum EnemyState
{
    Idle,
    Chase,
    Falling,
    Dead
}

/// <summary>
/// クロ状態でプレイヤーを追跡する見張り。
/// NavMeshAgent は追跡中だけ使用し、穴へ落ちる時は Rigidbody に制御を切り替える。
/// </summary>
public class EnemyController : MonoBehaviour
{
    [Header("Chase")]
    [SerializeField] private float _stoppingDistance = 1f;

    [Header("Falling")]
    [SerializeField] private float _fallForwardSpeed = 1.5f;

    public EnemyState CurrentState { get; private set; } = EnemyState.Idle;

    private WorldModeManager _worldModeManager;
    private Transform _player;
    private NavMeshAgent _navMeshAgent;
    private Rigidbody _rigidbody;

    private void Awake()
    {
        _worldModeManager = FindFirstObjectByType<WorldModeManager>();

        PlayerMove playerMove = FindFirstObjectByType<PlayerMove>();
        _player = playerMove != null ? playerMove.transform : null;

        _navMeshAgent = GetComponent<NavMeshAgent>();
        _rigidbody = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        if (!HasRequiredReferences())
        {
            enabled = false;
            return;
        }

        _rigidbody.isKinematic = true;
        _navMeshAgent.isStopped = true;
        _navMeshAgent.stoppingDistance = _stoppingDistance;

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
        if (CurrentState != EnemyState.Chase || !_navMeshAgent.isOnNavMesh)
        {
            return;
        }

        _navMeshAgent.SetDestination(_player.position);
    }

    public void FallIntoHole(Vector3 fallDirection)
    {
        if (CurrentState != EnemyState.Idle && CurrentState != EnemyState.Chase)
        {
            return;
        }

        StopAgent();
        _navMeshAgent.enabled = false;

        _rigidbody.isKinematic = false;
        _rigidbody.linearVelocity = fallDirection.normalized * _fallForwardSpeed;

        CurrentState = EnemyState.Falling;
    }

    public void Die()
    {
        if (CurrentState == EnemyState.Dead)
        {
            return;
        }

        StopAgent();
        _navMeshAgent.enabled = false;

        _rigidbody.linearVelocity = Vector3.zero;
        _rigidbody.isKinematic = true;

        CurrentState = EnemyState.Dead;
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

        _navMeshAgent.isStopped = CurrentState != EnemyState.Chase;
    }

    private void StopAgent()
    {
        if (!_navMeshAgent.enabled)
        {
            return;
        }

        _navMeshAgent.isStopped = true;

        if (_navMeshAgent.isOnNavMesh)
        {
            _navMeshAgent.ResetPath();
        }
    }

    private bool HasRequiredReferences()
    {
        if (_worldModeManager != null && _player != null &&
            _navMeshAgent != null && _rigidbody != null)
        {
            return true;
        }

        Debug.LogError(
            "EnemyController の必要な参照が見つかりません。WorldModeManager、Player、NavMeshAgent、Rigidbody を確認してください。",
            this
        );
        return false;
    }
}

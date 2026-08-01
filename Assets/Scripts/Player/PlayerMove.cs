using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMove : MonoBehaviour
{
    private static readonly int WalkAnimationHash = Animator.StringToHash("walk");
    private static readonly int JumpAnimationHash = Animator.StringToHash("jump");
    private static readonly int DeadAnimationHash = Animator.StringToHash("dead");
    private static readonly int DeathStateHash =
        Animator.StringToHash("Base Layer.death_player");

    [SerializeField] private float _moveForce = 5;
    [SerializeField] private float _jumpForce = 5;
    [SerializeField] private float _rotationSpeed = 12;
    
    private Rigidbody _rigidbody;
    private Animator _animator;
    private PhysicsMaterial _frictionlessMaterial;
    private GameInputActions _gameInputActions;
    private Vector2 _moveInputValue;
    private WorldModeManager _worldModeManager;
    private readonly HashSet<Collider> _groundContacts = new();
    private bool _isGrounded;
    private bool _jumpConsumed;

    public bool IsDead { get; private set; }
    public event Action<PlayerMove> Died;

    private void Awake(){
        _rigidbody = GetComponent<Rigidbody>();
        _animator = GetComponentInChildren<Animator>();
        _worldModeManager = FindFirstObjectByType<WorldModeManager>();

        if (_animator == null)
        {
            Debug.LogError(
                "Playerの子オブジェクトにAnimatorが見つかりません。",
                this
            );
        }

        ApplyFrictionlessMaterial();
        _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        // 段差や壁から受ける接触力では回転させず、向きは移動入力からだけ変更する。
        _rigidbody.constraints |= RigidbodyConstraints.FreezeRotation;
        _gameInputActions = new GameInputActions();

        //Actionイベントの登録
        _gameInputActions.Player.Move.started += OnMove;
        _gameInputActions.Player.Move.performed += OnMove;
        _gameInputActions.Player.Move.canceled += OnMove;
        _gameInputActions.Player.Jump.performed += OnJump;
        _gameInputActions.FindAction("Player/SwitchMode", throwIfNotFound: true).performed += OnSwitchMode;

    }

    private void OnEnable()
    {
        if (!IsDead)
        {
            _gameInputActions.Player.Enable();
        }
    }

    private void OnDisable()
    {
        _gameInputActions?.Player.Disable();
    }

    private void OnDestroy()
    {
        // 自身でインスタンス化したActionクラスはIDisposableを実装しているので、
        // 必ずDisposeする必要がある
        _gameInputActions?.Dispose();

        if (_frictionlessMaterial != null)
        {
            Destroy(_frictionlessMaterial);
        }
    }

    private void OnMove(InputAction.CallbackContext context){
        if (IsDead)
        {
            return;
        }

        //Moveアクションの入力取得
        _moveInputValue = context.ReadValue<Vector2>();
    }

    private void OnJump(InputAction.CallbackContext context){
        if (IsDead || !_isGrounded || _jumpConsumed)
        {
            return;
        }

        _jumpConsumed = true;

        if (_rigidbody.linearVelocity.y < 0f)
        {
            Vector3 velocity = _rigidbody.linearVelocity;
            velocity.y = 0f;
            _rigidbody.linearVelocity = velocity;
        }

        _rigidbody.AddForce(Vector3.up * _jumpForce, ForceMode.Impulse);

        if (_animator != null)
        {
            _animator.SetBool(WalkAnimationHash, false);
            _animator.SetBool(JumpAnimationHash, true);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        UpdateGroundContact(collision);
        TryDieFromEnemy(collision.collider);
    }

    private void OnCollisionStay(Collision collision)
    {
        UpdateGroundContact(collision);
        TryDieFromEnemy(collision.collider);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryDieFromEnemy(other);
    }

    private void OnCollisionExit(Collision collision)
    {
        _groundContacts.Remove(collision.collider);
        _isGrounded = _groundContacts.Count > 0;
    }

    private void UpdateGroundContact(Collision collision)
    {
        bool hasGroundContact = false;

        foreach (ContactPoint contact in collision.contacts)
        {
            if (Vector3.Dot(contact.normal, Vector3.up) > 0.5f)
            {
                hasGroundContact = true;
                break;
            }
        }

        if (hasGroundContact)
        {
            bool wasGrounded = _isGrounded;
            _groundContacts.Add(collision.collider);
            _isGrounded = true;

            if (!wasGrounded && _rigidbody.linearVelocity.y <= 0.1f)
            {
                _jumpConsumed = false;
            }
        }
        else
        {
            _groundContacts.Remove(collision.collider);
            _isGrounded = _groundContacts.Count > 0;
        }
    }

    private void OnSwitchMode(InputAction.CallbackContext context)
    {
        if (IsDead)
        {
            return;
        }

        if (_worldModeManager == null)
        {
            Debug.LogError("シーン内に WorldModeManager が見つかりません。", this);
            return;
        }

        _worldModeManager.ToggleMode();
    }

    /// <summary>
    /// クロ状態で見張りに接触した時に呼ばれる。死亡後は入力と物理移動を停止する。
    /// </summary>
    public void Die()
    {
        if (IsDead)
        {
            return;
        }

        IsDead = true;
        _moveInputValue = Vector2.zero;
        _jumpConsumed = true;

        if (_rigidbody != null)
        {
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.isKinematic = true;
        }

        _gameInputActions?.Player.Disable();

        if (_animator != null)
        {
            _animator.SetBool(WalkAnimationHash, false);
            _animator.SetBool(JumpAnimationHash, false);
            _animator.SetBool(DeadAnimationHash, true);
            _animator.Play(DeathStateHash, 0, 0f);
        }

        Died?.Invoke(this);
    }

    private void TryDieFromEnemy(Collider other)
    {
        if (IsDead || _worldModeManager == null ||
            _worldModeManager.CurrentMode != WorldMode.Kuro || other == null)
        {
            return;
        }

        EnemyController enemy = other.GetComponentInParent<EnemyController>();
        if (enemy != null && enemy.CurrentState != EnemyState.Dead)
        {
            Die();
        }
    }

    private void ApplyFrictionlessMaterial()
    {
        _frictionlessMaterial = new PhysicsMaterial("Player Frictionless")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };

        foreach (Collider playerCollider in GetComponentsInChildren<Collider>())
        {
            if (!playerCollider.isTrigger)
            {
                playerCollider.material = _frictionlessMaterial;
            }
        }
    }
    
    void FixedUpdate()
    {
        if (IsDead)
        {
            return;
        }

        // 接触による回転速度が残らないようにする。
        _rigidbody.angularVelocity = Vector3.zero;

        Vector3 moveDirection = new Vector3(
            _moveInputValue.x,
            0,
            _moveInputValue.y
        );
        moveDirection = Vector3.ClampMagnitude(moveDirection, 1f);

        if (moveDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            Quaternion nextRotation = Quaternion.Slerp(
                _rigidbody.rotation,
                targetRotation,
                _rotationSpeed * Time.fixedDeltaTime
            );
            _rigidbody.MoveRotation(nextRotation);
        }

        Vector3 currentVelocity = _rigidbody.linearVelocity;
        _rigidbody.linearVelocity = new Vector3(
            moveDirection.x * _moveForce,
            currentVelocity.y,
            moveDirection.z * _moveForce
        );

        UpdateAnimationState(moveDirection);
    }

    private void UpdateAnimationState(Vector3 moveDirection)
    {
        if (_animator == null)
        {
            return;
        }

        if (IsDead)
        {
            _animator.SetBool(WalkAnimationHash, false);
            _animator.SetBool(JumpAnimationHash, false);
            _animator.SetBool(DeadAnimationHash, true);
            return;
        }

        bool isJumping = !_isGrounded;
        bool isWalking = !isJumping && moveDirection.sqrMagnitude > 0.001f;

        _animator.SetBool(WalkAnimationHash, isWalking);
        _animator.SetBool(JumpAnimationHash, isJumping);
        _animator.SetBool(DeadAnimationHash, false);
    }
}

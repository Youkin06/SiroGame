using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class PlayerMove : MonoBehaviour
{
    private const float GroundNormalThreshold = 0.5f;
    private const float LandingRelativeVelocityThreshold = 0.1f;
    private const float FallDeathY = -5f;
    private static readonly int WalkAnimationHash = Animator.StringToHash("walk");
    private static readonly int JumpAnimationHash = Animator.StringToHash("jump");
    private static readonly int DeadAnimationHash = Animator.StringToHash("dead");
    private static readonly int DeathStateHash =
        Animator.StringToHash("Base Layer.death_player");

    [SerializeField] private float _moveForce = 5;
    [SerializeField] private float _jumpForce = 5;
    [SerializeField] private float _rotationSpeed = 12;
    [SerializeField, Min(0f)] private float _restartDelayAfterDeath = 0.5f;
    [SerializeField, Min(0.05f)] private float _footstepInterval = 0.32f;
    
    private Rigidbody _rigidbody;
    private Animator _animator;
    private AudioSource _footstepAudioSource;
    private PhysicsMaterial _frictionlessMaterial;
    private GameInputActions _gameInputActions;
    private Vector2 _moveInputValue;
    private WorldModeManager _worldModeManager;
    private readonly HashSet<Collider> _groundContacts = new();
    private bool _isGrounded;
    private bool _jumpConsumed;
    private Coroutine _restartCoroutine;
    private float _nextFootstepTime;
    private bool _wasWalkingForFootsteps;

    public bool IsDead { get; private set; }
    public event Action<PlayerMove> Died;

    private void Awake(){
        _rigidbody = GetComponent<Rigidbody>();
        _animator = GetComponentInChildren<Animator>();
        _footstepAudioSource = GetComponent<AudioSource>();
        _worldModeManager = FindFirstObjectByType<WorldModeManager>();

        if (_animator == null)
        {
            Debug.LogError(
                "Playerの子オブジェクトにAnimatorが見つかりません。",
                this
            );
        }

        ConfigureFootstepAudio();

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
        StopFootstepAudio();
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
        StopFootstepAudio();

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
        ContactPoint groundContact = default;
        bool hasGroundContact = false;

        foreach (ContactPoint contact in collision.contacts)
        {
            if (Vector3.Dot(contact.normal, Vector3.up) > GroundNormalThreshold)
            {
                groundContact = contact;
                hasGroundContact = true;
                break;
            }
        }

        if (hasGroundContact)
        {
            _groundContacts.Add(collision.collider);
            _isGrounded = true;

            // Dynamicな箱やKinematicな敵では、着地した最初の接触フレームに
            // 上向き速度が残る場合がある。上面に接触している間は毎回再判定し、
            // 相手の移動速度を差し引いた相対速度が落下・静止ならジャンプを回復する。
            if (GetRelativeVerticalVelocity(collision, groundContact) <=
                LandingRelativeVelocityThreshold)
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

    private float GetRelativeVerticalVelocity(
        Collision collision,
        ContactPoint groundContact
    )
    {
        Vector3 playerVelocity = _rigidbody.GetPointVelocity(groundContact.point);
        Rigidbody supportBody = collision.collider != null
            ? collision.collider.attachedRigidbody
            : null;
        Vector3 supportVelocity = supportBody != null
            ? supportBody.GetPointVelocity(groundContact.point)
            : Vector3.zero;

        return Vector3.Dot(playerVelocity - supportVelocity, Vector3.up);
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
        StopFootstepAudio();

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
        _restartCoroutine = StartCoroutine(RestartCurrentSceneAfterDeath());
    }

    private IEnumerator RestartCurrentSceneAfterDeath()
    {
        if (_restartDelayAfterDeath > 0f)
        {
            yield return new WaitForSecondsRealtime(_restartDelayAfterDeath);
        }

        TileLoadingScreen loadingScreen = TileLoadingScreen.Instance;
        if (loadingScreen == null)
        {
            Debug.LogError(
                "死亡後の再開にはTileLoadingScreenが必要です。" +
                "最初に開くシーンへ1つ配置してください。",
                this
            );
            _restartCoroutine = null;
            yield break;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        loadingScreen.LoadScene(activeScene.buildIndex);
        _restartCoroutine = null;
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
        // 物理更新ではY座標が閾値を飛び越えることがあるため、完全一致ではなく以下で判定する。
        if (!IsDead && _rigidbody.position.y <= FallDeathY)
        {
            Die();
            return;
        }

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

        bool isWalking = _isGrounded &&
                         moveDirection.sqrMagnitude > 0.001f;
        UpdateFootstepAudio(isWalking);
        UpdateAnimationState(moveDirection);
    }

    private void ConfigureFootstepAudio()
    {
        if (_footstepAudioSource == null)
        {
            Debug.LogError(
                "Playerに足音用AudioSourceがありません。" +
                "Player直下へAudioSourceを追加してください。",
                this
            );
            return;
        }

        if (_footstepAudioSource.clip == null)
        {
            Debug.LogError(
                "PlayerのAudioSourceにfootstep05が設定されていません。",
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

using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMove : MonoBehaviour
{
    [SerializeField] private float _moveForce = 5;
    [SerializeField] private float _jumpForce = 5;
    [SerializeField] private float _rotationSpeed = 12;
    
    private Rigidbody _rigidbody;
    private GameInputActions _gameInputActions;
    private Vector2 _moveInputValue;

    private void Awake(){
        _rigidbody = GetComponent<Rigidbody>();
        // 地面との接触で横倒しにならないよう、前後・左右方向の回転を固定する。
        _rigidbody.constraints |= RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        _gameInputActions = new GameInputActions();

        //Actionイベントの登録
        _gameInputActions.Player.Move.started += OnMove;
        _gameInputActions.Player.Move.performed += OnMove;
        _gameInputActions.Player.Move.canceled += OnMove;
        _gameInputActions.Player.Jump.performed += OnJump;

    }

    private void OnEnable()
    {
        _gameInputActions.Player.Enable();
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
    }

    private void OnMove(InputAction.CallbackContext context){
        //Moveアクションの入力取得
        _moveInputValue = context.ReadValue<Vector2>();
    }

    private void OnJump(InputAction.CallbackContext context){
        _rigidbody.AddForce(Vector3.up * _jumpForce, ForceMode.Impulse);
    }
    
    void FixedUpdate()
    {
        Vector3 moveDirection = new Vector3(
            _moveInputValue.x,
            0,
            _moveInputValue.y
        );

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

        _rigidbody.MovePosition(
            _rigidbody.position + moveDirection * _moveForce * Time.fixedDeltaTime
        );
    }
    
}

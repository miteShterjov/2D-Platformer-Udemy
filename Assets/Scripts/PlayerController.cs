using System;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    public static readonly int anim_param_movement = Animator.StringToHash("isMoving");

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;

    private Vector2 movementInput; // from new Input System (Player/Move)
    private Rigidbody2D rb;
    private Animator animator;

    // New Input System generated actions
    private InputSystem_Actions inputActions;
    private InputAction moveAction;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponentInChildren<Animator>();

        // Set up input actions
        inputActions = new InputSystem_Actions();
        moveAction = inputActions.Player.Move;
    }

    private void OnEnable()
    {
        inputActions.Player.Enable();
        moveAction.performed += OnMove;
        moveAction.canceled += OnMove; // when released, sets input to zero
    }

    private void OnDisable()
    {
        moveAction.performed -= OnMove;
        moveAction.canceled -= OnMove;
        inputActions.Player.Disable();
    }

    private void OnDestroy()
    {
        inputActions?.Dispose();
    }

    private void Update()
    {
        HandleAnimTransitions();
    }

    private void FixedUpdate()
    {
        // Apply horizontal velocity only; preserve existing vertical velocity (gravity/jumps)
        Vector2 v = rb.linearVelocity;
        v.x = movementInput.x * moveSpeed;
        rb.linearVelocity = v;

        if (movementInput.x > 0.1f) FlipPlayerSprite(true);
        else if (movementInput.x < -0.1f) FlipPlayerSprite(false);
    }

    private void OnMove(InputAction.CallbackContext ctx)
    {
        movementInput = ctx.ReadValue<Vector2>();
    }

    // Support PlayerInput "Send Messages" behavior to avoid MissingMethodException.
    // Unity will invoke this when an action named "Move" is triggered if PlayerInput is set to Send Messages.
    public void OnMove(InputValue value)
    {
        movementInput = value.Get<Vector2>();
    }

    private void HandleAnimTransitions()
    {
        if (Mathf.Abs(movementInput.x) > 0.1f) animator.SetBool(anim_param_movement, true);
        else animator.SetBool(anim_param_movement, false);
    }

    private void FlipPlayerSprite(bool faceRight)
    {
        Vector3 scale = transform.localScale;
        scale.x = faceRight ? Math.Abs(scale.x) : -Math.Abs(scale.x);
        transform.localScale = scale;
    }
}

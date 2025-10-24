using System;
using System.Collections;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    public static readonly int anim_param_move = Animator.StringToHash("xVelocity");
    public static readonly int anim_param_jump = Animator.StringToHash("yVelocity");
    public static readonly int anim_param_grounded = Animator.StringToHash("isGrounded");
    public static readonly int anim_param_wallSlide = Animator.StringToHash("isWallDetected");

    [Header("Movement")]
    [SerializeField, Tooltip("Movement speed of the player")] private float moveSpeed = 5f;
    [SerializeField, Tooltip("Force applied when jumping")] private float jumpForce = 7f;
    [SerializeField, Tooltip("Maximum number of jumps available")] private int maxJumps = 2;
    [Space]
    [Header("Jump Tuning")]
    [SerializeField, Tooltip("Time allowed to still jump after leaving ground")] private float coyoteTime = 0.1f;
    [SerializeField, Tooltip("Time a jump press will be buffered before landing")] private float jumpBufferTime = 0.1f;
    [SerializeField, Tooltip("Gravity scale when falling")] private float fallGravityScale = 5f;
    [SerializeField, Tooltip("Gravity scale when ascending without holding jump")] private float lowJumpGravityScale = 7f;
    [SerializeField, Tooltip("Applied on jump release to cut upward velocity")] private float jumpCutMultiplier = 0.6f;
    [Space]
    [Header("Collision Check")]
    [SerializeField, Tooltip("Radius of the ground check sphere")] private float groundCheckRadius = 0.2f;
    [SerializeField, Tooltip("Layer mask for the ground")] private LayerMask groundLayer;
    [SerializeField, Tooltip("Point at which to check for ground")] private Transform groundCheckPoint;
    [SerializeField, Tooltip("Is the player grounded?")] private bool isGrounded;
    [Space]
    [SerializeField, Tooltip("Transform used for wall checks")] private Transform primaryWallCheck;
    [SerializeField, Tooltip("Transform used for wall checks")] private Transform secondaryWallCheck;
    [SerializeField, Tooltip("Distance from check to wall")] private float wallCheckDistance = 0.2f;
    [SerializeField, Tooltip("Is the player touching a wall?")] private bool isTouchingWall;

    private Vector2 movementInput;
    private Rigidbody2D rb;
    private Animator animator;
    private InputSystem_Actions inputActions;
    private InputAction moveAction;
    private InputAction jumpAction;
    private int jumpCount;
    private float coyoteCounter;
    private float jumpBufferCounter;
    private float normalGravityScale;
    private bool jumpHeld;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponentInChildren<Animator>();
        normalGravityScale = rb.gravityScale;

        // Set up input actions
        inputActions = new InputSystem_Actions();
        moveAction = inputActions.Player.Move;
        jumpAction = inputActions.Player.Jump;
    }

    private void Start()
    {
        jumpCount = maxJumps;
    }

    private void OnEnable()
    {
        inputActions.Player.Enable();
        moveAction.performed += OnMove;
        moveAction.canceled += OnMove;
        jumpAction.performed += OnJumpPerformed;
        jumpAction.canceled += OnJumpCanceled;
    }

    private void OnDisable()
    {
        moveAction.performed -= OnMove;
        moveAction.canceled -= OnMove;
        jumpAction.performed -= OnJumpPerformed;
        jumpAction.canceled -= OnJumpCanceled;
        inputActions.Player.Disable();
        rb.gravityScale = normalGravityScale;
    }

    private void OnDestroy()
    {
        inputActions?.Dispose();
    }

    private void Update()
    {
        // Grounded and timers
        bool groundedNow = PlayerIsGrounded();
        if (groundedNow)
            coyoteCounter = coyoteTime;
        else
            coyoteCounter -= Time.deltaTime;

        if (jumpBufferCounter > 0f)
            jumpBufferCounter -= Time.deltaTime;

        // Use buffered jump when possible
        if (jumpBufferCounter > 0f && jumpCount > 0)
        {
            DoJump();
        }

        // Better jump gravity
        if (rb.linearVelocity.y < 0f)
            rb.gravityScale = fallGravityScale;
        else if (rb.linearVelocity.y > 0f && !jumpHeld)
            rb.gravityScale = lowJumpGravityScale;
        else
            rb.gravityScale = normalGravityScale;

        HandleAnimTransitions();
        HandleWallSlideInteraction();
    }


    private void FixedUpdate()
    {
        Vector2 playerMove = rb.linearVelocity;
        playerMove.x = movementInput.x * moveSpeed;
        rb.linearVelocity = playerMove;

        if (movementInput.x > 0.1f) FlipPlayerSprite(true);
        else if (movementInput.x < -0.1f) FlipPlayerSprite(false);

        if (PlayerIsGrounded() && jumpCount < maxJumps) jumpCount = maxJumps;
    }

    private void OnMove(InputAction.CallbackContext ctx)
    {
        movementInput = ctx.ReadValue<Vector2>();
    }

    private void OnJumpPerformed(InputAction.CallbackContext ctx)
    {
        jumpHeld = true;
        jumpBufferCounter = jumpBufferTime; // buffer the press
    }

    private void OnJumpCanceled(InputAction.CallbackContext ctx)
    {
        jumpHeld = false;
        // Cut jump if still moving up
        if (rb.linearVelocity.y > 0f)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y * jumpCutMultiplier);
    }

    // Support PlayerInput "Send Messages" (optional)
    public void OnJump(InputValue value)
    {
        if (value.isPressed)
        {
            jumpHeld = true;
            jumpBufferCounter = jumpBufferTime;
        }
        else
        {
            jumpHeld = false;
            if (rb.linearVelocity.y > 0f)
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y * jumpCutMultiplier);
        }
    }

    private void DoJump()
    {
        // Only consume a jump if we truly execute it
        Vector2 v = rb.linearVelocity;
        v.y = jumpForce;
        rb.linearVelocity = v;
        jumpCount--;
        coyoteCounter = 0f;
        jumpBufferCounter = 0f;
        // Debug.Log($"Jumped! Remaining jumps: {jumpCount}");
    }

    private void HandleWallSlideInteraction()
    {
        if (PlayerIsTouchingWall() && !isGrounded && rb.linearVelocity.y < 0f)
        {
            rb.gravityScale /= 2f; 
            if (rb.linearVelocity.y < -2f) rb.linearVelocity = new Vector2(rb.linearVelocity.x, -2f);
        }
    }

    private void HandleAnimTransitions()
    {
        animator.SetFloat(anim_param_move, rb.linearVelocity.x);
        animator.SetFloat(anim_param_jump, rb.linearVelocity.y);
        animator.SetBool(anim_param_grounded, isGrounded);
        animator.SetBool(anim_param_wallSlide, isTouchingWall);
    }

    private void FlipPlayerSprite(bool faceRight)
    {
        Vector3 scale = transform.localScale;
        scale.x = faceRight ? Math.Abs(scale.x) : -Math.Abs(scale.x);
        transform.localScale = scale;
    }

    private bool PlayerIsGrounded()
    {
        var checkPos = groundCheckPoint != null ? groundCheckPoint.position : transform.position;
        isGrounded = Physics2D.OverlapCircle(checkPos, groundCheckRadius, groundLayer);
        return isGrounded;
    }

    private bool PlayerIsTouchingWall()
    {
        bool primaryHit = Physics2D.Raycast(primaryWallCheck.position, Vector2.right * transform.localScale.x, wallCheckDistance, groundLayer);
        bool secondaryHit = Physics2D.Raycast(secondaryWallCheck.position, Vector2.right * transform.localScale.x, wallCheckDistance, groundLayer);
        isTouchingWall = primaryHit || secondaryHit;
        return isTouchingWall;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(groundCheckPoint.position, groundCheckRadius);

        Gizmos.color = Color.blue;
        Gizmos.DrawLine(primaryWallCheck.position, primaryWallCheck.position + Vector3.right * transform.localScale.x * wallCheckDistance);
        Gizmos.DrawLine(secondaryWallCheck.position, secondaryWallCheck.position + Vector3.right * transform.localScale.x * wallCheckDistance);

    }
}

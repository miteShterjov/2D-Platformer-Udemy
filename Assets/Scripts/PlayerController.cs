using System;
using System.Collections;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    #region Script Parameters
    public static readonly int anim_param_move = Animator.StringToHash("xVelocity");
    public static readonly int anim_param_jump = Animator.StringToHash("yVelocity");
    public static readonly int anim_param_grounded = Animator.StringToHash("isGrounded");
    public static readonly int anim_param_wallSlide = Animator.StringToHash("isWallDetected");
    public static readonly int anim_param_knockback = Animator.StringToHash("knocked");

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
    [Space]
    [Header("Wall Slide/Jump")]
    [SerializeField, Tooltip("Max downward speed while sliding on a wall")] private float wallSlideSpeed = 2f;
    [SerializeField, Tooltip("Horizontal and vertical force applied on wall jump")] private Vector2 wallJumpForce = new Vector2(8f, 12f);
    [SerializeField, Tooltip("Seconds to reduce horizontal control after wall jump")] private float wallJumpLockTime = 0.2f;
    [Space]
    [Header("Knockback")]
    [SerializeField, Tooltip("Force applied when the player is knocked back")] private Vector2 knockbackForce = new Vector2(10f, 5f);
    [SerializeField, Tooltip("Duration of the knockback effect")] private float knockbackDuration = 1f;
    [SerializeField, Tooltip("Is the player currently knocked back?")] private bool isKnocked = false;
    [SerializeField, Tooltip("Can the player be knocked back?")] private bool canBeKnocked = true;
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
    private bool isWallSliding;
    private float wallJumpCounter;
    #endregion

    #region  Unity Methods
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
        if (isKnocked) return;
        // if key K is pressed then call knockback method
        if (inputActions.Player.Testing.IsPressed()) Knockback();
        
        // Grounded and timers
        bool groundedNow = PlayerIsGrounded();
        if (groundedNow)
            coyoteCounter = coyoteTime;
        else
            coyoteCounter -= Time.deltaTime;

        if (jumpBufferCounter > 0f)
            jumpBufferCounter -= Time.deltaTime;

        // Use buffered jump when possible (prefer wall jump if sliding)
        if (jumpBufferCounter > 0f)
        {
            if (PlayerIsTouchingWall() && !isGrounded)
            {
                DoWallJump();
            }
            else if (jumpCount > 0)
            {
                DoJump();
            }
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
        if (wallJumpCounter > 0f)
        {
            wallJumpCounter -= Time.fixedDeltaTime;
        }
        else
        {
            playerMove.x = movementInput.x * moveSpeed;
        }
        rb.linearVelocity = playerMove;

        if (movementInput.x > 0.1f) FlipPlayerSprite(true);
        else if (movementInput.x < -0.1f) FlipPlayerSprite(false);

        if (PlayerIsGrounded() && jumpCount < maxJumps) jumpCount = maxJumps;
    }
    #endregion

    #region Player Movement Methods
    private void OnMove(InputAction.CallbackContext ctx)
    {
        movementInput = ctx.ReadValue<Vector2>();
    }

    private void OnJump(InputValue value)
    {
        if (value.isPressed)
        {
            jumpHeld = true;
            jumpBufferCounter = jumpBufferTime;

            if (PlayerIsTouchingWall() && !isGrounded)
            {
                DoWallJump();
            }
        }
        else
        {
            jumpHeld = false;
            if (rb.linearVelocity.y > 0f)
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y * jumpCutMultiplier);
        }
    }

    private void OnJumpPerformed(InputAction.CallbackContext ctx)
    {
        jumpHeld = true;
        jumpBufferCounter = jumpBufferTime; // buffer the press

        // If touching a wall and airborne, wall jump immediately
        if (PlayerIsTouchingWall() && !isGrounded)
        {
            DoWallJump();
        }
    }

    private void OnJumpCanceled(InputAction.CallbackContext ctx)
    {
        jumpHeld = false;
        // Cut jump if still moving up
        if (rb.linearVelocity.y > 0f)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y * jumpCutMultiplier);
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

    private void DoWallJump()
    {
        // Jump away from the wall based on facing direction
        int awayFromWallDir = transform.localScale.x > 0 ? -1 : 1;
        rb.linearVelocity = new Vector2(wallJumpForce.x * awayFromWallDir, wallJumpForce.y);

        // Flip to face the new direction
        FlipPlayerSprite(awayFromWallDir > 0);

        // Briefly lock horizontal control to let the wall jump breathe
        wallJumpCounter = wallJumpLockTime;
        jumpBufferCounter = 0f;

        // After a wall jump, allow one additional air jump if you have multi-jumps, otherwise consume it
        if (maxJumps > 1)
            jumpCount = Mathf.Max(0, maxJumps - 1);
        else
            jumpCount = 0;
    }

    private void Knockback()
    { 
        if (isKnocked) return;

        StartCoroutine(KnockbackCoroutine());
        animator.SetTrigger(anim_param_knockback);

        rb.linearVelocity = new Vector2(knockbackForce.x * (transform.localScale.x > 0 ? -1 : 1), knockbackForce.y);
    }

    private IEnumerator KnockbackCoroutine()
    {
        isKnocked = true;
        canBeKnocked = false;
        // Determine knockback direction based on current facing
        int knockbackDir = transform.localScale.x > 0 ? -1 : 1;

        yield return new WaitForSeconds(knockbackDuration);

        isKnocked = false;
        canBeKnocked = true;
    }

    #endregion

    #region Helpers
    private void HandleWallSlideInteraction()
    {
        // Consider wall sliding only when airborne and descending
        isWallSliding = PlayerIsTouchingWall() && !isGrounded && rb.linearVelocity.y < 0f;
        if (isWallSliding)
        {
            // Clamp fall speed while sliding
            if (rb.linearVelocity.y < -wallSlideSpeed)
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, -wallSlideSpeed);
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
    #endregion
}

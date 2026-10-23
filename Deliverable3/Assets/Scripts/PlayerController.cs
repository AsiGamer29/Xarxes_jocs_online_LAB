using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 7f;
    public float jumpForce = 14f;

    public Transform groundCheck;
    public float groundRadius = 0.2f;
    public LayerMask groundLayer;

    public float dashSpeed = 20f;
    public float dashDuration = 0.18f;
    public float dashCooldown = 0.4f;

    public KeyCode crouchKey = KeyCode.LeftControl;
    public KeyCode dashKey = KeyCode.LeftShift;

    private Rigidbody2D rb;
    private Animator anim;
    private SpriteRenderer sr;

    private float moveInput;
    private bool isGrounded;
    private bool isCrouching;
    private bool facingRight = true;

    private bool isDashing;
    private bool canDash = true;
    private float dashTimer;
    private float cooldownTimer;
    private float dashDirection;
    private float defaultGravity;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        sr = GetComponent<SpriteRenderer>();
        defaultGravity = rb.gravityScale;
    }

    void Update()
    {
        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundRadius, groundLayer);

        if (isDashing)
        {
            dashTimer -= Time.deltaTime;
            if (dashTimer <= 0f) EndDash();
            UpdateAnimator();
            return;
        }

        cooldownTimer -= Time.deltaTime;

        if (isGrounded) canDash = true;

        isCrouching = Input.GetKey(crouchKey) && isGrounded;

        moveInput = Input.GetAxisRaw("Horizontal");
        if (isCrouching) moveInput = 0f;

        // Girar el sprite según hacia dónde mire
        if (moveInput > 0) facingRight = true;
        if (moveInput < 0) facingRight = false;
        sr.flipX = !facingRight;

        if (Input.GetButtonDown("Jump") && isGrounded && !isCrouching)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
        }

        if (Input.GetKeyDown(dashKey) && canDash && !isCrouching && cooldownTimer <= 0f)
        {
            StartDash();
        }

        UpdateAnimator();
    }

    void FixedUpdate()
    {
        if (isDashing)
        {
            rb.linearVelocity = new Vector2(dashDirection * dashSpeed, 0f);
            return;
        }

        rb.linearVelocity = new Vector2(moveInput * moveSpeed, rb.linearVelocity.y);
    }

    void StartDash()
    {
        isDashing = true;
        canDash = false;
        dashTimer = dashDuration;

        dashDirection = 1f;
        if (!facingRight) dashDirection = -1f;

        rb.gravityScale = 0f;
    }

    void EndDash()
    {
        isDashing = false;
        cooldownTimer = dashCooldown;
        rb.gravityScale = defaultGravity;
        rb.linearVelocity = Vector2.zero;
    }

    void UpdateAnimator()
    {
        anim.SetFloat("Speed", Mathf.Abs(moveInput));
        anim.SetBool("IsCrouching", isCrouching);
        anim.SetBool("IsGrounded", isGrounded);
        anim.SetBool("IsDashing", isDashing);
    }

    void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(groundCheck.position, groundRadius);
    }
}
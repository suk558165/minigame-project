using Cysharp.Threading.Tasks;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 6f;
    public float jumpForce = 17f;
    public int maxJumpCharges = 2;
    public LayerMask groundLayer;
    public LayerMask platformLayer;
    public float dropDownDuration = 0.15f;

    [Tooltip("아래 점프 시 초기 하강 속도")]
    public float dropDownSpeed = 8f;

    [Header("Gravity")]
    public float gravityScale = 4f;
    public float fallGravityMultiplier = 2.5f;

    [Header("Air Control")]
    [Tooltip("공중에서 목표 속도에 도달하는 가속 (값이 클수록 즉각 반응, 작을수록 관성 유지)")]
    public float airAcceleration = 40f;

    [Header("Knockback")]
    public float knockbackDuration = 0.15f;

    [Header("Audio")]
    public AudioClip jumpSound;
    public AudioClip dashSound;

    [Header("Dash")]
    public float dashSpeedMultiplier = 3f;
    public float dashDuration = 0.3f;
    public float dashCooldown = 1f;
    public int maxDashCharges = 2;

    public bool IsGrounded { get; private set; }
    public bool IsOnPlatform { get; private set; }
    public bool IsDashing { get; private set; }

    /// <summary>마지막 대쉬가 끝난 시각(Time.time). 대쉬 후 버프 판정용.</summary>
    public float LastDashEndTime { get; private set; } = -999f;

    public float MoveInput { get; private set; }
    public Transform Visuals { get; private set; }
    public SpriteRenderer Sr { get; private set; }
    public bool AirAttackUsed { get; set; }

    private Rigidbody2D rb;
    private Animator animator;
    private Inventory inventory;

    private float baseWalkSpeed;
    private float baseJumpForce;
    private int baseDashCharges;
    private float baseDashDuration;

    private StatBonus cachedBonus;

    void RefreshStatBonus()
    {
        cachedBonus = inventory?.GetTotalStatBonus() ?? default;
    }

    float EffectiveWalkSpeed => baseWalkSpeed * (1f + cachedBonus.speed);
    float EffectiveJumpForce => baseJumpForce * (1f + cachedBonus.jump);
    int EffectiveDashCharges => baseDashCharges + cachedBonus.dashCount;
    float EffectiveDashDuration => baseDashDuration * (1f + cachedBonus.dashRange);

    private int jumpCharges;
    private bool wasGrounded;
    private float dashTimer;
    private float dashCooldownTimer;
    private int dashCharges;
    private float dashDirection;
    private Vector2 knockbackVelocity;
    private float knockbackTimer;
    private bool isDropping;

    private DashGhostEffect dashGhost;
    private Collider2D mainCollider;

    private static readonly int HashSpeed = Animator.StringToHash("Speed");
    private static readonly int HashIsGrounded = Animator.StringToHash("IsGrounded");

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        Sr = GetComponent<SpriteRenderer>();
        Visuals = transform.Find("Visuals");
        inventory = GetComponent<Inventory>();

        dashGhost = GetComponent<DashGhostEffect>();
        mainCollider = GetComponent<Collider2D>();

        baseWalkSpeed = walkSpeed;
        baseJumpForce = jumpForce;
        baseDashCharges = maxDashCharges;
        baseDashDuration = dashDuration;

        rb.gravityScale = gravityScale;
        dashCharges = maxDashCharges;
        jumpCharges = maxJumpCharges;

        isDropping = false;
    }

    public void HandleInput()
    {
        RefreshStatBonus();

        if (InventoryUI.IsOpen || ShopUI.IsOpen || PauseMenu.IsPaused)
        {
            MoveInput = 0f;
            return;
        }

        var im = InputManager.Instance;
        float left = Input.GetKey(im?.MoveLeft ?? KeyCode.LeftArrow) ? -1f : 0f;
        float right = Input.GetKey(im?.MoveRight ?? KeyCode.RightArrow) ? 1f : 0f;
        MoveInput = left + right;

        if (IsGrounded && !wasGrounded)
        {
            jumpCharges = maxJumpCharges;
            AirAttackUsed = false;
        }
        wasGrounded = IsGrounded;

        HandleJump();
        HandleDash();
    }

    void HandleJump()
    {
        var jumpKey = InputManager.Instance?.Jump ?? KeyCode.Space;
        if (!Input.GetKeyDown(jumpKey))
            return;

        bool pressingDown = Input.GetKey(InputManager.Instance?.MoveDown ?? KeyCode.DownArrow);

        if (pressingDown && IsOnPlatform)
        {
            DropDown().Forget();
            return;
        }

        if (jumpCharges > 0)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, EffectiveJumpForce);
            jumpCharges--;
            AudioManager.Instance?.PlaySFX(jumpSound);
        }
    }

    void HandleDash()
    {
        if (dashCharges == 0)
        {
            dashCooldownTimer -= Time.deltaTime;
            if (dashCooldownTimer <= 0f)
                dashCharges = EffectiveDashCharges;
        }

        var dashKey = InputManager.Instance?.Dash ?? KeyCode.Z;
        if (Input.GetKeyDown(dashKey) && !IsDashing && dashCharges > 0)
        {
            bool facingLeft =
                Visuals != null ? Visuals.localScale.x < 0f : (Sr != null && Sr.flipX);
            dashDirection = facingLeft ? -1f : 1f;

            IsDashing = true;
            dashTimer = EffectiveDashDuration;
            dashCharges--;
            if (dashCharges == 0)
                dashCooldownTimer = dashCooldown;
            AudioManager.Instance?.PlaySFX(dashSound);

            if (!IsGrounded)
                jumpCharges = 0;

            rb.gravityScale = 0f;
            dashGhost?.StartGhost();
        }

        if (IsDashing)
        {
            dashTimer -= Time.deltaTime;
            if (dashTimer <= 0f)
            {
                IsDashing = false;
                LastDashEndTime = Time.time;
                rb.gravityScale = gravityScale;
                dashGhost?.StopGhost();
            }
        }
    }

    public void UpdateAnimatorAndFlip(bool isAttacking, bool flipAllowed = true)
    {
        animator.SetFloat(HashSpeed, Mathf.Abs(MoveInput) > 0f ? 1f : 0f);
        animator.SetBool(HashIsGrounded, IsGrounded || IsDashing);

        if (!isAttacking || flipAllowed)
        {
            if (MoveInput > 0f)
                Flip(false);
            else if (MoveInput < 0f)
                Flip(true);
        }
    }

    public void Flip(bool flipLeft)
    {
        if (Visuals != null)
        {
            Vector3 scale = Visuals.localScale;
            scale.x = flipLeft ? -Mathf.Abs(scale.x) : Mathf.Abs(scale.x);
            Visuals.localScale = scale;
        }

        if (Sr != null)
            Sr.flipX = flipLeft;
    }

    public void ApplyKnockback(Vector2 velocity)
    {
        knockbackVelocity = velocity;
        knockbackTimer = knockbackDuration;
    }

    public void FixedUpdateMovement()
    {
        if (knockbackTimer > 0f)
        {
            knockbackTimer -= Time.fixedDeltaTime;
            rb.linearVelocity = knockbackVelocity;
            return;
        }

        if (IsDashing)
        {
            rb.linearVelocity = new Vector2(
                dashDirection * EffectiveWalkSpeed * dashSpeedMultiplier,
                0f
            );
        }
        else
        {
            float targetX = MoveInput * EffectiveWalkSpeed;
            float newX;
            if (IsGrounded)
            {
                // 지상에서는 즉시 반응 (걷기 느낌 유지)
                newX = targetX;
            }
            else
            {
                // 공중에서는 가속도 기반으로 천천히 변화 → 관성 유지, "막힌 느낌" 제거
                newX = Mathf.MoveTowards(
                    rb.linearVelocity.x,
                    targetX,
                    airAcceleration * Time.fixedDeltaTime
                );
            }
            rb.linearVelocity = new Vector2(newX, rb.linearVelocity.y);

            if (rb.linearVelocity.y < 0f)
                rb.linearVelocity +=
                    Vector2.up
                    * Physics2D.gravity.y
                    * (fallGravityMultiplier - 1f)
                    * Time.fixedDeltaTime;
        }

        // 발밑 판정은 몸 콜라이더 폭 전체로 한다. 가운데 좁은 원으로 재면 플랫폼 끝에 섰을 때
        // 콜라이더는 아직 바닥에 걸쳐 있는데 판정만 빠져 점프(공중) 동작이 나온다.
        // 폭은 콜라이더보다 살짝 좁게 잡아 옆 벽을 바닥으로 오인하지 않게 한다.
        Bounds body = mainCollider.bounds;
        Vector2 footCenter = new Vector2(body.center.x, body.min.y - 0.05f);
        Vector2 footSize = new Vector2(body.size.x - 0.04f, 0.1f);
        IsGrounded =
            Physics2D.OverlapBox(footCenter, footSize, 0f, groundLayer | platformLayer)
            && rb.linearVelocity.y <= 1.0f;
        IsOnPlatform = Physics2D.OverlapBox(footCenter, footSize, 0f, platformLayer);

        // 통과 중에는 접지로 보지 않는다. OverlapCircle은 IgnoreCollision을 무시하기 때문에
        // 그대로 두면 점프 횟수가 계속 회복되고 애니메이터가 착지 상태로 남는다.
        if (isDropping)
        {
            IsGrounded = false;
            IsOnPlatform = false;
        }
    }

    async UniTaskVoid DropDown()
    {
        if (isDropping || mainCollider == null)
            return;

        // 발밑 플랫폼을 콜라이더 폭 전체로 탐색.
        // 작은 원으로 찾으면 플랫폼 가장자리에 섰을 때 놓쳐서 아래 점프가 씹힌다.
        Bounds b = mainCollider.bounds;
        var hits = Physics2D.OverlapBoxAll(
            new Vector2(b.center.x, b.min.y - 0.05f),
            new Vector2(b.size.x * 0.9f, 0.2f),
            0f,
            platformLayer
        );

        var toIgnore = new System.Collections.Generic.List<Collider2D>();
        float lowestBottom = float.PositiveInfinity;
        foreach (var h in hits)
        {
            if (h == null)
                continue;
            toIgnore.Add(h);
            lowestBottom = Mathf.Min(lowestBottom, h.bounds.min.y);
        }
        if (toIgnore.Count == 0)
            return;

        isDropping = true;

        // 대쉬 중에는 FixedUpdateMovement가 y속도를 0으로 고정해 내려가지 못한다.
        // 그대로 두면 플랫폼 안에 낀 채 충돌이 복구되어 튕겨 나온다.
        if (IsDashing)
        {
            IsDashing = false;
            LastDashEndTime = Time.time;
            rb.gravityScale = gravityScale;
            dashGhost?.StopGhost();
        }

        foreach (var p in toIgnore)
            Physics2D.IgnoreCollision(mainCollider, p, true);

        rb.linearVelocity = new Vector2(rb.linearVelocity.x, -dropDownSpeed);

        // 몸 전체가 플랫폼 아래로 빠져나가면 즉시 충돌 복구.
        // 타임아웃은 안전장치일 뿐이며, 여기 걸리면 플랫폼 안에서 복구되어 튕길 수 있다.
        // 통과 도중 방이 전환되면 플레이어가 파괴될 수 있으므로 토큰으로 중단시킨다.
        var token = this.GetCancellationTokenOnDestroy();
        float elapsed = 0f;
        while (elapsed < 0.6f)
        {
            await UniTask.Yield(token);
            elapsed += Time.deltaTime;
            if (mainCollider == null)
                return;
            if (mainCollider.bounds.max.y < lowestBottom)
                break;
        }

        foreach (var p in toIgnore)
            if (p != null)
                Physics2D.IgnoreCollision(mainCollider, p, false);
        isDropping = false;
    }
}

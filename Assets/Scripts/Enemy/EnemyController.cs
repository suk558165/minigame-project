using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(SpriteRenderer))]
public class EnemyController : MonoBehaviour, IDamageable
{
    public static readonly List<EnemyController> Instances = new List<EnemyController>();

    // 도메인 리로드를 끈 상태에서도 이전 플레이의 잔여 항목이 남지 않도록 초기화
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instances.Clear();

    [Header("Stats")]
    public float maxHp = 50f;
    public float moveSpeed = 2f;
    public float damage = 10f;

    [Header("Detection")]
    public float detectionRange = 6f;
    public float attackRange = 1.2f;
    public float attackCooldown = 1.2f;
    public float spawnDelay = 2.5f;
    public LayerMask groundLayer;

    [Header("Patrol")]
    public float patrolDistance = 4f;
    public float chaseYThreshold = 1.2f;

    [Header("Flying")]
    [Tooltip("체크하면 중력 없이 날아다니며 층을 넘어 추격하고, 옆에 붙어 돌진 공격한다 (박쥐·벌)")]
    public bool isFlying = false;

    [Header("Ranged")]
    public bool isRanged = false;
    public bool aimAtPlayer = false;
    public GameObject projectilePrefab;

    [Tooltip("오른쪽을 볼 때 기준으로 배치한다. 왼쪽을 보면 좌우가 자동으로 뒤집힌다")]
    public Transform firePoint;
    public float projectileSpeed = 8f;

    [Tooltip("발사체 크기 배수. 몸집이 큰 적일수록 크게")]
    public float projectileScale = 1f;
    private ObjectPool<Projectile> projPool;
    public float safeDistance = 3f;

    [Tooltip("원거리 적의 공격 쿨다운에 곱해지는 배수 — 회피 시간 확보용")]
    public float rangedCooldownMultiplier = 1.8f;

    [Tooltip("원거리 적의 Y축 추적/공격 허용 범위 — 이 값 이내일 때만 공격 (일직선 체크)")]
    public float rangedYThreshold = 0.8f;

    [Tooltip("발사체 회전 속도 (도/초). 0이면 회전 없음")]
    public float projectileSpinSpeed = 0f;

    [Header("HP Bar")]
    public Vector3 hpBarOffset = new Vector3(0f, -0.6f, 0f);

    [Header("Knockback")]
    public float knockbackForce = 6f;
    public float knockbackDuration = 0.15f;

    [Header("Audio")]
    public AudioClip attackSound;
    public AudioClip deathSound;

    [Header("Melee")]
    public Collider2D meleeHitbox;

    [Header("Drops")]
    public GameObject goldDropPrefab;
    public int goldDropMin = 3;
    public int goldDropMax = 8;
    public GameObject potionDropPrefab;

    [Range(0f, 1f)]
    public float potionDropChance = 0.2f;
    public float potionHealAmount = 20f;

    [Header("Edge Detection")]
    public float edgeCheckDepth = 1.5f;
    public LayerMask platformLayer;

    [Header("Sprite")]
    [Tooltip("원본 스프라이트가 왼쪽을 보고 있으면 체크")]
    public bool spriteFacesLeft = false;

    // 추격 연출 — 던그리드·스컬 계열 기준값
    const float AggroMemory = 3f; // 시야에서 놓친 뒤에도 추격을 유지하는 시간
    const float LeashMultiplier = 1.5f; // 추격 중에는 감지 범위의 이 배수까지 계속 쫓는다
    const float AlertDuration = 0.35f; // 발견 순간 멈칫하는 시간
    const float AlertHopSpeed = 3.5f;
    const float ChaseSpeedMultiplier = 1.5f;
    const float FacingDeadzone = 0.3f; // 플레이어가 바로 위·아래일 때 좌우가 매 프레임 뒤집히지 않게
    const float HitStunDuration = 0.3f;
    const float RetreatRatio = 0.5f; // 원거리 적은 safeDistance의 이 비율보다 가까우면 물러난다
    const float PlayerCenterHeight = 0.9f;

    // 비행 몬스터
    const float FlyHoverHeight = 1.5f; // 순찰할 때 스폰 지점보다 이만큼 떠서 다닌다
    const float FlyBobAmplitude = 0.25f;
    const float FlyAcceleration = 12f; // 속도를 즉시 바꾸지 않고 가속해 날갯짓 관성을 준다
    const float FlyAttackSlotTolerance = 0.4f;
    const float LungeDuration = 0.25f;
    const float LungeSpeedMultiplier = 4f;

    private Rigidbody2D rb;
    private Animator animator;
    private SpriteRenderer sr;
    private Collider2D col;

    private float hp;
    private float hitboxOffsetX;
    private float hitboxCenterY;
    private bool isDead;
    public bool IsDead => isDead;
    private float attackTimer;
    private float spawnDelayTimer;
    private EnemyHealthBar healthBar;
    public float attackDamageDelay = 0.2f;

    private Transform player;
    private Collider2D playerCol;
    private Vector2 patrolOrigin;
    private int patrolDir = 1;

    private float aggroTimer;
    private float alertTimer;
    private float hitStunTimer;
    private float attackGrace;
    private float lungeTimer;
    private Vector2 lungeDir;

    private CancellationTokenSource _cts = new();

    private static readonly int HashSpeed = Animator.StringToHash("Speed");
    private static readonly int HashAttack = Animator.StringToHash("Attack");
    private static readonly int HashIsDead = Animator.StringToHash("IsDead");
    private static readonly int HashIsHit = Animator.StringToHash("IsHit");
    private static readonly int StateAttack = Animator.StringToHash("Attack");

    CancellationToken RefreshToken()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        return _cts.Token;
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        hp = maxHp;
        patrolOrigin = transform.position;
        healthBar = gameObject.AddComponent<EnemyHealthBar>();
        healthBar.Init(hpBarOffset);

        if (isFlying)
            rb.gravityScale = 0f;

        if (meleeHitbox != null)
        {
            meleeHitbox.enabled = false;
            hitboxOffsetX = Mathf.Abs(meleeHitbox.offset.x);
            hitboxCenterY = meleeHitbox.offset.y * meleeHitbox.transform.lossyScale.y;
        }
    }

    /// <summary>
    /// 스프라이트 반전과 히트박스 위치를 함께 처리한다.
    /// SpriteRenderer.flipX는 콜라이더에 영향이 없으므로 히트박스 오프셋을 직접 미러링해야 한다.
    /// </summary>
    void SetFacing(bool faceRight)
    {
        sr.flipX = faceRight ? spriteFacesLeft : !spriteFacesLeft;

        if (meleeHitbox != null)
        {
            Vector2 o = meleeHitbox.offset;
            o.x = faceRight ? hitboxOffsetX : -hitboxOffsetX;
            meleeHitbox.offset = o;
        }
    }

    bool IsFacingRight => spriteFacesLeft ? sr.flipX : !sr.flipX;

    void OnEnable() => Instances.Add(this);

    void OnDisable() => Instances.Remove(this);

    void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    void Start()
    {
        if (PlayerRef.Exists)
        {
            player = PlayerRef.Transform;
            playerCol = PlayerRef.GameObject.GetComponent<Collider2D>();
            Physics2D.IgnoreLayerCollision(gameObject.layer, PlayerRef.GameObject.layer, true);
        }
        // 적끼리 물리 충돌하면 겹쳐 스폰될 때 서로 머리 위로 올라탄다. 간격은 CheckAllies로 벌린다.
        Physics2D.IgnoreLayerCollision(gameObject.layer, gameObject.layer, true);
        spawnDelayTimer = spawnDelay;
        // 스폰 직후 즉시 공격 방지 — 첫 공격도 쿨다운 후에 발동
        attackTimer = attackCooldown;
    }

    void Update()
    {
        if (isDead)
            return;

        attackTimer -= Time.deltaTime;
        attackGrace -= Time.deltaTime;

        // 경직 중에는 속도를 건드리지 않는다 — 넉백이 이동 처리에 덮여 사라지지 않게.
        if (hitStunTimer > 0f)
        {
            hitStunTimer -= Time.deltaTime;
            animator.SetFloat(HashSpeed, 0f);
            return;
        }

        if (spawnDelayTimer > 0f)
        {
            spawnDelayTimer -= Time.deltaTime;
            Patrol();
            return;
        }

        // 공격 모션 중에는 제자리(비행 몬스터는 돌진). 미끄러지며 휘두르지 않게 한다.
        if (IsAttacking())
        {
            if (isFlying)
                UpdateLunge();
            else
                Move(0f);
            return;
        }

        UpdateAggro();
        if (aggroTimer <= 0f)
        {
            Patrol();
            return;
        }

        if (alertTimer > 0f)
        {
            alertTimer -= Time.deltaTime;
            if (isFlying)
                Fly(Vector2.zero, 0f);
            else
                Move(0f);
            FacePlayer();
            return;
        }

        if (isFlying)
            UpdateFlying();
        else if (isRanged)
            UpdateRanged();
        else
            UpdateMelee();
    }

    bool IsAttacking() =>
        attackGrace > 0f || animator.GetCurrentAnimatorStateInfo(0).shortNameHash == StateAttack;

    Vector2 PlayerCenter =>
        playerCol != null
            ? (Vector2)playerCol.bounds.center
            : (Vector2)player.position + Vector2.up * PlayerCenterHeight;

    // 기준점이 발밑이 아닌 스프라이트가 있다(애벌레는 발보다 1.4 아래). 높이 비교는 콜라이더 발밑끼리 한다.
    float FeetY => col != null && col.enabled ? col.bounds.min.y : transform.position.y;
    float PlayerFeetY => playerCol != null ? playerCol.bounds.min.y : player.position.y;

    /// <summary>
    /// 발견·추격 유지·포기를 관리한다.
    /// 한 번 발견하면 점프로 높이가 잠깐 벌어져도 놓치지 않고, 완전히 놓치면 그 자리를 새 순찰 중심으로 삼는다.
    /// </summary>
    void UpdateAggro()
    {
        if (player == null)
        {
            aggroTimer = 0f;
            return;
        }

        Vector2 d = new Vector2(player.position.x - transform.position.x, PlayerFeetY - FeetY);
        // 비행 몬스터는 높이 제한 없이 쫓는다.
        float yLimit = isFlying ? float.MaxValue : isRanged ? rangedYThreshold : chaseYThreshold;

        if (aggroTimer > 0f)
        {
            if (d.magnitude <= detectionRange * LeashMultiplier && Mathf.Abs(d.y) <= yLimit)
            {
                aggroTimer = AggroMemory;
                return;
            }
            aggroTimer -= Time.deltaTime;
            if (aggroTimer <= 0f)
                patrolOrigin = transform.position;
            return;
        }

        if (d.magnitude > detectionRange || Mathf.Abs(d.y) > yLimit)
            return;

        aggroTimer = AggroMemory;
        alertTimer = AlertDuration;
        FacePlayer();
        if (!isFlying && IsGrounded())
            rb.linearVelocity = new Vector2(0f, AlertHopSpeed);
    }

    void UpdateMelee()
    {
        float dx = player.position.x - transform.position.x;
        bool sameLevel = Mathf.Abs(PlayerFeetY - FeetY) <= chaseYThreshold;

        if (sameLevel && Mathf.Abs(dx) <= attackRange)
        {
            Move(0f);
            FacePlayer();
            if (attackTimer <= 0f)
                StartAttack();
            return;
        }

        ChaseTowards(dx);
    }

    void UpdateRanged()
    {
        float dx = player.position.x - transform.position.x;
        float adx = Mathf.Abs(dx);
        bool sameLine = Mathf.Abs(PlayerFeetY - FeetY) <= rangedYThreshold;

        if (!sameLine || adx > safeDistance)
        {
            ChaseTowards(dx);
            return;
        }

        // 너무 붙으면 뒤로 물러나며 거리를 벌린다. 몸은 계속 플레이어를 향한다.
        float away = -Mathf.Sign(dx);
        if (adx < safeDistance * RetreatRatio && !IsEdgeAhead(away))
            Move(away);
        else
            Move(0f);
        FacePlayer();

        if (attackTimer <= 0f)
            StartAttack();
    }

    /// <summary>
    /// 플레이어 옆 공격 위치로 날아가 붙는다. 히트박스 높이를 플레이어 몸 중앙에 맞춰야 돌진이 빗나가지 않는다.
    /// </summary>
    void UpdateFlying()
    {
        Vector2 target = PlayerCenter;
        float side = transform.position.x < target.x ? -1f : 1f;
        Vector2 slot = new Vector2(target.x + side * attackRange * 0.7f, target.y - hitboxCenterY);
        Vector2 toSlot = slot - (Vector2)transform.position;

        if (toSlot.magnitude <= FlyAttackSlotTolerance)
        {
            Fly(Vector2.zero, 0f);
            FacePlayer();
            if (attackTimer <= 0f)
                StartAttack();
            return;
        }

        Fly(toSlot.normalized, moveSpeed * ChaseSpeedMultiplier);
        FacePlayer();
    }

    void UpdateLunge()
    {
        if (lungeTimer > 0f)
        {
            lungeTimer -= Time.deltaTime;
            rb.linearVelocity = lungeDir * moveSpeed * LungeSpeedMultiplier;
            return;
        }
        rb.linearVelocity = Vector2.MoveTowards(
            rb.linearVelocity,
            Vector2.zero,
            FlyAcceleration * 3f * Time.deltaTime
        );
    }

    /// <summary>
    /// 플레이어 쪽으로 달린다. 발판 끝에 닿으면 멈춰서 플레이어를 바라보며 기다린다
    /// (다른 층의 플레이어를 쫓아 떨어지지 않는다).
    /// </summary>
    void ChaseTowards(float dx)
    {
        if (Mathf.Abs(dx) <= FacingDeadzone)
        {
            Move(0f);
            return;
        }

        float dir = Mathf.Sign(dx);
        if (IsEdgeAhead(dir))
        {
            Move(0f);
            SetFacing(dir > 0f);
            return;
        }
        Move(dir, moveSpeed * ChaseSpeedMultiplier);
    }

    void FacePlayer()
    {
        if (player == null)
            return;
        float dx = player.position.x - transform.position.x;
        if (Mathf.Abs(dx) > FacingDeadzone)
            SetFacing(dx > 0f);
    }

    void StartAttack()
    {
        attackTimer = isRanged
            ? attackCooldown * Mathf.Max(1f, rangedCooldownMultiplier)
            : attackCooldown;
        // 트리거를 건 프레임에는 애니메이터가 아직 Attack 상태가 아니므로 잠깐 잠금을 유지한다.
        attackGrace = 0.1f;
        animator.SetTrigger(HashAttack);
        AudioManager.Instance?.PlaySFX(attackSound);
        if (isRanged)
            ShootAfterDelay(attackDamageDelay, _cts.Token).Forget();

        if (isFlying && player != null)
        {
            Vector2 hitboxCenter = (Vector2)transform.position + Vector2.up * hitboxCenterY;
            lungeDir = (PlayerCenter - hitboxCenter).normalized;
            lungeTimer = LungeDuration;
        }
    }

    void ShootProjectile()
    {
        if (projectilePrefab == null || player == null)
            return;

        Vector3 origin = transform.position;
        if (firePoint != null)
        {
            // flipX는 자식 위치를 뒤집지 않으므로 바라보는 방향에 맞춰 직접 미러링한다.
            Vector3 offset = firePoint.position - transform.position;
            offset.x = Mathf.Abs(offset.x) * (IsFacingRight ? 1f : -1f);
            origin += offset;
        }

        Vector2 dir;
        if (aimAtPlayer)
            // 플레이어 기준점은 발밑이라 그대로 조준하면 바닥으로 쏜다. 몸 중앙을 노린다.
            dir = (PlayerCenter - (Vector2)origin).normalized;
        else
            dir = IsFacingRight ? Vector2.right : Vector2.left;

        if (projPool == null)
            projPool = new ObjectPool<Projectile>(projectilePrefab.GetComponent<Projectile>());
        var projComp = projPool.Get(origin, Quaternion.identity);
        projComp.Pool = projPool;
        projComp.transform.localScale = Vector3.one * projectileScale;
        projComp.Init(dir, projectileSpeed, damage, gameObject, spinSpeed: projectileSpinSpeed);
    }

    void Patrol()
    {
        if (isFlying)
        {
            FlyPatrol();
            return;
        }

        // 발 아래에 바닥이 없으면 즉시 수평 이동 중지 (안전망)
        if (!IsGrounded())
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        float distFromOrigin = transform.position.x - patrolOrigin.x;

        if (distFromOrigin >= patrolDistance)
            patrolDir = -1;
        else if (distFromOrigin <= -patrolDistance)
            patrolDir = 1;

        // 가는 방향이 다른 적에게 막혀 있으면 반대편이 비었을 때만 돌아선다 (양쪽 다 막히면 매 프레임 뒤집히므로).
        CheckAllies(out bool leftTaken, out bool rightTaken, out _);
        if (patrolDir < 0 ? leftTaken && !rightTaken : rightTaken && !leftTaken)
            patrolDir = -patrolDir;

        if (IsEdgeAhead(patrolDir))
        {
            // 양쪽 다 낭떠러지면 돌아설 곳이 없다. 뒤집으면 매 프레임 방향이 반전돼 제자리에서 떨린다.
            if (IsEdgeAhead(-patrolDir))
            {
                Move(0f);
                return;
            }
            patrolDir = -patrolDir;
            Move(patrolDir); // 멈추지 않고 즉시 반대로 이동
            return;
        }

        Move(patrolDir);
    }

    /// <summary>순찰 중심 위를 좌우로 오가며 살짝 위아래로 떠다닌다.</summary>
    void FlyPatrol()
    {
        float distFromOrigin = transform.position.x - patrolOrigin.x;
        if (distFromOrigin >= patrolDistance)
            patrolDir = -1;
        else if (distFromOrigin <= -patrolDistance)
            patrolDir = 1;

        // 반대편도 막혀 있으면 뒤집지 않는다 — 좁은 통로에 끼었을 때 매 프레임 반전되어 떨리므로.
        if (IsWallAhead(patrolDir) && !IsWallAhead(-patrolDir))
            patrolDir = -patrolDir;

        float hoverY = patrolOrigin.y + FlyHoverHeight + Mathf.Sin(Time.time * 2f) * FlyBobAmplitude;
        float vy = Mathf.Clamp(hoverY - transform.position.y, -1f, 1f);
        Fly(new Vector2(patrolDir, vy), moveSpeed);
    }

    bool IsWallAhead(float dir)
    {
        if (col == null)
            return false;
        Bounds b = col.bounds;
        return Physics2D
                .Raycast(b.center, Vector2.right * dir, b.extents.x + 0.3f, groundLayer)
                .collider != null;
    }

    bool IsGrounded()
    {
        float footY = col != null ? col.bounds.min.y : transform.position.y;
        Vector2 origin = new Vector2(transform.position.x, footY + 0.05f);
        return Physics2D.Raycast(origin, Vector2.down, 0.2f, groundLayer | platformLayer).collider
            != null;
    }

    bool IsEdgeAhead(float dir)
    {
        if (dir == 0f)
            return false;
        float xOffset = (col != null ? col.bounds.extents.x : 0.3f) + 0.3f;
        float footY = col != null ? col.bounds.min.y : transform.position.y;
        // 레이를 발보다 0.3 위에서 시작 → 콜라이더 내부에서 시작하는 오작동 방지
        const float rayStartOffset = 0.3f;
        Vector2 origin = new Vector2(transform.position.x + dir * xOffset, footY + rayStartOffset);
        return Physics2D
                .Raycast(
                    origin,
                    Vector2.down,
                    edgeCheckDepth + rayStartOffset,
                    groundLayer | platformLayer
                )
                .collider == null;
    }

    const float SeparationSpeed = 1.5f;
    const float SeparationMargin = 0.15f;

    /// <summary>
    /// 같은 층의 다른 적과의 간격 검사.
    /// left/rightTaken: 그 방향으로 걸어가면 겹치는지 (여유 간격 포함 — 경계에서 걷기/멈춤이 떨리지 않게).
    /// push: 이미 겹쳐 있을 때 밀려나야 할 방향과 세기(-1~1).
    /// </summary>
    void CheckAllies(out bool leftTaken, out bool rightTaken, out float push)
    {
        leftTaken = rightTaken = false;
        push = 0f;
        if (col == null)
            return;

        Bounds me = col.bounds;
        foreach (var other in Instances)
        {
            if (other == this || other.isDead || other.col == null)
                continue;
            Bounds ob = other.col.bounds;
            if (Mathf.Abs(me.min.y - ob.min.y) > 0.5f)
                continue; // 다른 층

            float dx = me.center.x - ob.center.x;
            float minDist = me.extents.x + ob.extents.x;
            if (Mathf.Abs(dx) >= minDist + SeparationMargin)
                continue;

            // 완전히 같은 위치면 인스턴스 ID로 방향을 갈라 서로 반대로 밀리게 한다.
            float away = Mathf.Abs(dx) > 0.001f
                ? Mathf.Sign(dx)
                : (GetInstanceID() > other.GetInstanceID() ? 1f : -1f);
            if (away > 0f)
                leftTaken = true;
            else
                rightTaken = true;
            // 겹친 깊이로 가중한다. 방향만 더하면 양옆에 끼인 적은 밀림이 상쇄되어 멈춘다.
            if (Mathf.Abs(dx) < minDist)
                push += away * (minDist - Mathf.Abs(dx));
        }
        push = Mathf.Clamp(push, -1f, 1f);
    }

    void Move(float dir) => Move(dir, moveSpeed);

    void Move(float dir, float speed)
    {
        CheckAllies(out bool leftTaken, out bool rightTaken, out float push);
        float vx = (dir < 0f && leftTaken) || (dir > 0f && rightTaken) ? 0f : dir * speed;
        if (push != 0f && !IsEdgeAhead(push))
            vx += push * SeparationSpeed;

        rb.linearVelocity = new Vector2(vx, rb.linearVelocity.y);
        animator.SetFloat(HashSpeed, vx != 0f ? 1f : 0f);

        if (dir > 0f)
            SetFacing(true);
        else if (dir < 0f)
            SetFacing(false);
    }

    void Fly(Vector2 dir, float speed)
    {
        CheckAllies(out _, out _, out float push);
        Vector2 target = dir * speed + Vector2.right * (push * SeparationSpeed);
        rb.linearVelocity = Vector2.MoveTowards(
            rb.linearVelocity,
            target,
            FlyAcceleration * Time.deltaTime
        );
        animator.SetFloat(HashSpeed, target.sqrMagnitude > 0.01f ? 1f : 0f);

        if (dir.x > 0.1f)
            SetFacing(true);
        else if (dir.x < -0.1f)
            SetFacing(false);
    }

    public void EnableHitbox()
    {
        if (meleeHitbox == null)
            return;
        var hitboxComp = meleeHitbox.GetComponent<MeleeHitbox>();
        if (hitboxComp != null)
            // 공격 지속 시간만큼만 활성화 → 이벤트 누락돼도 자동 비활성화
            hitboxComp.Activate(attackCooldown * 0.4f);
        else
            meleeHitbox.enabled = true;
    }

    public void DisableHitbox()
    {
        if (meleeHitbox == null)
            return;
        var hitboxComp = meleeHitbox.GetComponent<MeleeHitbox>();
        if (hitboxComp != null)
            hitboxComp.ForceDeactivate();
        else
            meleeHitbox.enabled = false;
    }

    async UniTaskVoid ShootAfterDelay(float delay, CancellationToken token)
    {
        await UniTask.Delay(System.TimeSpan.FromSeconds(delay), cancellationToken: token);
        // 발사 전에 맞아서 모션이 끊겼으면 쏘지 않는다.
        if (!isDead && hitStunTimer <= 0f)
            ShootProjectile();
    }

    public void TakeDamage(float amount, GameObject attacker = null)
    {
        if (isDead)
            return;

        hp -= amount;
        healthBar?.SetHealth(hp, maxHp);
        DamagePopup.Spawn(new Vector3(transform.position.x, FeetY + 0.5f, transform.position.z), amount);
        HitFlash(_cts.Token).Forget();

        if (hp <= 0f)
        {
            // 즉시 히트박스 비활성화하여 죽는 순간 데미지 방지
            if (meleeHitbox != null)
                meleeHitbox.enabled = false;
            Die();
            return;
        }

        DisableHitbox();

        // 맞으면 공격이 끊기고 잠깐 경직. 쌓여 있던 공격 트리거가 경직 뒤에 뒤늦게 나가지 않게 지운다.
        animator.ResetTrigger(HashAttack);
        animator.SetTrigger(HashIsHit);
        hitStunTimer = HitStunDuration;
        attackGrace = 0f;
        alertTimer = 0f;
        lungeTimer = 0f;
        spawnDelayTimer = 0f;
        // 등 뒤에서 맞았는데 순찰을 계속하면 어색하다 — 바로 추격 상태로.
        aggroTimer = AggroMemory;
        if (isFlying)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);

        if (player != null)
            Knockback((transform.position - player.position).normalized, _cts.Token).Forget();
    }

    async UniTask HitFlash(CancellationToken token)
    {
        sr.color = Color.red;
        await UniTask.Delay(System.TimeSpan.FromSeconds(0.15f), cancellationToken: token);
        sr.color = Color.white;
    }

    async UniTaskVoid Knockback(Vector2 dir, CancellationToken token)
    {
        float elapsed = 0f;
        rb.bodyType = RigidbodyType2D.Dynamic;
        while (elapsed < knockbackDuration)
        {
            rb.linearVelocity = new Vector2(dir.x * knockbackForce, rb.linearVelocity.y);
            elapsed += Time.deltaTime;
            await UniTask.Yield(token);
        }
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }

    public System.Action onDeath;

    void Die()
    {
        isDead = true;
        AudioManager.Instance?.PlaySFX(deathSound);
        healthBar?.SetHealth(0, maxHp);
        var token = RefreshToken();
        sr.color = Color.white;
        var hitbox = meleeHitbox != null ? meleeHitbox.GetComponent<MeleeHitbox>() : null;
        if (hitbox != null)
            hitbox.ForceDeactivate();
        else if (meleeHitbox != null)
            meleeHitbox.enabled = false;
        rb.linearVelocity = Vector2.zero;
        rb.bodyType = RigidbodyType2D.Kinematic;
        if (col != null)
            col.enabled = false;
        onDeath?.Invoke();
        onDeath = null;
        RunStats.Instance?.AddKill();
        SpawnDrops();
        animator.ResetTrigger(HashIsHit);
        animator.ResetTrigger(HashAttack);
        animator.SetBool(HashIsDead, true);
        DeathRoutine(token).Forget();
    }

    void SpawnDrops()
    {
        EnemyUtils.SpawnGoldDrops(goldDropPrefab, transform.position, groundLayer, 1, goldDropMin, goldDropMax, 50f, 130f);

        if (potionDropPrefab != null && Random.value < potionDropChance + MetaUpgrades.PotionDropBonus)
        {
            float floorY = EnemyUtils.FindFloorY(transform.position, groundLayer);
            Vector3 pos = transform.position + Vector3.up * 0.3f;
            var potion = Instantiate(potionDropPrefab, pos, Quaternion.identity);
            var worldPotion = potion.GetComponent<WorldPotion>();
            if (worldPotion != null)
            {
                worldPotion.healAmount = potionHealAmount;
                float angle = Random.Range(70f, 110f) * Mathf.Deg2Rad;
                float force = Random.Range(3f, 5f);
                worldPotion.Launch(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * force, floorY);
            }
        }
    }

    async UniTaskVoid DeathRoutine(CancellationToken token)
    {
        await UniTask.Yield(token);
        await UniTask.Yield(token);

        while (animator.IsInTransition(0))
            await UniTask.Yield(token);

        float elapsed = 0f;
        while (elapsed < 5f)
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            if (info.IsName("Death") && info.normalizedTime >= 1f)
                break;
            elapsed += Time.deltaTime;
            await UniTask.Yield(token);
        }

        Destroy(gameObject);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 1f, 0f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, attackRange);

        if (isRanged)
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, safeDistance);
        }

        if (meleeHitbox != null && meleeHitbox.enabled)
        {
            Gizmos.color = new Color(1f, 0.4f, 0f, 0.35f);
            var b = meleeHitbox.bounds;
            Gizmos.DrawCube(b.center, b.size);
            Gizmos.color = new Color(1f, 0.4f, 0f, 0.9f);
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }
}

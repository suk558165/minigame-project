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

    [Header("Ranged")]
    public bool isRanged = false;
    public bool aimAtPlayer = false;
    public GameObject projectilePrefab;
    public Transform firePoint;
    public float projectileSpeed = 8f;
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

    private Rigidbody2D rb;
    private Animator animator;
    private SpriteRenderer sr;
    private Collider2D col;

    private float hp;
    private float hitboxOffsetX;
    private bool isDead;
    public bool IsDead => isDead;
    private float attackTimer;
    private float spawnDelayTimer;
    private EnemyHealthBar healthBar;
    public float attackDamageDelay = 0.2f;

    private Transform player;
    private Vector2 patrolOrigin;
    private int patrolDir = 1;

    private CancellationTokenSource _cts = new();

    private static readonly int HashSpeed = Animator.StringToHash("Speed");
    private static readonly int HashAttack = Animator.StringToHash("Attack");
    private static readonly int HashIsDead = Animator.StringToHash("IsDead");
    private static readonly int HashIsHit = Animator.StringToHash("IsHit");

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

        if (meleeHitbox != null)
        {
            meleeHitbox.enabled = false;
            hitboxOffsetX = Mathf.Abs(meleeHitbox.offset.x);
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

        if (spawnDelayTimer > 0f)
        {
            spawnDelayTimer -= Time.deltaTime;
            Patrol();
            return;
        }

        float dist =
            player != null ? Vector2.Distance(transform.position, player.position) : float.MaxValue;

        if (isRanged)
            UpdateRanged(dist);
        else
            UpdateMelee(dist);
    }

    void UpdateMelee(float dist)
    {
        if (player == null)
        {
            Patrol();
            return;
        }
        bool sameLevel = Mathf.Abs(player.position.y - transform.position.y) <= chaseYThreshold;
        if (dist <= detectionRange && sameLevel)
        {
            if (dist > attackRange)
            {
                float dir = player.position.x > transform.position.x ? 1f : -1f;
                Move(IsEdgeAhead(dir) ? 0f : dir);
            }
            else
            {
                Move(0f);
            }

            if (attackTimer <= 0f && dist <= attackRange)
            {
                attackTimer = attackCooldown;
                animator.SetTrigger(HashAttack);
                AudioManager.Instance?.PlaySFX(attackSound);
            }
        }
        else
        {
            Patrol();
        }
    }

    void UpdateRanged(float dist)
    {
        if (player == null)
        {
            Patrol();
            return;
        }
        bool sameLine = Mathf.Abs(player.position.y - transform.position.y) <= rangedYThreshold;
        if (dist <= detectionRange && sameLine)
        {
            if (dist > safeDistance)
            {
                // 사정거리 밖 — 플레이어에게 접근
                float dir = player.position.x > transform.position.x ? 1f : -1f;
                Move(IsEdgeAhead(dir) ? 0f : dir);
            }
            else
            {
                // 사정거리 안 — 멈춰서 사격
                Move(0f);
            }

            // 멈춰있을 때도 플레이어 방향으로 스프라이트 전환
            bool playerOnLeft = player.position.x < transform.position.x;
            SetFacing(!playerOnLeft);

            if (attackTimer <= 0f && dist <= safeDistance)
            {
                attackTimer = attackCooldown * Mathf.Max(1f, rangedCooldownMultiplier);
                animator.SetTrigger(HashAttack);
                AudioManager.Instance?.PlaySFX(attackSound);
                ShootAfterDelay(attackDamageDelay, _cts.Token).Forget();
            }
        }
        else
        {
            Patrol();
        }
    }

    void ShootProjectile()
    {
        if (projectilePrefab == null || player == null)
            return;

        Vector3 origin = firePoint != null ? firePoint.position : transform.position;

        Vector2 dir;
        if (aimAtPlayer)
            dir = ((Vector2)player.position - (Vector2)origin).normalized;
        else
        {
            bool facingLeft = spriteFacesLeft ? !sr.flipX : sr.flipX;
            dir = facingLeft ? Vector2.left : Vector2.right;
        }

        if (projPool == null)
            projPool = new ObjectPool<Projectile>(projectilePrefab.GetComponent<Projectile>());
        var projComp = projPool.Get(origin, Quaternion.identity);
        projComp.Pool = projPool;
        projComp.Init(dir, projectileSpeed, damage, gameObject, spinSpeed: projectileSpinSpeed);
    }

    void Patrol()
    {
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
            patrolDir = -patrolDir;
            Move(patrolDir); // 멈추지 않고 즉시 반대로 이동
            return;
        }

        Move(patrolDir);
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

    void Move(float dir)
    {
        CheckAllies(out bool leftTaken, out bool rightTaken, out float push);
        float vx = (dir < 0f && leftTaken) || (dir > 0f && rightTaken) ? 0f : dir * moveSpeed;
        if (push != 0f && !IsEdgeAhead(push))
            vx += push * SeparationSpeed;

        rb.linearVelocity = new Vector2(vx, rb.linearVelocity.y);
        animator.SetFloat(HashSpeed, vx != 0f ? 1f : 0f);

        if (dir > 0f)
            SetFacing(true);
        else if (dir < 0f)
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
        if (!isDead)
            ShootProjectile();
    }

    // ShieldController 등이 등록해서 데미지 차단 여부를 결정
    public System.Func<bool> isAttackBlocked;

    public void TakeDamage(float amount, GameObject attacker = null)
    {
        if (isDead)
            return;

        if (isAttackBlocked != null && isAttackBlocked())
            return;

        hp -= amount;
        healthBar?.SetHealth(hp, maxHp);
        DamagePopup.Spawn(transform.position + Vector3.up * 0.5f, amount);
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

        animator.SetTrigger(HashIsHit);

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

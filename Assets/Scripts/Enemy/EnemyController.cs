using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(SpriteRenderer))]
public partial class EnemyController : MonoBehaviour, IDamageable
{
    public static readonly List<EnemyController> Instances = new List<EnemyController>();

    // 도메인 리로드를 끈 상태에서도 이전 플레이의 잔여 항목이 남지 않도록 초기화
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instances.Clear();

    [Header("Stats")]
    [SerializeField]
    private float maxHp = 50f;
    [SerializeField]
    private float moveSpeed = 2f;
    public float damage = 10f;

    [Header("Detection")]
    [SerializeField]
    private float detectionRange = 6f;
    [SerializeField]
    private float attackRange = 1.2f;
    [SerializeField]
    private float attackCooldown = 1.2f;
    [SerializeField]
    private float spawnDelay = 2.5f;
    [SerializeField]
    private LayerMask groundLayer;

    [Header("Patrol")]
    [SerializeField]
    private float patrolDistance = 4f;
    [SerializeField]
    private float chaseYThreshold = 1.2f;

    [Header("Flying")]
    [Tooltip("체크하면 중력 없이 날아다니며 층을 넘어 추격하고, 옆에 붙어 돌진 공격한다 (박쥐·벌)")]
    [SerializeField]
    private bool isFlying = false;

    [Header("Ranged")]
    [SerializeField]
    private bool isRanged = false;
    [SerializeField]
    private bool aimAtPlayer = false;
    [SerializeField]
    private GameObject projectilePrefab;

    [Tooltip("오른쪽을 볼 때 기준으로 배치한다. 왼쪽을 보면 좌우가 자동으로 뒤집힌다")]
    [SerializeField]
    private Transform firePoint;
    [SerializeField]
    private float projectileSpeed = 8f;

    [Tooltip("발사체 크기 배수. 몸집이 큰 적일수록 크게")]
    [SerializeField]
    private float projectileScale = 1f;
    private ObjectPool<Projectile> projPool;
    [SerializeField]
    private float safeDistance = 3f;

    [Tooltip("원거리 적의 공격 쿨다운에 곱해지는 배수 — 회피 시간 확보용")]
    [SerializeField]
    private float rangedCooldownMultiplier = 1.8f;

    [Tooltip("원거리 적의 Y축 추적/공격 허용 범위 — 이 값 이내일 때만 공격 (일직선 체크)")]
    [SerializeField]
    private float rangedYThreshold = 0.8f;

    [Tooltip("발사체 회전 속도 (도/초). 0이면 회전 없음")]
    [SerializeField]
    private float projectileSpinSpeed = 0f;

    [Header("HP Bar")]
    [SerializeField]
    private Vector3 hpBarOffset = new Vector3(0f, -0.6f, 0f);

    [Header("Knockback")]
    [SerializeField]
    private float knockbackForce = 6f;
    [SerializeField]
    private float knockbackDuration = 0.15f;

    [Header("Audio")]
    [SerializeField]
    private AudioClip attackSound;
    [SerializeField]
    private AudioClip deathSound;

    [Header("Melee")]
    [SerializeField]
    private Collider2D meleeHitbox;

    [Header("Drops")]
    [SerializeField]
    private GameObject goldDropPrefab;
    [SerializeField]
    private int goldDropMin = 3;
    [SerializeField]
    private int goldDropMax = 8;
    [SerializeField]
    private GameObject potionDropPrefab;

    [Range(0f, 1f)]
    [SerializeField]
    private float potionDropChance = 0.2f;
    [SerializeField]
    private float potionHealAmount = 20f;

    [Header("Edge Detection")]
    [SerializeField]
    private float edgeCheckDepth = 1.5f;
    [SerializeField]
    private LayerMask platformLayer;

    [Header("Sprite")]
    [Tooltip("원본 스프라이트가 왼쪽을 보고 있으면 체크")]
    [SerializeField]
    private bool spriteFacesLeft = false;

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
    [SerializeField]
    private float attackDamageDelay = 0.2f;

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

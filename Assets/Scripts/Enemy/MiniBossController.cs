using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class MiniBossController : MonoBehaviour, IDamageable
{
    public static readonly List<MiniBossController> Instances = new List<MiniBossController>();

    // 도메인 리로드를 끈 상태에서도 이전 플레이의 잔여 항목이 남지 않도록 초기화
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instances.Clear();

    [Header("Stats")]
    [SerializeField]
    private float maxHp = 250f;

    [SerializeField]
    private float moveSpeed = 3.5f;

    [SerializeField]
    private float damage = 15f;

    [Header("지면 파동")]
    [SerializeField]
    private GameObject wavePrefab;
    private ObjectPool<Projectile> wavePool;

    [SerializeField]
    private float waveSpeed = 6f;

    [SerializeField]
    private float waveSpawnOffsetY = 0.5f;

    [Header("도약 베기")]
    [SerializeField]
    private float leapJumpForce = 18f;

    [SerializeField]
    private float leapFallSpeed = 25f;

    [SerializeField]
    private float leapRadius = 2f;

    [SerializeField]
    private Collider2D meleeHitbox;

    [Header("연속 돌진")]
    [SerializeField]
    private int dashCount = 3;

    [SerializeField]
    private float dashSpeed = 14f;

    [SerializeField]
    private float dashDuration = 0.25f;

    [SerializeField]
    private float dashInterval = 0.15f;

    [SerializeField]
    private float dashHitRadius = 1.3f;

    [Header("패턴 공통")]
    [SerializeField]
    private float patternCooldown = 0.8f;

    [SerializeField]
    private float tellDuration = 0.3f;

    [SerializeField]
    private float detectionRange = 12f;

    [Header("Drops")]
    [SerializeField]
    private GameObject goldDropPrefab;

    [SerializeField]
    private int goldDropMin = 10;

    [SerializeField]
    private int goldDropMax = 20;

    [Header("Ground Check")]
    [SerializeField]
    private LayerMask groundLayer;

    [Header("Audio")]
    [SerializeField]
    private AudioClip deathSound;

    [SerializeField]
    private AudioClip groundWaveSound;

    [SerializeField]
    private AudioClip leapSlashSound;

    [SerializeField]
    private AudioClip dashSound;

    private Rigidbody2D rb;
    private SpriteRenderer sr;
    private Collider2D col;
    private Color originalColor;

    private float hp;
    private bool isDead;
    private bool isActing;
    private float cooldownTimer;
    private bool dashHitThisSegment;

    private Transform player;
    private EnemyHealthBar healthBar;
    private Animator animator;
    public System.Action onDeath;

    private CancellationTokenSource _cts = new();

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
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        animator = GetComponent<Animator>();
        hp = maxHp;
        originalColor = sr.color;
        healthBar = gameObject.AddComponent<EnemyHealthBar>();
        healthBar.Init(new Vector3(0f, -0.6f, 0f));

        if (meleeHitbox != null)
            meleeHitbox.enabled = false;
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
    }

    void Update()
    {
        if (isDead || player == null)
            return;
        if (Vector2.Distance(transform.position, player.position) > detectionRange)
            return;

        FlipToPlayer();

        if (isActing)
            return;

        cooldownTimer -= Time.deltaTime;
        if (cooldownTimer <= 0f)
        {
            cooldownTimer = patternCooldown;
            ExecutePattern(_cts.Token).Forget();
        }
        else
        {
            ChasePlayer();
        }
    }

    void FlipToPlayer() => EnemyUtils.FlipToPlayer(sr, player, transform);

    void ChasePlayer()
    {
        float dir = player.position.x > transform.position.x ? 1f : -1f;
        if (IsWallAhead(dir))
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }
        rb.linearVelocity = new Vector2(dir * moveSpeed, rb.linearVelocity.y);
    }

    bool IsWallAhead(float direction)
    {
        float centerY = col != null ? col.bounds.center.y : transform.position.y;
        Vector2 origin = new Vector2(transform.position.x, centerY);
        Vector2 dir = direction > 0 ? Vector2.right : Vector2.left;
        float dist = col != null ? col.bounds.extents.x + 0.2f : 0.7f;
        return Physics2D.Raycast(origin, dir, dist, groundLayer).collider != null;
    }

    async UniTaskVoid ExecutePattern(CancellationToken token)
    {
        isActing = true;
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

        int pattern = Random.Range(0, 3);
        switch (pattern)
        {
            case 0:
                await GroundWave(token);
                break;
            case 1:
                await LeapSlash(token);
                break;
            case 2:
                await MultiDash(token);
                break;
        }

        isActing = false;
        if (animator != null && !isDead)
            animator.Play("Idle", 0, 0f);
    }

    // ── 텔 연출 ──────────────────────────────────────

    UniTask TellFlash(Color color) => EnemyUtils.TellFlash(sr, color, originalColor, tellDuration);

    UniTask TellShake() => EnemyUtils.TellShake(transform, tellDuration);

    // ── 패턴 1: 지면 파동 ─────────────────────────────
    // 바닥을 내리쳐 좌우로 파동이 퍼져나감

    async UniTask GroundWave(CancellationToken token)
    {
        if (animator != null)
            animator.Play("GroundWave", 0, 0f);
        await TellShake();

        rb.linearVelocity = Vector2.zero;

        AudioManager.Instance?.PlaySFX(groundWaveSound);
        if (wavePrefab != null)
        {
            Vector3 origin = transform.position + Vector3.up * waveSpawnOffsetY;

            if (wavePool == null)
                wavePool = new ObjectPool<Projectile>(wavePrefab.GetComponent<Projectile>());

            var left = wavePool.Get(origin, Quaternion.identity);
            left.Pool = wavePool;
            left.Init(Vector2.left, waveSpeed, damage, gameObject);
            left.transform.rotation = Quaternion.identity;
            var leftSr = left.GetComponent<SpriteRenderer>();
            if (leftSr != null)
            {
                leftSr.flipX = false;
                // Projectile.Init이 왼쪽 방향이면 세로 반전을 켜므로, 회전을 되돌린 파동은 다시 끈다.
                leftSr.flipY = false;
            }

            var right = wavePool.Get(origin, Quaternion.identity);
            right.Pool = wavePool;
            right.Init(Vector2.right, waveSpeed, damage, gameObject);
            right.transform.rotation = Quaternion.identity;
            var rightSr = right.GetComponent<SpriteRenderer>();
            if (rightSr != null)
                rightSr.flipX = true;
        }

        await UniTask.Delay(System.TimeSpan.FromSeconds(0.3f), cancellationToken: token);
    }

    // ── 패턴 2: 도약 베기 ─────────────────────────────
    // 점프 후 플레이어 위치로 낙하, 착지 충격파

    async UniTask LeapSlash(CancellationToken token)
    {
        if (animator != null)
            animator.Play("LeapSlash", 0, 0f);
        await TellFlash(Color.yellow);

        AudioManager.Instance?.PlaySFX(leapSlashSound);
        rb.linearVelocity = new Vector2(0f, leapJumpForce);
        await UniTask.Delay(System.TimeSpan.FromSeconds(0.2f), cancellationToken: token);

        // 플레이어 X로 이동 후 급낙하
        if (player != null)
        {
            Vector3 p = transform.position;
            p.x = player.position.x;
            transform.position = p;
        }

        while (!IsGrounded())
        {
            rb.linearVelocity = new Vector2(0f, -leapFallSpeed);
            await UniTask.Yield(token);
        }

        rb.linearVelocity = Vector2.zero;

        // 착지 충격 — IgnoreLayerCollision으로 트리거가 막히므로 직접 거리 계산
        DealAreaDamage(transform.position, leapRadius);

        await UniTask.Delay(System.TimeSpan.FromSeconds(0.2f), cancellationToken: token);
    }

    // ── 패턴 3: 연속 돌진 ─────────────────────────────
    // 짧은 대시를 dashCount 회 반복, 마지막에 짧은 스턴

    async UniTask MultiDash(CancellationToken token)
    {
        if (animator != null && animator.HasState(0, Animator.StringToHash("MultiDash")))
            animator.Play("MultiDash", 0, 0f);
        await TellFlash(Color.cyan);

        AudioManager.Instance?.PlaySFX(dashSound);
        bool prevRootMotion = animator != null && animator.applyRootMotion;
        if (animator != null)
            animator.applyRootMotion = false;

        for (int i = 0; i < dashCount; i++)
        {
            if (player == null)
                break;

            FlipToPlayer();
            float dir = player.position.x > transform.position.x ? 1f : -1f;
            dashHitThisSegment = false;

            float elapsed = 0f;
            while (elapsed < dashDuration)
            {
                rb.linearVelocity = new Vector2(dir * dashSpeed, rb.linearVelocity.y);

                if (!dashHitThisSegment)
                    DealAreaDamage(transform.position, dashHitRadius);

                elapsed += Time.deltaTime;
                await UniTask.Yield(token);
            }

            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

            if (i < dashCount - 1)
                await UniTask.Delay(System.TimeSpan.FromSeconds(dashInterval), cancellationToken: token);
        }

        if (animator != null)
            animator.applyRootMotion = prevRootMotion;

        // 스턴
        rb.linearVelocity = Vector2.zero;
        sr.color = Color.gray;
        await UniTask.Delay(System.TimeSpan.FromSeconds(0.3f), cancellationToken: token);
        sr.color = originalColor;
    }

    // ── 유틸 ──────────────────────────────────────────

    void DealAreaDamage(Vector3 center, float radius)
    {
        if (player == null)
            return;
        if (Vector2.Distance(center, player.position) > radius)
            return;

        player.GetComponent<IDamageable>()?.TakeDamage(damage, gameObject);
        dashHitThisSegment = true;

        var ctrl = player.GetComponent<PlayerController>();
        if (ctrl != null)
        {
            Vector2 knockDir = ((Vector2)player.position - (Vector2)center).normalized;
            ctrl.Knockback(new Vector2(knockDir.x, 0.3f).normalized * 7f);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (isDead || meleeHitbox == null || !meleeHitbox.enabled)
            return;
        if (!other.CompareTag("Player"))
            return;
        other.GetComponentInParent<IDamageable>()?.TakeDamage(damage, gameObject);
    }

    bool IsGrounded() => EnemyUtils.IsGrounded(col, transform, groundLayer);

    // ── 피격 / 사망 ────────────────────────────────────

    public void TakeDamage(float amount, GameObject attacker = null)
    {
        if (isDead)
            return;
        amount *= MetaUpgrades.BossDamageMult;
        hp -= amount;
        DamagePopup.Spawn(transform.position + Vector3.up * 0.5f, amount);

        healthBar?.SetHealth(hp, maxHp);

        if (hp <= 0f)
        {
            Die();
            return;
        }

        HitFlash().Forget();
    }

    UniTask HitFlash() => EnemyUtils.HitFlash(sr, originalColor, () => isDead);

    void Die()
    {
        isDead = true;
        AudioManager.Instance?.PlaySFX(deathSound);
        var token = RefreshToken();
        if (animator != null)
            animator.enabled = false;
        sr.color = originalColor;
        healthBar?.SetHealth(0, maxHp);
        if (meleeHitbox != null)
            meleeHitbox.enabled = false;
        rb.linearVelocity = Vector2.zero;
        rb.bodyType = RigidbodyType2D.Kinematic;
        if (col != null)
            col.enabled = false;
        onDeath?.Invoke();
        onDeath = null;
        RunStats.Instance?.AddKill();
        SpawnDrops();
        DeathRoutine(token).Forget();
    }

    void SpawnDrops()
    {
        EnemyUtils.SpawnGoldDrops(goldDropPrefab, transform.position, groundLayer, 3, goldDropMin, goldDropMax);
    }

    async UniTaskVoid DeathRoutine(CancellationToken token)
    {
        await EnemyUtils.DeathBlink(sr);
        Destroy(gameObject);
    }
}

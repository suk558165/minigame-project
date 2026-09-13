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

    // 패턴이 유지하려는 색(기본/돌진 스턴 회색). 피격 플래시가 끝날 때 이 색으로 되돌린다.
    private Color baseColor;

    // animator.Play 중복 호출 방지용 현재 상태 이름 (PlayState 참고)
    private string currentState;

    // MiniBoss_SkeletonKingController 클립 길이 (12fps). 패턴 타이밍을 애니메이션에 맞추는 기준.
    const float ClipGroundWave = 0.667f;  // cast_1~8
    const float ClipLeapSlash = 0.583f;   // attack1_1~7

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
        baseColor = originalColor;
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
            PlayState("Idle");
            return;
        }
        rb.linearVelocity = new Vector2(dir * moveSpeed, rb.linearVelocity.y);
        PlayState("Walk");
    }

    // animator.Play 를 매 프레임 호출하면 클립이 0프레임에서 계속 리셋되므로 상태가 바뀔 때만 호출한다.
    // 공격 동작은 같은 상태를 다시 처음부터 재생해야 하므로 restart 로 강제한다.
    void PlayState(string state, bool restart = false)
    {
        if (animator == null)
            return;
        if (!restart && currentState == state)
            return;
        currentState = state;
        animator.Play(state, 0, 0f);
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
        PlayState("Idle"); // 텔(예고) 구간은 걷기가 아닌 정지 자세

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
        if (!isDead)
            PlayState("Idle");
    }

    // ── 텔 연출 ──────────────────────────────────────

    UniTask TellFlash(Color color) => EnemyUtils.TellFlash(sr, color, originalColor, tellDuration);

    UniTask TellShake() => EnemyUtils.TellShake(transform, tellDuration);

    // ── 패턴 1: 지면 파동 ─────────────────────────────
    // 바닥을 내리쳐 좌우로 파동이 퍼져나감

    async UniTask GroundWave(CancellationToken token)
    {
        PlayState("GroundWave", true); // 시전 앞부분이 예고 역할을 겸한다
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

        // 시전 클립 잔여 재생 (0.3s 고정이라 클립이 잘리거나 마지막 프레임에서 멈췄다)
        await UniTask.Delay(
            System.TimeSpan.FromSeconds(Mathf.Max(0.1f, ClipGroundWave - tellDuration)),
            cancellationToken: token
        );
    }

    // ── 패턴 2: 도약 베기 ─────────────────────────────
    // 점프 후 플레이어 위치로 낙하, 착지 충격파

    async UniTask LeapSlash(CancellationToken token)
    {
        await TellFlash(Color.yellow);

        AudioManager.Instance?.PlaySFX(leapSlashSound);
        PlayState("Jump", true); // 공중 구간은 점프 루프 (베기는 착지 시점에)
        rb.linearVelocity = new Vector2(0f, leapJumpForce);
        await UniTask.Delay(System.TimeSpan.FromSeconds(0.2f), cancellationToken: token);

        // 플레이어 X로 이동 후 급낙하
        if (player != null)
        {
            Vector3 p = transform.position;
            p.x = player.position.x;
            transform.position = p;
        }

        // 착지하지 못하는 위치(맵 밖 등)에서 무한 대기하지 않도록 타임아웃을 둔다.
        float fallElapsed = 0f;
        while (!IsGrounded() && fallElapsed < 3f)
        {
            rb.linearVelocity = new Vector2(0f, -leapFallSpeed);
            fallElapsed += Time.deltaTime;
            await UniTask.Yield(token);
        }

        rb.linearVelocity = Vector2.zero;

        // 착지 충격 — IgnoreLayerCollision으로 트리거가 막히므로 직접 거리 계산
        PlayState("LeapSlash", true);
        DealAreaDamage(transform.position, leapRadius);

        // 베기 클립 완주 (0.2s 에서 잘려 마지막 프레임이 굳던 문제)
        await UniTask.Delay(System.TimeSpan.FromSeconds(ClipLeapSlash), cancellationToken: token);
    }

    // ── 패턴 3: 연속 돌진 ─────────────────────────────
    // 짧은 대시를 dashCount 회 반복, 마지막에 짧은 스턴

    async UniTask MultiDash(CancellationToken token)
    {
        await TellFlash(Color.cyan);

        AudioManager.Instance?.PlaySFX(dashSound);
        PlayState("MultiDash", true); // 진입 동작(attack2) → 이후 attack2_loop 으로 연결
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

            // 2번째 돌진부터는 루프 클립으로 유지 (마지막 프레임 고정 방지)
            if (i > 0)
                PlayState("DashLoop");

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
        PlayState("Idle");
        baseColor = Color.gray;
        sr.color = baseColor;
        await UniTask.Delay(System.TimeSpan.FromSeconds(0.3f), cancellationToken: token);
        baseColor = originalColor;
        sr.color = baseColor;
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

    UniTask HitFlash() => EnemyUtils.HitFlash(sr, baseColor, () => isDead);

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

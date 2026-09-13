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
    const float ClipMultiDash = 0.583f;   // attack2_1~7

    // 도약 베기
    const float LeapAirPose = 0.167f;     // attack1_3: 칼을 뒤로 뺀 프레임 — 급강하 자세
    const float LeapSlashHit = 0.25f;     // attack1_4: 큰 베기 궤적 프레임
    const float LeapRiseHeight = 4f;
    const float LeapRiseTime = 0.4f;
    const float LeapHangTime = 0.1f;

    // 연속 돌진: 찌르기 이펙트(attack2_4)가 나오기 전까지의 준비 동작 길이
    const float DashWindup = 0.25f;

    // 피격 경직: 패턴 중이 아닐 때만 짧게 멈칫한다. 연타에 계속 묶이지 않도록 간격을 둔다.
    const float StaggerDuration = 0.2f;
    const float StaggerCooldown = 0.8f;
    const float StaggerKnockSpeed = 3f;
    private float staggerTimer;
    private float staggerDirX;
    private float lastStaggerTime = -10f;

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

        // 패턴 중에는 패턴이 직접 방향을 정한다 (돌진 도중 플레이어를 지나치며 뒤집히던 문제)
        if (isActing)
            return;

        // 경직: 밀려나며 감속. 추격 속도가 덮어쓰지 않도록 여기서 반환한다.
        if (staggerTimer > 0f)
        {
            staggerTimer -= Time.deltaTime;
            float k = Mathf.Max(0f, staggerTimer / StaggerDuration);
            rb.linearVelocity = new Vector2(staggerDirX * StaggerKnockSpeed * k, rb.linearVelocity.y);
            return;
        }

        FlipToPlayer();

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
    void PlayState(string state, bool restart = false, float normalizedTime = 0f)
    {
        if (animator == null)
            return;
        if (!restart && currentState == state)
            return;
        currentState = state;
        animator.speed = 1f; // FreezePose 로 멈춘 재생을 되돌린다
        animator.Play(state, 0, normalizedTime);
    }

    // 공중 급강하처럼 한 프레임 자세를 유지해야 할 때 사용 (다음 PlayState 에서 재생 재개)
    void FreezePose(string state, float normalizedTime)
    {
        PlayState(state, true, normalizedTime);
        if (animator != null)
            animator.speed = 0f;
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
    // 플레이어 머리 위로 곡선 도약 → 정점에서 자세 고정 → 급강하 → 착지 베기

    async UniTask LeapSlash(CancellationToken token)
    {
        await TellFlash(Color.yellow);

        FlipToPlayer();
        AudioManager.Instance?.PlaySFX(leapSlashSound);
        PlayState("Jump", true);

        // 곡선으로 플레이어 위까지 이동 (0.2초 상승 후 옆으로 순간이동하던 문제)
        float targetX = player != null ? player.position.x : transform.position.x;
        await EnemyUtils.LeapArc(rb, transform, targetX, LeapRiseHeight, LeapRiseTime, token);

        // 정점: 칼을 뒤로 뺀 자세로 잠깐 멈췄다가 급강하
        FlipToPlayer();
        FreezePose("LeapSlash", LeapAirPose / ClipLeapSlash);
        await UniTask.Delay(System.TimeSpan.FromSeconds(LeapHangTime), cancellationToken: token);

        await EnemyUtils.DiveUntilGrounded(rb, leapFallSpeed, IsGrounded, token);

        // 착지 순간 베기 궤적 프레임부터 재생
        // 착지 충격 — IgnoreLayerCollision으로 트리거가 막히므로 직접 거리 계산
        FlipToPlayer();
        PlayState("LeapSlash", true, LeapSlashHit / ClipLeapSlash);
        DealAreaDamage(transform.position, leapRadius);
        CameraFollow.Instance?.Shake(0.15f, 0.15f);

        await UniTask.Delay(System.TimeSpan.FromSeconds(ClipLeapSlash - LeapSlashHit + 0.1f), cancellationToken: token);
    }

    // ── 패턴 3: 연속 돌진 ─────────────────────────────
    // 준비 동작 → 찌르기 이펙트 구간에 맞춰 이동, dashCount 회 반복, 마지막에 짧은 스턴

    async UniTask MultiDash(CancellationToken token)
    {
        // 예고 깜빡임이 끝나는 순간 첫 찌르기 이펙트가 나오도록, 끝나기 DashWindup 전에 준비 동작을 시작한다.
        var tell = TellFlash(Color.cyan);
        await UniTask.Delay(System.TimeSpan.FromSeconds(Mathf.Max(0f, tellDuration - DashWindup)), cancellationToken: token);
        FlipToPlayer();
        PlayState("MultiDash", true);
        await tell;

        AudioManager.Instance?.PlaySFX(dashSound);
        bool prevRootMotion = animator != null && animator.applyRootMotion;
        if (animator != null)
            animator.applyRootMotion = false;

        for (int i = 0; i < dashCount; i++)
        {
            if (player == null)
                break;

            if (i > 0)
            {
                // 멈춘 동안: 앞부분은 찌르기 후 회수 동작(이후 Idle), 마지막 DashWindup 은 다음 준비 동작
                float windup = Mathf.Min(DashWindup, dashInterval);
                await UniTask.Delay(System.TimeSpan.FromSeconds(dashInterval - windup), cancellationToken: token);
                FlipToPlayer();
                PlayState("MultiDash", true, (DashWindup - windup) / ClipMultiDash);
                await UniTask.Delay(System.TimeSpan.FromSeconds(windup), cancellationToken: token);
            }

            // 준비 동작에서 정한 방향으로 고정한다
            float dir = sr.flipX ? -1f : 1f;
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

    // 넉백은 가로 방향 위주로 살짝 띄운다
    void DealAreaDamage(Vector3 center, float radius)
    {
        if (EnemyUtils.DealAreaDamage(center, radius, damage, gameObject, 7f, 0.3f))
            dashHitThisSegment = true;
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

        if (!isActing && player != null && Time.time - lastStaggerTime >= StaggerCooldown)
        {
            lastStaggerTime = Time.time;
            staggerTimer = StaggerDuration;
            staggerDirX = transform.position.x >= player.position.x ? 1f : -1f;
            PlayState("Hit", true);
        }
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

using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SpriteRenderer))]
public partial class MiniBossController : BossBase
{
    public static readonly List<MiniBossController> Instances = new List<MiniBossController>();

    // 도메인 리로드를 끈 상태에서도 이전 플레이의 잔여 항목이 남지 않도록 초기화
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instances.Clear();

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

    [Header("Audio")]
    [SerializeField]
    private AudioClip groundWaveSound;

    [SerializeField]
    private AudioClip leapSlashSound;

    [SerializeField]
    private AudioClip dashSound;

    // 피격 경직 중 밀려나는 속도 (공통 경직 판정은 BossBase)
    const float StaggerKnockSpeed = 3f;
    private float staggerDirX;

    private bool dashHitThisSegment;
    private EnemyHealthBar healthBar;

    protected override int GoldDropCount => 3;

    protected override void Awake()
    {
        base.Awake();
        healthBar = gameObject.AddComponent<EnemyHealthBar>();
        healthBar.Init(new Vector3(0f, -0.6f, 0f));
    }

    void OnEnable() => Instances.Add(this);

    void OnDisable() => Instances.Remove(this);

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

    bool IsWallAhead(float direction)
    {
        float centerY = col != null ? col.bounds.center.y : transform.position.y;
        Vector2 origin = new Vector2(transform.position.x, centerY);
        Vector2 dir = direction > 0 ? Vector2.right : Vector2.left;
        float dist = col != null ? col.bounds.extents.x + 0.2f : 0.7f;
        return Physics2D.Raycast(origin, dir, dist, groundLayer).collider != null;
    }

    // ── 패턴 선택 (실제 패턴 구현은 MiniBossController.Patterns.cs) ──

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

    // ── 피격 / 사망 (공통 처리는 BossBase) ──

    protected override void OnDamaged() => healthBar?.SetHealth(hp, maxHp);

    protected override void OnStagger() =>
        staggerDirX = transform.position.x >= player.position.x ? 1f : -1f;

    protected override void OnDied() => healthBar?.SetHealth(0, maxHp);
}

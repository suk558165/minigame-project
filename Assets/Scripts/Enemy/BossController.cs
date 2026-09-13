using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SpriteRenderer))]
public partial class BossController : MonoBehaviour, IDamageable
{
    public static readonly List<BossController> Instances = new List<BossController>();

    // 도메인 리로드를 끈 상태에서도 이전 플레이의 잔여 항목이 남지 않도록 초기화
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instances.Clear();

    [Header("Stats")]
    [SerializeField]
    private float maxHp = 1000f;

    [SerializeField]
    private float moveSpeed = 3f;

    [SerializeField]
    private float damage = 20f;

    [Header("Phase 2")]
    [Tooltip("HP 비율이 이 값 이하가 되면 Phase 2 진입")]
    [SerializeField]
    private float phase2Threshold = 0.4f;

    [Tooltip("Phase 2에서 패턴 간 쿨타임 배율 (1보다 작으면 빨라짐)")]
    [SerializeField]
    private float phase2CooldownMult = 0.3f;

    [Header("돌진 패턴")]
    [SerializeField]
    private float chargeSpeed = 12f;

    [SerializeField]
    private float chargeDuration = 0.5f;

    [SerializeField]
    private float chargeStunDuration = 0.5f;

    [Header("내려찍기 패턴")]
    [SerializeField]
    private float slamJumpForce = 15f;

    [SerializeField]
    private float slamFallSpeed = 20f;

    [SerializeField]
    private GameObject slamWarningPrefab;

    [Header("투사체 패턴")]
    [SerializeField]
    private GameObject projectilePrefab;

    [SerializeField]
    private int projectileCount = 3;

    [SerializeField]
    private float projectileSpeed = 8f;

    [SerializeField]
    private float projectileSpread = 30f;

    [Header("연속 베기 패턴")]
    [SerializeField]
    private int comboHitCount = 3;

    [SerializeField]
    private float comboInterval = 0.15f;

    [SerializeField]
    private float comboRange = 1.5f;

    [SerializeField]
    private Collider2D meleeHitbox;
    private float hitboxOffsetX;

    [Header("패턴 공통")]
    [SerializeField]
    private float patternCooldown = 0.6f;

    [SerializeField]
    private float tellDuration = 0.35f;

    [SerializeField]
    private float detectionRange = 12f;

    [Header("Drops")]
    [SerializeField]
    private GameObject goldDropPrefab;

    [SerializeField]
    private int goldDropMin = 20;

    [SerializeField]
    private int goldDropMax = 40;

    [Header("Knockback")]
    [SerializeField]
    private float knockbackForce = 3f;

    [SerializeField]
    private float knockbackDuration = 0.1f;

    [Header("Ground Check")]
    [SerializeField]
    private LayerMask groundLayer;

    [Header("Audio")]
    [SerializeField]
    private AudioClip deathSound;

    [SerializeField]
    private AudioClip slamSound;

    [SerializeField]
    private AudioClip comboSound;

    [SerializeField]
    private AudioClip projectileSound;

    [SerializeField]
    private AudioClip dashSound;

    private Rigidbody2D rb;
    private SpriteRenderer sr;
    private Collider2D col;

    private float hp;
    private bool isDead;
    public bool IsDead => isDead;
    private bool isPhase2;
    private bool isActing;
    private float cooldownTimer;
    private bool attackFlip;

    private Transform player;
    private Color originalColor;

    // 패턴이 유지하려는 색(기본/돌진 스턴 회색). 피격 플래시가 끝날 때 이 색으로 되돌린다.
    private Color baseColor;

    // animator.Play 중복 호출 방지용 현재 상태 이름 (PlayState 참고)
    private string currentState;

    private CancellationTokenSource _cts = new();

    [Header("UI")]
    [SerializeField]
    private string bossDisplayName = "BOSS";

    [SerializeField]
    private GameObject bossHealthBarUIPrefab;

    private BossHealthBarUI healthBarUI;
    private Animator animator;

    private ObjectPool<Projectile> projPool;

    [Header("Phase 2 - 바닥 가시")]
    [Tooltip("솟아오르는 가시 프리팹 (SpriteRenderer + Collider2D(IsTrigger) + BossSpike)")]
    [SerializeField]
    private GameObject spikePrefab;

    [Tooltip("가시/마법 낙하 예고 표식 프리팹 (이미지)")]
    [SerializeField]
    private GameObject warningPrefab;

    [Header("Phase 2 - 공중 마법")]
    [Tooltip("비우면 일반 투사체 프리팹을 재사용")]
    [SerializeField]
    private GameObject magicProjectilePrefab;

    [SerializeField]
    private float magicProjectileSpeed = 11f;

    private ObjectPool<Projectile> magicPool;
    private bool untargetable;
    private Vector3 preAirbornePos;

    public System.Action onDeath;

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

        // BossHealthBarUI 인스턴스 확보: 씬에 없으면 프리팹/스크립트로 생성
        healthBarUI = BossHealthBarUI.Instance;
        if (healthBarUI == null)
        {
            if (bossHealthBarUIPrefab != null)
            {
                var go = Instantiate(bossHealthBarUIPrefab);
                healthBarUI = go.GetComponent<BossHealthBarUI>();
            }
            else
            {
                var go = new GameObject("BossHealthBarUI");
                healthBarUI = go.AddComponent<BossHealthBarUI>();
            }
        }
        healthBarUI.Show(bossDisplayName);
        healthBarUI.SetHealth(hp, maxHp);

        if (meleeHitbox != null)
        {
            meleeHitbox.enabled = false;
            hitboxOffsetX = Mathf.Abs(meleeHitbox.offset.x);
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
    }

    void Update()
    {
        if (isDead || player == null)
            return;

        // 보스 인트로 연출 중에는 행동 금지.
        if (BossIntro.IsPlaying)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        float dist = Vector2.Distance(transform.position, player.position);
        if (dist > detectionRange)
            return;

        if (isActing)
            return;

        FlipToPlayer();

        cooldownTimer -= Time.deltaTime;
        if (cooldownTimer <= 0f)
        {
            cooldownTimer = isPhase2 ? patternCooldown * phase2CooldownMult : patternCooldown;
            PickAndExecutePattern(_cts.Token).Forget();
        }
        else
        {
            ChasePlayer();
        }
    }

    void FlipToPlayer()
    {
        if (player == null)
            return;
        float dx = player.position.x - transform.position.x;
        if (Mathf.Abs(dx) < 0.3f)
            return;
        bool flip = dx > 0f;
        sr.flipX = attackFlip ? !flip : flip;

        // flipX는 콜라이더에 영향이 없으므로 히트박스 오프셋을 직접 미러링
        if (meleeHitbox != null)
        {
            Vector2 o = meleeHitbox.offset;
            o.x = dx > 0f ? hitboxOffsetX : -hitboxOffsetX;
            meleeHitbox.offset = o;
        }
    }

    void ChasePlayer()
    {
        float dir = player.position.x > transform.position.x ? 1f : -1f;
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

    // ── 패턴 선택 (실제 패턴 구현은 BossController.Patterns.cs) ──

    async UniTaskVoid PickAndExecutePattern(CancellationToken token)
    {
        isActing = true;
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        PlayState("Idle"); // 텔(예고) 구간은 걷기가 아닌 정지 자세

        if (isPhase2)
        {
            // Phase 2: 가시 / 공중 마법 / 내려찍기
            switch (Random.Range(0, 3))
            {
                case 0:
                    await SpikeStormAttack(token);
                    break;
                case 1:
                    await AirMagicAttack(token);
                    break;
                case 2:
                    await SlamAttack(token);
                    break;
            }
        }
        else
        {
            float dist = Vector2.Distance(transform.position, player.position);

            // 근거리면 근접 패턴 우선, 원거리면 돌진/투사체
            int pattern;
            if (dist <= comboRange * 1.5f)
                pattern = Random.Range(0, 2); // 0: 연속베기, 1: 내려찍기
            else
                pattern = Random.Range(2, 4); // 2: 돌진, 3: 투사체

            switch (pattern)
            {
                case 0:
                    await ComboAttack(token);
                    break;
                case 1:
                    await SlamAttack(token);
                    break;
                case 2:
                    await ChargeAttack(token);
                    break;
                case 3:
                    await ProjectileAttack(token);
                    break;
            }
        }

        isActing = false;
        if (!isDead)
            PlayState("Idle");
    }

    // ── 피격 ──

    void OnTriggerEnter2D(Collider2D other)
    {
        if (isDead || meleeHitbox == null || !meleeHitbox.enabled)
            return;
        if (!other.CompareTag("Player"))
            return;
        other.GetComponent<IDamageable>()?.TakeDamage(damage, gameObject);
    }

    public void TakeDamage(float amount, GameObject attacker = null)
    {
        if (isDead)
            return;

        // 공중으로 이탈한 동안(가시/공중마법 패턴)은 피격 무시
        if (untargetable)
            return;

        amount *= MetaUpgrades.BossDamageMult;
        hp -= amount;
        DamagePopup.Spawn(transform.position + Vector3.up * 0.5f, amount);

        if (!isPhase2 && hp <= maxHp * phase2Threshold)
        {
            isPhase2 = true;
            Phase2Flash(_cts.Token).Forget();
        }

        healthBarUI?.SetHealth(hp, maxHp);

        if (hp <= 0f)
        {
            if (meleeHitbox != null)
                meleeHitbox.enabled = false;
            Die();
            return;
        }

        HitFlash().Forget();

        if (!isActing && player != null)
            Knockback((transform.position - player.position).normalized, _cts.Token).Forget();
    }

    UniTask HitFlash() => EnemyUtils.HitFlash(sr, baseColor, () => isDead);

    async UniTaskVoid Phase2Flash(CancellationToken token)
    {
        for (int i = 0; i < 5; i++)
        {
            sr.color = new Color(1f, 0.3f, 0.3f);
            await UniTask.Delay(System.TimeSpan.FromSeconds(0.1f), cancellationToken: token);
            sr.color = originalColor;
            await UniTask.Delay(System.TimeSpan.FromSeconds(0.1f), cancellationToken: token);
        }
    }

    async UniTaskVoid Knockback(Vector2 dir, CancellationToken token)
    {
        float elapsed = 0f;
        while (elapsed < knockbackDuration)
        {
            rb.linearVelocity = new Vector2(dir.x * knockbackForce, rb.linearVelocity.y);
            elapsed += Time.deltaTime;
            await UniTask.Yield(token);
        }
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }

    // ── 사망 ──

    void Die()
    {
        isDead = true;
        AudioManager.Instance?.PlaySFX(deathSound);
        var token = RefreshToken();
        if (animator != null)
            animator.enabled = false;
        sr.color = originalColor;
        healthBarUI?.SetHealth(0, maxHp);
        healthBarUI?.Hide();
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
        EnemyUtils.SpawnGoldDrops(
            goldDropPrefab,
            transform.position,
            groundLayer,
            5,
            goldDropMin,
            goldDropMax
        );
    }

    async UniTaskVoid DeathRoutine(CancellationToken token)
    {
        await EnemyUtils.DeathBlink(sr);
        Destroy(gameObject);
    }
}

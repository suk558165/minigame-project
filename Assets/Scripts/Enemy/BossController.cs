using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SpriteRenderer))]
public partial class BossController : BossBase
{
    public static readonly List<BossController> Instances = new List<BossController>();

    // 도메인 리로드를 끈 상태에서도 이전 플레이의 잔여 항목이 남지 않도록 초기화
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instances.Clear();

    [Header("Phase 2")]
    [Tooltip("HP 비율이 이 값 이하가 되면 Phase 2 진입")]
    [SerializeField]
    private float phase2Threshold = 0.4f;

    [Tooltip("Phase 2에서 패턴 간 쿨타임 배율 (1보다 작으면 빨라짐)")]
    [SerializeField]
    private float phase2CooldownMult = 0.3f;

    [Header("순간이동 베기 패턴")]
    [SerializeField]
    private float chargeStunDuration = 0.5f;

    [Header("내려찍기 패턴")]
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

    private float hitboxOffsetX;

    [Header("Knockback")]
    [SerializeField]
    private float knockbackForce = 3f;

    [SerializeField]
    private float knockbackDuration = 0.1f;

    [Header("Audio")]
    [SerializeField]
    private AudioClip slamSound;

    [SerializeField]
    private AudioClip comboSound;

    [SerializeField]
    private AudioClip projectileSound;

    [SerializeField]
    private AudioClip dashSound;

    private bool isPhase2;

    [Header("UI")]
    [SerializeField]
    private string bossDisplayName = "BOSS";

    [SerializeField]
    private GameObject bossHealthBarUIPrefab;

    private BossHealthBarUI healthBarUI;

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

    protected override int GoldDropCount => 5;

    protected override void Awake()
    {
        base.Awake();

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
            hitboxOffsetX = Mathf.Abs(meleeHitbox.offset.x);
    }

    void OnEnable() => Instances.Add(this);

    void OnDisable() => Instances.Remove(this);

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

        // 경직 중에는 추격 속도가 넉백을 덮어쓰지 않도록 아무것도 하지 않는다
        if (staggerTimer > 0f)
        {
            staggerTimer -= Time.deltaTime;
            return;
        }

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

        // 스프라이트 원본은 오른쪽을 본다 — 플레이어가 왼쪽일 때만 뒤집는다.
        // (EnemyUtils.FlipToPlayer 와 같은 규칙)
        sr.flipX = dx < 0f;

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

    // ── 피격 / 사망 (공통 처리는 BossBase) ──

    // 공중으로 이탈한 동안(가시/공중마법 패턴)은 피격 무시
    protected override bool CanTakeDamage => !untargetable;

    protected override void OnDamaged()
    {
        if (!isPhase2 && hp <= maxHp * phase2Threshold)
        {
            isPhase2 = true;
            Phase2Flash(_cts.Token).Forget();
        }

        healthBarUI?.SetHealth(hp, maxHp);
    }

    protected override void OnStagger() =>
        Knockback((transform.position - player.position).normalized, _cts.Token).Forget();

    protected override void OnDied()
    {
        healthBarUI?.SetHealth(0, maxHp);
        healthBarUI?.Hide();
    }

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
}

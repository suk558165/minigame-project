using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 보스·미니보스 공통 부분: 스탯/드롭/사망 사운드 필드, 애니메이션 재생, 예고 연출,
/// 피격(경직 포함)과 사망 처리. 이동·패턴·체력바 표시는 각 보스 클래스에 둔다.
/// </summary>
public abstract class BossBase : MonoBehaviour, IDamageable
{
    [Header("Stats")]
    [SerializeField]
    protected float maxHp = 250f;

    [SerializeField]
    protected float moveSpeed = 3f;

    [SerializeField]
    protected float damage = 15f;

    [SerializeField]
    protected Collider2D meleeHitbox;

    [Header("패턴 공통")]
    [SerializeField]
    protected float patternCooldown = 0.8f;

    [SerializeField]
    protected float tellDuration = 0.3f;

    [SerializeField]
    protected float detectionRange = 12f;

    [Header("Drops")]
    [SerializeField]
    protected GameObject goldDropPrefab;

    [SerializeField]
    protected int goldDropMin = 10;

    [SerializeField]
    protected int goldDropMax = 20;

    [Header("Ground Check")]
    [SerializeField]
    protected LayerMask groundLayer;

    [Header("Audio")]
    [SerializeField]
    protected AudioClip deathSound;

    protected Rigidbody2D rb;
    protected SpriteRenderer sr;
    protected Collider2D col;
    protected Animator animator;
    protected Transform player;

    protected float hp;
    protected bool isDead;
    public bool IsDead => isDead;
    protected bool isActing;
    protected float cooldownTimer;

    protected Color originalColor;

    // 패턴이 유지하려는 색(기본/스턴 회색). 피격 플래시가 끝날 때 이 색으로 되돌린다.
    protected Color baseColor;

    // animator.Play 중복 호출 방지용 현재 상태 이름 (PlayState 참고)
    private string currentState;

    // 피격 경직: 패턴 중이 아닐 때만 짧게 멈칫한다. 연타에 계속 묶이지 않도록 간격을 둔다.
    protected const float StaggerDuration = 0.2f;
    const float StaggerCooldown = 0.8f;
    protected float staggerTimer;
    private float lastStaggerTime = -10f;

    protected CancellationTokenSource _cts = new();

    public System.Action onDeath;

    /// <summary>사망 시 뿌리는 금화 묶음 수.</summary>
    protected abstract int GoldDropCount { get; }

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        animator = GetComponent<Animator>();
        hp = maxHp;
        originalColor = sr.color;
        baseColor = originalColor;

        if (meleeHitbox != null)
            meleeHitbox.enabled = false;
    }

    protected virtual void Start()
    {
        if (PlayerRef.Exists)
        {
            player = PlayerRef.Transform;
            Physics2D.IgnoreLayerCollision(gameObject.layer, PlayerRef.GameObject.layer, true);
        }
    }

    protected virtual void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    CancellationToken RefreshToken()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        return _cts.Token;
    }

    // ── 애니메이션 ──

    // animator.Play 를 매 프레임 호출하면 클립이 0프레임에서 계속 리셋되므로 상태가 바뀔 때만 호출한다.
    // 공격 동작은 같은 상태를 다시 처음부터 재생해야 하므로 restart 로 강제한다.
    protected void PlayState(string state, bool restart = false, float normalizedTime = 0f)
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
    protected void FreezePose(string state, float normalizedTime)
    {
        PlayState(state, true, normalizedTime);
        if (animator != null)
            animator.speed = 0f;
    }

    // ── 예고 연출 ──

    protected UniTask TellFlash(Color color) =>
        EnemyUtils.TellFlash(sr, color, originalColor, tellDuration);

    protected UniTask TellShake() => EnemyUtils.TellShake(transform, tellDuration);

    protected bool IsGrounded() => EnemyUtils.IsGrounded(col, transform, groundLayer);

    // ── 피격 ──

    void OnTriggerEnter2D(Collider2D other)
    {
        if (isDead || meleeHitbox == null || !meleeHitbox.enabled)
            return;
        if (!other.CompareTag("Player"))
            return;
        other.GetComponentInParent<IDamageable>()?.TakeDamage(damage, gameObject);
    }

    public void TakeDamage(float amount, GameObject attacker = null)
    {
        if (isDead || !CanTakeDamage)
            return;

        amount *= MetaUpgrades.BossDamageMult;
        hp -= amount;
        DamagePopup.Spawn(transform.position + Vector3.up * 0.5f, amount);
        OnDamaged();

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
            PlayState("Hit", true);
            OnStagger();
        }
    }

    /// <summary>false 면 피격을 무시한다 (공중으로 이탈한 동안 등).</summary>
    protected virtual bool CanTakeDamage => true;

    /// <summary>체력이 깎인 직후, 사망 판정 전에 호출 (체력바 갱신 등).</summary>
    protected abstract void OnDamaged();

    /// <summary>경직이 걸린 순간 호출 (넉백 등).</summary>
    protected abstract void OnStagger();

    UniTask HitFlash() => EnemyUtils.HitFlash(sr, baseColor, () => isDead);

    // ── 사망 ──

    void Die()
    {
        isDead = true;
        AudioManager.Instance?.PlaySFX(deathSound);
        var token = RefreshToken();
        if (animator != null)
            animator.enabled = false;
        sr.color = originalColor;
        OnDied();
        if (meleeHitbox != null)
            meleeHitbox.enabled = false;
        rb.linearVelocity = Vector2.zero;
        rb.bodyType = RigidbodyType2D.Kinematic;
        if (col != null)
            col.enabled = false;
        onDeath?.Invoke();
        onDeath = null;
        RunStats.Instance?.AddKill();
        EnemyUtils.SpawnGoldDrops(goldDropPrefab, transform.position, GoldDropCount, goldDropMin, goldDropMax);
        DeathRoutine(token).Forget();
    }

    /// <summary>사망 처리 중 호출 (체력바 정리 등).</summary>
    protected abstract void OnDied();

    async UniTaskVoid DeathRoutine(CancellationToken token)
    {
        await EnemyUtils.DeathBlink(sr);
        Destroy(gameObject);
    }
}

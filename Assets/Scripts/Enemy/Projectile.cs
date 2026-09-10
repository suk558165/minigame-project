using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Projectile : MonoBehaviour
{
    public float lifetime = 5f;
    [Tooltip("원본 스프라이트가 향하는 각도 (오른쪽=0, 왼쪽=180)")]
    public float spriteAngleOffset;

    [Tooltip("이 레이어에 닿으면 소멸한다. 비워두면 Ground 를 사용 (일방향 발판은 통과)")]
    public LayerMask blockingLayer;

    public ObjectPool<Projectile> Pool;

    private bool released;
    private float damage;
    private float knockbackForce;
    private GameObject shooter;
    private float lifesteal;
    private bool ready;
    private int pierceRemaining;
    private float spinSpeed;
    private Transform homingTarget;
    private float homingTurnSpeed;
    private System.Collections.Generic.HashSet<int> hitIds = new();
    private Rigidbody2D rb;
    private SpriteRenderer sr;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponentInChildren<SpriteRenderer>();
        rb.gravityScale = 0f;
        rb.linearDamping = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        if (blockingLayer == 0)
            blockingLayer = LayerMask.GetMask("Ground");

        // 모든 콜라이더를 트리거로 설정 (이전 RequireComponent로 남은 콜라이더 대응)
        foreach (var col in GetComponents<Collider2D>())
            col.isTrigger = true;
    }

    public void Init(
        Vector2 direction,
        float speed,
        float damage,
        GameObject shooter = null,
        float knockbackForce = 8f,
        int pierce = 0,
        float spinSpeed = 0f,
        float lifesteal = 0f
    )
    {
        this.damage = damage;
        released = false;
        ready = false;
        hitIds.Clear();
        CancelInvoke();
        this.knockbackForce = knockbackForce;
        this.shooter = shooter;
        this.lifesteal = lifesteal;
        this.pierceRemaining = pierce;
        this.spinSpeed = spinSpeed;
        this.homingTarget = null;
        this.homingTurnSpeed = 0f;
        rb.linearVelocity = direction.normalized * speed;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle - spriteAngleOffset);
        // 회전만 하면 반대쪽으로 날아갈 때 그림이 위아래로 뒤집힌다(해골·불꽃 등). 세로 반전으로 바로 세운다.
        if (sr != null)
            sr.flipY = Mathf.Abs(Mathf.DeltaAngle(0f, angle - spriteAngleOffset)) > 90f;
        Invoke(nameof(Activate), 0.05f);
        Invoke(nameof(ReleaseSelf), lifetime);
    }

    public void SetHoming(Transform target, float turnSpeed)
    {
        homingTarget = target;
        homingTurnSpeed = turnSpeed;
    }

    void Update()
    {
        if (spinSpeed != 0f)
            transform.Rotate(0f, 0f, spinSpeed * Time.deltaTime);

        if (homingTarget != null && rb.linearVelocity.sqrMagnitude > 0.01f)
        {
            Vector2 toTarget = ((Vector2)homingTarget.position - rb.position).normalized;
            Vector2 cur = rb.linearVelocity.normalized;
            Vector2 newDir = Vector2.Lerp(cur, toTarget, homingTurnSpeed * Time.deltaTime).normalized;
            rb.linearVelocity = newDir * rb.linearVelocity.magnitude;
        }
    }

    void Activate() => ready = true;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!ready)
            return;

        if (other.isTrigger)
            return;

        if (shooter != null && other.transform.IsChildOf(shooter.transform))
            return;

        // 벽·바닥에 막힘. 이게 없으면 화살이 지형을 그대로 통과한다.
        if ((blockingLayer.value & (1 << other.gameObject.layer)) != 0)
        {
            ReleaseSelf();
            return;
        }

        int enemyLayer = LayerMask.NameToLayer("Enemy");
        int shooterLayer = shooter != null ? shooter.layer : -1;

        if (shooter != null)
        {
            bool shooterIsEnemy = shooterLayer == enemyLayer;
            var targetBody = other.attachedRigidbody;
            int targetLayer = targetBody != null ? targetBody.gameObject.layer : other.gameObject.layer;
            bool targetIsEnemy = targetLayer == enemyLayer;

            if (shooterIsEnemy && targetIsEnemy)
                return; // 적 → 적 무시
            if (!shooterIsEnemy && !targetIsEnemy)
                return; // 플레이어 → 플레이어 무시
        }

        var damageable = other.GetComponentInParent<IDamageable>();
        if (damageable != null)
        {
            // 콜라이더가 아니라 피격 대상 기준으로 중복을 막는다.
            // 콜라이더 ID로 하면 콜라이더가 여러 개인 적을 중복 타격한다.
            var targetObj = (damageable as Component)?.gameObject;
            int targetId = targetObj != null ? targetObj.GetInstanceID() : other.GetInstanceID();
            if (!hitIds.Add(targetId))
                return;

            damageable.TakeDamage(damage, shooter);

            // 흡혈: 플레이어가 쏜 투사체가 적을 맞히면 가한 데미지 비율만큼 회복
            if (lifesteal > 0f && shooter != null)
                shooter.GetComponent<PlayerHealth>()?.Heal(damage * lifesteal);

            if (other.CompareTag("Player"))
            {
                var playerCtrl = other.GetComponentInParent<PlayerController>();
                if (playerCtrl != null)
                {
                    Vector2 dir = rb.linearVelocity.normalized;
                    playerCtrl.Knockback(new Vector2(dir.x, 0.3f).normalized * knockbackForce);
                }
            }

            if (pierceRemaining <= 0)
                ReleaseSelf();
            else
                pierceRemaining--;
        }
    }

    void ReleaseSelf()
    {
        if (released)
            return;

        released = true;
        CancelInvoke();
        if (Pool != null)
            Pool.Release(this);
        else
            Destroy(gameObject);
    }
}

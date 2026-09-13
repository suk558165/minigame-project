using System.Collections.Generic;
using UnityEngine;

public class WorldPotion : WorldDrop
{
    public static readonly List<WorldPotion> Instances = new List<WorldPotion>();

    // 도메인 리로드를 끈 상태에서도 이전 플레이의 잔여 항목이 남지 않도록 초기화
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instances.Clear();

    public float healAmount = 20f;

    [SerializeField]
    private float pickupRadius = 1.2f;

    [SerializeField]
    private float magnetRadius = 4f;

    [SerializeField]
    private float magnetSpeed = 6f;

    private Transform player;
    private PlayerHealth health;

    void OnEnable() => Instances.Add(this);

    void OnDisable() => Instances.Remove(this);

    void Start()
    {
        // 포션의 모든 콜라이더(자식 포함)를 트리거로 — 물리 충돌 방지
        foreach (var col in GetComponentsInChildren<Collider2D>(true))
            col.isTrigger = true;

        // 자식의 Rigidbody2D를 Kinematic으로 — 스크립트가 직접 위치를 제어
        foreach (var rb in GetComponentsInChildren<Rigidbody2D>(true))
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
        }

        // 플레이어의 모든 콜라이더(자식 포함)와 충돌 무시
        if (PlayerRef.Exists)
        {
            var playerCols = PlayerRef.GameObject.GetComponentsInChildren<Collider2D>(true);
            foreach (var myCol in GetComponentsInChildren<Collider2D>(true))
            foreach (var pc in playerCols)
                if (pc != null)
                    Physics2D.IgnoreCollision(myCol, pc, true);
        }

        if (!launched)
            SnapToGround();
    }

    void Update()
    {
        UpdateFall();

        // 날아가는 동안은 줍기·자석 없이 떨어지기만 한다
        if (launched)
            return;

        if (player == null)
        {
            if (!PlayerRef.Exists)
                return;
            player = PlayerRef.Transform;
            health = PlayerRef.Health;
        }

        if (health == null || health.IsDead)
            return;

        float dist = Vector2.Distance(transform.position, player.position);

        if (dist <= pickupRadius)
        {
            var bonus = Inventory.Instance?.GetTotalStatBonus() ?? default;
            float amount = healAmount * (1f + bonus.potionHealMult);
            health.Heal(amount);
            DamagePopup.SpawnHeal(player.position + Vector3.up * 0.5f, amount);
            Destroy(gameObject);
            return;
        }

        bool needsHeal = health.CurrentHp < health.EffectiveMaxHp;
        if (needsHeal && dist <= magnetRadius)
        {
            Vector3 dir = (player.position - transform.position).normalized;
            transform.position += dir * magnetSpeed * Time.deltaTime;
        }
    }
}

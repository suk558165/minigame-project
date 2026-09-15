using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerCombat))]
public class PlayerController : MonoBehaviour
{
    [Header("Debug")]
    public bool previewDeath;

    public bool InputLocked { get; set; }

    private PlayerHealth health;
    private Rigidbody2D rb;
    private Animator animator;
    private PlayerMovement movement;
    private PlayerCombat combat;
    private WeaponInventory weaponInventory;
    private Inventory inventory;
    private bool deathHandled;
    private SortingGroup sortingGroup;
    private const int DefaultSortingOrder = 32000;
    private const int BehindShopSortingOrder = -100;

    private static readonly int HashIsDead = Animator.StringToHash("IsDead");

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        movement = GetComponent<PlayerMovement>();
        combat = GetComponent<PlayerCombat>();
        health = GetComponent<PlayerHealth>();
        weaponInventory = GetComponentInChildren<WeaponInventory>();
        inventory = GetComponent<Inventory>();

        sortingGroup = GetComponent<SortingGroup>();
        if (sortingGroup == null)
            sortingGroup = gameObject.AddComponent<SortingGroup>();
        sortingGroup.sortingLayerName = "Default";
        sortingGroup.sortingOrder = DefaultSortingOrder;
    }

    void Update()
    {
        if (sortingGroup != null)
            sortingGroup.sortingOrder = ShopUI.IsOpen
                ? BehindShopSortingOrder
                : DefaultSortingOrder;

        bool dead = (health != null && health.IsDead) || previewDeath;
        if (dead)
        {
            animator.SetBool(HashIsDead, true);
            if (!deathHandled)
            {
                // 죽는 즉시 Kinematic 으로 굳히면 공중에서 죽었을 때 누운 채로 떠 있는다.
                // 입력만 끊고 중력으로 떨어뜨린 뒤, 바닥에 닿으면 그때 물리를 정지시킨다.
                movement.StopForDeath();

                if (movement.IsGrounded)
                {
                    deathHandled = true;
                    rb.linearVelocity = Vector2.zero;
                    rb.bodyType = RigidbodyType2D.Kinematic;
                }
            }
            return;
        }

        if (InputLocked || DialogueUI.IsOpen)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            movement.UpdateAnimatorAndFlip(false);
            return;
        }

        movement.HandleInput();
        combat.HandleInput();
        bool flipAllowed =
            !combat.IsAttacking || (weaponInventory?.Current?.flipDuringAttack ?? false);
        movement.UpdateAnimatorAndFlip(combat.IsAttacking, flipAllowed);
    }

    void FixedUpdate()
    {
        movement.FixedUpdateMovement();
    }

    public void Knockback(Vector2 velocity)
    {
        if (health == null || health.IsDead)
            return;
        if (movement.IsDashing)
            return;
        movement.ApplyKnockback(velocity);
    }

    /// <summary>
    /// 마을 귀환 시 죽음 상태를 완전히 초기화합니다.
    /// HP 복구 + 물리/애니메이터 상태 복원
    /// </summary>
    public void Revive()
    {
        health?.Revive();
        weaponInventory?.ResetToDefault();
        inventory?.ResetOnDeath();

        deathHandled = false;
        rb.bodyType = RigidbodyType2D.Dynamic;
        animator.SetBool(HashIsDead, false);
    }
}

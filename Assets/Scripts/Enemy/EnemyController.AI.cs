using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// EnemyController 의 추적·공격 판단 부분 (필드·수명주기·Update 는 EnemyController.cs).
public partial class EnemyController
{
    /// <summary>
    /// 발견·추격 유지·포기를 관리한다.
    /// 한 번 발견하면 점프로 높이가 잠깐 벌어져도 놓치지 않고, 완전히 놓치면 그 자리를 새 순찰 중심으로 삼는다.
    /// </summary>
    void UpdateAggro()
    {
        if (player == null)
        {
            aggroTimer = 0f;
            return;
        }

        Vector2 d = new Vector2(player.position.x - transform.position.x, PlayerFeetY - FeetY);
        // 비행 몬스터는 높이 제한 없이 쫓는다.
        float yLimit = isFlying ? float.MaxValue : isRanged ? rangedYThreshold : chaseYThreshold;

        if (aggroTimer > 0f)
        {
            if (d.magnitude <= detectionRange * LeashMultiplier && Mathf.Abs(d.y) <= yLimit)
            {
                aggroTimer = AggroMemory;
                return;
            }
            aggroTimer -= Time.deltaTime;
            if (aggroTimer <= 0f)
                patrolOrigin = transform.position;
            return;
        }

        if (d.magnitude > detectionRange || Mathf.Abs(d.y) > yLimit)
            return;

        aggroTimer = AggroMemory;
        alertTimer = AlertDuration;
        FacePlayer();
        if (!isFlying && IsGrounded())
            rb.linearVelocity = new Vector2(0f, AlertHopSpeed);
    }

    void UpdateMelee()
    {
        float dx = player.position.x - transform.position.x;
        bool sameLevel = Mathf.Abs(PlayerFeetY - FeetY) <= chaseYThreshold;

        if (sameLevel && Mathf.Abs(dx) <= attackRange)
        {
            Move(0f);
            FacePlayer();
            if (attackTimer <= 0f)
                StartAttack();
            return;
        }

        ChaseTowards(dx);
    }

    void UpdateRanged()
    {
        float dx = player.position.x - transform.position.x;
        float adx = Mathf.Abs(dx);
        bool sameLine = Mathf.Abs(PlayerFeetY - FeetY) <= rangedYThreshold;

        if (!sameLine || adx > safeDistance)
        {
            ChaseTowards(dx);
            return;
        }

        // 너무 붙으면 뒤로 물러나며 거리를 벌린다. 몸은 계속 플레이어를 향한다.
        float away = -Mathf.Sign(dx);
        if (adx < safeDistance * RetreatRatio && !IsEdgeAhead(away))
            Move(away);
        else
            Move(0f);
        FacePlayer();

        if (attackTimer <= 0f)
            StartAttack();
    }

    /// <summary>
    /// 플레이어 옆 공격 위치로 날아가 붙는다. 히트박스 높이를 플레이어 몸 중앙에 맞춰야 돌진이 빗나가지 않는다.
    /// </summary>
    void UpdateFlying()
    {
        Vector2 target = PlayerCenter;
        float side = transform.position.x < target.x ? -1f : 1f;
        Vector2 slot = new Vector2(target.x + side * attackRange * 0.7f, target.y - hitboxCenterY);
        Vector2 toSlot = slot - (Vector2)transform.position;

        if (toSlot.magnitude <= FlyAttackSlotTolerance)
        {
            Fly(Vector2.zero, 0f);
            FacePlayer();
            if (attackTimer <= 0f)
                StartAttack();
            return;
        }

        Fly(toSlot.normalized, moveSpeed * ChaseSpeedMultiplier);
        FacePlayer();
    }

    void UpdateLunge()
    {
        if (lungeTimer > 0f)
        {
            lungeTimer -= Time.deltaTime;
            rb.linearVelocity = lungeDir * moveSpeed * LungeSpeedMultiplier;
            return;
        }
        rb.linearVelocity = Vector2.MoveTowards(
            rb.linearVelocity,
            Vector2.zero,
            FlyAcceleration * 3f * Time.deltaTime
        );
    }

    /// <summary>
    /// 플레이어 쪽으로 달린다. 발판 끝에 닿으면 멈춰서 플레이어를 바라보며 기다린다
    /// (다른 층의 플레이어를 쫓아 떨어지지 않는다).
    /// </summary>
    void ChaseTowards(float dx)
    {
        if (Mathf.Abs(dx) <= FacingDeadzone)
        {
            Move(0f);
            return;
        }

        float dir = Mathf.Sign(dx);
        if (IsEdgeAhead(dir))
        {
            Move(0f);
            SetFacing(dir > 0f);
            return;
        }
        Move(dir, moveSpeed * ChaseSpeedMultiplier);
    }

    void FacePlayer()
    {
        if (player == null)
            return;
        float dx = player.position.x - transform.position.x;
        if (Mathf.Abs(dx) > FacingDeadzone)
            SetFacing(dx > 0f);
    }

    void StartAttack()
    {
        attackTimer = isRanged
            ? attackCooldown * Mathf.Max(1f, rangedCooldownMultiplier)
            : attackCooldown;
        // 트리거를 건 프레임에는 애니메이터가 아직 Attack 상태가 아니므로 잠깐 잠금을 유지한다.
        attackGrace = 0.1f;
        animator.SetTrigger(HashAttack);
        AudioManager.Instance?.PlaySFX(attackSound);
        if (isRanged)
            ShootAfterDelay(attackDamageDelay, _cts.Token).Forget();

        if (isFlying && player != null)
        {
            Vector2 hitboxCenter = (Vector2)transform.position + Vector2.up * hitboxCenterY;
            lungeDir = (PlayerCenter - hitboxCenter).normalized;
            lungeTimer = LungeDuration;
        }
    }

    void ShootProjectile()
    {
        if (projectilePrefab == null || player == null)
            return;

        Vector3 origin = transform.position;
        if (firePoint != null)
        {
            // flipX는 자식 위치를 뒤집지 않으므로 바라보는 방향에 맞춰 직접 미러링한다.
            Vector3 offset = firePoint.position - transform.position;
            offset.x = Mathf.Abs(offset.x) * (IsFacingRight ? 1f : -1f);
            origin += offset;
        }

        Vector2 dir;
        if (aimAtPlayer)
            // 플레이어 기준점은 발밑이라 그대로 조준하면 바닥으로 쏜다. 몸 중앙을 노린다.
            dir = (PlayerCenter - (Vector2)origin).normalized;
        else
            dir = IsFacingRight ? Vector2.right : Vector2.left;

        if (projPool == null)
            projPool = new ObjectPool<Projectile>(projectilePrefab.GetComponent<Projectile>());
        var projComp = projPool.Get(origin, Quaternion.identity);
        projComp.Pool = projPool;
        projComp.transform.localScale = Vector3.one * projectileScale;
        projComp.Init(dir, projectileSpeed, damage, gameObject, spinSpeed: projectileSpinSpeed);
    }

    public void EnableHitbox()
    {
        if (meleeHitbox == null)
            return;
        var hitboxComp = meleeHitbox.GetComponent<MeleeHitbox>();
        if (hitboxComp != null)
            // 공격 지속 시간만큼만 활성화 → 이벤트 누락돼도 자동 비활성화
            hitboxComp.Activate(attackCooldown * 0.4f);
        else
            meleeHitbox.enabled = true;
    }

    public void DisableHitbox()
    {
        if (meleeHitbox == null)
            return;
        var hitboxComp = meleeHitbox.GetComponent<MeleeHitbox>();
        if (hitboxComp != null)
            hitboxComp.ForceDeactivate();
        else
            meleeHitbox.enabled = false;
    }

    async UniTaskVoid ShootAfterDelay(float delay, CancellationToken token)
    {
        await UniTask.Delay(System.TimeSpan.FromSeconds(delay), cancellationToken: token);
        // 발사 전에 맞아서 모션이 끊겼으면 쏘지 않는다.
        if (!isDead && hitStunTimer <= 0f)
            ShootProjectile();
    }
}

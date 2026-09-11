using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// BossController 의 패턴 연출 부분 (상태/수명주기는 BossController.cs).
public partial class BossController
{
    // ── 텔 (예고 연출) ──

    UniTask TellFlash(Color color) =>
        EnemyUtils.TellFlash(sr, color, originalColor, tellDuration);

    UniTask TellShake() => EnemyUtils.TellShake(transform, tellDuration);

    // ── 패턴: 돌진 공격 (돌진 후 베기) ──

    async UniTask ChargeAttack(CancellationToken token)
    {
        await TellFlash(Color.red);

        attackFlip = true;
        FlipToPlayer();
        AudioManager.Instance?.PlaySFX(dashSound);
        if (animator != null)
            animator.Play("DashRun", 0, 0f);

        float dir = player.position.x > transform.position.x ? 1f : -1f;
        float elapsed = 0f;

        // 돌진: 플레이어 근처까지 이동 (데미지 없음)
        while (elapsed < chargeDuration)
        {
            transform.position += new Vector3(dir * chargeSpeed * Time.deltaTime, 0f, 0f);

            if (Vector2.Distance(transform.position, player.position) <= comboRange)
                break;

            elapsed += Time.deltaTime;
            await UniTask.Yield(token);
        }

        // 도착 후 베기
        rb.linearVelocity = Vector2.zero;
        FlipToPlayer();
        if (animator != null)
            animator.Play("Dash", 0, 0f);

        await UniTask.Delay(System.TimeSpan.FromSeconds(0.2f), cancellationToken: token);
        DealAreaDamage(transform.position, comboRange);
        await UniTask.Delay(System.TimeSpan.FromSeconds(0.15f), cancellationToken: token);

        // 스턴 (반격 타이밍)
        sr.color = Color.gray;
        await UniTask.Delay(System.TimeSpan.FromSeconds(chargeStunDuration), cancellationToken: token);
        sr.color = originalColor;

        attackFlip = false;
    }

    // ── 패턴: 내려찍기 ──

    async UniTask SlamAttack(CancellationToken token)
    {
        await TellShake();

        if (animator != null)
            animator.Play("Slam", 0, 0f);

        // 점프
        rb.linearVelocity = new Vector2(0f, slamJumpForce);

        // 점프 후 실제로 지면을 벗어날 때까지 대기
        float liftWait = 0f;
        while (IsGrounded() && liftWait < 0.3f)
        {
            liftWait += Time.deltaTime;
            await UniTask.Yield(token);
        }

        await UniTask.Delay(System.TimeSpan.FromSeconds(0.2f), cancellationToken: token);

        // 경고 표시 (바닥 전체 — 항상 지면 높이)
        float floorY = EnemyUtils.FindFloorY(player.position, groundLayer);
        Vector3 targetPos = new Vector3(player.position.x, floorY, 0f);
        GameObject warning = null;
        if (slamWarningPrefab != null)
        {
            warning = Instantiate(slamWarningPrefab, targetPos, Quaternion.identity);
            warning.transform.localScale = new Vector3(100f, 0.3f, 1f);
        }

        await UniTask.Delay(System.TimeSpan.FromSeconds(0.15f), cancellationToken: token);

        // 급강하
        rb.MovePosition(new Vector2(targetPos.x, rb.position.y));
        rb.linearVelocity = new Vector2(0f, -slamFallSpeed);

        float fallTimeout = 3f;
        float fallElapsed = 0f;
        while (!IsGrounded() && fallElapsed < fallTimeout)
        {
            rb.linearVelocity = new Vector2(0f, -slamFallSpeed);
            fallElapsed += Time.deltaTime;
            await UniTask.Yield(token);
        }
        rb.linearVelocity = Vector2.zero;

        if (warning != null)
            Destroy(warning);

        // 착지 데미지
        AudioManager.Instance?.PlaySFX(slamSound);
        SlamGroundDamage();

        await UniTask.Delay(System.TimeSpan.FromSeconds(0.25f), cancellationToken: token);
    }

    // ── 패턴: 투사체 ──

    async UniTask ProjectileAttack(CancellationToken token)
    {
        await TellFlash(new Color(1f, 0.5f, 0f));

        FlipToPlayer();
        if (animator != null)
            animator.Play("Charge", 0, 0f);

        if (projectilePrefab == null || player == null)
            return;

        AudioManager.Instance?.PlaySFX(projectileSound);
        // 기준점(발밑)끼리 조준하면 발밑에서 바닥으로 쏜다. 몸 중앙에서 플레이어 몸 중앙을 노린다.
        Vector2 origin = col != null && col.enabled ? (Vector2)col.bounds.center : (Vector2)transform.position;
        var playerCol = player.GetComponent<Collider2D>();
        Vector2 target = playerCol != null ? (Vector2)playerCol.bounds.center : (Vector2)player.position;
        Vector2 baseDir = (target - origin).normalized;
        float baseAngle = Mathf.Atan2(baseDir.y, baseDir.x) * Mathf.Rad2Deg;

        for (int i = 0; i < projectileCount; i++)
        {
            float offset = 0f;
            if (projectileCount > 1)
                offset = Mathf.Lerp(
                    -projectileSpread / 2f,
                    projectileSpread / 2f,
                    (float)i / (projectileCount - 1)
                );

            float angle = (baseAngle + offset) * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            var projComp = GetPooledProjectile(origin);
            projComp.Pool = projPool;
            projComp.Init(dir, projectileSpeed, damage, gameObject);
        }

        await UniTask.Delay(System.TimeSpan.FromSeconds(0.25f), cancellationToken: token);
    }

    Projectile GetPooledProjectile(Vector2 origin)
    {
        if (projPool == null)
            projPool = new ObjectPool<Projectile>(projectilePrefab.GetComponent<Projectile>());
        return projPool.Get(origin, Quaternion.identity);
    }

    // ── Phase 2 패턴: 바닥 가시 (공중 이탈 후 랜덤 가시) ──

    async UniTask SpikeStormAttack(CancellationToken token)
    {
        await GoAirborne(4f, 0.4f, token);

        const float spacing = 1.4f;
        const float halfSpan = 7f;
        const int waves = 2;
        const float warnTime = 0.45f;
        var positions = new List<Vector3>();

        for (int w = 0; w < waves; w++)
        {
            positions.Clear();
            float centerX = player != null ? player.position.x : transform.position.x;
            for (float x = centerX - halfSpan; x <= centerX + halfSpan; x += spacing)
            {
                if (Random.value > 0.55f)
                    continue;
                float floorY = EnemyUtils.FindFloorY(
                    new Vector3(x, transform.position.y, 0f),
                    groundLayer
                );
                positions.Add(new Vector3(x, floorY, 0f));
            }

            // 예고 표식
            if (warningPrefab != null)
                foreach (var p in positions)
                    Destroy(Instantiate(warningPrefab, p, Quaternion.identity), warnTime);
            await UniTask.Delay(System.TimeSpan.FromSeconds(warnTime), cancellationToken: token);

            // 가시 솟구침
            AudioManager.Instance?.PlaySFX(slamSound);
            if (spikePrefab != null)
                foreach (var p in positions)
                {
                    var spike = Instantiate(spikePrefab, p, Quaternion.identity);
                    spike.GetComponent<BossSpike>()?.Init(damage, gameObject);
                }

            await UniTask.Delay(System.TimeSpan.FromSeconds(0.7f), cancellationToken: token);
        }

        await ReturnFromAir(0.4f, token);
    }

    // ── Phase 2 패턴: 공중 마법 (플레이어 추적 낙하) ──

    async UniTask AirMagicAttack(CancellationToken token)
    {
        await GoAirborne(4f, 0.4f, token);

        const int count = 5;
        const float spawnInterval = 0.5f;
        const float spawnOffsetY = 3f;
        const float homingTurn = 3f;

        for (int i = 0; i < count; i++)
        {
            if (player == null)
                break;

            float offsetX = (i % 2 == 0 ? -1f : 1f) * (2f + i * 0.5f);
            Vector3 spawnPos = (Vector3)rb.position + new Vector3(offsetX, spawnOffsetY, 0f);

            AudioManager.Instance?.PlaySFX(projectileSound);
            var magic = GetPooledMagic(spawnPos);
            magic.Pool = magicPool;
            Vector2 dir = ((Vector2)player.position - (Vector2)spawnPos).normalized;
            magic.Init(dir, magicProjectileSpeed * 0.6f, damage, gameObject);
            magic.SetHoming(player, homingTurn);

            await UniTask.Delay(System.TimeSpan.FromSeconds(spawnInterval), cancellationToken: token);
        }

        await UniTask.Delay(System.TimeSpan.FromSeconds(0.5f), cancellationToken: token);
        await ReturnFromAir(0.4f, token);
    }

    Projectile GetPooledMagic(Vector3 pos)
    {
        if (magicPool == null)
        {
            var prefab = magicProjectilePrefab != null ? magicProjectilePrefab : projectilePrefab;
            magicPool = new ObjectPool<Projectile>(prefab.GetComponent<Projectile>());
        }
        return magicPool.Get(pos, Quaternion.identity);
    }

    // ── 공중 이탈 / 복귀 (가시·공중마법 공용) ──

    private bool isBobbing;
    private Vector3 bobBasePos;

    async UniTask GoAirborne(float height, float duration, CancellationToken token)
    {
        preAirbornePos = transform.position;
        untargetable = true;
        if (col != null)
            col.enabled = false;
        if (meleeHitbox != null)
            meleeHitbox.enabled = false;
        rb.linearVelocity = Vector2.zero;
        rb.bodyType = RigidbodyType2D.Kinematic;

        if (animator != null)
            animator.Play("Fly", 0, 0f);

        Vector3 start = transform.position;
        Vector3 target = start + Vector3.up * height;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            transform.position = Vector3.Lerp(start, target, k);
            await UniTask.Yield(token);
        }
        transform.position = target;

        // Fly → Float 전환, 둥둥 흔들림 시작
        bobBasePos = target;
        isBobbing = true;
        if (animator != null)
            animator.Play("Float", 0, 0f);
        BobLoop(token).Forget();
    }

    async UniTaskVoid BobLoop(CancellationToken token)
    {
        float elapsed = 0f;
        const float bobAmplitude = 0.3f;
        const float bobSpeed = 2f;
        while (isBobbing)
        {
            elapsed += Time.deltaTime;
            float offsetY = Mathf.Sin(elapsed * bobSpeed) * bobAmplitude;
            transform.position = bobBasePos + new Vector3(0f, offsetY, 0f);
            await UniTask.Yield(token);
        }
    }

    async UniTask ReturnFromAir(float duration, CancellationToken token)
    {
        isBobbing = false;

        if (animator != null)
            animator.Play("Fly", 0, 0f);

        Vector3 start = transform.position;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            transform.position = Vector3.Lerp(start, preAirbornePos, k);
            await UniTask.Yield(token);
        }
        transform.position = preAirbornePos;
        rb.bodyType = RigidbodyType2D.Dynamic;
        if (col != null)
            col.enabled = true;
        untargetable = false;
    }

    // ── 패턴: 연속 베기 ──

    async UniTask ComboAttack(CancellationToken token)
    {
        await TellShake();

        attackFlip = true;
        FlipToPlayer();
        AudioManager.Instance?.PlaySFX(comboSound);
        if (animator != null)
            animator.Play("Combo", 0, 0f);

        for (int i = 0; i < comboHitCount; i++)
        {
            FlipToPlayer();
            DealAreaDamage(transform.position, comboRange);

            if (i < comboHitCount - 1)
                await UniTask.Delay(System.TimeSpan.FromSeconds(comboInterval), cancellationToken: token);
        }

        attackFlip = false;
        await UniTask.Delay(System.TimeSpan.FromSeconds(0.15f), cancellationToken: token);
    }

    // ── 범위 데미지 ──

    void DealAreaDamage(Vector3 center, float radius)
    {
        if (player == null)
            return;
        if (Vector2.Distance(center, player.position) <= radius)
        {
            player.GetComponent<IDamageable>()?.TakeDamage(damage, gameObject);
            var playerCtrl = player.GetComponent<PlayerController>();
            if (playerCtrl != null)
            {
                Vector2 knockDir = ((Vector2)player.position - (Vector2)center).normalized;
                playerCtrl.Knockback(knockDir * 8f);
            }
        }
    }

    void SlamGroundDamage()
    {
        if (player == null)
            return;

        var movement = player.GetComponent<PlayerMovement>();
        if (movement != null && !movement.IsGrounded)
            return;

        player.GetComponent<IDamageable>()?.TakeDamage(damage, gameObject);
        var playerCtrl = player.GetComponent<PlayerController>();
        if (playerCtrl != null)
            playerCtrl.Knockback(Vector2.up * 8f);
    }

    bool IsGrounded() => EnemyUtils.IsGrounded(col, transform, groundLayer);
}

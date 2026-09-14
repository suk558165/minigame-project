using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// BossController 의 패턴 연출 부분 (상태/수명주기는 BossController.cs).
public partial class BossController
{
    // Boss_DeathAngelController 클립 길이 (12fps). 패턴 타이밍을 애니메이션에 맞추는 기준.
    const float ClipSlam = 0.667f;   // cast_1~8
    const float ClipCharge = 0.5f;   // ready_teleport_1~6
    const float ClipDash = 0.833f;   // attack_teleport_1~10
    const float ClipCombo = 0.583f;  // attack_1~7

    // 순간이동 베기 프레임 시점
    const float VanishTime = 0.417f; // ready_teleport_6: 완전히 흩어진 프레임
    const float ReformTime = 0.25f;  // attack_teleport_4: 몸이 다시 모인 프레임
    const float SlashTime = 0.5f;    // attack_teleport_7: 큰 낫 궤적 프레임
    const float TeleportGap = 1f;    // 다시 나타날 때 플레이어와의 거리 (comboRange 안쪽)

    // 내려찍기
    const float SlamRaisePose = 0.25f; // cast_4: 낫을 치켜든 프레임 — 급강하 자세
    const float SlamRiseHeight = 6f;
    const float SlamRiseTime = 0.45f;
    const float SlamHangTime = 0.15f;

    // ── 패턴: 순간이동 베기 ──
    // 그림이 돌진이 아니라 '흩어졌다가 다시 나타나 베는' 동작이다 (ready_teleport → attack_teleport).
    // 흩어지는 동작과 다시 모이는 동작이 예고 역할을 하므로 별도 깜빡임은 두지 않는다.

    async UniTask ChargeAttack(CancellationToken token)
    {
        FlipToPlayer();
        AudioManager.Instance?.PlaySFX(dashSound);
        PlayState("Charge", true);

        await UniTask.Delay(System.TimeSpan.FromSeconds(VanishTime), cancellationToken: token);
        untargetable = true; // 흩어져 보이지 않는 동안은 맞지 않는다
        await UniTask.Delay(System.TimeSpan.FromSeconds(ClipCharge - VanishTime), cancellationToken: token);

        // 플레이어 옆(원래 있던 쪽)에 나타난다. 사이에 벽이 있으면 벽 앞까지만.
        float side = transform.position.x < player.position.x ? -1f : 1f;
        float targetX = player.position.x + side * TeleportGap;
        float dx = targetX - transform.position.x;
        if (col != null && Mathf.Abs(dx) > 0.01f)
        {
            float dir = Mathf.Sign(dx);
            float halfWidth = col.bounds.extents.x;
            var wall = Physics2D.Raycast(col.bounds.center, new Vector2(dir, 0f), Mathf.Abs(dx) + halfWidth, groundLayer);
            if (wall.collider != null)
                targetX = wall.point.x - dir * (halfWidth + 0.05f);
        }
        transform.position = new Vector3(targetX, transform.position.y, transform.position.z);

        FlipToPlayer();
        PlayState("Dash", true);

        await UniTask.Delay(System.TimeSpan.FromSeconds(ReformTime), cancellationToken: token);
        untargetable = false;
        await UniTask.Delay(System.TimeSpan.FromSeconds(SlashTime - ReformTime), cancellationToken: token);
        DealAreaDamage(transform.position, comboRange);
        await UniTask.Delay(System.TimeSpan.FromSeconds(ClipDash - SlashTime), cancellationToken: token);

        // 스턴 (반격 타이밍)
        PlayState("Idle");
        baseColor = Color.gray;
        sr.color = baseColor;
        await UniTask.Delay(System.TimeSpan.FromSeconds(chargeStunDuration), cancellationToken: token);
        baseColor = originalColor;
        sr.color = baseColor;
    }

    // ── 패턴: 내려찍기 ──
    // 지상에서 시전 → 플레이어 머리 위로 곡선 도약 → 정점에서 자세 고정 → 급강하 → 착지 베기

    async UniTask SlamAttack(CancellationToken token)
    {
        await TellShake();

        // 떠오르면서 시전하면 동작이 공중에서 뭉개지므로 지상에서 끝까지 재생한다.
        FlipToPlayer();
        PlayState("Slam", true);
        await UniTask.Delay(System.TimeSpan.FromSeconds(ClipSlam), cancellationToken: token);

        // 경고 표시 (바닥 전체 — 항상 지면 높이)
        float floorY = EnemyUtils.FindFloorY(player.position, groundLayer);
        Vector3 targetPos = new Vector3(player.position.x, floorY, 0f);
        GameObject warning = null;
        if (slamWarningPrefab != null)
        {
            warning = Instantiate(slamWarningPrefab, targetPos, Quaternion.identity);
            warning.transform.localScale = new Vector3(100f, 0.3f, 1f);
        }

        // 정점까지 곡선 이동 (정점에서 옆으로 순간이동하던 문제)
        PlayState("Fly", true);
        await EnemyUtils.LeapArc(rb, transform, targetPos.x, SlamRiseHeight, SlamRiseTime, token);

        // 정점: 낫을 치켜든 자세로 잠깐 멈췄다가 급강하
        FlipToPlayer();
        FreezePose("Slam", SlamRaisePose / ClipSlam);
        await UniTask.Delay(System.TimeSpan.FromSeconds(SlamHangTime), cancellationToken: token);

        await EnemyUtils.DiveUntilGrounded(rb, slamFallSpeed, IsGrounded, token);

        if (warning != null)
            Destroy(warning);

        // 착지 데미지
        AudioManager.Instance?.PlaySFX(slamSound);
        SlamGroundDamage();
        CameraFollow.Instance?.Shake(0.2f, 0.3f);

        // 착지 순간 낫 궤적 프레임부터 재생 (충격 자세 없이 바로 대기로 넘어가던 문제)
        PlayState("Dash", true, SlashTime / ClipDash);
        await UniTask.Delay(System.TimeSpan.FromSeconds(ClipDash - SlashTime + 0.1f), cancellationToken: token);
    }

    // ── 패턴: 투사체 ──

    async UniTask ProjectileAttack(CancellationToken token)
    {
        await TellFlash(new Color(1f, 0.5f, 0f));

        if (projectilePrefab == null || player == null)
            return;

        FlipToPlayer();
        PlayState("Charge", true);

        // 시전 동작이 어느 정도 진행된 뒤 발사 (0프레임 발사 방지)
        const float castLead = 0.3f;
        await UniTask.Delay(System.TimeSpan.FromSeconds(castLead), cancellationToken: token);

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

        // 남은 시전 클립 재생 (0.5s 클립이 0.25s 에서 잘려나가던 문제)
        await UniTask.Delay(
            System.TimeSpan.FromSeconds(ClipCharge - castLead),
            cancellationToken: token
        );
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
        await GoAirborne(4f, 0.5f, token);

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

        await ReturnFromAir(0.5f, token);
    }

    // ── Phase 2 패턴: 공중 마법 (플레이어 추적 낙하) ──

    async UniTask AirMagicAttack(CancellationToken token)
    {
        await GoAirborne(4f, 0.5f, token);

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
        await ReturnFromAir(0.5f, token);
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

        PlayState("Fly", true);

        Vector3 start = transform.position;
        Vector3 target = start + Vector3.up * height;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            transform.position = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, k)); // 출발·도착 감속
            await UniTask.Yield(token);
        }
        transform.position = target;

        // Fly → Float 전환, 둥둥 흔들림 시작
        bobBasePos = target;
        isBobbing = true;
        PlayState("Float", true);
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

        PlayState("Fly", true);

        Vector3 start = transform.position;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            transform.position = Vector3.Lerp(start, preAirbornePos, Mathf.SmoothStep(0f, 1f, k));
            await UniTask.Yield(token);
        }
        transform.position = preAirbornePos;
        rb.bodyType = RigidbodyType2D.Dynamic;
        if (col != null)
            col.enabled = true;
        untargetable = false;

        // 착지 직후 비행 루프를 끊고 잠깐 멈춘다
        PlayState("Idle");
        await UniTask.Delay(System.TimeSpan.FromSeconds(0.15f), cancellationToken: token);
    }

    // ── 패턴: 연속 베기 ──

    async UniTask ComboAttack(CancellationToken token)
    {
        await TellShake();

        FlipToPlayer();
        AudioManager.Instance?.PlaySFX(comboSound);
        PlayState("Combo", true);

        // 휘두르기 시작 프레임에 첫 타격을 맞춘다 (0프레임 피해 방지)
        const float swingLead = 0.17f;
        await UniTask.Delay(System.TimeSpan.FromSeconds(swingLead), cancellationToken: token);

        for (int i = 0; i < comboHitCount; i++)
        {
            FlipToPlayer();
            DealAreaDamage(transform.position, comboRange);

            if (i < comboHitCount - 1)
                await UniTask.Delay(System.TimeSpan.FromSeconds(comboInterval), cancellationToken: token);
        }

        // 클립 잔여 시간만큼 후딜 (타격이 클립 밖으로 밀려나지 않도록)
        float used = swingLead + comboInterval * (comboHitCount - 1);
        await UniTask.Delay(
            System.TimeSpan.FromSeconds(Mathf.Max(0.1f, ClipCombo - used)),
            cancellationToken: token
        );
    }

    // ── 범위 데미지 ──

    void DealAreaDamage(Vector3 center, float radius) =>
        EnemyUtils.DealAreaDamage(center, radius, damage, gameObject, 8f);

    void SlamGroundDamage()
    {
        if (!PlayerRef.Exists)
            return;

        var movement = PlayerRef.Movement;
        if (movement != null && !movement.IsGrounded)
            return;

        PlayerRef.Damageable?.TakeDamage(damage, gameObject);
        if (PlayerRef.Controller != null)
            PlayerRef.Controller.Knockback(Vector2.up * 8f);
    }
}

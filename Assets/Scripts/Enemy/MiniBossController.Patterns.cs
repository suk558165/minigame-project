using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// MiniBossController 의 패턴 연출 부분 (상태/수명주기는 MiniBossController.cs).
public partial class MiniBossController
{
    // MiniBoss_SkeletonKingController 클립 길이 (12fps). 패턴 타이밍을 애니메이션에 맞추는 기준.
    const float ClipGroundWave = 0.667f;  // cast_1~8
    const float ClipLeapSlash = 0.583f;   // attack1_1~7
    const float ClipMultiDash = 0.583f;   // attack2_1~7

    // 도약 베기
    const float LeapAirPose = 0.167f;     // attack1_3: 칼을 뒤로 뺀 프레임 — 급강하 자세
    const float LeapSlashHit = 0.25f;     // attack1_4: 큰 베기 궤적 프레임
    const float LeapRiseHeight = 4f;
    const float LeapRiseTime = 0.4f;
    const float LeapHangTime = 0.1f;

    // 연속 돌진: 찌르기 이펙트(attack2_4)가 나오기 전까지의 준비 동작 길이
    const float DashWindup = 0.25f;

    // ── 패턴 1: 지면 파동 ─────────────────────────────
    // 바닥을 내리쳐 좌우로 파동이 퍼져나감

    async UniTask GroundWave(CancellationToken token)
    {
        PlayState("GroundWave", true); // 시전 앞부분이 예고 역할을 겸한다
        await TellShake();

        rb.linearVelocity = Vector2.zero;

        AudioManager.Instance?.PlaySFX(groundWaveSound);
        if (wavePrefab != null)
        {
            Vector3 origin = transform.position + Vector3.up * waveSpawnOffsetY;

            if (wavePool == null)
                wavePool = new ObjectPool<Projectile>(wavePrefab.GetComponent<Projectile>());

            var left = wavePool.Get(origin, Quaternion.identity);
            left.Pool = wavePool;
            left.Init(Vector2.left, waveSpeed, damage, gameObject);
            left.transform.rotation = Quaternion.identity;
            var leftSr = left.GetComponent<SpriteRenderer>();
            if (leftSr != null)
            {
                leftSr.flipX = false;
                // Projectile.Init이 왼쪽 방향이면 세로 반전을 켜므로, 회전을 되돌린 파동은 다시 끈다.
                leftSr.flipY = false;
            }

            var right = wavePool.Get(origin, Quaternion.identity);
            right.Pool = wavePool;
            right.Init(Vector2.right, waveSpeed, damage, gameObject);
            right.transform.rotation = Quaternion.identity;
            var rightSr = right.GetComponent<SpriteRenderer>();
            if (rightSr != null)
                rightSr.flipX = true;
        }

        // 시전 클립 잔여 재생 (0.3s 고정이라 클립이 잘리거나 마지막 프레임에서 멈췄다)
        await UniTask.Delay(
            System.TimeSpan.FromSeconds(Mathf.Max(0.1f, ClipGroundWave - tellDuration)),
            cancellationToken: token
        );
    }

    // ── 패턴 2: 도약 베기 ─────────────────────────────
    // 플레이어 머리 위로 곡선 도약 → 정점에서 자세 고정 → 급강하 → 착지 베기

    async UniTask LeapSlash(CancellationToken token)
    {
        await TellFlash(Color.yellow);

        FlipToPlayer();
        AudioManager.Instance?.PlaySFX(leapSlashSound);
        PlayState("Jump", true);

        // 곡선으로 플레이어 위까지 이동 (0.2초 상승 후 옆으로 순간이동하던 문제)
        float targetX = player != null ? player.position.x : transform.position.x;
        await EnemyUtils.LeapArc(rb, transform, targetX, LeapRiseHeight, LeapRiseTime, token);

        // 정점: 칼을 뒤로 뺀 자세로 잠깐 멈췄다가 급강하
        FlipToPlayer();
        FreezePose("LeapSlash", LeapAirPose / ClipLeapSlash);
        await UniTask.Delay(System.TimeSpan.FromSeconds(LeapHangTime), cancellationToken: token);

        await EnemyUtils.DiveUntilGrounded(rb, leapFallSpeed, IsGrounded, token);

        // 착지 순간 베기 궤적 프레임부터 재생
        // 착지 충격 — IgnoreLayerCollision으로 트리거가 막히므로 직접 거리 계산
        FlipToPlayer();
        PlayState("LeapSlash", true, LeapSlashHit / ClipLeapSlash);
        DealAreaDamage(transform.position, leapRadius);
        CameraFollow.Instance?.Shake(0.15f, 0.15f);

        await UniTask.Delay(System.TimeSpan.FromSeconds(ClipLeapSlash - LeapSlashHit + 0.1f), cancellationToken: token);
    }

    // ── 패턴 3: 연속 돌진 ─────────────────────────────
    // 준비 동작 → 찌르기 이펙트 구간에 맞춰 이동, dashCount 회 반복, 마지막에 짧은 스턴

    async UniTask MultiDash(CancellationToken token)
    {
        // 예고 깜빡임이 끝나는 순간 첫 찌르기 이펙트가 나오도록, 끝나기 DashWindup 전에 준비 동작을 시작한다.
        var tell = TellFlash(Color.cyan);
        await UniTask.Delay(System.TimeSpan.FromSeconds(Mathf.Max(0f, tellDuration - DashWindup)), cancellationToken: token);
        FlipToPlayer();
        PlayState("MultiDash", true);
        await tell;

        AudioManager.Instance?.PlaySFX(dashSound);
        bool prevRootMotion = animator != null && animator.applyRootMotion;
        if (animator != null)
            animator.applyRootMotion = false;

        for (int i = 0; i < dashCount; i++)
        {
            if (player == null)
                break;

            if (i > 0)
            {
                // 멈춘 동안: 앞부분은 찌르기 후 회수 동작(이후 Idle), 마지막 DashWindup 은 다음 준비 동작
                float windup = Mathf.Min(DashWindup, dashInterval);
                await UniTask.Delay(System.TimeSpan.FromSeconds(dashInterval - windup), cancellationToken: token);
                FlipToPlayer();
                PlayState("MultiDash", true, (DashWindup - windup) / ClipMultiDash);
                await UniTask.Delay(System.TimeSpan.FromSeconds(windup), cancellationToken: token);
            }

            // 준비 동작에서 정한 방향으로 고정한다
            float dir = sr.flipX ? -1f : 1f;
            dashHitThisSegment = false;

            float elapsed = 0f;
            while (elapsed < dashDuration)
            {
                rb.linearVelocity = new Vector2(dir * dashSpeed, rb.linearVelocity.y);

                if (!dashHitThisSegment)
                    DealAreaDamage(transform.position, dashHitRadius);

                elapsed += Time.deltaTime;
                await UniTask.Yield(token);
            }

            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }

        if (animator != null)
            animator.applyRootMotion = prevRootMotion;

        // 스턴
        rb.linearVelocity = Vector2.zero;
        PlayState("Idle");
        baseColor = Color.gray;
        sr.color = baseColor;
        await UniTask.Delay(System.TimeSpan.FromSeconds(0.3f), cancellationToken: token);
        baseColor = originalColor;
        sr.color = baseColor;
    }

    // ── 범위 데미지 ──

    // 넉백은 가로 방향 위주로 살짝 띄운다
    void DealAreaDamage(Vector3 center, float radius)
    {
        if (EnemyUtils.DealAreaDamage(center, radius, damage, gameObject, 7f, 0.3f))
            dashHitThisSegment = true;
    }
}

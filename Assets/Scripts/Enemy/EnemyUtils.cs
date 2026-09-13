using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public static class EnemyUtils
{
    public static async UniTask HitFlash(SpriteRenderer sr, Color originalColor, System.Func<bool> isDead)
    {
        if (sr == null)
            return;
        // 새빨갛게 칠하면 그림이 통째로 사라져 보여 옅은 붉은빛으로 짧게만 번쩍인다
        sr.color = new Color(1f, 0.55f, 0.55f);
        await UniTask.Delay(System.TimeSpan.FromSeconds(0.08f));
        // await 도중 적이 파괴될 수 있다
        if (sr != null && !isDead())
            sr.color = originalColor;
    }

    public static async UniTask DeathBlink(SpriteRenderer sr)
    {
        for (int i = 0; i < 8; i++)
        {
            if (sr == null)
                return;
            sr.enabled = !sr.enabled;
            await UniTask.Delay(System.TimeSpan.FromSeconds(0.15f));
        }
    }

    public static async UniTask TellFlash(SpriteRenderer sr, Color color, Color originalColor, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (sr == null)
                return;
            float t = Mathf.PingPong(elapsed * 10f, 1f);
            sr.color = Color.Lerp(originalColor, color, t);
            elapsed += Time.deltaTime;
            await UniTask.Yield();
        }
        if (sr != null)
            sr.color = originalColor;
    }

    public static async UniTask TellShake(Transform transform, float duration)
    {
        if (transform == null)
            return;
        Vector3 origin = transform.position;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (transform == null)
                return;
            float offsetX = Random.Range(-0.05f, 0.05f);
            transform.position = origin + new Vector3(offsetX, 0f, 0f);
            elapsed += Time.deltaTime;
            await UniTask.Yield();
        }
        if (transform != null)
            transform.position = origin;
    }

    /// <summary>
    /// 곡선 도약: 지금 위치에서 (targetX, 시작 높이 + height) 까지 이동한다.
    /// 가로는 출발·도착 감속, 세로는 정점에 가까울수록 감속.
    /// 벽·천장에 걸리지 않도록 Kinematic 으로 바꿔 두며, DiveUntilGrounded 에서 되돌린다.
    /// </summary>
    public static async UniTask LeapArc(Rigidbody2D rb, Transform transform, float targetX, float height, float duration, CancellationToken token)
    {
        rb.linearVelocity = Vector2.zero;
        rb.bodyType = RigidbodyType2D.Kinematic;
        Vector3 start = transform.position;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            float x = Mathf.Lerp(start.x, targetX, Mathf.SmoothStep(0f, 1f, k));
            float y = start.y + height * (1f - (1f - k) * (1f - k));
            transform.position = new Vector3(x, y, start.z);
            await UniTask.Yield(token);
        }
    }

    /// <summary>
    /// 급강하: Dynamic 으로 되돌리고 착지할 때까지 일정 속도로 떨어진다.
    /// 착지하지 못하는 위치(맵 밖 등)에서 무한 대기하지 않도록 타임아웃을 둔다.
    /// </summary>
    public static async UniTask DiveUntilGrounded(Rigidbody2D rb, float speed, System.Func<bool> isGrounded, CancellationToken token, float timeout = 3f)
    {
        rb.bodyType = RigidbodyType2D.Dynamic;
        float elapsed = 0f;
        while (!isGrounded() && elapsed < timeout)
        {
            rb.linearVelocity = new Vector2(0f, -speed);
            elapsed += Time.deltaTime;
            await UniTask.Yield(token);
        }
        rb.linearVelocity = Vector2.zero;
    }

    /// <summary>
    /// 플레이어가 반경 안이면 피해와 넉백을 준다. 맞았으면 true.
    /// 넉백 방향은 중심→플레이어이고, upBias 가 0보다 크면 가로 방향에 위쪽 성분을 섞는다.
    /// IgnoreLayerCollision 으로 트리거가 막히는 보스 패턴에서 직접 거리로 판정할 때 사용한다.
    /// </summary>
    public static bool DealAreaDamage(Vector3 center, float radius, float damage, GameObject attacker, float knockbackForce, float upBias = 0f)
    {
        if (!PlayerRef.Exists)
            return false;
        Vector2 playerPos = PlayerRef.Transform.position;
        if (Vector2.Distance(center, playerPos) > radius)
            return false;

        PlayerRef.Damageable?.TakeDamage(damage, attacker);
        if (PlayerRef.Controller != null)
        {
            Vector2 dir = (playerPos - (Vector2)center).normalized;
            if (upBias > 0f)
                dir = new Vector2(dir.x, upBias).normalized;
            PlayerRef.Controller.Knockback(dir * knockbackForce);
        }
        return true;
    }

    public static void FlipToPlayer(SpriteRenderer sr, Transform player, Transform self)
    {
        if (player == null)
            return;
        sr.flipX = player.position.x < self.position.x;
    }

    public static bool IsGrounded(Collider2D col, Transform transform, LayerMask groundLayer)
    {
        float footY = col != null ? col.bounds.min.y : transform.position.y;
        Vector2 origin = new Vector2(transform.position.x, footY + 0.1f);
        return Physics2D.Raycast(origin, Vector2.down, 0.5f, groundLayer).collider != null;
    }

    public static float FindFloorY(Vector3 pos, LayerMask groundLayer)
    {
        var hit = Physics2D.Raycast(pos, Vector2.down, 20f, groundLayer);
        return hit.collider != null ? hit.point.y : pos.y;
    }

    public static void SpawnGoldDrops(
        GameObject prefab, Vector3 pos, LayerMask groundLayer,
        int count, int minGold, int maxGold,
        // 각도가 45도에 가까울수록 옆으로 멀리 날아간다. 수직에 가깝게 두어
        // 죽은 자리 근처에 떨어지게 한다. (force 6 기준 수평 0.9칸 이내)
        float minAngle = 75f, float maxAngle = 105f)
    {
        if (prefab == null)
            return;

        Vector3 spawnPos = pos + Vector3.up * 0.3f;
        float floorY = FindFloorY(pos, groundLayer);

        for (int i = 0; i < count; i++)
        {
            var gold = Object.Instantiate(prefab, spawnPos, Quaternion.identity);
            var worldGold = gold.GetComponent<WorldGold>();
            if (worldGold != null)
            {
                worldGold.amount = Random.Range(minGold, maxGold + 1);
                float angle = Random.Range(minAngle, maxAngle) * Mathf.Deg2Rad;
                float force = Random.Range(3f, 6f);
                worldGold.Launch(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * force, floorY);
            }
        }
    }
}

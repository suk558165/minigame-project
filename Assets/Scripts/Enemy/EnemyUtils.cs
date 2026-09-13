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

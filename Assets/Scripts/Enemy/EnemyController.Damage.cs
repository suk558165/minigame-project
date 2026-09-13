using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// EnemyController 의 피격·넉백·사망·드롭 부분 (필드·수명주기·Update 는 EnemyController.cs).
public partial class EnemyController
{
    public void TakeDamage(float amount, GameObject attacker = null)
    {
        if (isDead)
            return;

        hp -= amount;
        healthBar?.SetHealth(hp, maxHp);
        DamagePopup.Spawn(new Vector3(transform.position.x, FeetY + 0.5f, transform.position.z), amount);
        HitFlash(_cts.Token).Forget();

        if (hp <= 0f)
        {
            // 즉시 히트박스 비활성화하여 죽는 순간 데미지 방지
            if (meleeHitbox != null)
                meleeHitbox.enabled = false;
            Die();
            return;
        }

        DisableHitbox();

        // 맞으면 공격이 끊기고 잠깐 경직. 쌓여 있던 공격 트리거가 경직 뒤에 뒤늦게 나가지 않게 지운다.
        animator.ResetTrigger(HashAttack);
        animator.SetTrigger(HashIsHit);
        hitStunTimer = HitStunDuration;
        attackGrace = 0f;
        alertTimer = 0f;
        lungeTimer = 0f;
        spawnDelayTimer = 0f;
        // 등 뒤에서 맞았는데 순찰을 계속하면 어색하다 — 바로 추격 상태로.
        aggroTimer = AggroMemory;
        if (isFlying)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);

        if (player != null)
            Knockback((transform.position - player.position).normalized, _cts.Token).Forget();
    }

    async UniTask HitFlash(CancellationToken token)
    {
        sr.color = Color.red;
        await UniTask.Delay(System.TimeSpan.FromSeconds(0.15f), cancellationToken: token);
        sr.color = Color.white;
    }

    async UniTaskVoid Knockback(Vector2 dir, CancellationToken token)
    {
        float elapsed = 0f;
        rb.bodyType = RigidbodyType2D.Dynamic;
        while (elapsed < knockbackDuration)
        {
            rb.linearVelocity = new Vector2(dir.x * knockbackForce, rb.linearVelocity.y);
            elapsed += Time.deltaTime;
            await UniTask.Yield(token);
        }
        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
    }

    public System.Action onDeath;

    void Die()
    {
        isDead = true;
        AudioManager.Instance?.PlaySFX(deathSound);
        healthBar?.SetHealth(0, maxHp);
        var token = RefreshToken();
        sr.color = Color.white;
        var hitbox = meleeHitbox != null ? meleeHitbox.GetComponent<MeleeHitbox>() : null;
        if (hitbox != null)
            hitbox.ForceDeactivate();
        else if (meleeHitbox != null)
            meleeHitbox.enabled = false;
        rb.linearVelocity = Vector2.zero;
        rb.bodyType = RigidbodyType2D.Kinematic;
        if (col != null)
            col.enabled = false;
        onDeath?.Invoke();
        onDeath = null;
        RunStats.Instance?.AddKill();
        SpawnDrops();
        animator.ResetTrigger(HashIsHit);
        animator.ResetTrigger(HashAttack);
        animator.SetBool(HashIsDead, true);
        DeathRoutine(token).Forget();
    }

    void SpawnDrops()
    {
        EnemyUtils.SpawnGoldDrops(goldDropPrefab, transform.position, groundLayer, 1, goldDropMin, goldDropMax);

        if (potionDropPrefab != null && Random.value < potionDropChance + MetaUpgrades.PotionDropBonus)
        {
            float floorY = EnemyUtils.FindFloorY(transform.position, groundLayer);
            Vector3 pos = transform.position + Vector3.up * 0.3f;
            var potion = Instantiate(potionDropPrefab, pos, Quaternion.identity);
            var worldPotion = potion.GetComponent<WorldPotion>();
            if (worldPotion != null)
            {
                worldPotion.healAmount = potionHealAmount;
                float angle = Random.Range(70f, 110f) * Mathf.Deg2Rad;
                float force = Random.Range(3f, 5f);
                worldPotion.Launch(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * force, floorY);
            }
        }
    }

    async UniTaskVoid DeathRoutine(CancellationToken token)
    {
        await UniTask.Yield(token);
        await UniTask.Yield(token);

        while (animator.IsInTransition(0))
            await UniTask.Yield(token);

        float elapsed = 0f;
        while (elapsed < 5f)
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            if (info.IsName("Death") && info.normalizedTime >= 1f)
                break;
            elapsed += Time.deltaTime;
            await UniTask.Yield(token);
        }

        Destroy(gameObject);
    }
}

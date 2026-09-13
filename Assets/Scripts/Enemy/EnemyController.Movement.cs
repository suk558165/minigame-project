using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// EnemyController 의 순찰·이동·지형 감지 부분 (필드·수명주기·Update 는 EnemyController.cs).
public partial class EnemyController
{
    void Patrol()
    {
        if (isFlying)
        {
            FlyPatrol();
            return;
        }

        // 발 아래에 바닥이 없으면 즉시 수평 이동 중지 (안전망)
        if (!IsGrounded())
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        float distFromOrigin = transform.position.x - patrolOrigin.x;

        if (distFromOrigin >= patrolDistance)
            patrolDir = -1;
        else if (distFromOrigin <= -patrolDistance)
            patrolDir = 1;

        // 가는 방향이 다른 적에게 막혀 있으면 반대편이 비었을 때만 돌아선다 (양쪽 다 막히면 매 프레임 뒤집히므로).
        CheckAllies(out bool leftTaken, out bool rightTaken, out _);
        if (patrolDir < 0 ? leftTaken && !rightTaken : rightTaken && !leftTaken)
            patrolDir = -patrolDir;

        if (IsEdgeAhead(patrolDir))
        {
            // 양쪽 다 낭떠러지면 돌아설 곳이 없다. 뒤집으면 매 프레임 방향이 반전돼 제자리에서 떨린다.
            if (IsEdgeAhead(-patrolDir))
            {
                Move(0f);
                return;
            }
            patrolDir = -patrolDir;
            Move(patrolDir); // 멈추지 않고 즉시 반대로 이동
            return;
        }

        Move(patrolDir);
    }

    /// <summary>순찰 중심 위를 좌우로 오가며 살짝 위아래로 떠다닌다.</summary>
    void FlyPatrol()
    {
        float distFromOrigin = transform.position.x - patrolOrigin.x;
        if (distFromOrigin >= patrolDistance)
            patrolDir = -1;
        else if (distFromOrigin <= -patrolDistance)
            patrolDir = 1;

        // 반대편도 막혀 있으면 뒤집지 않는다 — 좁은 통로에 끼었을 때 매 프레임 반전되어 떨리므로.
        if (IsWallAhead(patrolDir) && !IsWallAhead(-patrolDir))
            patrolDir = -patrolDir;

        float hoverY = patrolOrigin.y + FlyHoverHeight + Mathf.Sin(Time.time * 2f) * FlyBobAmplitude;
        float vy = Mathf.Clamp(hoverY - transform.position.y, -1f, 1f);
        Fly(new Vector2(patrolDir, vy), moveSpeed);
    }

    bool IsWallAhead(float dir)
    {
        if (col == null)
            return false;
        Bounds b = col.bounds;
        return Physics2D
                .Raycast(b.center, Vector2.right * dir, b.extents.x + 0.3f, groundLayer)
                .collider != null;
    }

    bool IsGrounded()
    {
        float footY = col != null ? col.bounds.min.y : transform.position.y;
        Vector2 origin = new Vector2(transform.position.x, footY + 0.05f);
        return Physics2D.Raycast(origin, Vector2.down, 0.2f, groundLayer | platformLayer).collider
            != null;
    }

    bool IsEdgeAhead(float dir)
    {
        if (dir == 0f)
            return false;
        float xOffset = (col != null ? col.bounds.extents.x : 0.3f) + 0.3f;
        float footY = col != null ? col.bounds.min.y : transform.position.y;
        // 레이를 발보다 0.3 위에서 시작 → 콜라이더 내부에서 시작하는 오작동 방지
        const float rayStartOffset = 0.3f;
        Vector2 origin = new Vector2(transform.position.x + dir * xOffset, footY + rayStartOffset);
        return Physics2D
                .Raycast(
                    origin,
                    Vector2.down,
                    edgeCheckDepth + rayStartOffset,
                    groundLayer | platformLayer
                )
                .collider == null;
    }

    const float SeparationSpeed = 1.5f;
    const float SeparationMargin = 0.15f;

    /// <summary>
    /// 같은 층의 다른 적과의 간격 검사.
    /// left/rightTaken: 그 방향으로 걸어가면 겹치는지 (여유 간격 포함 — 경계에서 걷기/멈춤이 떨리지 않게).
    /// push: 이미 겹쳐 있을 때 밀려나야 할 방향과 세기(-1~1).
    /// </summary>
    void CheckAllies(out bool leftTaken, out bool rightTaken, out float push)
    {
        leftTaken = rightTaken = false;
        push = 0f;
        if (col == null)
            return;

        Bounds me = col.bounds;
        foreach (var other in Instances)
        {
            if (other == this || other.isDead || other.col == null)
                continue;
            Bounds ob = other.col.bounds;
            if (Mathf.Abs(me.min.y - ob.min.y) > 0.5f)
                continue; // 다른 층

            float dx = me.center.x - ob.center.x;
            float minDist = me.extents.x + ob.extents.x;
            if (Mathf.Abs(dx) >= minDist + SeparationMargin)
                continue;

            // 완전히 같은 위치면 인스턴스 ID로 방향을 갈라 서로 반대로 밀리게 한다.
            float away = Mathf.Abs(dx) > 0.001f
                ? Mathf.Sign(dx)
                : (GetInstanceID() > other.GetInstanceID() ? 1f : -1f);
            if (away > 0f)
                leftTaken = true;
            else
                rightTaken = true;
            // 겹친 깊이로 가중한다. 방향만 더하면 양옆에 끼인 적은 밀림이 상쇄되어 멈춘다.
            if (Mathf.Abs(dx) < minDist)
                push += away * (minDist - Mathf.Abs(dx));
        }
        push = Mathf.Clamp(push, -1f, 1f);
    }

    void Move(float dir) => Move(dir, moveSpeed);

    void Move(float dir, float speed)
    {
        CheckAllies(out bool leftTaken, out bool rightTaken, out float push);
        float vx = (dir < 0f && leftTaken) || (dir > 0f && rightTaken) ? 0f : dir * speed;
        if (push != 0f && !IsEdgeAhead(push))
            vx += push * SeparationSpeed;

        rb.linearVelocity = new Vector2(vx, rb.linearVelocity.y);
        animator.SetFloat(HashSpeed, vx != 0f ? 1f : 0f);

        if (dir > 0f)
            SetFacing(true);
        else if (dir < 0f)
            SetFacing(false);
    }

    void Fly(Vector2 dir, float speed)
    {
        CheckAllies(out _, out _, out float push);
        Vector2 target = dir * speed + Vector2.right * (push * SeparationSpeed);
        rb.linearVelocity = Vector2.MoveTowards(
            rb.linearVelocity,
            target,
            FlyAcceleration * Time.deltaTime
        );
        animator.SetFloat(HashSpeed, target.sqrMagnitude > 0.01f ? 1f : 0f);

        if (dir.x > 0.1f)
            SetFacing(true);
        else if (dir.x < -0.1f)
            SetFacing(false);
    }
}

using UnityEngine;

/// <summary>
/// 몬스터·상자에서 튀어나와 떨어진 뒤 바닥에 놓이는 드롭 아이템(금화·물약)의 공통 낙하 처리.
/// 줍기·자석 동작은 아이템마다 달라 각 클래스에 둔다.
/// </summary>
public abstract class WorldDrop : MonoBehaviour
{
    const float Gravity = 20f;
    const float GroundProbeDistance = 20f;

    // 바닥 없는 구덩이로 떨어진 경우 무한히 떨어지지 않도록, 던져진 높이에서 이만큼 내려가면 없앤다.
    const float MaxFallBelowLaunch = 30f;

    private Vector2 velocity;
    private float launchY;
    protected bool launched;
    protected bool grounded;

    public void Launch(Vector2 force)
    {
        velocity = force;
        launchY = transform.position.y;
        launched = true;
    }

    /// <summary>던져진 동안 낙하시키고, 착지했거나 놓여 있으면 바닥에 붙인다. 매 프레임 호출.</summary>
    protected void UpdateFall()
    {
        if (launched)
        {
            velocity.y -= Gravity * Time.deltaTime;
            transform.position += (Vector3)velocity * Time.deltaTime;

            // 착지는 지금 있는 자리 아래에 실제 바닥이 있을 때만 한다.
            // 던져진 지점의 바닥 높이를 기준으로 삼으면, 발판 끝에서 옆으로 떨어진 경우
            // 원래 발판 높이에서 공중에 멈춘다 (아래가 구덩이면 영영 떠 있다).
            if (velocity.y < 0f)
            {
                if (TryFindGroundY(out float ground))
                {
                    if (transform.position.y <= ground)
                    {
                        transform.position = new Vector3(transform.position.x, ground, transform.position.z);
                        launched = false;
                        grounded = true;
                    }
                }
                else if (transform.position.y < launchY - MaxFallBelowLaunch)
                {
                    Destroy(gameObject);
                    return;
                }
            }
        }

        if (!grounded && !launched)
            SnapToGround();
    }

    protected void SnapToGround()
    {
        if (TryFindGroundY(out float y) && y < transform.position.y)
            transform.position = new Vector3(transform.position.x, y, transform.position.z);
        grounded = true;
    }

    bool TryFindGroundY(out float y)
    {
        var hit = Physics2D.Raycast(
            transform.position,
            Vector2.down,
            GroundProbeDistance,
            LayerMask.GetMask("Ground", "Platform")
        );
        y = hit.collider != null ? hit.point.y : 0f;
        return hit.collider != null;
    }
}

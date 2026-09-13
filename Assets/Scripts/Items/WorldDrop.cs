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

    // 루트 위치에서 그림 밑까지의 거리. 스프라이트 기준점이 가운데라 루트를 바닥에 두면
    // 그림 아래 절반이 바닥에 파묻히므로, 이만큼 띄워서 놓는다.
    private float restOffset;

    protected virtual void Awake()
    {
        var sr = GetComponentInChildren<SpriteRenderer>(true);
        if (sr != null && sr.sprite != null)
        {
            Bounds local = sr.sprite.bounds;
            float bottom = sr.transform.TransformPoint(new Vector3(local.center.x, local.min.y, 0f)).y;
            restOffset = Mathf.Max(0f, transform.position.y - bottom);
        }
    }

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
            float prevY = transform.position.y;
            velocity.y -= Gravity * Time.deltaTime;
            transform.position += (Vector3)velocity * Time.deltaTime;

            // 착지는 지금 있는 자리 아래에 실제 바닥이 있을 때만 한다.
            // 던져진 지점의 바닥 높이를 기준으로 삼으면, 발판 끝에서 옆으로 떨어진 경우
            // 원래 발판 높이에서 공중에 멈춘다 (아래가 구덩이면 영영 떠 있다).
            // 바닥은 이전 프레임 높이에서 찾는다. 한 프레임에 바닥 속으로 들어간 뒤 그 자리에서 찾으면
            // 레이가 바닥 안에서 시작해 파묻힌 위치를 바닥으로 인식한다.
            if (velocity.y < 0f)
            {
                if (TryFindGroundY(prevY, out float ground))
                {
                    if (transform.position.y - restOffset <= ground)
                    {
                        PlaceOnGround(ground);
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
        // 루트가 이미 바닥 속으로 들어가 있을 수 있으므로 그림 높이만큼 위에서 바닥을 찾는다.
        if (TryFindGroundY(transform.position.y + restOffset, out float y))
            PlaceOnGround(y);
        grounded = true;
    }

    void PlaceOnGround(float groundY) =>
        transform.position = new Vector3(transform.position.x, groundY + restOffset, transform.position.z);

    bool TryFindGroundY(float originY, out float y)
    {
        var hit = Physics2D.Raycast(
            new Vector2(transform.position.x, originY),
            Vector2.down,
            GroundProbeDistance,
            LayerMask.GetMask("Ground", "Platform")
        );
        y = hit.collider != null ? hit.point.y : 0f;
        return hit.collider != null;
    }
}

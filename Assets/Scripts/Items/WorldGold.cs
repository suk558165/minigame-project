using System.Collections.Generic;
using UnityEngine;

public class WorldGold : MonoBehaviour
{
    public static readonly List<WorldGold> Instances = new List<WorldGold>();

    // 도메인 리로드를 끈 상태에서도 이전 플레이의 잔여 항목이 남지 않도록 초기화
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instances.Clear();

    public int amount = 5;
    public float magnetRadius = 3f;
    public float pickupRadius = 0.8f;
    public AudioClip pickupSound;

    [Tooltip("자석 최고 속도. 플레이어 대쉬 속도보다 커야 따라잡는다")]
    public float magnetSpeed = 22f;

    [Tooltip("자석 가속도. 0에서 최고 속도까지 붙는 빠르기")]
    public float magnetAcceleration = 60f;

    private Transform player;
    private Inventory inventory;

    private Vector2 velocity;
    private float gravity = 20f;
    private float groundY;
    private bool launched;
    private bool grounded;
    private bool magnetized;
    private float magnetVelocity;

    public void Launch(Vector2 force, float floorY)
    {
        velocity = force;
        groundY = floorY;
        launched = true;
    }

    void OnEnable() => Instances.Add(this);

    void OnDisable() => Instances.Remove(this);

    void Start()
    {
        if (!launched)
            SnapToGround();
    }

    void Update()
    {
        FindPlayer();

        if (launched)
        {
            velocity.y -= gravity * Time.deltaTime;
            transform.position += (Vector3)velocity * Time.deltaTime;

            // 착지 높이는 던져진 지점이 아니라 지금 동전이 있는 자리의 지면으로 판정한다.
            // 스폰 시점의 floorY 로 판정하면, 옆으로 날아가 지형이 달라진 곳에서
            // 원래 지면 높이에 그대로 멈춰 공중에 떠 있게 된다.
            if (velocity.y < 0f)
            {
                float ground = FindGroundY();
                if (transform.position.y <= ground)
                {
                    transform.position = new Vector3(
                        transform.position.x,
                        ground,
                        transform.position.z
                    );
                    launched = false;
                    grounded = true;
                }
            }
        }

        if (!grounded && !launched)
            SnapToGround();

        if (player == null || inventory == null)
            return;

        float dist = Vector2.Distance(transform.position, player.position);

        // 한 번 범위에 들어오면 계속 따라간다 (플레이어가 멀어져도 풀리지 않음)
        if (!magnetized && dist <= magnetRadius)
            magnetized = true;

        if (magnetized)
        {
            // 자석에 걸리면 낙하 물리를 멈추고 공중에서도 끌려온다
            launched = false;
            magnetVelocity = Mathf.MoveTowards(
                magnetVelocity,
                magnetSpeed,
                magnetAcceleration * Time.deltaTime
            );
            transform.position = Vector3.MoveTowards(
                transform.position,
                player.position,
                magnetVelocity * Time.deltaTime
            );
            dist = Vector2.Distance(transform.position, player.position);
        }

        if (dist <= pickupRadius)
        {
            AudioManager.Instance?.PlaySFX(pickupSound);
            float goldDrop = inventory.GetTotalStatBonus().goldDrop;
            inventory.AddGold(Mathf.RoundToInt(amount * (1f + goldDrop)));
            Destroy(gameObject);
            return;
        }
    }

    void SnapToGround()
    {
        float y = FindGroundY();
        if (y < transform.position.y)
            transform.position = new Vector3(transform.position.x, y, transform.position.z);
        grounded = true;
    }

    float FindGroundY()
    {
        var hit = Physics2D.Raycast(
            transform.position,
            Vector2.down,
            20f,
            LayerMask.GetMask("Ground", "Platform")
        );
        if (hit.collider != null)
            return hit.point.y;
        return groundY;
    }

    void FindPlayer()
    {
        if (player != null)
            return;
        if (!PlayerRef.Exists)
            return;
        player = PlayerRef.Transform;
        inventory = PlayerRef.Inventory;
    }
}

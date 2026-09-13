using System.Collections.Generic;
using UnityEngine;

public class WorldGold : WorldDrop
{
    public static readonly List<WorldGold> Instances = new List<WorldGold>();

    // 도메인 리로드를 끈 상태에서도 이전 플레이의 잔여 항목이 남지 않도록 초기화
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instances.Clear();

    public int amount = 5;

    [SerializeField]
    private float magnetRadius = 3f;

    [SerializeField]
    private float pickupRadius = 0.8f;

    [SerializeField]
    private AudioClip pickupSound;

    [Tooltip("자석 최고 속도. 플레이어 대쉬 속도보다 커야 따라잡는다")]
    [SerializeField]
    private float magnetSpeed = 22f;

    [Tooltip("자석 가속도. 0에서 최고 속도까지 붙는 빠르기")]
    [SerializeField]
    private float magnetAcceleration = 60f;

    private Transform player;
    private Inventory inventory;

    private bool magnetized;
    private float magnetVelocity;

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
        UpdateFall();

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
        }
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

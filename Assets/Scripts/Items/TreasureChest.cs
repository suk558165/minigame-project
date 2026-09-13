using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class TreasureChest : MonoBehaviour
{
    public static readonly List<TreasureChest> Instances = new List<TreasureChest>();

    // 도메인 리로드를 끈 상태에서도 이전 플레이의 잔여 항목이 남지 않도록 초기화
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instances.Clear();

    [Header("Reward")]
    public int goldMin = 20;
    public int goldMax = 40;
    public GameObject goldDropPrefab;

    [Header("Launch")]
    public int coinCount = 5;
    public float launchForceMin = 3f;
    public float launchForceMax = 6f;
    public float launchAngleMin = 60f;
    public float launchAngleMax = 120f;
    public float launchDuration = 0.5f;

    [Header("Interaction")]
    public float interactRange = 1.5f;

    [Header("Audio")]
    public AudioClip openSound;

    [Header("UI")]
    public GameObject hintObject;

    [Header("Animation")]
    // Animator가 없을 때 사용되는 코드 애니메이션 총 재생 시간
    public float builtinAnimDuration = 0.8f;

    // Animator가 있을 때 Open 트리거 후 대기 시간 (애니메이션 클립 길이에 맞게 설정)
    public float animatorOpenDuration = 0.5f;

    private bool opened;
    private Transform player;
    private Animator animator;
    private Vector3 originScale;
    private Vector3 originPos;

    void OnEnable() => Instances.Add(this);

    void OnDisable() => Instances.Remove(this);

    /// <summary>
    /// 열지 않은 채 방을 떠날 때 호출한다.
    /// 동전을 뿌려봐야 플레이어가 이미 다음 방이라 주울 수 없으므로 보상을 바로 지급한다.
    /// </summary>
    public void ClaimAndDestroy()
    {
        if (!opened)
        {
            opened = true;
            var inventory = PlayerRef.Inventory;
            if (inventory != null)
            {
                int gold = Random.Range(goldMin, goldMax + 1);
                float bonus = inventory.GetTotalStatBonus().goldDrop;
                inventory.AddGold(Mathf.RoundToInt(gold * (1f + bonus)));
                AudioManager.Instance?.PlaySFX(openSound);
            }
        }
        Destroy(gameObject);
    }

    void Start()
    {
        player = PlayerRef.Transform;

        animator = GetComponent<Animator>();
        originScale = transform.localScale;
        originPos = transform.position;

        if (hintObject != null)
            hintObject.SetActive(false);

        // 플레이어와 물리 충돌 무시
        if (PlayerRef.Exists)
        {
            var playerCol = PlayerRef.GameObject.GetComponent<Collider2D>();
            if (playerCol != null)
            {
                foreach (var col in GetComponents<Collider2D>())
                    Physics2D.IgnoreCollision(col, playerCol, true);
            }
        }
    }

    void Update()
    {
        if (opened || player == null)
            return;

        bool inRange = Vector2.Distance(transform.position, player.position) <= interactRange;

        if (hintObject != null)
            hintObject.SetActive(inRange);

        var interactKey = InputManager.Instance?.Interact ?? KeyCode.A;
        if (inRange && Input.GetKeyDown(interactKey))
            Open();
    }

    void Open()
    {
        opened = true;

        if (hintObject != null)
            hintObject.SetActive(false);

        AudioManager.Instance?.PlaySFX(openSound);
        SpawnGoldCoins();

        if (animator != null)
            AnimatorOpenRoutine().Forget();
        else
            BuiltinOpenRoutine().Forget();
    }

    // Animator 보유 시: Open 트리거 → 클립 재생 대기 → 파괴
    async UniTaskVoid AnimatorOpenRoutine()
    {
        animator.SetTrigger("Open");
        await UniTask.Delay(System.TimeSpan.FromSeconds(animatorOpenDuration), cancellationToken: this.GetCancellationTokenOnDestroy());
        Destroy(gameObject);
    }

    // Animator 없을 때: 바운스 → 셰이크 → 축소 소멸
    async UniTaskVoid BuiltinOpenRoutine()
    {
        // 연출 도중 방이 전환되면 상자가 파괴된다. 토큰 없이 돌리면
        // 파괴된 transform 에 접근해 MissingReferenceException 이 난다.
        var token = this.GetCancellationTokenOnDestroy();

        // 1. 위로 튀어오르기
        await MoveLocal(originPos, originPos + Vector3.up * 0.35f, 0.12f, token);
        await MoveLocal(transform.position, originPos, 0.08f, token);

        // 2. 찌그러짐 (squash & stretch)
        await ScaleTo(
            new Vector3(originScale.x * 1.35f, originScale.y * 0.65f, originScale.z),
            0.07f,
            token
        );
        await ScaleTo(
            new Vector3(originScale.x * 0.75f, originScale.y * 1.35f, originScale.z),
            0.07f,
            token
        );
        await ScaleTo(originScale, 0.06f, token);

        // 3. 셰이크
        float shakeTime = 0.25f;
        float elapsed = 0f;
        float intensity = 0.08f;
        while (elapsed < shakeTime)
        {
            float progress = elapsed / shakeTime;
            float offset = Mathf.Lerp(intensity, 0f, progress);
            transform.position = originPos + (Vector3)(Random.insideUnitCircle * offset);
            elapsed += Time.deltaTime;
            await UniTask.Yield(token);
        }
        transform.position = originPos;

        // 4. 축소되며 소멸
        await ScaleTo(Vector3.zero, 0.2f, token);

        Destroy(gameObject);
    }

    async UniTask MoveLocal(Vector3 from, Vector3 to, float duration, CancellationToken token)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(from, to, elapsed / duration);
            await UniTask.Yield(token);
        }
        transform.position = to;
    }

    void SpawnGoldCoins()
    {
        if (goldDropPrefab == null)
            return;

        int totalGold = Random.Range(goldMin, goldMax + 1);
        int perCoin = Mathf.Max(1, totalGold / coinCount);
        int remainder = totalGold - perCoin * coinCount;

        float floorY = transform.position.y;
        Vector3 spawnPos = transform.position + Vector3.up * 0.3f;

        for (int i = 0; i < coinCount; i++)
        {
            var go = Instantiate(goldDropPrefab, spawnPos, Quaternion.identity);
            var wg = go.GetComponent<WorldGold>();
            if (wg == null)
                continue;

            wg.amount = perCoin + (i == 0 ? remainder : 0);

            float angle = Random.Range(launchAngleMin, launchAngleMax);
            float force = Random.Range(launchForceMin, launchForceMax);
            float rad = angle * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

            wg.Launch(dir * force, floorY);
        }
    }

    async UniTask ScaleTo(Vector3 target, float duration, CancellationToken token)
    {
        Vector3 start = transform.localScale;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.localScale = Vector3.Lerp(start, target, elapsed / duration);
            await UniTask.Yield(token);
        }
        transform.localScale = target;
    }
}

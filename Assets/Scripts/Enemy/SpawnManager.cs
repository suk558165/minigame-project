using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class SpawnManager : MonoBehaviour
{
    [System.Serializable]
    public class SpawnGroup
    {
        public GameObject prefab;

        [Tooltip("이 그룹이 사용할 스폰포인트 (spawnPoints 배열의 인덱스)")]
        public int spawnPointIndex;

        [Tooltip("이 포인트에서 스폰할 마릿수")]
        public int count = 1;

        [Tooltip("여러 마리일 때 스폰 간격")]
        public float interval = 0.2f;
    }

    [System.Serializable]
    public class Wave
    {
        public List<SpawnGroup> groups = new List<SpawnGroup>();

        [Tooltip("이 웨이브 스폰 전 대기 시간")]
        public float delayBeforeSpawn = 0f;

        [Tooltip("true이면 그룹 내 interval 무시하고 모든 적 동시 스폰")]
        public bool instantSpawn = false;

        [Tooltip(
            "비어있으면 전멸 시 바로 다음 웨이브. 지정하면 전멸 후 이 존에 진입해야 다음 웨이브"
        )]
        public Collider2D triggerZone;
    }

    [Header("스폰 포인트 (좌끝, 좌중간, 중앙, 우중간, 우끝 등)")]
    [SerializeField]
    private Transform[] spawnPoints;

    [Header("스폰 이펙트")]
    [SerializeField]
    private GameObject spawnEffectPrefab;

    [Tooltip("이펙트 재생 후 몬스터가 나타나기까지 대기 시간")]
    [SerializeField]
    private float spawnEffectDelay = 0f;

    [Tooltip("이펙트 생성 Y 오프셋 (음수 = 아래)")]
    [SerializeField]
    private float spawnEffectYOffset = -0.5f;

    [Header("웨이브 설정")]
    [SerializeField]
    private List<Wave> waves = new List<Wave>();

    [Header("BGM")]
    [SerializeField]
    private AudioClip bossBGM;

    [Tooltip("보스 처치 후 재생할 BGM. 비워두면 음악 정지.")]
    [SerializeField]
    private AudioClip afterBossBGM;

    public System.Action onAllEnemiesDead;

    private int currentWaveIndex;
    private int aliveCount;
    private int pendingSpawnCount;
    private bool waitingForTrigger;
    private bool allWavesCleared;
    private readonly List<GameObject> spawnedEnemies = new List<GameObject>();

    private CancellationTokenSource _cts = new();

    CancellationToken RefreshToken()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        return _cts.Token;
    }

    void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    void Start()
    {
        currentWaveIndex = 0;
        aliveCount = 0;
        waitingForTrigger = false;

        foreach (var wave in waves)
        {
            if (wave.triggerZone != null)
            {
                // 같은 GameObject의 모든 Collider2D를 트리거로 강제 — 플레이어가 충돌로 막히지 않게.
                foreach (var col in wave.triggerZone.GetComponents<Collider2D>())
                    col.isTrigger = true;

                // 트리거 이벤트는 콜라이더가 붙은 GameObject에서만 발생하므로 포워더 부착.
                if (wave.triggerZone.GetComponent<WaveTriggerForwarder>() == null)
                {
                    var fwd = wave.triggerZone.gameObject.AddComponent<WaveTriggerForwarder>();
                    fwd.target = this;
                }

                wave.triggerZone.gameObject.SetActive(false);
            }
        }

        if (bossBGM != null)
            AudioManager.Instance?.PlayBGM(bossBGM);

        if (waves.Count == 0)
            return;

        var bossIntro = GetComponentInChildren<BossIntro>();
        if (bossIntro != null)
        {
            Wave firstWave = waves[0];
            // 카메라가 보스 위치에 도착하면 보스를 스폰 — 연출 후 입력 잠금 해제.
            bossIntro.Play(onSpawn: () => SpawnWave(firstWave, _cts.Token).Forget(), onComplete: null);
        }
        else
        {
            SpawnWave(waves[0], _cts.Token).Forget();
        }
    }

    async UniTask SpawnWave(Wave wave, CancellationToken token)
    {
        if (wave.delayBeforeSpawn > 0f)
            await UniTask.Delay(System.TimeSpan.FromSeconds(wave.delayBeforeSpawn), cancellationToken: token);

        // 이 웨이브가 스폰할 총 적 수를 미리 더해서, 스폰이 다 끝나기 전엔 클리어 판정이 안 나도록.
        int totalPending = 0;
        foreach (var group in wave.groups)
        {
            if (
                group.prefab != null
                && group.spawnPointIndex >= 0
                && group.spawnPointIndex < spawnPoints.Length
                && spawnPoints[group.spawnPointIndex] != null
            )
            {
                totalPending += group.count;
            }
        }
        pendingSpawnCount += totalPending;

        foreach (var group in wave.groups)
            SpawnGroupItems(group, wave.instantSpawn, token).Forget();
    }

    async UniTaskVoid SpawnGroupItems(SpawnGroup group, bool instant, CancellationToken token)
    {
        if (group.prefab == null)
            return;
        if (group.spawnPointIndex < 0 || group.spawnPointIndex >= spawnPoints.Length)
            return;

        Transform point = spawnPoints[group.spawnPointIndex];
        if (point == null)
            return;

        for (int i = 0; i < group.count; i++)
        {
            if (allWavesCleared)
            {
                pendingSpawnCount = Mathf.Max(0, pendingSpawnCount - (group.count - i));
                return;
            }
            await SpawnEnemyWithEffect(group.prefab, point.position, token);
            if (!instant && i < group.count - 1 && group.interval > 0f)
                await UniTask.Delay(System.TimeSpan.FromSeconds(group.interval), cancellationToken: token);
        }
    }

    const float SpawnSpacing = 1.5f;

    // 이펙트 대기 중인 자리. 같은 포인트의 그룹이 동시에 시작해도 같은 자리를 고르지 않게 한다.
    private readonly List<Vector3> reservedSpawnPositions = new List<Vector3>();

    /// <summary>
    /// 스폰포인트 주변에서 살아있는 적·예약된 자리와 겹치지 않는 곳을 고른다.
    /// 0, +1, -1, +2, -2 칸 순서로 찾고, 옆 칸은 지형 안이거나 발밑이 비어 있으면 건너뛴다.
    /// </summary>
    Vector3 PickSpawnPosition(Vector3 point)
    {
        int solidMask = LayerMask.GetMask("Ground");
        int floorMask = LayerMask.GetMask("Ground", "Platform");
        for (int i = 0; i < 9; i++)
        {
            int step = (i + 1) / 2 * (i % 2 == 1 ? 1 : -1);
            Vector3 candidate = point + Vector3.right * (step * SpawnSpacing);
            if (IsSpawnOccupied(candidate))
                continue;
            if (
                i > 0
                && (
                    Physics2D.OverlapPoint(candidate, solidMask) != null
                    || Physics2D.Raycast(candidate, Vector2.down, 3f, floorMask).collider == null
                )
            )
                continue;
            return candidate;
        }
        return point;
    }

    bool IsSpawnOccupied(Vector3 pos)
    {
        float minGap = SpawnSpacing * 0.9f;
        foreach (var r in reservedSpawnPositions)
            if (Mathf.Abs(r.x - pos.x) < minGap && Mathf.Abs(r.y - pos.y) < 1f)
                return true;
        foreach (var e in EnemyController.Instances)
        {
            Vector3 p = e.transform.position;
            if (!e.IsDead && Mathf.Abs(p.x - pos.x) < minGap && Mathf.Abs(p.y - pos.y) < 1f)
                return true;
        }
        return false;
    }

    async UniTask SpawnEnemyWithEffect(GameObject prefab, Vector3 position, CancellationToken token)
    {
        position = PickSpawnPosition(position);
        reservedSpawnPositions.Add(position);
        try
        {
            if (spawnEffectPrefab != null)
            {
                var fxPos = position + new Vector3(0f, spawnEffectYOffset, 0f);
                var fx = Instantiate(spawnEffectPrefab, fxPos, Quaternion.identity);
                Destroy(fx, 0.8f);
                if (spawnEffectDelay > 0f)
                    await UniTask.Delay(System.TimeSpan.FromSeconds(spawnEffectDelay), cancellationToken: token);
            }
        }
        finally
        {
            reservedSpawnPositions.Remove(position);
        }

        var go = Instantiate(prefab, position, Quaternion.identity);
        go.transform.SetParent(transform.root, worldPositionStays: true);
        spawnedEnemies.Add(go);
        aliveCount++;
        pendingSpawnCount = Mathf.Max(0, pendingSpawnCount - 1);

        var enemy = go.GetComponent<EnemyController>();
        if (enemy != null)
        {
            enemy.onDeath += OnEnemyDied;
            return;
        }

        var boss = go.GetComponent<BossController>();
        if (boss != null)
        {
            boss.onDeath += OnEnemyDied;
            return;
        }

        var miniBoss = go.GetComponent<MiniBossController>();
        if (miniBoss != null)
        {
            miniBoss.onDeath += OnEnemyDied;
            return;
        }

        // 알 수 없는 컴포넌트 — onDeath 연결 불가, aliveCount 즉시 보정
        aliveCount = Mathf.Max(0, aliveCount - 1);
    }

    public void CleanupAll()
    {
        RefreshToken();
        foreach (var go in spawnedEnemies)
            if (go != null)
                Destroy(go);
        spawnedEnemies.Clear();
    }

    void OnEnemyDied()
    {
        aliveCount--;

        if (aliveCount > 0)
            return;

        // 이번 웨이브가 아직 스폰 중이면 클리어 판정 보류.
        if (pendingSpawnCount > 0)
            return;

        AdvanceWave();
    }

    void AdvanceWave()
    {
        currentWaveIndex++;

        if (currentWaveIndex >= waves.Count)
        {
            allWavesCleared = true;
            if (bossBGM != null)
                AudioManager.Instance?.PlayBGM(afterBossBGM);
            onAllEnemiesDead?.Invoke();
            return;
        }

        Wave nextWave = waves[currentWaveIndex];

        Wave clearedWave = waves[currentWaveIndex - 1];
        if (clearedWave.triggerZone != null)
        {
            waitingForTrigger = true;
            clearedWave.triggerZone.gameObject.SetActive(true);
        }
        else
        {
            SpawnWave(nextWave, _cts.Token).Forget();
        }
    }

    // SpawnManager 자체 GameObject에 콜라이더가 있을 때를 위한 fallback.
    void OnTriggerEnter2D(Collider2D other) => HandleTrigger(other);

    // WaveTriggerForwarder에서 호출됨.
    public void NotifyWaveTrigger(Collider2D other) => HandleTrigger(other);

    void HandleTrigger(Collider2D other)
    {
        if (!waitingForTrigger)
            return;
        if (!other.CompareTag("Player"))
            return;

        waitingForTrigger = false;

        Wave clearedWave = waves[currentWaveIndex - 1];
        if (clearedWave.triggerZone != null)
            clearedWave.triggerZone.gameObject.SetActive(false);

        SpawnWave(waves[currentWaveIndex], _cts.Token).Forget();
    }
}

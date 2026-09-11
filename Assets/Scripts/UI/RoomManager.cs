using System.Threading;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Tilemaps;

public enum RoomType
{
    Normal,
    Shop,
    MiniBoss,
    Boss,
}

public class RoomManager : MonoBehaviour
{
    private const int TotalRooms = 10;
    private const int ShopRoom = 4;
    private const int MiniBossRoom = 7;
    private const int BossRoom = 10;

    [Header("Normal Room Pool")]
    [SerializeField]
    private GameObject[] normalRoomPrefabs;

    [Header("Special Room Prefabs")]
    [SerializeField]
    private GameObject shopRoomPrefab;

    [SerializeField]
    private GameObject miniBossRoomPrefab;

    [SerializeField]
    private GameObject bossRoomPrefab;

    [Header("Chest")]
    [SerializeField]
    private GameObject chestPrefab;

    [Header("References")]
    [SerializeField]
    private Transform player;

    public int CurrentRoomNumber { get; private set; }
    public RoomType CurrentRoomType { get; private set; }

    public static RoomManager Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    private GameObject currentRoom;
    private List<int> normalRoomOrder;
    private int normalRoomCursor;
    private Portal currentPortal;
    private CancellationTokenSource _masterCts;

    private CameraFollow cameraFollow;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Start()
    {
        cameraFollow = CameraFollow.Instance;
    }

    public void SetPlayer(Transform playerTransform)
    {
        player = playerTransform;
    }

    public void ResetDungeon()
    {
        _masterCts?.Cancel();
        _masterCts?.Dispose();
        _masterCts = null;
        if (currentRoom != null)
        {
            currentRoom.GetComponentInChildren<SpawnManager>()?.CleanupAll();
            currentRoom.SetActive(false);
            Destroy(currentRoom);
            currentRoom = null;
        }

        CurrentRoomNumber = 0;
        player = null;
    }

    public void StartGame()
    {
        ShuffleNormalRooms();
        _masterCts?.Cancel();
        _masterCts?.Dispose();
        _masterCts = new CancellationTokenSource();
        LoadRoomWithFade(1, true, _masterCts.Token).Forget();
    }

    public void ResumeGame(int roomNumber)
    {
        var data = SaveManager.Instance?.Data;
        if (data != null && data.savedRoomOrder.Count > 0)
        {
            normalRoomOrder = new List<int>(data.savedRoomOrder);
            normalRoomCursor = data.savedRoomCursor;

            // PickNormalRoomIndex는 호출 시마다 cursor++ 하므로,
            // 마지막에 일반 방이었다면 cursor가 이미 다음 방을 가리키고 있다.
            // 컨티뉴는 같은 방을 다시 로드하므로 커서를 1 되돌려 동일 프리팹을 재사용한다.
            if (GetRoomType(roomNumber) == RoomType.Normal && normalRoomCursor > 0)
                normalRoomCursor--;
        }
        else
        {
            ShuffleNormalRooms();
        }
        _masterCts?.Cancel();
        _masterCts?.Dispose();
        _masterCts = new CancellationTokenSource();
        LoadRoomWithFade(roomNumber, true, _masterCts.Token).Forget();
    }

    void ShuffleNormalRooms()
    {
        normalRoomOrder = new List<int>();
        for (int i = 0; i < normalRoomPrefabs.Length; i++)
            normalRoomOrder.Add(i);

        for (int i = normalRoomOrder.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (normalRoomOrder[i], normalRoomOrder[j]) = (normalRoomOrder[j], normalRoomOrder[i]);
        }

        normalRoomCursor = 0;
        SaveRoomLayout();
    }

    void SaveRoomLayout()
    {
        var data = SaveManager.Instance?.Data;
        if (data == null)
            return;
        data.savedRoomOrder = new List<int>(normalRoomOrder);
        data.savedRoomCursor = normalRoomCursor;
    }

    int PickNormalRoomIndex()
    {
        if (normalRoomCursor >= normalRoomOrder.Count)
            ShuffleNormalRooms();

        int idx = normalRoomOrder[normalRoomCursor++];
        SaveRoomLayout();
        return idx;
    }

    RoomType GetRoomType(int roomNumber)
    {
        return roomNumber switch
        {
            ShopRoom => RoomType.Shop,
            MiniBossRoom => RoomType.MiniBoss,
            BossRoom => RoomType.Boss,
            _ => RoomType.Normal,
        };
    }

    GameObject GetRoomPrefab(RoomType type)
    {
        return type switch
        {
            RoomType.Shop => shopRoomPrefab,
            RoomType.MiniBoss => miniBossRoomPrefab,
            RoomType.Boss => bossRoomPrefab,
            _ => normalRoomPrefabs[PickNormalRoomIndex()],
        };
    }

    void LoadRoom(int roomNumber)
    {
        foreach (var g in WorldGold.Instances.ToArray())
            Destroy(g.gameObject);
        foreach (var p in WorldPotion.Instances.ToArray())
            Destroy(p.gameObject);

        if (currentRoom != null)
            Destroy(currentRoom);

        CurrentRoomNumber = roomNumber;
        CurrentRoomType = GetRoomType(roomNumber);

        var prefab = GetRoomPrefab(CurrentRoomType);
        currentRoom = Instantiate(prefab);
        SplitPlatformColliders(currentRoom);

        var spawnPoint = currentRoom.GetComponentInChildren<PlayerSpawnPoint>();
        if (spawnPoint != null && player != null)
        {
            player.position = spawnPoint.transform.position;
            var rb = player.GetComponent<Rigidbody2D>();
            if (rb != null)
                rb.linearVelocity = Vector2.zero;
        }

        var boundsObj = currentRoom.transform.Find("CameraBounds");
        var cameraBounds =
            boundsObj != null
                ? boundsObj.GetComponent<PolygonCollider2D>()
                : currentRoom.GetComponentInChildren<PolygonCollider2D>();

        if (cameraFollow == null)
            cameraFollow = CameraFollow.Instance;

        if (cameraBounds != null)
            cameraFollow?.SetBoundsFromPolygon(cameraBounds);
        else
            cameraFollow?.RefreshBounds(currentRoom.transform);

        currentPortal = currentRoom.GetComponentInChildren<Portal>();

        if (SaveManager.Instance != null)
        {
            SaveManager.Instance.Data.lastLocation = "Dungeon";
            SaveManager.Instance.Data.lastRoomNumber = roomNumber;
            SaveManager.Instance.Save();
        }

        if (CurrentRoomType == RoomType.Shop)
        {
            if (currentPortal != null)
                currentPortal.SetActive(true);
            return;
        }

        var spawnMgr = currentRoom.GetComponentInChildren<SpawnManager>();
        if (spawnMgr != null)
            spawnMgr.onAllEnemiesDead += OnRoomCleared;
    }

    /// <summary>
    /// 통과 발판 타일맵의 콜라이더를 발판 덩어리마다 BoxCollider2D로 나눈다.
    /// 타일맵 콜라이더는 방의 모든 발판이 콜라이더 하나라서, 아래 점프로 충돌을 끄면
    /// 아래층 발판까지 함께 꺼져 1층·2층·3층이 겹친 곳에서 중간 층을 건너뛴다.
    /// </summary>
    static void SplitPlatformColliders(GameObject room)
    {
        foreach (var tc in room.GetComponentsInChildren<TilemapCollider2D>())
        {
            if (!tc.usedByEffector)
                continue;

            var tm = tc.GetComponent<Tilemap>();
            tm.CompressBounds();
            var cb = tm.cellBounds;
            var used = new HashSet<Vector3Int>();

            int BottomOf(int x, int y)
            {
                while (tm.HasTile(new Vector3Int(x, y - 1, 0)))
                    y--;
                return y;
            }

            // 위에서부터 훑으며 윗면·아랫면 높이가 같은 칸을 가로로 묶어 한 덩어리로 만든다.
            for (int y = cb.yMax - 1; y >= cb.yMin; y--)
            for (int x = cb.xMin; x < cb.xMax; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                if (!tm.HasTile(cell) || used.Contains(cell))
                    continue;

                int bottom = BottomOf(x, y);
                int right = x;
                while (right + 1 < cb.xMax)
                {
                    var next = new Vector3Int(right + 1, y, 0);
                    if (
                        !tm.HasTile(next)
                        || used.Contains(next)
                        || tm.HasTile(next + Vector3Int.up)
                        || BottomOf(next.x, y) != bottom
                    )
                        break;
                    right++;
                }

                for (int bx = x; bx <= right; bx++)
                for (int by = bottom; by <= y; by++)
                    used.Add(new Vector3Int(bx, by, 0));

                Vector3 min = tm.CellToLocal(new Vector3Int(x, bottom, 0));
                Vector3 max = tm.CellToLocal(new Vector3Int(right + 1, y + 1, 0));
                var box = tc.gameObject.AddComponent<BoxCollider2D>();
                box.offset = (min + max) * 0.5f;
                box.size = max - min;
                box.usedByEffector = true;
            }
            tc.enabled = false;
        }
    }

    void OnRoomCleared()
    {
        if (CurrentRoomType == RoomType.Boss)
        {
            OnGameClear();
            return;
        }

        SaveManager.Instance?.Save();

        SpawnChest();

        if (currentPortal != null)
            currentPortal.SetActive(true);
    }

    void SpawnChest()
    {
        if (chestPrefab == null || currentRoom == null)
            return;

        var spawnPoint = currentRoom.GetComponentInChildren<ChestSpawnPoint>();
        if (spawnPoint == null)
            return;

        Instantiate(chestPrefab, spawnPoint.transform.position, Quaternion.identity);
    }

    public void GoToNextRoom()
    {
        int next = CurrentRoomNumber + 1;
        if (next > TotalRooms)
        {
            OnGameClear();
            return;
        }

        _masterCts?.Cancel();
        _masterCts?.Dispose();
        _masterCts = new CancellationTokenSource();
        LoadRoomWithFade(next, false, _masterCts.Token).Forget();
    }

    void OnGameClear()
    {
        ResetPlayerInventory();
        GameClearUI.Instance?.Show();
    }

    void ResetPlayerInventory()
    {
        if (player == null)
            return;

        var inventory = player.GetComponentInChildren<Inventory>();
        if (inventory != null)
        {
            // 장착 장비 초기화
            for (int i = inventory.Accessories.Count - 1; i >= 0; i--)
                inventory.RemoveAccessory(i);

            // 백팩 초기화
            for (int i = inventory.Backpack.Count - 1; i >= 0; i--)
                inventory.RemoveFromBackpack(i);

            // 장착 무기 초기화
            var weaponInv = inventory.WeaponInventory;
            if (weaponInv != null)
                weaponInv.weapons.Clear();
        }

        // 세이브 데이터에서도 무기 초기화 후 저장
        if (SaveManager.Instance != null)
        {
            SaveManager.Instance.Data.equippedWeapons.Clear();
            SaveManager.Instance.Data.lastLocation = "Village";
            SaveManager.Instance.Data.lastRoomNumber = 1;
            SaveManager.Instance.Save();
        }
    }

    async UniTaskVoid LoadRoomWithFade(int roomNumber, bool isFirst, CancellationToken token)
    {
        // 1. 화면을 어둡게 (이전 방이 더이상 보이지 않게)
        if (!isFirst && ScreenFader.Instance != null)
            await ScreenFader.Instance.FadeOut();

        // 2. 새 방 로드 (플레이어 위치도 새 스폰포인트로 이동)
        LoadRoom(roomNumber);

        // 3. 플레이어 위치를 함수로 미리 받아서
        Vector3 spawnPos = GetPlayerWorldPosition();

        // 4. 카메라를 그 위치로 즉시 이동
        SnapCameraTo(spawnPos);

        // 5. CameraFollow가 새 위치를 반영할 시간 확보
        await UniTask.Yield(token);

        // 6. 페이드 인 → 카메라가 새 위치에 자리 잡힌 상태에서 맵이 드러남
        if (ScreenFader.Instance != null)
            await ScreenFader.Instance.FadeIn();
    }

    Vector3 GetPlayerWorldPosition()
    {
        if (player != null)
            return player.position;
        if (PlayerRef.Exists)
            return PlayerRef.Transform.position;
        return Vector3.zero;
    }

    void SnapCameraTo(Vector3 worldPos)
    {
        if (cameraFollow == null)
            cameraFollow = CameraFollow.Instance;
        if (cameraFollow == null)
            return;

        // 플레이어가 있으면 대상 기준으로 정렬한다(오프셋·룩어헤드·경계까지 반영).
        // 없을 때만 좌표로 강제 이동. 둘 다 새 방 경계로 클램프된다.
        if (player != null)
            cameraFollow.SnapToTarget();
        else
            cameraFollow.ForcePosition(worldPos);
    }
}

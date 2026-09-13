using Cysharp.Threading.Tasks;
using UnityEngine;

public class GameFlowController : MonoBehaviour
{
    public static GameFlowController Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    [Header("프리팹")]
    [SerializeField]
    private GameObject titlePrefab;

    [SerializeField]
    private GameObject villagePrefab;

    [SerializeField]
    private GameObject playerPrefab;

    [SerializeField]
    private GameObject uiCanvasPrefab;

    [SerializeField]
    private GameObject pauseMenuPrefab;

    [SerializeField]
    private GameObject tutorialRoomPrefab;

    [Header("데이터")]
    [SerializeField]
    private ItemDatabase itemDatabase;

    [SerializeField]
    private MetaUpgradeConfig metaUpgradeConfig;

    [Header("레퍼런스")]
    [SerializeField]
    private RoomManager roomManager;

    private GameObject titleInstance;
    private GameObject villageInstance;
    private GameObject playerInstance;
    private GameObject pauseMenuInstance;
    private GameObject tutorialInstance;
    private GameObject uiCanvasInstance;

    /// <summary>UICanvas 프리팹의 Screen Space 캔버스. 상점 등 런타임 UI의 부모로 쓴다.</summary>
    public Canvas UICanvas { get; private set; }

    void Awake()
    {
        Instance = this;
        if (uiCanvasPrefab != null)
        {
            uiCanvasInstance = Instantiate(uiCanvasPrefab);
            UICanvas = uiCanvasInstance.GetComponentInChildren<Canvas>(true);
        }

        // SaveManager 가 씬에 없으면 자동 생성
        if (SaveManager.Instance == null)
            new GameObject("SaveManager").AddComponent<SaveManager>();

        // ItemDatabase 초기화
        if (itemDatabase != null)
            itemDatabase.Init();

        // MetaUpgradeConfig 초기화
        if (metaUpgradeConfig != null)
            metaUpgradeConfig.Init();

        // RunStats 가 씬에 없으면 자동 생성
        if (RunStats.Instance == null)
            new GameObject("RunStats").AddComponent<RunStats>();

        // InputManager 가 씬에 없으면 자동 생성
        if (InputManager.Instance == null)
            new GameObject("InputManager").AddComponent<InputManager>();

        // WeaponSlotUI 가 씬에 없으면 자동 생성
        if (WeaponSlotUI.Instance == null)
            new GameObject("WeaponSlotUI").AddComponent<WeaponSlotUI>();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Start()
    {
        GoToTitle();
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape))
            return;

        // 타이틀 화면에서는 일시정지 불가
        if (titleInstance != null)
            return;

        // 플레이어가 없으면 (게임 중이 아니면) 무시
        if (playerInstance == null)
            return;

        // 인벤토리가 열려있으면 ESC 무시
        if (InventoryUI.IsOpen)
            return;

        // GameOver / GameClear / 상점 등이 이미 화면을 점유 중이면 무시
        if (TimeScaleLock.IsFrozen && !PauseMenu.IsPaused)
            return;

        if (PauseMenu.IsPaused)
        {
            ClosePauseMenu();
        }
        else
        {
            OpenPauseMenu();
        }
    }

    void OpenPauseMenu()
    {
        if (pauseMenuInstance == null)
        {
            if (pauseMenuPrefab == null)
                return;
            pauseMenuInstance = Instantiate(pauseMenuPrefab);
        }
        else
        {
            pauseMenuInstance.SetActive(true);
        }

        PauseMenu.Instance.Open();
    }

    public void ClosePauseMenu()
    {
        if (PauseMenu.Instance != null)
            PauseMenu.Instance.Close();
        if (pauseMenuInstance != null)
            pauseMenuInstance.SetActive(false);
    }

    public void GoToTitle()
    {
        AudioManager.Instance?.PlayDefaultBGM();
        TimeScaleLock.ReleaseAll();
        roomManager.ResetDungeon();

        if (villageInstance != null)
        {
            Destroy(villageInstance);
            villageInstance = null;
        }
        if (tutorialInstance != null)
        {
            Destroy(tutorialInstance);
            tutorialInstance = null;
        }
        if (playerInstance != null)
        {
            Destroy(playerInstance);
            playerInstance = null;
        }

        GameOverUI.Instance?.ResetUI();
        GameClearUI.Instance?.ResetUI();

        if (uiCanvasInstance != null)
            uiCanvasInstance.SetActive(false);

        BossHealthBarUI.Instance?.Hide();
        WeaponSlotUI.Instance?.SetActive(false);

        titleInstance = Instantiate(titlePrefab);
    }

    public void StartNewGame()
    {
        // 세이브 초기화
        if (SaveManager.Instance != null)
            SaveManager.Instance.DeleteSave();

        DestroyTitle();
        SpawnPlayer();

        if (tutorialRoomPrefab != null)
        {
            GoToTutorial();
            return;
        }

        GoToVillage();
    }

    public void ContinueGame()
    {
        if (!HasSaveData())
            return;

        DestroyTitle();
        SpawnPlayer();
        LoadPlayerData();

        var data = SaveManager.Instance.Data;
        if (data.lastLocation == "Dungeon" && data.lastRoomNumber > 0)
        {
            if (playerInstance != null)
                roomManager.SetPlayer(playerInstance.transform);
            RunStats.Instance?.StartRun();
            roomManager.ResumeGame(data.lastRoomNumber);
        }
        else
        {
            GoToVillage();
        }
    }

    public bool HasSaveData()
    {
        return SaveManager.Instance != null
            && System.IO.File.Exists(
                System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "save.dat")
            );
    }

    void DestroyTitle()
    {
        if (titleInstance != null)
        {
            Destroy(titleInstance);
            titleInstance = null;
        }

        if (uiCanvasInstance != null)
            uiCanvasInstance.SetActive(true);

        WeaponSlotUI.Instance?.SetActive(true);
    }

    void SpawnPlayer()
    {
        playerInstance = Instantiate(playerPrefab);

        if (CameraFollow.Instance != null)
            CameraFollow.Instance.SetFollowTarget(playerInstance.transform);

        roomManager.SetPlayer(playerInstance.transform);
    }

    void LoadPlayerData()
    {
        if (SaveManager.Instance == null)
            return;

        var data = SaveManager.Instance.Data;
        var inventory = playerInstance.GetComponentInChildren<Inventory>();

        if (inventory == null)
            return;

        // 골드 복원
        inventory.LoadGold();

        // 장착 무기 복원
        if (itemDatabase != null && data.equippedWeapons.Count > 0)
        {
            var weaponInv = inventory.WeaponInventory;
            if (weaponInv != null)
            {
                weaponInv.weapons.Clear();
                foreach (var weaponName in data.equippedWeapons)
                {
                    var weapon = itemDatabase.FindWeapon(weaponName);
                    weaponInv.weapons.Add(weapon); // null이면 빈 슬롯
                }
            }
        }
    }

    void GoToTutorial()
    {
        tutorialInstance = Instantiate(tutorialRoomPrefab);
        SetupSceneCamera(tutorialInstance);

        ScreenFader.Instance?.FadeIn();

        var tutorial = tutorialInstance.GetComponentInChildren<TutorialManager>();
        if (tutorial != null)
        {
            tutorial.onTutorialComplete += () =>
            {
                Destroy(tutorialInstance);
                tutorialInstance = null;
                GoToVillage();
            };
            tutorial.Begin(playerInstance);
        }
    }

    /// <summary>방 인스턴스에서 스폰포인트와 카메라 바운드를 찾아 플레이어/카메라를 배치한다.</summary>
    void SetupSceneCamera(GameObject sceneInstance)
    {
        var spawn = sceneInstance.GetComponentInChildren<PlayerSpawnPoint>();
        if (spawn != null && playerInstance != null)
        {
            playerInstance.transform.position = spawn.transform.position;
            var rb = playerInstance.GetComponent<Rigidbody2D>();
            if (rb != null)
                rb.linearVelocity = Vector2.zero;
        }

        var cam = CameraFollow.Instance;
        if (cam == null)
            return;

        var bounds = sceneInstance
            .transform.Find("CameraBounds")
            ?.GetComponent<PolygonCollider2D>();
        if (bounds != null)
            cam.SetBoundsFromPolygon(bounds);
        else
            cam.RefreshBounds();

        if (playerInstance != null)
        {
            cam.ForcePosition(playerInstance.transform.position);
            cam.SnapToTarget();
        }
    }

    void GoToVillage()
    {
        if (SaveManager.Instance != null)
        {
            SaveManager.Instance.Data.lastLocation = "Village";
            SaveManager.Instance.Save();
        }

        // 이미 마을이 떠 있으면 중복 생성하지 않도록 먼저 정리
        if (villageInstance != null)
            Destroy(villageInstance);

        villageInstance = Instantiate(villagePrefab);
        SetupSceneCamera(villageInstance);
    }

    public void ReturnToVillage()
    {
        ReturnToVillageRoutine().Forget();
    }

    async UniTaskVoid ReturnToVillageRoutine()
    {
        if (ScreenFader.Instance != null)
            await ScreenFader.Instance.FadeOut();

        roomManager.ResetDungeon();
        GameClearUI.Instance?.ResetUI();
        GameOverUI.Instance?.ResetUI();
        BossHealthBarUI.Instance?.Hide();

        if (playerInstance != null)
        {
            var inv = playerInstance.GetComponentInChildren<Inventory>();
            if (inv != null && SaveManager.Instance != null)
            {
                SaveManager.Instance.Data.gold = inv.Gold;
                SaveManager.Instance.Save();
            }

            playerInstance.GetComponent<PlayerController>()?.Revive();
        }

        GoToVillage();

        if (ScreenFader.Instance != null)
            await ScreenFader.Instance.FadeIn();
    }

    public void EnterDungeon()
    {
        if (villageInstance != null)
        {
            Destroy(villageInstance);
            villageInstance = null;
        }

        // ResetDungeon()에서 player 레퍼런스가 null 됐을 수 있으므로 재설정
        if (playerInstance != null)
            roomManager.SetPlayer(playerInstance.transform);

        RunStats.Instance?.StartRun();
        MetaUpgrades.BeginRun();
        roomManager.StartGame();
    }
}

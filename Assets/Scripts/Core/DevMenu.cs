#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

/// <summary>
/// 개발용 맵 워프 / 디버그 메뉴. 에디터·개발 빌드에서만 컴파일된다.
///
/// F1  : 메뉴 열기/닫기
/// F2  : 적 전멸 (기존 GameManager 의 F1 디버그 키를 옮겨온 것)
/// F3  : 다음 방
/// F4  : 현재 방 다시 로드
/// </summary>
public class DevMenu : MonoBehaviour
{
    const KeyCode ToggleKey = KeyCode.F1;
    const KeyCode KillAllKey = KeyCode.F2;
    const KeyCode NextRoomKey = KeyCode.F3;
    const KeyCode ReloadRoomKey = KeyCode.F4;

    static readonly int[] GoldAmounts = { 100, 1000, 10000 };

    // 방 번호는 RoomManager 의 특수 방 규칙과 동일하게 맞춘다.
    const int ShopRoom = 4;
    const int MiniBossRoom = 7;
    const int BossRoom = 10;

    static DevMenu instance;

    bool open;
    Vector2 scroll;
    Rect window = new Rect(20f, 20f, 300f, 460f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (instance != null)
            return;

        var go = new GameObject("[DevMenu]");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<DevMenu>();
    }

    void Update()
    {
        if (Input.GetKeyDown(ToggleKey))
            open = !open;

        var rm = RoomManager.Instance;
        if (rm == null || rm.CurrentRoomNumber <= 0)
            return;

        if (Input.GetKeyDown(KillAllKey))
            KillAllEnemies();

        if (Input.GetKeyDown(NextRoomKey))
            rm.GoToNextRoom();

        if (Input.GetKeyDown(ReloadRoomKey))
            rm.DevWarpTo(rm.CurrentRoomNumber);
    }

    /// <summary>현재 방의 적·미니보스·보스를 즉시 처치한다.</summary>
    static void KillAllEnemies()
    {
        foreach (var e in EnemyController.Instances.ToArray())
            e.TakeDamage(99999f);
        foreach (var m in MiniBossController.Instances.ToArray())
            m.TakeDamage(99999f);
        foreach (var b in BossController.Instances.ToArray())
            b.TakeDamage(99999f);
    }

    void OnGUI()
    {
        if (!open)
        {
            GUI.Label(new Rect(10f, 10f, 200f, 20f), $"[{ToggleKey}] 개발자 메뉴");
            return;
        }

        window = GUI.Window(GetInstanceID(), window, DrawWindow, "개발자 메뉴 (맵 워프)");
    }

    void DrawWindow(int id)
    {
        var flow = GameFlowController.Instance;
        if (flow == null)
        {
            GUILayout.Label("GameFlowController 가 없다.");
            return;
        }

        var rm = flow.DevRoomManager;

        GUILayout.Label(
            rm != null && rm.CurrentRoomNumber > 0
                ? $"현재: {rm.CurrentRoomNumber}번 방 ({rm.CurrentRoomType})"
                : "현재: 던전 밖"
        );
        GUILayout.Label($"[{KillAllKey}] 적 전멸  [{NextRoomKey}] 다음 방  [{ReloadRoomKey}] 재로드");

        scroll = GUILayout.BeginScrollView(scroll);

        GUILayout.Label("— 디버그 —");
        GUI.enabled = rm != null && rm.CurrentRoomNumber > 0;
        if (GUILayout.Button($"적 전멸 ({KillAllKey})"))
            KillAllEnemies();
        if (GUILayout.Button($"다음 방 ({NextRoomKey})"))
            rm.GoToNextRoom();
        GUI.enabled = true;

        GUILayout.Space(6f);
        var inventory = PlayerRef.Exists ? PlayerRef.Inventory : null;
        GUILayout.Label(inventory != null ? $"— 골드 (보유 {inventory.Gold}) —" : "— 골드 (플레이어 없음) —");
        GUI.enabled = inventory != null;
        GUILayout.BeginHorizontal();
        foreach (int amount in GoldAmounts)
        {
            if (GUILayout.Button($"+{amount}"))
                inventory.AddGold(amount);
        }
        GUILayout.EndHorizontal();
        GUI.enabled = true;

        GUILayout.Space(6f);
        GUILayout.Label("— 씬 —");
        if (GUILayout.Button("마을 (Village)"))
            flow.DevWarpToVillage();
        if (GUILayout.Button("튜토리얼"))
            flow.DevWarpToTutorial();

        GUILayout.Space(6f);
        GUILayout.Label("— 특수 방 —");
        if (GUILayout.Button($"상점 ({ShopRoom}번 방)"))
            flow.DevWarpToRoom(ShopRoom);
        if (GUILayout.Button($"미니보스 ({MiniBossRoom}번 방)"))
            flow.DevWarpToRoom(MiniBossRoom);
        if (GUILayout.Button($"보스 ({BossRoom}번 방)"))
            flow.DevWarpToRoom(BossRoom);

        GUILayout.Space(6f);
        GUILayout.Label("— 일반 맵 —");
        var prefabs = rm != null ? rm.DevNormalRoomPrefabs : null;
        if (prefabs == null || prefabs.Length == 0)
        {
            GUILayout.Label("RoomManager 에 일반 방 프리팹이 없다.");
        }
        else
        {
            for (int i = 0; i < prefabs.Length; i++)
            {
                if (prefabs[i] == null)
                    continue;

                if (GUILayout.Button(prefabs[i].name))
                {
                    // 일반 맵은 진행도 영향이 적도록 1번 방 자리에 띄운다.
                    flow.DevWarpToRoom(1, i);
                }
            }
        }

        GUILayout.EndScrollView();
        GUI.DragWindow(new Rect(0f, 0f, window.width, 20f));
    }
}
#endif

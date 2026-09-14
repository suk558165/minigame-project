using UnityEngine;

/// <summary>
/// NPC에 붙이는 컴포넌트.
/// DialogueData ScriptableObject를 연결하고,
/// 씬에 DialogueUI가 있어야 대화창이 표시됩니다.
/// </summary>
public class NpcController : MonoBehaviour
{
    [Header("대화 데이터")]
    public DialogueData dialogueData;

    [Header("강화창 (설정 시 대화 대신 강화창을 연다)")]
    public UpgradeShopUI upgradeShopUI;

    [Header("강화창 부모 캔버스 (GameFlowController.UICanvas 가 없을 때만 사용)")]
    public Canvas parentCanvas;

    [Header("상호작용 범위")]
    public float interactRange = 2f;

    [Header("힌트 오브젝트 (말풍선 등, 없어도 됨)")]
    public GameObject hintObject;

    private Transform player;
    private bool isTalking;

    void Start()
    {
        player = PlayerRef.Transform;
        if (hintObject != null)
            hintObject.SetActive(false);
    }

    void Update()
    {
        if (player == null)
        {
            if (PlayerRef.Exists)
                player = PlayerRef.Transform;
            return;
        }

        // 대화 중일 때 입력을 DialogueUI로 전달
        if (isTalking)
        {
            DialogueUI.Instance?.HandleInput();
            return;
        }

        float dist = Vector2.Distance(transform.position, player.position);
        bool inRange = dist <= interactRange;

        // 강화 NPC: 대화 대신 강화창을 토글한다.
        if (upgradeShopUI != null)
        {
            if (hintObject != null)
                hintObject.SetActive(inRange && !UpgradeShopUI.IsOpen);

            if (!inRange)
            {
                // 사거리를 벗어나면 A키가 더 이상 닿지 않으므로 열린 창을 닫아 준다.
                // (닫지 않으면 TimeScaleLock 이 걸린 채로 남는다.)
                upgradeShopUI.Close();
                return;
            }

            var key = InputManager.Instance?.Interact ?? KeyCode.A;
            if (Input.GetKeyDown(key))
            {
                if (UpgradeShopUI.IsOpen)
                    upgradeShopUI.Close();
                else
                {
                    EnsureUpgradeUI();
                    upgradeShopUI.Open();
                }
            }
            return;
        }

        if (hintObject != null)
            hintObject.SetActive(inRange && !DialogueUI.IsOpen);

        if (!inRange || DialogueUI.IsOpen)
            return;

        var interactKey = InputManager.Instance?.Interact ?? KeyCode.A;
        if (Input.GetKeyDown(interactKey))
            StartTalk();
    }

    void OnDisable()
    {
        // NPC 가 사라질 때(씬 전환 등) 창이 열려 있으면 TimeScaleLock 이 남아
        // 다음 씬이 timeScale 0 으로 시작한다.
        if (upgradeShopUI != null)
            upgradeShopUI.Close();
    }

    /// <summary>
    /// upgradeShopUI 가 프리팹 에셋을 가리키고 있으면 씬에 인스턴스를 만들어 교체한다.
    /// (에셋 그대로 Open() 하면 프로젝트의 프리팹만 켜져 화면에 아무것도 안 나온다.)
    /// Shop.EnsureShopUI 와 같은 패턴.
    /// </summary>
    void EnsureUpgradeUI()
    {
        if (upgradeShopUI == null || upgradeShopUI.gameObject.scene.IsValid())
            return;

        Canvas uiCanvas = GameFlowController.Instance?.UICanvas;
        Transform parent = uiCanvas != null ? uiCanvas.transform : (parentCanvas != null ? parentCanvas.transform : null);
        upgradeShopUI = Instantiate(upgradeShopUI, parent);

        var rt = upgradeShopUI.GetComponent<RectTransform>();
        if (rt != null && parent != null)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        var canvas = upgradeShopUI.GetComponent<Canvas>();
        if (canvas != null && parent != null)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;
        }
    }

    void StartTalk()
    {
        if (DialogueUI.Instance == null || dialogueData == null)
            return;

        isTalking = true;
        if (hintObject != null)
            hintObject.SetActive(false);

        DialogueUI.Instance.StartDialogue(dialogueData, OnDialogueFinished);
    }

    void OnDialogueFinished()
    {
        isTalking = false;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 0.5f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}

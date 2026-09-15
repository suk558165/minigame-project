using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

public class GameClearUI : MonoBehaviour
{
    [Header("Panel")]
    public CanvasGroup canvasGroup;
    public float fadeInDuration = 0.6f;

    [Header("Background")]
    [Tooltip("게임클리어 배경 스프라이트 (책 오른쪽 페이지)")]
    public Sprite backgroundSprite;

    [Header("Stats")]
    public TextMeshProUGUI playTimeText;
    public TextMeshProUGUI deathCountText;
    public TextMeshProUGUI killCountText;
    public TextMeshProUGUI goldEarnedText;
    public TextMeshProUGUI damageDealtText;
    public TextMeshProUGUI damageTakenText;
    public TextMeshProUGUI itemsGainedText;

    [Header("Return")]
    public TextMeshProUGUI returnHintText;
    public KeyCode returnKey = KeyCode.X;

    private bool triggered;
    private bool canReturn;
    private CancellationTokenSource _masterCts;

    public static GameClearUI Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    void Awake()
    {
        Instance = this;
        Hide();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void OnEnable()
    {
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
    }

    void OnDisable()
    {
        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
    }

    void OnLocaleChanged(Locale _)
    {
        if (triggered)
            PopulateStats();
    }

    public void Show()
    {
        if (triggered)
            return;
        triggered = true;
        RunStats.Instance?.StopTimer();
        TimeScaleLock.Acquire(this);

        for (var t = transform; t != null; t = t.parent)
        {
            if (!t.gameObject.activeSelf)
                t.gameObject.SetActive(true);
        }

        _masterCts?.Cancel();
        _masterCts?.Dispose();
        _masterCts = new CancellationTokenSource();
        ShowRoutine(_masterCts.Token).Forget();
    }

    async UniTaskVoid ShowRoutine(CancellationToken token)
    {
        BossHealthBarUI.Instance?.Hide();
        WeaponSlotUI.Instance?.SetActive(false);
        MinimapController.Instance?.Hide();

        SetupBackground();
        PopulateStats();

        float elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            token.ThrowIfCancellationRequested();
            elapsed += Time.unscaledDeltaTime;
            if (canvasGroup != null)
                canvasGroup.alpha = Mathf.Clamp01(elapsed / fadeInDuration);
            await UniTask.Yield(token);
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        canReturn = true;

        if (returnHintText != null)
            returnHintText.text = L10n.Format(
                "ui.return_village",
                "[ {0} ] 마을로 돌아가기",
                returnKey
            );
    }

    void SetupBackground()
    {
        if (backgroundSprite == null)
            return;

        foreach (var img in GetComponentsInChildren<Image>(true))
        {
            if (img.gameObject != gameObject && img.gameObject.name != "BG" && img.gameObject.name != "Dim")
            {
                var c = img.color;
                img.color = new Color(c.r, c.g, c.b, 0f);
            }
        }

        if (transform.Find("Dim") == null)
        {
            var dimGo = new GameObject("Dim", typeof(RectTransform));
            dimGo.transform.SetParent(transform, false);
            dimGo.transform.SetAsFirstSibling();
            var dimRt = dimGo.GetComponent<RectTransform>();
            dimRt.anchorMin = new Vector2(-1f, -1f);
            dimRt.anchorMax = new Vector2(2f, 2f);
            dimRt.offsetMin = Vector2.zero;
            dimRt.offsetMax = Vector2.zero;
            var dimImg = dimGo.AddComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0.7f);
            dimImg.raycastTarget = false;
            BookPageLayout.IgnoreLayout(dimGo);
        }

        var bgTransform = transform.Find("BG");
        Image bgImage;

        if (bgTransform != null)
        {
            bgImage = bgTransform.GetComponent<Image>();
        }
        else
        {
            var bgGo = new GameObject("BG", typeof(RectTransform));
            bgGo.transform.SetParent(transform, false);
            bgGo.transform.SetSiblingIndex(1);

            var rt = bgGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            bgImage = bgGo.AddComponent<Image>();
            bgImage.raycastTarget = false;
        }

        BookPageLayout.IgnoreLayout(bgImage.gameObject);

        bgImage.sprite = backgroundSprite;
        bgImage.preserveAspect = true;
        bgImage.color = Color.white;

        // 게임클리어는 책의 오른쪽 페이지를 쓴다.
        BookPageLayout.Apply(
            (RectTransform)transform,
            backgroundSprite,
            BookPageLayout.RightPage
        );
    }

    void Update()
    {
        if (canReturn && Input.GetKeyDown(returnKey))
            ReturnToVillage();
    }

    void PopulateStats()
    {
        var s = RunStats.Instance;
        if (s == null)
            return;

        int sec = Mathf.FloorToInt(s.PlayTime);
        string time = $"{sec / 3600:D2}:{(sec % 3600) / 60:D2}:{sec % 60:D2}";
        SetText(playTimeText, L10n.Format("ui.stats.playtime", "플레이 타임  {0}", time));
        SetText(deathCountText, L10n.Format("ui.stats.deaths", "사망 횟수  {0}", s.Deaths));
        SetText(killCountText, L10n.Format("ui.stats.kills", "처치 수  {0}", s.Kills));
        SetText(
            goldEarnedText,
            L10n.Format("ui.stats.gold_earned", "획득 골드  {0}", s.GoldEarned)
        );
        SetText(
            damageDealtText,
            L10n.Format("ui.stats.damage_dealt", "총 딜량  {0}", Mathf.RoundToInt(s.DamageDealt))
        );
        SetText(
            damageTakenText,
            L10n.Format("ui.stats.damage_taken", "받은 피해  {0}", Mathf.RoundToInt(s.DamageTaken))
        );
        SetText(
            itemsGainedText,
            L10n.Format("ui.stats.items_gained", "획득 아이템  {0}", s.ItemsGained)
        );
    }

    void SetText(TextMeshProUGUI label, string value)
    {
        if (label != null)
            label.text = value;
    }

    public void ReturnToVillage()
    {
        // 마을 귀환은 상태 전체 재설정 — 남아있는 잠금까지 전부 해제한다.
        TimeScaleLock.ReleaseAll();
        GameFlowController.Instance?.ReturnToVillage();
    }

    void Hide()
    {
        if (canvasGroup == null)
            return;
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    public void ResetUI()
    {
        TimeScaleLock.Release(this);
        triggered = false;
        canReturn = false;
        _masterCts?.Cancel();
        _masterCts?.Dispose();
        _masterCts = null;
        Hide();
    }
}

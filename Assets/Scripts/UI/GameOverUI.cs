using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

public class GameOverUI : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField]
    private CanvasGroup canvasGroup;
    [SerializeField]
    private float fadeInDuration = 0.6f;

    [Header("Background")]
    [Tooltip("게임오버 배경 스프라이트 (책 왼쪽 페이지)")]
    [SerializeField]
    private Sprite backgroundSprite;

    [Header("Stats")]
    [SerializeField]
    private TextMeshProUGUI playTimeText;
    [SerializeField]
    private TextMeshProUGUI deathCountText;
    [SerializeField]
    private TextMeshProUGUI killCountText;
    [SerializeField]
    private TextMeshProUGUI goldEarnedText;
    [SerializeField]
    private TextMeshProUGUI damageDealtText;
    [SerializeField]
    private TextMeshProUGUI damageTakenText;
    [SerializeField]
    private TextMeshProUGUI itemsGainedText;

    [Header("Audio")]
    [SerializeField]
    private AudioClip gameOverSound;

    [Header("Return")]
    [SerializeField]
    private TextMeshProUGUI returnHintText;
    [SerializeField]
    private KeyCode returnKey = KeyCode.X;

    [Header("사망 연출")]
    [Tooltip("죽는 애니메이션이 재생될 시간. 이 시간이 지난 뒤 게임을 멈추고 결과창을 띄운다.")]
    [SerializeField]
    private float deathAnimDuration = 1.2f;

    private bool triggered;
    private bool canReturn;
    private CancellationTokenSource _masterCts;

    public static GameOverUI Instance { get; private set; }

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

    void Update()
    {
        if (!triggered)
        {
            var ph = PlayerRef.Health;
            if (ph != null && ph.IsDead)
            {
                triggered = true;
                RunStats.Instance?.StopTimer();
                AudioManager.Instance?.PlaySFX(gameOverSound);
                _masterCts?.Cancel();
                _masterCts?.Dispose();
                _masterCts = new CancellationTokenSource();
                ShowRoutine(_masterCts.Token).Forget();
            }
            return;
        }

        if (canReturn && Input.GetKeyDown(returnKey))
            ReturnToVillage();
    }

    async UniTaskVoid ShowRoutine(CancellationToken token)
    {
        // 죽는 애니메이션이 다 나온 뒤에 게임을 멈춘다.
        // 사망 프레임에 바로 timeScale=0 을 걸면 Animator(UpdateMode=Normal)가
        // 그대로 얼어붙어 사망 모션이 한 프레임만 보이고 정지한다.
        if (deathAnimDuration > 0f)
            await UniTask.Delay(
                System.TimeSpan.FromSeconds(deathAnimDuration),
                cancellationToken: token
            );

        TimeScaleLock.Acquire(this);

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

        if (returnHintText != null)
            returnHintText.text = L10n.Format(
                "ui.return_village",
                "[ {0} ] 마을로 돌아가기",
                returnKey
            );

        canReturn = true;
    }

    void SetupBackground()
    {
        if (backgroundSprite == null)
            return;

        // 기존 자식 Image들의 배경색 제거 (아이콘 이미지는 유지)
        foreach (var img in GetComponentsInChildren<Image>(true))
        {
            if (img.gameObject != gameObject && img.gameObject.name != "BG" && img.gameObject.name != "Dim")
            {
                var c = img.color;
                img.color = new Color(c.r, c.g, c.b, 0f);
            }
        }

        // 화면 전체 어두운 딤
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

        // 책 페이지 배경
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

        // 게임오버는 책의 왼쪽 페이지를 쓴다.
        BookPageLayout.Apply(
            (RectTransform)transform,
            backgroundSprite,
            BookPageLayout.LeftPage
        );
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

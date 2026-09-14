using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 인벤토리 / 상점에서 아이템 호버 시 이름·설명을 띄우는 툴팁.
///
/// 두 가지 모드 지원:
/// 1. 자동 생성: 씬에 ItemTooltip 컴포넌트가 없으면 첫 호출 시 캔버스를 찾아 패널을 코드로 생성.
/// 2. 수동 연결: 씬에 미리 ItemTooltip을 만들어 nameText/descText/iconImage를 인스펙터에서 연결한 경우 그걸 사용.
/// </summary>
public class ItemTooltip : MonoBehaviour
{
    public static ItemTooltip Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    [Header("UI 연결 (수동 모드)")]
    public GameObject rootPanel;

    [SerializeField]
    private TextMeshProUGUI nameText;

    [SerializeField]
    private TextMeshProUGUI descText;

    [SerializeField]
    private TextMeshProUGUI statsText;

    [SerializeField]
    private Image iconImage;

    [Header("위치")]
    public Vector2 cursorOffset = new Vector2(16f, -16f);

    private RectTransform _rt;
    private Canvas _canvas;
    private RectTransform _canvasRT;
    private bool _isShown;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        InitRefs();
        Hide();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// 툴팁 정렬 순서. 상점·강화창(100)과 데미지 팝업(200)보다 위,
    /// 피격 화면 효과(999)보다 아래.
    /// </summary>
    const int SortingOrder = 300;

    void InitRefs()
    {
        _rt = (rootPanel != null ? rootPanel.transform : transform) as RectTransform;

        // 아래에서 자기 자신에 정렬용 Canvas 를 붙이므로, 부모에서부터 찾아야
        // 커서 위치 계산과 화면 밖 보정이 그 Canvas 기준으로 틀어지지 않는다.
        var parent = transform.parent;
        _canvas = parent != null ? parent.GetComponentInParent<Canvas>() : GetComponentInParent<Canvas>();
        if (_canvas != null)
            _canvasRT = _canvas.transform as RectTransform;

        EnsureTopMost();
    }

    /// <summary>
    /// 툴팁에 자체 Canvas 를 붙여 항상 패널 위에 그린다.
    /// 상점 패널이 overrideSorting 으로 100 을 쓰기 때문에,
    /// 계층 순서만으로는 툴팁이 패널 뒤에 가려진다.
    /// </summary>
    void EnsureTopMost()
    {
        var target = _rt != null ? _rt.gameObject : gameObject;
        var canvas = target.GetComponent<Canvas>();
        if (canvas == null)
            canvas = target.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = SortingOrder;
    }

    /// <summary>씬에 인스턴스가 없으면 캔버스를 찾아 코드로 툴팁 GameObject를 만들고 반환.</summary>
    public static ItemTooltip GetOrCreate(Canvas canvas = null)
    {
        if (Instance != null)
            return Instance;

        if (canvas == null)
            return null;

        var rootCanvas = canvas.rootCanvas;

        // 루트 캔버스 하위의 기존 TMP 텍스트에서 폰트/머티리얼을 빌려와 같은 룩앤필 유지
        TMP_FontAsset borrowedFont = null;
        Material borrowedMat = null;
        var sceneTmps = rootCanvas.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var t in sceneTmps)
        {
            if (t == null || t.font == null)
                continue;
            borrowedFont = t.font;
            borrowedMat = t.fontMaterial;
            break;
        }
        var panel = new GameObject(
            "ItemTooltip (Runtime)",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(ItemTooltip)
        );
        panel.transform.SetParent(rootCanvas.transform, false);

        var rt = panel.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(300, 0);
        rt.pivot = new Vector2(0, 1);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);

        var bg = panel.GetComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.08f, 0.92f);
        bg.raycastTarget = false;

        // 내용에 맞춰 패널이 세로로 자동 확장 — 텍스트가 상자를 넘치지 않도록
        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = 4f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = panel.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        CreateTMP(
            panel.transform,
            "NameText",
            out var nameTMP,
            18,
            FontStyles.Bold,
            borrowedFont,
            borrowedMat
        );
        nameTMP.textWrappingMode = TextWrappingModes.Normal;

        CreateTMP(
            panel.transform,
            "DescText",
            out var descTMP,
            14,
            FontStyles.Normal,
            borrowedFont,
            borrowedMat
        );
        descTMP.textWrappingMode = TextWrappingModes.Normal;

        CreateTMP(
            panel.transform,
            "StatsText",
            out var statsTMP,
            13,
            FontStyles.Normal,
            borrowedFont,
            borrowedMat
        );
        statsTMP.textWrappingMode = TextWrappingModes.Normal;
        statsTMP.color = new Color(0.7f, 0.85f, 1f);

        var tt = panel.GetComponent<ItemTooltip>();
        tt.rootPanel = panel;
        tt.nameText = nameTMP;
        tt.descText = descTMP;
        tt.statsText = statsTMP;
        tt.InitRefs();
        tt.Hide();

        return tt;
    }

    static GameObject CreateTMP(
        Transform parent,
        string name,
        out TextMeshProUGUI tmp,
        float fontSize,
        FontStyles style,
        TMP_FontAsset font = null,
        Material fontMaterial = null
    )
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null)
            tmp.font = font;
        if (fontMaterial != null)
            tmp.fontMaterial = fontMaterial;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.raycastTarget = false;
        return go;
    }

    void LateUpdate()
    {
        if (!_isShown || _canvasRT == null || _rt == null)
            return;

        Camera cam =
            _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        if (
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRT,
                Input.mousePosition,
                cam,
                out Vector2 local
            )
        )
            return;

        _rt.anchoredPosition = local + cursorOffset;
        ClampInsideCanvas();
    }

    public void Show(ScriptableObject item)
    {
        if (item == null)
        {
            Hide();
            return;
        }

        string name = "";
        string desc = "";
        string stats = "";
        Sprite icon = null;

        if (item is WeaponData w)
        {
            name =
                w.weaponName != null && !w.weaponName.IsEmpty
                    ? w.weaponName.GetLocalizedString()
                    : w.name;
            desc =
                w.description != null && !w.description.IsEmpty
                    ? w.description.GetLocalizedString()
                    : "";
            icon = w.sprite;
            stats = BuildWeaponStats(w);
        }
        else if (item is AccessoryData a)
        {
            name =
                a.accessoryName != null && !a.accessoryName.IsEmpty
                    ? a.accessoryName.GetLocalizedString()
                    : a.name;
            desc =
                a.description != null && !a.description.IsEmpty
                    ? a.description.GetLocalizedString()
                    : "";
            icon = a.icon;
            stats = BuildAccessoryStats(a);
        }
        else
        {
            name = item.name;
        }

        if (nameText != null)
            nameText.text = name;
        if (descText != null)
            descText.text = desc;
        if (statsText != null)
            statsText.text = stats;
        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }

        var target = rootPanel != null ? rootPanel : gameObject;
        target.SetActive(true);
        _isShown = true;

        // 텍스트 갱신 후 즉시 레이아웃을 재계산해 첫 프레임 크기 깜빡임 방지
        if (_rt != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_rt);
    }

    static string BuildWeaponStats(WeaponData w)
    {
        var sb = new StringBuilder();
        sb.Append($"공격력 {w.damage}");
        sb.Append($"  |  쿨타임 {w.attackCooldown}초");
        string typeName = w.weaponType switch
        {
            WeaponType.Melee => "근접",
            WeaponType.Ranged => "원거리",
            WeaponType.Magic => "마법",
            _ => ""
        };
        if (!string.IsNullOrEmpty(typeName))
            sb.Append($"  |  {typeName}");
        return sb.ToString();
    }

    static string BuildAccessoryStats(AccessoryData a)
    {
        var sb = new StringBuilder();
        void Add(string label, float val, string suffix = "")
        {
            if (val == 0) return;
            if (sb.Length > 0) sb.Append('\n');
            string sign = val > 0 ? "+" : "";
            sb.Append($"{label} {sign}{val}{suffix}");
        }
        void AddPercent(string label, float val)
        {
            if (val == 0) return;
            if (sb.Length > 0) sb.Append('\n');
            string sign = val > 0 ? "+" : "";
            sb.Append($"{label} {sign}{val * 100f:0.#}%");
        }
        void AddInt(string label, int val, string suffix = "")
        {
            if (val == 0) return;
            if (sb.Length > 0) sb.Append('\n');
            string sign = val > 0 ? "+" : "";
            sb.Append($"{label} {sign}{val}{suffix}");
        }

        Add("최대 HP", a.maxHpBonus);
        Add("공격력", a.damageBonus);
        Add("이동속도", a.speedBonus);
        Add("점프력", a.jumpBonus);
        AddPercent("크리티컬 확률", a.criticalChance);
        AddPercent("크리티컬 데미지", a.criticalDamage);
        AddPercent("공격속도", a.attackSpeedBonus);
        Add("방어력", a.damageReduction);
        AddPercent("받는 피해", a.damageReceivedMult);
        AddPercent("주는 피해", a.damageDealtMult);
        AddInt("대쉬 횟수", a.dashCountBonus);
        Add("대쉬 거리", a.dashRangeBonus);
        AddPercent("회피율", a.evasionRate);
        AddPercent("골드 드랍", a.goldDropBonus);
        AddInt("화살 수", a.arrowCount);
        AddPercent("화살 데미지", a.arrowDamageMult);
        AddInt("관통", a.penetrationCount);

        return sb.ToString();
    }

    public void Hide()
    {
        var target = rootPanel != null ? rootPanel : gameObject;
        target.SetActive(false);
        _isShown = false;
    }

    void ClampInsideCanvas()
    {
        if (_canvasRT == null || _rt == null)
            return;

        Vector2 canvasSize = _canvasRT.rect.size;
        Vector2 panelSize = _rt.rect.size;
        Vector2 pos = _rt.anchoredPosition;

        float minX = -canvasSize.x * 0.5f;
        float maxX = canvasSize.x * 0.5f - panelSize.x;
        float minY = -canvasSize.y * 0.5f + panelSize.y;
        float maxY = canvasSize.y * 0.5f;

        pos.x = Mathf.Clamp(pos.x, minX, maxX);
        pos.y = Mathf.Clamp(pos.y, minY, maxY);
        _rt.anchoredPosition = pos;
    }
}

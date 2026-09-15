using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 게임오버·게임클리어 결과창을 책 페이지 스프라이트(gameover&amp;clearUI) 위에 앉힌다.
///
/// 두 패널 모두 루트에 VerticalLayoutGroup 이 달려 있어서 자식이 전부 세로 행으로 배치된다.
/// 배경(BG)·딤(Dim)도 자식이라 그대로 두면 레이아웃이 이들을 한 행으로 취급해 찌그러뜨린다.
/// 또 BG 는 preserveAspect 로 그려지므로 패널 자체가 페이지 비율이 아니면
/// 책은 가운데 좁게 들어가고 글자만 그 밖으로 삐져나간다.
/// </summary>
public static class BookPageLayout
{
    /// <summary>책이 화면(캔버스)에서 차지할 비율. 1 이면 화면에 꽉 차 가장자리가 잘린다.</summary>
    const float ScreenFill = 0.9f;

    /// <summary>캔버스를 못 찾았을 때 쓰는 높이(레퍼런스 해상도 1080 기준).</summary>
    const float FallbackHeight = 900f;

    /// <summary>행 왼쪽 들여쓰기(양피지 안쪽 폭 대비). 모든 행이 같은 x 에서 시작하게 한다.</summary>
    const float RowIndent = 0.06f;

    // 페이지 스프라이트(960x1075) 안에서 실제 양피지가 차지하는 영역의 여백 비율.
    // (left, right, top, bottom) — 제본선 때문에 좌우 페이지의 가로 여백이 다르다.
    public static readonly Vector4 LeftPage = new Vector4(0.146f, 0.063f, 0.195f, 0.074f);
    public static readonly Vector4 RightPage = new Vector4(0.120f, 0.135f, 0.195f, 0.074f);

    /// <summary>배경·딤을 레이아웃 계산에서 빼 전체 영역을 그대로 덮게 한다.</summary>
    public static void IgnoreLayout(GameObject go)
    {
        var element = go.GetComponent<LayoutElement>();
        if (element == null)
            element = go.AddComponent<LayoutElement>();
        element.ignoreLayout = true;
    }

    /// <summary>
    /// 스탯 행을 같은 x 에서 시작하게 맞춘다.
    /// 프리팹의 행 padding.left 가 200 으로 박혀 있어 좁은 페이지에서는 글자가 밀려 줄바꿈되고,
    /// 아이콘은 투명 처리돼도 레이아웃 폭은 그대로 차지해 행마다 시작 위치가 어긋난다.
    /// </summary>
    static void AlignRow(RectTransform row, int indent)
    {
        var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
        if (rowLayout == null)
            return; // 제목·안내문은 행이 아니므로 그대로 둔다(가운데 정렬 유지).

        rowLayout.padding = new RectOffset(indent, indent, 0, 0);
        rowLayout.childAlignment = TextAnchor.MiddleLeft;

        // 보이지 않는 아이콘이 자리만 차지해 글자를 밀어내지 않게 한다.
        foreach (RectTransform child in row)
        {
            if (child.name == "Icon")
                IgnoreLayout(child.gameObject);
        }
    }

    /// <param name="inset">(left, right, top, bottom) — 페이지 크기 대비 비율</param>
    public static void Apply(RectTransform panel, Sprite pageSprite, Vector4 inset)
    {
        if (panel == null || pageSprite == null)
            return;

        // 패널을 페이지와 같은 비율로 맞춘다 → BG 가 패널을 정확히 가득 채운다.
        // 크기는 화면(부모 캔버스) 기준으로 잡아야 해상도가 바뀌어도 책이 잘리지 않는다.
        float aspect = pageSprite.rect.width / pageSprite.rect.height;
        float height = FallbackHeight;

        if (panel.parent is RectTransform canvasRect)
        {
            Vector2 avail = canvasRect.rect.size * ScreenFill;
            // 세로로 맞추되 가로가 넘치면 가로 기준으로 줄인다.
            height = Mathf.Min(avail.y, avail.x / aspect);
        }

        float width = height * aspect;

        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(width, height);

        var layout = panel.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
            return;

        layout.padding = new RectOffset(
            Mathf.RoundToInt(inset.x * width),
            Mathf.RoundToInt(inset.y * width),
            Mathf.RoundToInt(inset.z * height),
            Mathf.RoundToInt(inset.w * height)
        );

        // ChildControlWidth 가 꺼져 있어 행 너비는 각 행의 sizeDelta 를 그대로 쓴다.
        // 양피지 안쪽 너비로 맞춰야 아이콘·수치가 페이지 밖으로 나가지 않는다.
        float inner = width - layout.padding.left - layout.padding.right;
        int indent = Mathf.RoundToInt(inner * RowIndent);

        foreach (RectTransform child in panel)
        {
            if (child.name == "BG" || child.name == "Dim")
                continue;

            child.sizeDelta = new Vector2(inner, child.sizeDelta.y);
            AlignRow(child, indent);
        }

        LayoutRebuilder.MarkLayoutForRebuild(panel);
    }
}

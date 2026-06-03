using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ItemInfoWorldUI : MonoBehaviour
{
    [Tooltip("\u9762\u677F\u5728\u7269\u4F53\u53F3\u4FA7\u7684\u4E16\u754C\u7A7A\u95F4\u504F\u79FB")]
    public float sideOffset = 0.55f;

    [Tooltip("\u9762\u677F\u6700\u5927\u5BBD\u5EA6\uFF08\u50CF\u7D20\uFF09\u3002\u4ECB\u7ECD\u6587\u5B57\u6309\u6B64\u5BBD\u5EA6\u81EA\u52A8\u6362\u884C\u3002")]
    public float maxPanelWidth = 260f;

    [Tooltip("\u9762\u677F\u6700\u5C0F\u9AD8\u5EA6\uFF08\u50CF\u7D20\uFF09\u3002\u5185\u5BB9\u6BD4\u8FD9\u4E2A\u77ED\u65F6\u9762\u677F\u4ECD\u4FDD\u6301\u8BE5\u9AD8\u5EA6\u3002")]
    public float minPanelHeight = 72f;

    [Tooltip("\u9762\u677F\u6700\u5927\u9AD8\u5EA6\uFF08\u50CF\u7D20\uFF09\u3002\u5185\u5BB9\u8D85\u8FC7\u65F6\u4F1A\u88AB\u88C1\u5207\uFF0C\u53EF\u7528\u9F20\u6807\u6EDA\u8F6E\u4E0A\u4E0B\u6EDA\u52A8\u3002\u8BBE\u4E3A 0 \u8868\u793A\u4E0D\u9650\u5236\u3002")]
    public float maxPanelHeight = 210f;

    [Tooltip("\u9F20\u6807\u6EDA\u8F6E\u6EDA\u52A8\u7075\u654F\u5EA6\uFF08\u50CF\u7D20 / \u6EDA\u52A8\u5355\u4F4D\uFF09\u3002")]
    public float scrollSensitivity = 50f;

    [Tooltip("\u5185\u8FB9\u8DDD\uFF1A\u5DE6\u3001\u53F3\u3001\u4E0A\u3001\u4E0B\uFF08\u50CF\u7D20\uFF09")]
    public Vector4 padding = new Vector4(10f, 10f, 8f, 8f);

    [Tooltip("\u540D\u79F0\u4E0E\u6076\u641E\u4EF7\u503C\u4E4B\u95F4\u7684\u95F4\u8DDD")]
    public float gapNameToPrank = 1f;

    [Tooltip("\u6076\u641E\u4EF7\u503C\u4E0E\u4ECB\u7ECD\u4E4B\u95F4\u7684\u95F4\u8DDD")]
    public float gapPrankToDesc = 4f;

    [Tooltip("\u6CA1\u6709\u6076\u641E\u4EF7\u503C\u65F6\uFF0C\u540D\u79F0\u4E0E\u4ECB\u7ECD\u4E4B\u95F4\u7684\u95F4\u8DDD")]
    public float gapNameToDesc = 4f;

    [Tooltip("\u7070\u8272\u5E95\u8272")]
    public Color backgroundColor = new Color(0.22f, 0.22f, 0.22f, 0.9f);

    [Tooltip("\u6587\u5B57\u989C\u8272")]
    public Color textColor = Color.white;

    [Tooltip("\u540D\u79F0\u5B57\u53F7")]
    public float nameFontSize = 18f;

    [Tooltip("\u6076\u641E\u4EF7\u503C\u6807\u7B7E\u5B57\u53F7")]
    public float prankValueFontSize = 12f;

    [Tooltip("\u4ECB\u7ECD\u5B57\u53F7")]
    public float descriptionFontSize = 13f;

    Canvas canvas;
    RectTransform panelRect;
    RectTransform viewportRect;
    RectTransform contentRect;
    TMP_Text nameText;
    TMP_Text prankValueText;
    TMP_Text descriptionText;
    Camera uiCamera;
    Transform followTarget;
    float contentTotalHeight;
    float viewportHeight;

    public void Initialize(Camera camera)
    {
        uiCamera = camera;
        if (canvas != null) return;
        BuildUi();
    }

    public void Show(ItemInformation info, Transform anchor)
    {
        if (info == null || anchor == null || panelRect == null) return;

        followTarget = anchor;
        nameText.text = info.ResolvedDisplayName;
        descriptionText.text = info.itemDescription ?? string.Empty;

        bool showPrank = info.HasPrankValueLabel;
        if (prankValueText != null)
        {
            prankValueText.gameObject.SetActive(showPrank);
            if (showPrank)
                prankValueText.text = $"<b>{info.prankValueLabel}</b>";
        }

        LayoutPanel(showPrank);
        panelRect.gameObject.SetActive(true);
        UpdatePosition();
    }

    void LayoutPanel(bool showPrank)
    {
        float left = padding.x, right = padding.y, top = padding.z, bottom = padding.w;
        float contentWidth = Mathf.Max(40f, maxPanelWidth - left - right);

        float nameH = nameText.GetPreferredValues(nameText.text, contentWidth, 0f).y;
        float descH = descriptionText.GetPreferredValues(descriptionText.text, contentWidth, 0f).y;
        float prankH = 0f;
        if (showPrank && prankValueText != null)
            prankH = prankValueText.GetPreferredValues(prankValueText.text, contentWidth, 0f).y;

        float between = showPrank ? gapNameToPrank + prankH + gapPrankToDesc : gapNameToDesc;
        float contentH = top + nameH + between + descH + bottom;

        contentTotalHeight = contentH;
        float minH = Mathf.Max(0f, minPanelHeight);
        float maxH = maxPanelHeight > 0f ? maxPanelHeight : float.MaxValue;
        float panelH = Mathf.Clamp(contentH, minH, maxH);
        viewportHeight = panelH;

        panelRect.sizeDelta = new Vector2(maxPanelWidth, panelH);
        if (contentRect != null)
        {
            contentRect.sizeDelta = new Vector2(0f, contentH);
            contentRect.anchoredPosition = Vector2.zero;
        }

        float y = -top;
        PlaceText(nameText.rectTransform, left, contentWidth, y, nameH);
        y -= nameH;

        if (showPrank && prankValueText != null)
        {
            y -= gapNameToPrank;
            PlaceText(prankValueText.rectTransform, left, contentWidth, y, prankH);
            y -= prankH + gapPrankToDesc;
        }
        else
        {
            y -= gapNameToDesc;
        }
        PlaceText(descriptionText.rectTransform, left, contentWidth, y, descH);
    }

    static void PlaceText(RectTransform rt, float xLeft, float w, float yTop, float h)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(xLeft, yTop);
    }

    public void Hide()
    {
        followTarget = null;
        if (panelRect != null)
            panelRect.gameObject.SetActive(false);
    }

    void Update()
    {
        if (panelRect == null || !panelRect.gameObject.activeSelf) return;
        HandleScrollInput();
    }

    void HandleScrollInput()
    {
        if (contentRect == null) return;
        if (contentTotalHeight <= viewportHeight + 0.5f) return;

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) < 0.001f) return;

        Vector2 pos = contentRect.anchoredPosition;
        pos.y -= scroll * scrollSensitivity;
        float maxScroll = Mathf.Max(0f, contentTotalHeight - viewportHeight);
        pos.y = Mathf.Clamp(pos.y, 0f, maxScroll);
        contentRect.anchoredPosition = pos;
    }

    void LateUpdate()
    {
        if (followTarget == null || !panelRect.gameObject.activeSelf) return;
        UpdatePosition();
    }

    void UpdatePosition()
    {
        if (uiCamera == null || followTarget == null) return;

        Bounds bounds = CalculateWorldBounds(followTarget.gameObject);
        Vector3 worldPos = bounds.center + uiCamera.transform.right * sideOffset;
        Vector3 screenPos = uiCamera.WorldToScreenPoint(worldPos);

        if (screenPos.z <= 0f)
        {
            panelRect.gameObject.SetActive(false);
            return;
        }

        panelRect.gameObject.SetActive(true);
        panelRect.position = screenPos;
    }

    public static Bounds CalculateWorldBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(root.transform.position, Vector3.one * 0.5f);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("ItemInfoCanvas");
        canvasGo.transform.SetParent(transform, false);

        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;

        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGo.AddComponent<GraphicRaycaster>();

        // 面板根：固定宽度，高度由 LayoutPanel 计算
        var panelGo = new GameObject("ItemInfoPanel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        panelRect = panelGo.AddComponent<RectTransform>();
        panelRect.pivot = new Vector2(0f, 0.5f);
        panelRect.sizeDelta = new Vector2(maxPanelWidth, minPanelHeight);

        var bg = panelGo.AddComponent<Image>();
        bg.color = backgroundColor;

        // Viewport：占满 panel，加 RectMask2D 做裁切（超出 panel 的内容不可见）
        var viewportGo = new GameObject("Viewport");
        viewportGo.transform.SetParent(panelGo.transform, false);
        viewportRect = viewportGo.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        viewportGo.AddComponent<RectMask2D>();

        // Content：内容容器，pivot 顶部、水平 stretch；高度由 LayoutPanel 设置为内容总高度
        var contentGo = new GameObject("Content");
        contentGo.transform.SetParent(viewportGo.transform, false);
        contentRect = contentGo.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0f, 1f);
        contentRect.sizeDelta = new Vector2(0f, minPanelHeight);
        contentRect.anchoredPosition = Vector2.zero;

        nameText = CreateChildTmp(contentGo.transform, "ItemName", nameFontSize, FontStyles.Bold,
            TextAlignmentOptions.TopLeft, wrapping: true, ellipsis: false);

        prankValueText = CreateChildTmp(contentGo.transform, "ItemPrankValue", prankValueFontSize, FontStyles.Normal,
            TextAlignmentOptions.TopLeft, wrapping: false, ellipsis: true);

        descriptionText = CreateChildTmp(contentGo.transform, "ItemDescription", descriptionFontSize, FontStyles.Normal,
            TextAlignmentOptions.TopLeft, wrapping: true, ellipsis: false);

        panelRect.gameObject.SetActive(false);
    }

    TMP_Text CreateChildTmp(Transform parent, string name, float fontSize, FontStyles style,
        TextAlignmentOptions align, bool wrapping, bool ellipsis)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(maxPanelWidth, fontSize * 1.5f);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.color = textColor;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.enableWordWrapping = wrapping;
        tmp.overflowMode = ellipsis ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
        tmp.richText = true;
        return tmp;
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 准星对准物品时在屏幕准星旁显示名称与介绍。尺寸与偏移均基于 1920×1080，由 CanvasScaler 自适应。
/// </summary>
public class ItemInfoWorldUI : MonoBehaviour
{
    [Tooltip("面板相对准星（屏幕中心）的偏移，参考分辨率像素。X 正=右，Y 正=上。")]
    public Vector2 crosshairOffset = new Vector2(56f, 0f);

    [Tooltip("面板最大宽度（参考分辨率 1920×1080 像素）。")]
    public float maxPanelWidth = 520f;

    [Tooltip("面板最小高度（参考分辨率 1920×1080 像素）。")]
    public float minPanelHeight = 144f;

    [Tooltip("面板最大高度（参考分辨率 1920×1080 像素）。0 = 不限制。")]
    public float maxPanelHeight = 420f;

    [Tooltip("鼠标滚轮滚动灵敏度（参考分辨率像素 / 滚动单位）。")]
    public float scrollSensitivity = 100f;

    [Tooltip("内边距：左、右、上、下（参考分辨率像素）。")]
    public Vector4 padding = new Vector4(20f, 20f, 16f, 16f);

    [Tooltip("名称与介绍间距（参考分辨率像素）。")]
    public float gapNameToDesc = 8f;

    public Color backgroundColor = new Color(0.22f, 0.22f, 0.22f, 0.9f);
    public Color textColor = Color.white;

    [Tooltip("名称字号（参考分辨率 1920×1080）。")]
    public float nameFontSize = 36f;

    [Tooltip("介绍字号（参考分辨率 1920×1080）。")]
    public float descriptionFontSize = 26f;

    [Header("UI 引用（留空则运行时自动创建）")]
    public Canvas canvas;
    public CanvasScaler canvasScaler;
    public RectTransform panelRect;
    public RectTransform contentRect;
    public TMP_Text nameText;
    public TMP_Text descriptionText;

    Camera uiCamera;
    Transform followTarget;
    ItemInformation shownInfo;
    float contentTotalHeight;
    float viewportHeight;
    int _trackedScreenWidth;
    int _trackedScreenHeight;
    bool _ownsRuntimeCanvas;

    public void Initialize(Camera camera)
    {
        uiCamera = camera;
        EnsureUiBuilt();
        TrackScreenSize(forceRelayout: true);
    }

    /// <summary>编辑器烘焙 / 运行时共用。</summary>
    public void EnsureUiBuilt()
    {
        if (canvas != null && panelRect != null)
        {
            RuntimeUiUtility.NormalizeOverlayCanvas(canvas, transform);
            return;
        }

        BuildUi();
    }

    public void Show(ItemInformation info, Transform anchor)
    {
        if (info == null || anchor == null || panelRect == null) return;

        bool contentChanged = shownInfo != info;
        followTarget = anchor;
        shownInfo = info;

        if (contentChanged)
        {
            nameText.text = SanitizeForMsyh(info.ResolvedDisplayName);
            descriptionText.text = SanitizeForMsyh(info.itemDescription);
            TrackScreenSize(forceRelayout: true);
            LayoutPanel();
        }

        panelRect.gameObject.SetActive(true);
        UpdatePosition();
    }

    public void Hide()
    {
        followTarget = null;
        shownInfo = null;
        if (panelRect != null)
            panelRect.gameObject.SetActive(false);
    }

    void LayoutPanel()
    {
        float left = padding.x, right = padding.y, top = padding.z, bottom = padding.w;
        float contentWidth = Mathf.Max(40f, maxPanelWidth - left - right);

        float nameH = nameText.GetPreferredValues(nameText.text, contentWidth, 0f).y;
        float descH = descriptionText.GetPreferredValues(descriptionText.text, contentWidth, 0f).y;

        float contentH = top + nameH + gapNameToDesc + descH + bottom;

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
        y -= nameH + gapNameToDesc;
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

    void Update()
    {
        if (panelRect == null || !panelRect.gameObject.activeSelf) return;
        if (TrackScreenSize(forceRelayout: true))
            LayoutPanel();
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
        if (followTarget == null || panelRect == null || !panelRect.gameObject.activeSelf) return;
        UpdatePosition();
    }

    void UpdatePosition()
    {
        if (uiCamera == null || followTarget == null || panelRect == null) return;

        Vector3 worldCheck = CalculateWorldBounds(followTarget.gameObject).center;
        Vector3 screenCheck = uiCamera.WorldToScreenPoint(worldCheck);
        if (screenCheck.z <= 0f)
        {
            panelRect.gameObject.SetActive(false);
            return;
        }

        panelRect.gameObject.SetActive(true);

        float scale = canvas != null && canvas.scaleFactor > 0f
            ? canvas.scaleFactor
            : GameDisplaySettings.UiScaleFactor;
        float panelScreenW = maxPanelWidth * scale;
        float margin = GameDisplaySettings.ScaleDesignPixels(16f);
        float centerX = Screen.width * 0.5f;
        float gap = Mathf.Abs(crosshairOffset.x);

        bool placeOnRight = centerX + gap + panelScreenW + margin <= Screen.width;
        if (!placeOnRight && centerX - gap - panelScreenW - margin < 0f)
            placeOnRight = true;

        SetPanelSide(placeOnRight);

        float offsetX = Mathf.Abs(crosshairOffset.x);
        float offsetY = crosshairOffset.y;
        panelRect.anchoredPosition = placeOnRight
            ? new Vector2(offsetX, offsetY)
            : new Vector2(-offsetX, offsetY);
    }

    void SetPanelSide(bool placeOnRight)
    {
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(placeOnRight ? 0f : 1f, 0.5f);
    }

    bool TrackScreenSize(bool forceRelayout)
    {
        GameDisplaySettings.RefreshUiScaleFactorIfNeeded();
        if (!forceRelayout &&
            Screen.width == _trackedScreenWidth &&
            Screen.height == _trackedScreenHeight)
            return false;

        _trackedScreenWidth = Screen.width;
        _trackedScreenHeight = Screen.height;

        if (canvasScaler != null)
            canvasScaler.referenceResolution = GameDisplaySettings.DesignReferenceResolution;

        return true;
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

    static string SanitizeForMsyh(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        return text
            .Replace('\u2018', '\u300C')
            .Replace('\u2019', '\u300D')
            .Replace('\u201C', '\u300C')
            .Replace('\u201D', '\u300D');
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("ItemInfoCanvas");
        canvasGo.transform.SetParent(transform, false);

        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;

        canvasScaler = canvasGo.AddComponent<CanvasScaler>();
        RuntimeUiUtility.ConfigureOverlayCanvasScaler(canvasScaler);
        canvasGo.AddComponent<GraphicRaycaster>();

        var canvasRect = canvasGo.GetComponent<RectTransform>();
        canvasRect.anchorMin = Vector2.zero;
        canvasRect.anchorMax = Vector2.one;
        canvasRect.offsetMin = Vector2.zero;
        canvasRect.offsetMax = Vector2.zero;

        var panelGo = new GameObject("ItemInfoPanel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        panelRect = panelGo.AddComponent<RectTransform>();
        SetPanelSide(true);
        panelRect.sizeDelta = new Vector2(maxPanelWidth, minPanelHeight);

        var bg = panelGo.AddComponent<Image>();
        bg.color = backgroundColor;

        var viewportGo = new GameObject("Viewport");
        viewportGo.transform.SetParent(panelGo.transform, false);
        var viewportRect = viewportGo.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        viewportGo.AddComponent<RectMask2D>();

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

        descriptionText = CreateChildTmp(contentGo.transform, "ItemDescription", descriptionFontSize, FontStyles.Normal,
            TextAlignmentOptions.TopLeft, wrapping: true, ellipsis: false);

        panelRect.gameObject.SetActive(false);
        _ownsRuntimeCanvas = Application.isPlaying;
        RuntimeUiUtility.MarkPlayModeOnly(canvasGo);
        RuntimeUiUtility.NormalizeOverlayCanvas(canvas, transform);
    }

    void OnDestroy()
    {
        if (!_ownsRuntimeCanvas || canvas == null) return;

        GameObject go = canvas.gameObject;
        canvas = null;
        if (go == null) return;

        if (Application.isPlaying)
            Destroy(go);
        else
            DestroyImmediate(go);
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

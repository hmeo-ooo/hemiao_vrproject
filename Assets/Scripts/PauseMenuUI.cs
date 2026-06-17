using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 按 Esc 弹出的暂停界面：展示可在 Inspector 中编辑的游戏教程，并提供回到游戏按钮。
/// </summary>
public class PauseMenuUI : MonoBehaviour
{
    [Header("UI 引用（留空则运行时自动创建）")]
    public GameObject panelRoot;
    public TMP_Text pauseTitleText;
    public TMP_Text tutorialBodyText;
    public Button resumeButton;
    public TMP_Text resumeButtonLabel;

    [Header("文案（可在 Inspector 中编辑）")]
    public string pauseTitle = "游戏暂停";

    [TextArea(6, 24)]
    public string tutorialBody =
        "移动：W / A / S / D\n" +
        "视角：移动鼠标\n" +
        "跳跃：空格\n" +
        "下蹲：C 或 Left Ctrl\n" +
        "抓取 / 放下：鼠标左键\n" +
        "分类投放：将垃圾扔进对应颜色的通道\n" +
        "暂停：Esc";

    public string resumeLabel = "回到游戏";

    [Header("字体（中文请指定 TMP Font Asset）")]
    public TMP_FontAsset uiFont;

    Canvas _overlayCanvas;
    Action _onResume;

    void Awake()
    {
        EnsureUiBuilt();
        HideImmediate();
    }

    void OnEnable()
    {
        if (resumeButton != null)
            resumeButton.onClick.AddListener(HandleResumeClicked);
    }

    void OnDisable()
    {
        if (resumeButton != null)
            resumeButton.onClick.RemoveListener(HandleResumeClicked);
    }

    public void Show(Action onResume)
    {
        EnsureUiBuilt();
        _onResume = onResume;
        ApplyCopy();

        if (panelRoot != null)
            panelRoot.SetActive(true);
        if (_overlayCanvas != null)
            _overlayCanvas.enabled = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Hide()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
        if (_overlayCanvas != null)
            _overlayCanvas.enabled = false;

        _onResume = null;
    }

    void HideImmediate()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
        if (_overlayCanvas != null)
            _overlayCanvas.enabled = false;
    }

    void OnDestroy()
    {
        RuntimeUiUtility.DestroyCanvas(ref _overlayCanvas);
        panelRoot = null;
        pauseTitleText = null;
        tutorialBodyText = null;
        resumeButton = null;
        resumeButtonLabel = null;
    }

    void HandleResumeClicked()
    {
        Action cb = _onResume;
        Hide();
        cb?.Invoke();
    }

    void ApplyCopy()
    {
        if (pauseTitleText != null)
            pauseTitleText.text = pauseTitle ?? string.Empty;
        if (tutorialBodyText != null)
            tutorialBodyText.text = tutorialBody ?? string.Empty;
        if (resumeButtonLabel != null)
            resumeButtonLabel.text = resumeLabel ?? string.Empty;

        ApplyFont();
    }

    void ApplyFont()
    {
        TMP_FontAsset font = uiFont != null ? uiFont : TMP_Settings.defaultFontAsset;
        if (font == null) return;

        if (pauseTitleText != null) pauseTitleText.font = font;
        if (tutorialBodyText != null) tutorialBodyText.font = font;
        if (resumeButtonLabel != null) resumeButtonLabel.font = font;
    }

    void EnsureUiBuilt()
    {
        if (panelRoot != null) return;
        BuildRuntimeUi();
    }

    void BuildRuntimeUi()
    {
        var canvasGo = new GameObject("PauseMenuCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(null, false);

        _overlayCanvas = canvasGo.GetComponent<Canvas>();
        _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _overlayCanvas.sortingOrder = 280;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        panelRoot = CreatePanel(canvasGo.transform);
        panelRoot.name = "PausePanel";
        panelRoot.SetActive(false);

        Image bg = panelRoot.GetComponent<Image>();
        if (bg != null)
            bg.color = new Color(0f, 0f, 0f, 0.72f);

        var card = CreateCard(panelRoot.transform);

        pauseTitleText = CreateAnchoredTmp(card.transform, "PauseTitle",
            new Vector2(0.06f, 0.86f), new Vector2(0.94f, 0.96f),
            44, FontStyles.Bold, TextAlignmentOptions.Center);

        tutorialBodyText = CreateScrollableBody(card.transform, "TutorialScroll",
            new Vector2(0.06f, 0.18f), new Vector2(0.94f, 0.84f));

        resumeButton = CreateAnchoredButton(card.transform, "ResumeButton",
            new Vector2(0.5f, 0.08f), new Vector2(280f, 64f));
        resumeButtonLabel = resumeButton.GetComponentInChildren<TMP_Text>();

        ApplyCopy();
        RuntimeUiUtility.MarkPlayModeOnly(canvasGo);
    }

    static GameObject CreatePanel(Transform parent)
    {
        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);

        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);
        return panel;
    }

    static GameObject CreateCard(Transform parent)
    {
        var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(parent, false);

        var rt = card.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(920f, 720f);
        rt.anchoredPosition = Vector2.zero;

        card.GetComponent<Image>().color = new Color(0.05f, 0.08f, 0.12f, 0.96f);
        return card;
    }

    static TMP_Text CreateAnchoredTmp(
        Transform parent,
        string name,
        Vector2 anchorMin,
        Vector2 anchorMax,
        float fontSize,
        FontStyles style,
        TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = Color.white;
        tmp.alignment = align;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.richText = true;

        TMP_FontAsset font = TMP_Settings.defaultFontAsset;
        if (font != null)
            tmp.font = font;

        return tmp;
    }

    static TMP_Text CreateScrollableBody(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        var scrollGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(parent, false);

        var scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchorMin = anchorMin;
        scrollRt.anchorMax = anchorMax;
        scrollRt.offsetMin = Vector2.zero;
        scrollRt.offsetMax = Vector2.zero;

        var scrollBg = scrollGo.GetComponent<Image>();
        scrollBg.color = new Color(0.08f, 0.11f, 0.16f, 0.9f);

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(scrollGo.transform, false);

        var viewportRt = viewport.GetComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = new Vector2(8f, 8f);
        viewportRt.offsetMax = new Vector2(-8f, -8f);

        var viewportImg = viewport.GetComponent<Image>();
        viewportImg.color = new Color(1f, 1f, 1f, 0.02f);
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        var content = new GameObject("Content", typeof(RectTransform), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);

        var contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0f, 0f);

        var fitter = content.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var bodyGo = new GameObject("TutorialBody", typeof(RectTransform));
        bodyGo.transform.SetParent(content.transform, false);

        var bodyRt = bodyGo.GetComponent<RectTransform>();
        bodyRt.anchorMin = new Vector2(0f, 1f);
        bodyRt.anchorMax = new Vector2(1f, 1f);
        bodyRt.pivot = new Vector2(0.5f, 1f);
        bodyRt.anchoredPosition = Vector2.zero;
        bodyRt.sizeDelta = new Vector2(-16f, 0f);

        var tmp = bodyGo.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 26;
        tmp.color = new Color(0.92f, 0.94f, 0.98f, 1f);
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.richText = true;

        TMP_FontAsset font = TMP_Settings.defaultFontAsset;
        if (font != null)
            tmp.font = font;

        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.viewport = viewportRt;
        scroll.content = contentRt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;

        return tmp;
    }

    static Button CreateAnchoredButton(Transform parent, string name, Vector2 anchor, Vector2 size)
    {
        var btnGo = new GameObject(name,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnGo.transform.SetParent(parent, false);

        var rt = btnGo.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;

        var img = btnGo.GetComponent<Image>();
        img.color = Color.white;

        var btn = btnGo.GetComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = new Color(1f, 1f, 1f, 0.3f);
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.45f);
        colors.pressedColor = new Color(1f, 1f, 1f, 0.2f);
        colors.selectedColor = colors.highlightedColor;
        btn.colors = colors;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(btnGo.transform, false);

        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        var tmp = labelGo.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 28;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        return btn;
    }
}

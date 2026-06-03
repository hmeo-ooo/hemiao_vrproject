using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 每关开始前的指引 / 教程 UI（单页）。
/// 由 LevelSessionController 在玩家点击「进入关卡」后调用 Show。
/// </summary>
public class LevelTutorialUI : MonoBehaviour
{
    [Header("UI 引用（留空则运行时自动创建）")]
    public GameObject panelRoot;
    public TMP_Text titleText;
    public TMP_Text bodyText;
    public Button startButton;
    public TMP_Text startButtonLabel;
    public Button backButton;
    public TMP_Text backButtonLabel;

    [Header("文案")]
    public string startLabel = "开始关卡";
    public string backLabel = "返回";

    [Header("字体（中文请指定 TMP Font Asset）")]
    public TMP_FontAsset uiFont;

    Canvas _overlayCanvas;
    Action _onConfirmed;
    Action _onBack;

    void Awake()
    {
        EnsureUiBuilt();
        HideImmediate();
    }

    void OnEnable()
    {
        if (startButton != null)
            startButton.onClick.AddListener(HandleStartClicked);
        if (backButton != null)
            backButton.onClick.AddListener(HandleBackClicked);
    }

    void OnDisable()
    {
        if (startButton != null)
            startButton.onClick.RemoveListener(HandleStartClicked);
        if (backButton != null)
            backButton.onClick.RemoveListener(HandleBackClicked);
    }

    public void Show(LevelDefinition level, Action onConfirmed, Action onBack = null)
    {
        EnsureUiBuilt();
        _onConfirmed = onConfirmed;
        _onBack = onBack;

        if (level != null)
        {
            if (titleText != null)
                titleText.text = level.ResolveTutorialTitle();
            if (bodyText != null)
                bodyText.text = level.tutorialBody ?? string.Empty;
        }

        if (startButtonLabel != null)
            startButtonLabel.text = startLabel;
        if (backButtonLabel != null)
            backButtonLabel.text = backLabel;

        EnsureBodyWrapping();
        ApplyFont();

        if (panelRoot != null)
            panelRoot.SetActive(true);
        if (_overlayCanvas != null)
            _overlayCanvas.enabled = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void EnsureBodyWrapping()
    {
        if (bodyText != null)
        {
            bodyText.enableWordWrapping = true;
            bodyText.overflowMode = TextOverflowModes.Overflow;
        }
        if (titleText != null)
        {
            titleText.enableWordWrapping = true;
            titleText.overflowMode = TextOverflowModes.Overflow;
        }
    }

    public void Hide()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
        if (_overlayCanvas != null)
            _overlayCanvas.enabled = false;

        _onConfirmed = null;
        _onBack = null;
    }

    void HideImmediate()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
        if (_overlayCanvas != null)
            _overlayCanvas.enabled = false;
    }

    void HandleStartClicked()
    {
        Action cb = _onConfirmed;
        Hide();
        cb?.Invoke();
    }

    void HandleBackClicked()
    {
        Action cb = _onBack;
        Hide();
        cb?.Invoke();
    }

    void EnsureUiBuilt()
    {
        if (panelRoot != null) return;
        BuildRuntimeUi();
    }

    void BuildRuntimeUi()
    {
        var canvasGo = new GameObject("LevelTutorialCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        _overlayCanvas = canvasGo.GetComponent<Canvas>();
        _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _overlayCanvas.sortingOrder = 250;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        panelRoot = CreatePanel(canvasGo.transform);
        panelRoot.name = "TutorialPanel";
        panelRoot.SetActive(false);

        Image bg = panelRoot.GetComponent<Image>();
        if (bg != null)
            bg.color = new Color(0.04f, 0.06f, 0.1f, 0.96f);

        // 标题：占顶部 ~10% 高度，左右各留 5%
        titleText = CreateAnchoredTmp(panelRoot.transform, "TitleText",
            new Vector2(0.05f, 0.84f), new Vector2(0.95f, 0.94f),
            48, FontStyles.Bold, TextAlignmentOptions.Center);

        // 正文：占中部，左右各留 8%，上下与标题/按钮留间距
        bodyText = CreateAnchoredTmp(panelRoot.transform, "BodyText",
            new Vector2(0.08f, 0.20f), new Vector2(0.92f, 0.80f),
            30, FontStyles.Normal, TextAlignmentOptions.TopLeft);

        // 按钮行：底部居中
        var buttonRow = new GameObject("ButtonRow", typeof(RectTransform));
        buttonRow.transform.SetParent(panelRoot.transform, false);
        var rowRt = buttonRow.GetComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0.5f, 0.06f);
        rowRt.anchorMax = new Vector2(0.5f, 0.14f);
        rowRt.pivot = new Vector2(0.5f, 0.5f);
        rowRt.sizeDelta = new Vector2(560f, 80f);
        rowRt.anchoredPosition = Vector2.zero;

        var hLayout = buttonRow.AddComponent<HorizontalLayoutGroup>();
        hLayout.childAlignment = TextAnchor.MiddleCenter;
        hLayout.spacing = 24;
        hLayout.childControlWidth = false;
        hLayout.childControlHeight = false;
        hLayout.childForceExpandWidth = false;
        hLayout.childForceExpandHeight = false;

        backButton = CreateButton(buttonRow.transform, backLabel, new Vector2(220, 72));
        startButton = CreateButton(buttonRow.transform, startLabel, new Vector2(260, 72));
        startButtonLabel = startButton.GetComponentInChildren<TMP_Text>();
        backButtonLabel = backButton.GetComponentInChildren<TMP_Text>();

        EnsureBodyWrapping();
        ApplyFont();
    }

    void ApplyFont()
    {
        TMP_FontAsset font = uiFont != null ? uiFont : TMP_Settings.defaultFontAsset;
        if (font == null) return;

        if (titleText != null) titleText.font = font;
        if (bodyText != null) bodyText.font = font;
        if (startButtonLabel != null) startButtonLabel.font = font;
        if (backButtonLabel != null) backButtonLabel.font = font;
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

        panel.GetComponent<Image>().color = new Color(0.04f, 0.06f, 0.1f, 0.96f);
        return panel;
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

    static Button CreateButton(Transform parent, string label, Vector2 size)
    {
        var btnGo = new GameObject(label + "Button",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnGo.transform.SetParent(parent, false);

        var rt = btnGo.GetComponent<RectTransform>();
        rt.sizeDelta = size;

        var le = btnGo.AddComponent<LayoutElement>();
        le.minWidth = size.x;
        le.preferredWidth = size.x;
        le.minHeight = size.y;
        le.preferredHeight = size.y;

        var img = btnGo.GetComponent<Image>();
        img.color = new Color(0.2f, 0.45f, 0.75f, 1f);

        var btn = btnGo.GetComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = img.color;
        colors.highlightedColor = new Color(0.28f, 0.55f, 0.88f, 1f);
        colors.pressedColor = new Color(0.16f, 0.36f, 0.62f, 1f);
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
        tmp.text = label;
        tmp.fontSize = 28;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        return btn;
    }
}

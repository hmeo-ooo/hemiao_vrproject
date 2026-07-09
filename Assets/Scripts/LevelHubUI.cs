using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 关卡开始前 / 倒计时结束后的准备界面。
/// </summary>
public class LevelHubUI : MonoBehaviour
{
    [Header("可选：留空则在运行时自动创建 Screen Space UI")]
    public GameObject panelRoot;
    public TMP_Text levelText;
    public TMP_Text creditsText;
    public TMP_Text debtText;
    public TMP_Text statusText;
    public Button enterLevelButton;
    public Button repayDebtButton;
    public Button selectLevelButton;

    [Header("关卡选择面板（可选，留空自动创建）")]
    public GameObject levelSelectPanel;
    public Transform levelSelectGrid;
    public Button levelSelectBackButton;

    [Header("文案（LiberationSans 仅支持拉丁字符，中文请指定 Hub Font）")]
    public TMP_FontAsset hubFont;

    public string levelFormat = "Day {0}";

    [Tooltip("关卡选择面板里每个按钮的文案格式，{0} = LevelDefinition.levelNumber。\n" +
             "留空时按钮使用 LevelDefinition.displayName。")]
    public string levelButtonFormat = "Day {0}";

    public string creditsFormat = "Credits: {0}";
    public string debtFormat = "Debt: {0:N0}";
    public string enterButtonLabel = "Enter Level";
    public string repayButtonLabel = "Repay Debt";
    public string selectLevelButtonLabel = "Select Day";
    public string selectLevelTitle = "Select Day";
    public string selectLevelBackLabel = "Back";

    [Header("Hub 按钮样式")]
    public Color hubButtonColor = new Color(1f, 1f, 1f, 0.3f);
    public Vector2 hubMainButtonSize = new Vector2(240f, 52f);

    [Header("关卡选择按钮样式")]
    public Vector2 levelSelectButtonSize = new Vector2(150f, 72f);
    public float levelSelectBackButtonWidth = 160f;
    public float levelSelectBackButtonHeight = 52f;

    Canvas _overlayCanvas;
    bool _ownsRuntimeCanvas;
    LevelSessionController _session;
    readonly System.Collections.Generic.List<Button> _levelButtons = new System.Collections.Generic.List<Button>();

    void Awake()
    {
        EnsureUiBuilt();
        HideImmediate();
    }

    public void Bind(LevelSessionController session)
    {
        _session = session;
        EnsureUiBuilt();
        WireButtons();
    }

    public void EnsureUiBuilt()
    {
        if (panelRoot != null)
        {
            if (_overlayCanvas == null)
                _overlayCanvas = panelRoot.GetComponentInParent<Canvas>();
            RuntimeUiUtility.NormalizeOverlayCanvas(_overlayCanvas, transform);
            return;
        }

        BuildRuntimeUi();
    }

    void WireButtons()
    {
        if (_session == null) return;

        if (enterLevelButton != null)
        {
            enterLevelButton.onClick.RemoveAllListeners();
            enterLevelButton.onClick.AddListener(_session.OnEnterLevelButtonClicked);
        }

        if (repayDebtButton != null)
        {
            repayDebtButton.onClick.RemoveAllListeners();
            repayDebtButton.onClick.AddListener(_session.OnRepayDebtButtonClicked);
        }

        if (selectLevelButton != null)
        {
            selectLevelButton.onClick.RemoveAllListeners();
            selectLevelButton.onClick.AddListener(OpenLevelSelect);
        }

        if (levelSelectBackButton != null)
        {
            levelSelectBackButton.onClick.RemoveAllListeners();
            levelSelectBackButton.onClick.AddListener(CloseLevelSelect);
        }
    }

    public void OpenLevelSelect()
    {
        if (levelSelectPanel == null) return;
        RebuildLevelButtons();
        levelSelectPanel.SetActive(true);
    }

    public void CloseLevelSelect()
    {
        if (levelSelectPanel != null)
            levelSelectPanel.SetActive(false);
    }

    void HandleLevelButtonClicked(int index)
    {
        CloseLevelSelect();
        if (_session != null)
            _session.OnLevelPicked(index);
    }

    void RebuildLevelButtons()
    {
        if (levelSelectGrid == null) return;

        for (int i = levelSelectGrid.childCount - 1; i >= 0; i--)
        {
            Transform child = levelSelectGrid.GetChild(i);
            if (child == null) continue;
            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }

        _levelButtons.Clear();

        LevelManager lm = LevelManager.Instance;
        if (lm == null) return;

        TMP_FontAsset font = ResolveHubFont();

        for (int i = 0; i < lm.LevelCount; i++)
        {
            LevelDefinition def = lm.levels[i];
            string label = BuildLevelButtonLabel(def, i);

            int captured = i;
            Button btn = CreateLevelSelectButton(levelSelectGrid, label);
            ApplyFontToButton(btn, font);

            Image img = btn.GetComponent<Image>();
            if (img != null)
                ApplyHubButtonColor(img, def == null);

            btn.interactable = def != null;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => HandleLevelButtonClicked(captured));
            _levelButtons.Add(btn);
        }
    }

    void OnEnable()
    {
        Subscribe();
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void Subscribe()
    {
        if (CreditManager.Instance != null)
            CreditManager.Instance.OnCreditsChanged += HandleCreditsChanged;
        if (DebtManager.Instance != null)
            DebtManager.Instance.OnDebtChanged += HandleDebtChanged;
        if (LevelManager.Instance != null)
            LevelManager.Instance.LevelLoaded += HandleLevelLoaded;
    }

    void Unsubscribe()
    {
        if (CreditManager.Instance != null)
            CreditManager.Instance.OnCreditsChanged -= HandleCreditsChanged;
        if (DebtManager.Instance != null)
            DebtManager.Instance.OnDebtChanged -= HandleDebtChanged;
        if (LevelManager.Instance != null)
            LevelManager.Instance.LevelLoaded -= HandleLevelLoaded;
    }

    void HandleCreditsChanged(int _) => Refresh();
    void HandleDebtChanged(int _) => Refresh();
    void HandleLevelLoaded(LevelDefinition _) => Refresh();

    public void Show()
    {
        RuntimeUiUtility.NormalizeOverlayCanvas(_overlayCanvas, transform);

        if (panelRoot != null)
            panelRoot.SetActive(true);
        if (levelSelectPanel != null)
            levelSelectPanel.SetActive(false);
        if (_overlayCanvas != null)
            _overlayCanvas.enabled = true;

        Refresh();
        ApplyAllButtonStyles();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Hide()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
        if (levelSelectPanel != null)
            levelSelectPanel.SetActive(false);
        if (_overlayCanvas != null)
            _overlayCanvas.enabled = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void HideImmediate()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
        if (levelSelectPanel != null)
            levelSelectPanel.SetActive(false);
        if (_overlayCanvas != null)
            _overlayCanvas.enabled = false;
    }

    void OnDestroy()
    {
        if (_ownsRuntimeCanvas)
            RuntimeUiUtility.DestroyCanvas(ref _overlayCanvas);
        _overlayCanvas = null;
        _levelButtons.Clear();
    }

    public void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message ?? string.Empty;
    }

    public void Refresh()
    {
        LevelDefinition def = LevelManager.Instance != null ? LevelManager.Instance.CurrentLevel : null;
        int levelNum = def != null ? def.levelNumber : 1;

        if (levelText != null)
            levelText.text = SafeFormat(levelFormat, levelNum);

        int credits = CreditManager.Instance != null ? CreditManager.Instance.credits : 0;
        if (creditsText != null)
            creditsText.text = SafeFormat(creditsFormat, credits);

        int debt = DebtManager.Instance != null ? DebtManager.Instance.CurrentDebt : 0;
        if (debtText != null)
            debtText.text = SafeFormat(debtFormat, debt);

        if (repayDebtButton != null)
        {
            bool canRepay = debt > 0 && credits > 0 && CreditManager.Instance != null;
            repayDebtButton.interactable = canRepay;
            ApplyHubButtonColor(repayDebtButton.GetComponent<Image>(), !canRepay);
        }
    }

    string BuildLevelButtonLabel(LevelDefinition def, int slotIndex)
    {
        if (def == null)
            return $"(empty {slotIndex + 1})";

        if (!string.IsNullOrEmpty(levelButtonFormat))
            return SafeFormat(levelButtonFormat, def.levelNumber);

        if (!string.IsNullOrEmpty(def.displayName))
            return def.displayName;

        return $"Day {def.levelNumber}";
    }

    static string SafeFormat(string format, object value)
    {
        if (string.IsNullOrEmpty(format))
            return value?.ToString() ?? string.Empty;

        try
        {
            return string.Format(format, value);
        }
        catch (FormatException)
        {
            Debug.LogWarning($"[LevelHubUI] Invalid format string: \"{format}\". Using plain value.");
            return $"{format} {value}";
        }
    }

    void BuildRuntimeUi()
    {
        var canvasGo = new GameObject("LevelHubCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        _overlayCanvas = canvasGo.GetComponent<Canvas>();
        _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _overlayCanvas.sortingOrder = 200;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        RuntimeUiUtility.ConfigureOverlayCanvasScaler(scaler);

        panelRoot = CreatePanel(canvasGo.transform);
        panelRoot.SetActive(false);

        var vLayout = panelRoot.AddComponent<VerticalLayoutGroup>();
        vLayout.childAlignment = TextAnchor.MiddleCenter;
        vLayout.spacing = 16;
        vLayout.padding = new RectOffset(48, 48, 48, 48);
        vLayout.childControlWidth = true;
        vLayout.childControlHeight = true;
        vLayout.childForceExpandWidth = false;
        vLayout.childForceExpandHeight = false;

        levelText = CreateTmp(panelRoot.transform, 36, FontStyles.Bold);
        creditsText = CreateTmp(panelRoot.transform, 28, FontStyles.Normal);
        debtText = CreateTmp(panelRoot.transform, 28, FontStyles.Normal);
        statusText = CreateTmp(panelRoot.transform, 22, FontStyles.Italic);
        statusText.color = new Color(1f, 0.85f, 0.4f);

        enterLevelButton = CreateHubButton(panelRoot.transform, enterButtonLabel, hubMainButtonSize);
        selectLevelButton = CreateHubButton(panelRoot.transform, selectLevelButtonLabel, hubMainButtonSize);
        repayDebtButton = CreateHubButton(panelRoot.transform, repayButtonLabel, hubMainButtonSize);

        BuildLevelSelectPanel(canvasGo.transform);
        ApplyHubFont();
        ApplyAllButtonStyles();
        _ownsRuntimeCanvas = Application.isPlaying;
        RuntimeUiUtility.MarkPlayModeOnly(canvasGo);
    }

    void BuildLevelSelectPanel(Transform canvasRoot)
    {
        levelSelectPanel = CreatePanel(canvasRoot);
        levelSelectPanel.name = "LevelSelectPanel";
        levelSelectPanel.SetActive(false);

        Image bg = levelSelectPanel.GetComponent<Image>();
        if (bg != null)
            bg.color = new Color(0.04f, 0.06f, 0.1f, 0.95f);

        var vLayout = levelSelectPanel.AddComponent<VerticalLayoutGroup>();
        vLayout.childAlignment = TextAnchor.MiddleCenter;
        vLayout.spacing = 18;
        vLayout.padding = new RectOffset(64, 64, 64, 64);
        vLayout.childControlWidth = true;
        vLayout.childControlHeight = true;
        vLayout.childForceExpandWidth = false;
        vLayout.childForceExpandHeight = false;

        TMP_Text title = CreateTmp(levelSelectPanel.transform, 42, FontStyles.Bold);
        title.text = selectLevelTitle;

        var gridGo = new GameObject("LevelGrid", typeof(RectTransform));
        gridGo.transform.SetParent(levelSelectPanel.transform, false);

        var gridLe = gridGo.AddComponent<LayoutElement>();
        gridLe.minHeight = 300;
        gridLe.preferredHeight = 400;

        var grid = gridGo.AddComponent<GridLayoutGroup>();
        grid.cellSize = levelSelectButtonSize;
        grid.spacing = new Vector2(12, 12);
        grid.childAlignment = TextAnchor.MiddleCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 5;
        levelSelectGrid = gridGo.transform;

        levelSelectBackButton = CreateLevelSelectButton(levelSelectPanel.transform, selectLevelBackLabel);
        var backLe = levelSelectBackButton.GetComponent<LayoutElement>();
        if (backLe != null)
        {
            backLe.preferredWidth = levelSelectBackButtonWidth;
            backLe.minWidth = levelSelectBackButtonWidth;
            backLe.preferredHeight = levelSelectBackButtonHeight;
            backLe.minHeight = levelSelectBackButtonHeight;
        }

        Image backImg = levelSelectBackButton.GetComponent<Image>();
        if (backImg != null)
            ApplyHubButtonColor(backImg, false);
    }

    void ApplyAllButtonStyles()
    {
        StyleHubButton(enterLevelButton, hubMainButtonSize);
        StyleHubButton(selectLevelButton, hubMainButtonSize);
        StyleHubButton(repayDebtButton, hubMainButtonSize, treatAsDisabled: repayDebtButton != null && !repayDebtButton.interactable);

        if (levelSelectBackButton != null)
        {
            var backLe = levelSelectBackButton.GetComponent<LayoutElement>();
            if (backLe != null)
            {
                backLe.preferredWidth = levelSelectBackButtonWidth;
                backLe.minWidth = levelSelectBackButtonWidth;
                backLe.preferredHeight = levelSelectBackButtonHeight;
                backLe.minHeight = levelSelectBackButtonHeight;
            }
            ApplyHubButtonColor(levelSelectBackButton.GetComponent<Image>(), false);
        }
    }

    void StyleHubButton(Button btn, Vector2 size, bool treatAsDisabled = false)
    {
        if (btn == null) return;

        var le = btn.GetComponent<LayoutElement>();
        if (le == null) le = btn.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = size.x;
        le.minWidth = size.x;
        le.preferredHeight = size.y;
        le.minHeight = size.y;

        ApplyHubButtonColor(btn.GetComponent<Image>(), treatAsDisabled);
    }

    void ApplyHubFont()
    {
        TMP_FontAsset font = ResolveHubFont();
        if (font == null) return;

        ApplyFont(levelText, font);
        ApplyFont(creditsText, font);
        ApplyFont(debtText, font);
        ApplyFont(statusText, font);
        ApplyFontToButton(enterLevelButton, font);
        ApplyFontToButton(repayDebtButton, font);
        ApplyFontToButton(selectLevelButton, font);
        ApplyFontToButton(levelSelectBackButton, font);

        if (levelSelectPanel != null)
        {
            foreach (var tmp in levelSelectPanel.GetComponentsInChildren<TMP_Text>(true))
                if (tmp != null) tmp.font = font;
        }
    }

    TMP_FontAsset ResolveHubFont()
    {
        if (hubFont != null) return hubFont;
        return Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
    }

    static void ApplyFont(TMP_Text text, TMP_FontAsset font)
    {
        if (text == null || font == null) return;
        text.font = font;
    }

    static void ApplyFontToButton(Button button, TMP_FontAsset font)
    {
        if (button == null || font == null) return;
        var tmp = button.GetComponentInChildren<TMP_Text>();
        if (tmp != null) tmp.font = font;
    }

    static GameObject CreatePanel(Transform parent)
    {
        var panel = new GameObject("LevelHubPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);

        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = panel.GetComponent<Image>();
        img.color = new Color(0.05f, 0.08f, 0.12f, 0.92f);
        return panel;
    }

    static TMP_Text CreateTmp(Transform parent, float fontSize, FontStyles style)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        var le = go.AddComponent<LayoutElement>();
        le.minHeight = fontSize + 12;

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.text = "...";
        tmp.raycastTarget = false;
        TMP_FontAsset font = TMP_Settings.defaultFontAsset;
        if (font != null)
            tmp.font = font;
        return tmp;
    }

    static Button CreateButton(Transform parent, string label)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 56;
        le.preferredHeight = 56;

        var img = go.GetComponent<Image>();
        img.color = Color.white;

        var btn = go.GetComponent<Button>();
        btn.transition = Selectable.Transition.ColorTint;

        var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        var tmp = textGo.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 26;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        return btn;
    }

    Button CreateHubButton(Transform parent, string label, Vector2 size)
    {
        Button btn = CreateButton(parent, label);
        StyleHubButton(btn, size);
        return btn;
    }

    Button CreateLevelSelectButton(Transform parent, string label)
    {
        Button btn = CreateButton(parent, label);
        StyleHubButton(btn, levelSelectButtonSize);
        return btn;
    }

    void ApplyHubButtonColor(Image img, bool faded)
    {
        if (img == null) return;

        Color fill = faded
            ? new Color(1f, 1f, 1f, 0.12f)
            : hubButtonColor;

        // ColorTint 会与 Image.color 相乘，因此底图保持白色，颜色只写在 Button 色块里。
        img.color = Color.white;

        Button btn = img.GetComponent<Button>();
        if (btn == null) return;

        float alpha = fill.a;
        var colors = btn.colors;
        colors.normalColor = fill;
        colors.highlightedColor = new Color(1f, 1f, 1f, Mathf.Min(1f, alpha + 0.15f));
        colors.pressedColor = new Color(1f, 1f, 1f, Mathf.Max(0.1f, alpha - 0.1f));
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(1f, 1f, 1f, 0.12f);
        btn.colors = colors;
    }
}

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

    public string levelFormat = "Level: {0}";
    public string creditsFormat = "Credits: {0}";
    public string debtFormat = "Debt: {0:N0}";
    public string enterButtonLabel = "Enter Level";
    public string repayButtonLabel = "Repay Debt";
    public string selectLevelButtonLabel = "Select Level";
    public string selectLevelTitle = "Select Level";
    public string selectLevelBackLabel = "Back";

    Canvas _overlayCanvas;
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

    void EnsureUiBuilt()
    {
        if (panelRoot == null)
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

        int current = lm.CurrentLevelIndex;
        TMP_FontAsset font = ResolveHubFont();

        for (int i = 0; i < lm.LevelCount; i++)
        {
            LevelDefinition def = lm.levels[i];
            string label = def != null
                ? (!string.IsNullOrEmpty(def.displayName) ? def.displayName : $"Level {def.levelNumber}")
                : $"(empty {i + 1})";

            int captured = i;
            Button btn = CreateButton(levelSelectGrid, label);
            ApplyFontToButton(btn, font);

            Image img = btn.GetComponent<Image>();
            if (img != null)
            {
                if (def == null)
                    img.color = new Color(0.3f, 0.3f, 0.3f, 1f);
                else if (i == current)
                    img.color = new Color(0.25f, 0.7f, 0.4f, 1f);
                else
                    img.color = new Color(0.2f, 0.45f, 0.75f, 1f);
            }

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
        if (panelRoot != null)
            panelRoot.SetActive(true);
        if (levelSelectPanel != null)
            levelSelectPanel.SetActive(false);
        if (_overlayCanvas != null)
            _overlayCanvas.enabled = true;

        Refresh();
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

    public void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message ?? string.Empty;
    }

    public void Refresh()
    {
        LevelDefinition def = LevelManager.Instance != null ? LevelManager.Instance.CurrentLevel : null;
        int levelNum = def != null ? def.levelNumber : 1;
        string levelName = def != null && !string.IsNullOrEmpty(def.displayName) ? def.displayName : $"Level {levelNum}";

        if (levelText != null)
            levelText.text = SafeFormat(levelFormat, levelName);

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
        }
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
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        panelRoot = CreatePanel(canvasGo.transform);
        panelRoot.SetActive(false);

        var vLayout = panelRoot.AddComponent<VerticalLayoutGroup>();
        vLayout.childAlignment = TextAnchor.MiddleCenter;
        vLayout.spacing = 16;
        vLayout.padding = new RectOffset(48, 48, 48, 48);
        vLayout.childControlWidth = true;
        vLayout.childControlHeight = true;
        vLayout.childForceExpandWidth = true;
        vLayout.childForceExpandHeight = false;

        levelText = CreateTmp(panelRoot.transform, 36, FontStyles.Bold);
        creditsText = CreateTmp(panelRoot.transform, 28, FontStyles.Normal);
        debtText = CreateTmp(panelRoot.transform, 28, FontStyles.Normal);
        statusText = CreateTmp(panelRoot.transform, 22, FontStyles.Italic);
        statusText.color = new Color(1f, 0.85f, 0.4f);

        enterLevelButton = CreateButton(panelRoot.transform, enterButtonLabel);
        selectLevelButton = CreateButton(panelRoot.transform, selectLevelButtonLabel);
        repayDebtButton = CreateButton(panelRoot.transform, repayButtonLabel);

        BuildLevelSelectPanel(canvasGo.transform);
        ApplyHubFont();
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
        vLayout.childForceExpandWidth = true;
        vLayout.childForceExpandHeight = false;

        TMP_Text title = CreateTmp(levelSelectPanel.transform, 42, FontStyles.Bold);
        title.text = selectLevelTitle;

        var gridGo = new GameObject("LevelGrid", typeof(RectTransform));
        gridGo.transform.SetParent(levelSelectPanel.transform, false);

        var gridLe = gridGo.AddComponent<LayoutElement>();
        gridLe.minHeight = 360;
        gridLe.preferredHeight = 480;

        var grid = gridGo.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(220, 90);
        grid.spacing = new Vector2(16, 16);
        grid.childAlignment = TextAnchor.MiddleCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 5;
        levelSelectGrid = gridGo.transform;

        levelSelectBackButton = CreateButton(levelSelectPanel.transform, selectLevelBackLabel);
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
        img.color = new Color(0.2f, 0.45f, 0.75f, 1f);

        var btn = go.GetComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.35f, 0.6f, 0.9f);
        colors.pressedColor = new Color(0.15f, 0.35f, 0.6f);
        btn.colors = colors;

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
}

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 游戏内 HUD：Day、倒计时、连击、信用点。
/// Screen Space Overlay 2D UI，固定显示在屏幕左上角。
/// </summary>
[RequireComponent(typeof(Canvas), typeof(RectTransform))]
public class BillboardUI : MonoBehaviour
{
    const string HudPanelName = "HudPanel";

    [Header("Screen Space HUD")]
    [Tooltip("固定为屏幕 2D UI（Screen Space Overlay），显示在左上角。")]
    public bool useScreenSpaceTopLeft = true;

    public int canvasSortOrder = 50;
    public Vector2 hudScreenOffset = new Vector2(24f, -24f);
    public float hudFontSize = 28f;
    public float hudLineSpacing = 8f;
    public Vector2 hudLineSize = new Vector2(420f, 36f);

    [Tooltip("隐藏 HUD 面板背景，仅保留文字。")]
    public bool hidePanelBackground = true;

    [Header("HUD 文本（可留空，按名称自动查找：day / countdown / combo / credits）")]
    public TMP_Text dayText;
    public TMP_Text creditsText;
    public TMP_Text comboText;

    [Header("HUD - 文案格式")]
    public string creditsFormat = "Credits: {0}";

    [Tooltip("{0} = current combo count.")]
    public string comboFormat = "Combo x{0}";

    [Tooltip("Appended when combo bonus is active. {0} = bonus percent.")]
    public string comboBonusFormat = " (+{0}%)";

    public Color comboIdleColor = Color.white;
    public Color comboActiveColor = new Color(1f, 0.85f, 0.2f, 1f);
    public Color comboBonusColor = new Color(0.4f, 1f, 0.5f, 1f);

    static readonly string[] HudLineOrder = { "day", "countdown", "combo", "credits" };

    RectTransform _hudPanel;

    void Awake()
    {
        if (useScreenSpaceTopLeft)
            ApplyScreenSpaceLayout();
    }

    void OnEnable()
    {
        SubscribeCredits();
        SubscribeCombo();
    }

    void OnDisable()
    {
        UnsubscribeCredits();
        UnsubscribeCombo();
    }

    void Start()
    {
        EnsureComboText();
        SubscribeCredits();
        SubscribeCombo();
        RefreshCreditsDisplay();
        RefreshComboDisplay();
    }

    public void RefreshHudLayout()
    {
        if (useScreenSpaceTopLeft)
            ApplyScreenSpaceLayout();
    }

    void ApplyScreenSpaceLayout()
    {
        SetupCanvas();
        ResolveHudTexts();
        _hudPanel = EnsureHudPanel();
        OrganizeHudLines(_hudPanel);
        RefreshCreditsDisplay();
        RefreshComboDisplay();
    }

    void SetupCanvas()
    {
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = null;
        canvas.sortingOrder = canvasSortOrder;
        canvas.pixelPerfect = false;

        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        var scaler = GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = gameObject.AddComponent<CanvasScaler>();

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        var root = transform as RectTransform;
        root.localScale = Vector3.one;
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.pivot = new Vector2(0.5f, 0.5f);
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;
        root.anchoredPosition = Vector2.zero;
    }

    void ResolveHudTexts()
    {
        if (dayText == null) dayText = FindHudText("day");
        if (creditsText == null) creditsText = FindHudText("credits");
        if (comboText == null) comboText = FindHudText("combo");
    }

    TMP_Text FindHudText(string lineName)
    {
        Transform t = transform.Find(HudPanelName + "/" + lineName);
        if (t == null) t = transform.Find("Panel/" + lineName);
        if (t == null) t = transform.Find(lineName);
        return t != null ? t.GetComponent<TMP_Text>() : null;
    }

    RectTransform EnsureHudPanel()
    {
        Transform existing = transform.Find(HudPanelName);
        if (existing == null)
        {
            Transform legacyPanel = transform.Find("Panel");
            if (legacyPanel != null)
            {
                legacyPanel.name = HudPanelName;
                existing = legacyPanel;
            }
        }

        GameObject panelGo;
        if (existing != null)
        {
            panelGo = existing.gameObject;
        }
        else
        {
            panelGo = new GameObject(HudPanelName, typeof(RectTransform));
            panelGo.layer = gameObject.layer;
            panelGo.transform.SetParent(transform, false);
        }

        var panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.localScale = Vector3.one;
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = hudScreenOffset;

        var bg = panelGo.GetComponent<Image>();
        if (bg != null)
            bg.enabled = !hidePanelBackground;

        var layout = panelGo.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
            layout = panelGo.AddComponent<VerticalLayoutGroup>();

        layout.childAlignment = TextAnchor.UpperLeft;
        layout.spacing = hudLineSpacing;
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var fitter = panelGo.GetComponent<ContentSizeFitter>();
        if (fitter == null)
            fitter = panelGo.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return panelRect;
    }

    void OrganizeHudLines(RectTransform hudPanel)
    {
        float lineHeight = Mathf.Max(hudLineSize.y, hudFontSize + 6f);

        for (int i = 0; i < HudLineOrder.Length; i++)
        {
            string lineName = HudLineOrder[i];
            TMP_Text line = GetLineText(lineName);
            if (line == null) continue;

            var rt = line.rectTransform;
            rt.SetParent(hudPanel, false);
            rt.localScale = Vector3.one;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(hudLineSize.x, lineHeight);

            var le = line.GetComponent<LayoutElement>();
            if (le == null) le = line.gameObject.AddComponent<LayoutElement>();
            le.minHeight = lineHeight;
            le.preferredHeight = lineHeight;
            le.preferredWidth = hudLineSize.x;

            line.fontSize = hudFontSize;
            line.enableAutoSizing = false;
            line.alignment = TextAlignmentOptions.MidlineLeft;
            line.margin = Vector4.zero;
            line.raycastTarget = false;
            line.overflowMode = TextOverflowModes.Overflow;
        }
    }

    TMP_Text GetLineText(string lineName)
    {
        switch (lineName)
        {
            case "day": return dayText ?? FindHudText("day");
            case "countdown": return FindHudText("countdown");
            case "combo": return comboText ?? FindHudText("combo");
            case "credits": return creditsText ?? FindHudText("credits");
            default: return null;
        }
    }

    void SubscribeCredits()
    {
        if (CreditManager.Instance == null) return;
        CreditManager.Instance.OnCreditsChanged -= HandleCreditsChanged;
        CreditManager.Instance.OnCreditsChanged += HandleCreditsChanged;
    }

    void UnsubscribeCredits()
    {
        if (CreditManager.Instance == null) return;
        CreditManager.Instance.OnCreditsChanged -= HandleCreditsChanged;
    }

    void SubscribeCombo()
    {
        if (CreditManager.Instance == null) return;
        CreditManager.Instance.OnThrowComboChanged -= HandleThrowComboChanged;
        CreditManager.Instance.OnThrowComboChanged += HandleThrowComboChanged;
    }

    void UnsubscribeCombo()
    {
        if (CreditManager.Instance == null) return;
        CreditManager.Instance.OnThrowComboChanged -= HandleThrowComboChanged;
    }

    void HandleCreditsChanged(int _) => RefreshCreditsDisplay();

    void HandleThrowComboChanged(int _) => RefreshComboDisplay();

    void RefreshCreditsDisplay()
    {
        if (creditsText == null) creditsText = FindHudText("credits");
        if (creditsText == null) return;

        int total = CreditManager.Instance != null ? CreditManager.Instance.credits : 0;
        creditsText.text = string.Format(creditsFormat, total);
    }

    void RefreshComboDisplay()
    {
        EnsureComboText();
        if (comboText == null) return;

        int combo = CreditManager.Instance != null ? CreditManager.Instance.ThrowComboCount : 0;
        int bonusPercent = CreditManager.Instance != null
            ? CreditManager.Instance.GetThrowComboBonusPercent(combo)
            : 0;

        string text = string.Format(comboFormat, combo);
        if (bonusPercent > 0 && !string.IsNullOrEmpty(comboBonusFormat))
            text += string.Format(comboBonusFormat, bonusPercent);

        comboText.text = text;
        comboText.color = bonusPercent > 0
            ? comboBonusColor
            : combo > 0 ? comboActiveColor : comboIdleColor;
    }

    void EnsureComboText()
    {
        if (comboText != null) return;

        RectTransform hudPanel = _hudPanel != null ? _hudPanel : EnsureHudPanel();
        Transform existing = hudPanel.Find("combo");
        if (existing != null)
        {
            comboText = existing.GetComponent<TMP_Text>();
            return;
        }

        TMP_Text template = creditsText ?? dayText ?? FindHudText("countdown");
        if (template == null) return;

        var go = new GameObject("combo", typeof(RectTransform), typeof(CanvasRenderer));
        go.layer = template.gameObject.layer;
        go.transform.SetParent(hudPanel, false);

        comboText = go.AddComponent<TextMeshProUGUI>();
        comboText.font = template.font;
        comboText.fontSharedMaterial = template.fontSharedMaterial;
        comboText.fontStyle = FontStyles.Bold;
        comboText.raycastTarget = false;
        comboText.text = string.Format(comboFormat, 0);

        OrganizeHudLines(hudPanel);
    }
}

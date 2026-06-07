using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 游戏内 HUD：信用点、连击、Day、倒计时。
/// 默认以 Screen Space Overlay 固定在屏幕左上角。
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class BillboardUI : MonoBehaviour
{
    [Header("Screen Space HUD")]
    [Tooltip("启用后固定为 2D UI，显示在屏幕左上角，不再跟随相机旋转。")]
    public bool useScreenSpaceTopLeft = true;

    public Vector2 hudScreenOffset = new Vector2(20f, -20f);
    public float hudFontSize = 28f;
    public float hudLineSpacing = 6f;
    public Vector2 hudLineSize = new Vector2(420f, 36f);

    [Tooltip("隐藏 Panel 半透明背景，仅保留文字。")]
    public bool hidePanelBackground = true;

    [Header("HUD - Credits")]
    [Tooltip("TMP text for earned credits (same panel as level and countdown).")]
    public TMP_Text creditsText;

    [Tooltip("Format string. {0} is the current credit total.")]
    public string creditsFormat = "credits:{0}";

    [Header("HUD - Combo")]
    [Tooltip("TMP text for throw combo. Auto-created beside credits when empty.")]
    public TMP_Text comboText;

    [Tooltip("{0} = current combo count.")]
    public string comboFormat = "Combo x{0}";

    [Tooltip("Appended when combo bonus is active. {0} = bonus percent.")]
    public string comboBonusFormat = " (+{0}%)";

    public Color comboIdleColor = Color.white;
    public Color comboActiveColor = new Color(1f, 0.85f, 0.2f, 1f);
    public Color comboBonusColor = new Color(0.4f, 1f, 0.5f, 1f);

    static readonly string[] HudLineOrder = { "day", "countdown", "combo", "credits" };

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
        if (useScreenSpaceTopLeft)
            ApplyScreenSpaceLayout();

        EnsureComboText();
        SubscribeCredits();
        SubscribeCombo();
        RefreshCreditsDisplay();
        RefreshComboDisplay();
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

    void ApplyScreenSpaceLayout()
    {
        var canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = null;
        }

        var scaler = GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        var root = transform as RectTransform;
        if (root != null)
        {
            root.localScale = Vector3.one;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.pivot = new Vector2(0.5f, 0.5f);
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            root.anchoredPosition = Vector2.zero;
        }

        RectTransform panelRect = FindHudPanelRect();
        if (panelRect == null) return;

        panelRect.localScale = Vector3.one;
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = hudScreenOffset;
        panelRect.sizeDelta = new Vector2(hudLineSize.x, 220f);

        var bg = panelRect.GetComponent<Image>();
        if (bg != null && hidePanelBackground)
            bg.enabled = false;

        LayoutHudLines(panelRect);
    }

    RectTransform FindHudPanelRect()
    {
        if (creditsText != null)
        {
            var parent = creditsText.transform.parent as RectTransform;
            if (parent != null) return parent;
        }

        var panel = transform.Find("Panel") as RectTransform;
        return panel;
    }

    void LayoutHudLines(RectTransform panelRect)
    {
        var lines = CollectHudLines(panelRect);
        float y = 0f;
        float lineHeight = Mathf.Max(hudLineSize.y, hudFontSize + 4f);

        for (int i = 0; i < lines.Count; i++)
        {
            TMP_Text line = lines[i];
            if (line == null) continue;

            var rt = line.rectTransform;
            rt.SetParent(panelRect, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.localScale = Vector3.one;
            rt.sizeDelta = hudLineSize;
            rt.anchoredPosition = new Vector2(0f, y);

            line.fontSize = hudFontSize;
            line.enableAutoSizing = false;
            line.alignment = TextAlignmentOptions.TopLeft;
            line.margin = Vector4.zero;
            line.raycastTarget = false;

            y -= lineHeight + hudLineSpacing;
        }
    }

    List<TMP_Text> CollectHudLines(RectTransform panelRect)
    {
        var result = new List<TMP_Text>();
        for (int i = 0; i < HudLineOrder.Length; i++)
        {
            Transform child = panelRect.Find(HudLineOrder[i]);
            if (child == null) continue;
            var text = child.GetComponent<TMP_Text>();
            if (text != null)
                result.Add(text);
        }
        return result;
    }

    void EnsureComboText()
    {
        if (comboText != null) return;

        RectTransform panelRect = FindHudPanelRect();
        if (panelRect == null) return;

        Transform existing = panelRect.Find("combo");
        if (existing != null)
        {
            comboText = existing.GetComponent<TMP_Text>();
            return;
        }

        TMP_Text template = creditsText;
        if (template == null)
        {
            for (int i = 0; i < HudLineOrder.Length; i++)
            {
                Transform child = panelRect.Find(HudLineOrder[i]);
                if (child == null) continue;
                template = child.GetComponent<TMP_Text>();
                if (template != null) break;
            }
        }
        if (template == null) return;

        var go = new GameObject("combo", typeof(RectTransform), typeof(CanvasRenderer));
        go.layer = template.gameObject.layer;
        go.transform.SetParent(panelRect, false);

        comboText = go.AddComponent<TextMeshProUGUI>();
        comboText.font = template.font;
        comboText.fontSharedMaterial = template.fontSharedMaterial;
        comboText.fontStyle = FontStyles.Bold;
        comboText.raycastTarget = false;
        comboText.text = string.Format(comboFormat, 0);

        if (useScreenSpaceTopLeft)
            LayoutHudLines(panelRect);
    }
}

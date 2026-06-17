using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 进入场景后首先显示的开始界面：全屏黑色 70% 蒙版 + 居中 Start 按钮。
/// </summary>
public class StartScreenUI : MonoBehaviour
{
    [Header("可选：留空则在运行时自动创建 Screen Space UI")]
    public GameObject panelRoot;
    public Button startButton;

    [Header("文案")]
    public string startButtonLabel = "Start";

    [Tooltip("蒙版颜色（默认黑色 70% 透明度）")]
    public Color overlayColor = new Color(0f, 0f, 0f, 0.7f);

    Canvas _canvas;
    Action _onStartClicked;

    void Awake()
    {
        EnsureUiBuilt();
        HideImmediate();
    }

    public void Show(Action onStartClicked)
    {
        _onStartClicked = onStartClicked;
        EnsureUiBuilt();

        if (panelRoot != null)
            panelRoot.SetActive(true);
        if (_canvas != null)
            _canvas.enabled = true;

        if (startButton != null)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(HandleStartClicked);
        }

        GameplayInputGate.SetBlocked(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Hide()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
        if (_canvas != null)
            _canvas.enabled = false;

        if (startButton != null)
            startButton.onClick.RemoveAllListeners();
    }

    void HandleStartClicked()
    {
        Hide();
        _onStartClicked?.Invoke();
        _onStartClicked = null;
    }

    void HideImmediate()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
        if (_canvas != null)
            _canvas.enabled = false;
    }

    void OnDestroy()
    {
        RuntimeUiUtility.DestroyCanvas(ref _canvas);
        panelRoot = null;
        startButton = null;
    }

    void EnsureUiBuilt()
    {
        if (panelRoot == null)
            BuildRuntimeUi();
    }

    void BuildRuntimeUi()
    {
        EnsureEventSystem();

        var canvasGo = new GameObject("StartScreenCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(null, false);

        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 250;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        panelRoot = new GameObject("StartScreenPanel", typeof(RectTransform), typeof(Image));
        panelRoot.transform.SetParent(canvasGo.transform, false);

        var panelRt = panelRoot.GetComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;

        var overlay = panelRoot.GetComponent<Image>();
        overlay.color = overlayColor;
        overlay.raycastTarget = true;

        startButton = CreateCenterButton(panelRoot.transform, startButtonLabel);
        panelRoot.SetActive(false);
        RuntimeUiUtility.MarkPlayModeOnly(canvasGo);
    }

    static Button CreateCenterButton(Transform parent, string label)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(StartButtonFeedback));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(360, 120);
        rt.anchoredPosition = Vector2.zero;

        var img = go.GetComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0f);

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.None;

        var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);

        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        var tmp = textGo.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 64;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        TMP_FontAsset font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font != null)
            tmp.font = font;

        go.GetComponent<StartButtonFeedback>().Initialize(rt, tmp);

        return btn;
    }

    static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;

        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }
}

/// <summary>
/// Start 按钮悬停放大、按下变灰反馈。
/// </summary>
sealed class StartButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    const float HoverScale = 1.15f;
    const float ScaleSpeed = 14f;

    static readonly Color NormalColor = Color.white;
    static readonly Color PressedColor = new Color(0.55f, 0.55f, 0.55f, 1f);

    RectTransform _rect;
    TMP_Text _label;
    float _targetScale = 1f;
    bool _pressed;

    public void Initialize(RectTransform rect, TMP_Text label)
    {
        _rect = rect;
        _label = label;
        _rect.localScale = Vector3.one;
    }

    void Update()
    {
        if (_rect == null) return;
        _rect.localScale = Vector3.Lerp(
            _rect.localScale,
            Vector3.one * _targetScale,
            Time.unscaledDeltaTime * ScaleSpeed);
    }

    void RefreshColor()
    {
        if (_label != null)
            _label.color = _pressed ? PressedColor : NormalColor;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _targetScale = HoverScale;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _targetScale = 1f;
        _pressed = false;
        RefreshColor();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        _pressed = true;
        RefreshColor();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        _pressed = false;
        RefreshColor();
    }
}

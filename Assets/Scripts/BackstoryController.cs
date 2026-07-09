using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 按数组分段播放剧情文字。
/// 独立场景：autoPlayOnStart = true，结束后可 loadNextSceneOnFinish。
/// 主流程：由 <see cref="LevelSessionController"/> 调用 <see cref="Show"/>。
/// </summary>
public class BackstoryController : MonoBehaviour
{
    [Header("剧情文本（按顺序播放）")]
    [TextArea(2, 8)]
    public string[] segments =
    {
        "在上层区，科技是永生，是悬浮在云端的全息神明。\n\n在下层区，科技是诅咒，是流淌在下水道里的霓虹废料。",
        "欢迎来到404号回收室。\n\n你是一名负债百万的「电子法医」。在这个窄小的工作室里，仿生人的断肢、报废的AI大脑、还有那些沾着血的违禁核心……别人眼里的破烂，是你不被做成罐头的唯一机会。",
        "别看了，招财猫可帮你拜不掉债主。拉动电闸，接收更多的「垃圾」——\n\n只要手速够快，阎王就收不走你的命。",
        "今天，是你的Day 1。\n\n给你安排的任务非常基础：只需要把混在一起的垃圾分别投送到对应的通道。没有自爆的核心，没有帮派的流弹，也没有视网膜病毒。但这仅仅是风暴前的平静。\n\n看清垃圾的材质——别把金属扔进生物道，弄错了一分钱也别想拿到手。"
    };

    [Header("启动模式")]
    [Tooltip("勾选后进入场景即自动播放（独立背景故事场景用）。")]
    public bool autoPlayOnStart = true;

    [Tooltip("勾选后运行时自动创建全屏 Overlay UI（主流程 LevelSession 用）。")]
    public bool buildOverlayUi = true;

    [Header("UI 引用（留空则自动查找 / 创建）")]
    public Canvas targetCanvas;
    public GameObject panelRoot;
    public TMP_Text titleText;
    public TMP_Text bodyText;
    public TMP_Text progressText;
    public Button nextButton;
    public TMP_Text nextButtonLabel;

    [Header("文案")]
    public string panelTitle = "背景故事";
    public string nextLabel = "下一步";
    public string finishLabel = "继续";

    [Header("逐字播放")]
    public bool useTypewriter = true;
    [Range(0.005f, 0.3f)]
    public float charInterval = 0.04f;
    public bool clickSkipsTypewriter = true;

    [Header("快捷键")]
    public bool advanceWithKeyboard = true;
    public KeyCode[] advanceKeys = { KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter };

    [Header("结束行为")]
    [Tooltip("最后一段后是否自动加载 Build Settings 中的下一个场景（独立场景用）。")]
    public bool loadNextSceneOnFinish = true;
    public UnityEvent onFinished;

    [Header("字体（中文请指定支持中文的 TMP Font Asset）")]
    public TMP_FontAsset bodyFont;

    int _index = -1;
    bool _finished;
    bool _isTyping;
    bool _sessionMode;
    bool _ownsRuntimeCanvas;
    Coroutine _typingRoutine;
    Action _sessionFinishCallback;

    public bool HasContent
    {
        get
        {
            if (segments == null || segments.Length == 0) return false;
            for (int i = 0; i < segments.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(segments[i]))
                    return true;
            }
            return false;
        }
    }

    void Awake()
    {
        if (buildOverlayUi || panelRoot != null)
            EnsureOverlayUiBuilt();
        else
            EnsureLegacyReferences();

        SetUiVisible(false);
    }

    bool ShouldBuildOverlayUi()
    {
        if (!buildOverlayUi || panelRoot != null) return false;
        return GetComponentInParent<Canvas>() == null;
    }

    void Start()
    {
        if (!autoPlayOnStart) return;
        if (!HasContent)
        {
            Debug.LogWarning("[BackstoryController] Segments 为空，没有内容可播放。");
            return;
        }

        BeginPlayback(loadSceneWhenDone: loadNextSceneOnFinish, onFinished: null);
    }

    void OnEnable()
    {
        if (nextButton != null)
            nextButton.onClick.AddListener(OnNextButtonClicked);
    }

    void OnDisable()
    {
        if (nextButton != null)
            nextButton.onClick.RemoveListener(OnNextButtonClicked);
    }

    void Update()
    {
        if (_finished || !advanceWithKeyboard || advanceKeys == null) return;

        for (int i = 0; i < advanceKeys.Length; i++)
        {
            if (Input.GetKeyDown(advanceKeys[i]))
            {
                OnNextButtonClicked();
                break;
            }
        }
    }

    public void Show(Action onFinished)
    {
        if (!HasContent)
        {
            onFinished?.Invoke();
            return;
        }

        BeginPlayback(loadSceneWhenDone: false, onFinished);
    }

    public void Hide()
    {
        if (_typingRoutine != null)
        {
            StopCoroutine(_typingRoutine);
            _typingRoutine = null;
        }

        _isTyping = false;
        _finished = true;
        _index = -1;
        _sessionFinishCallback = null;
        _sessionMode = false;
        SetUiVisible(false);
    }

    void BeginPlayback(bool loadSceneWhenDone, Action onFinished)
    {
        if (ShouldBuildOverlayUi())
            EnsureOverlayUiBuilt();
        else if (panelRoot == null)
            EnsureLegacyReferences();

        _sessionMode = !loadSceneWhenDone;
        _sessionFinishCallback = onFinished;
        loadNextSceneOnFinish = loadSceneWhenDone;
        _finished = false;
        _index = -1;

        if (titleText != null)
            titleText.text = panelTitle ?? string.Empty;

        ApplyFont();
        ShowSegment(0);
        SetUiVisible(true);

        if (_sessionMode)
        {
            GameplayInputGate.SetBlocked(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    public void Restart()
    {
        BeginPlayback(loadSceneWhenDone: loadNextSceneOnFinish, onFinished: _sessionFinishCallback);
    }

    public void Advance() => OnNextButtonClicked();

    void OnNextButtonClicked()
    {
        if (_finished) return;

        if (_isTyping && clickSkipsTypewriter)
        {
            CompleteTypewriter();
            return;
        }

        int next = _index + 1;
        if (next >= segments.Length)
        {
            Finish();
            return;
        }

        ShowSegment(next);
    }

    void ShowSegment(int idx)
    {
        _index = idx;
        string text = segments[idx] ?? string.Empty;

        if (_typingRoutine != null)
            StopCoroutine(_typingRoutine);

        if (useTypewriter && charInterval > 0f && !string.IsNullOrEmpty(text))
            _typingRoutine = StartCoroutine(TypewriterRoutine(text));
        else if (bodyText != null)
            bodyText.text = text;

        UpdateProgressLabel();
        UpdateButtonLabel();
    }

    IEnumerator TypewriterRoutine(string full)
    {
        _isTyping = true;
        if (bodyText != null)
            bodyText.text = string.Empty;

        var wait = new WaitForSecondsRealtime(charInterval);
        for (int i = 1; i <= full.Length; i++)
        {
            if (bodyText != null)
                bodyText.text = full.Substring(0, i);
            yield return wait;
        }

        _isTyping = false;
        _typingRoutine = null;
    }

    void CompleteTypewriter()
    {
        if (_typingRoutine != null)
        {
            StopCoroutine(_typingRoutine);
            _typingRoutine = null;
        }

        if (bodyText != null && _index >= 0 && _index < segments.Length)
            bodyText.text = segments[_index] ?? string.Empty;

        _isTyping = false;
    }

    void UpdateProgressLabel()
    {
        if (progressText == null || segments == null || segments.Length <= 1) return;
        progressText.text = $"{_index + 1} / {segments.Length}";
    }

    void UpdateButtonLabel()
    {
        if (nextButtonLabel == null || segments == null) return;
        bool isLast = _index >= segments.Length - 1;
        nextButtonLabel.text = isLast ? finishLabel : nextLabel;
    }

    void Finish()
    {
        if (_finished) return;
        _finished = true;

        onFinished?.Invoke();

        if (_sessionMode)
        {
            Action cb = _sessionFinishCallback;
            Hide();
            cb?.Invoke();
            return;
        }

        if (loadNextSceneOnFinish)
        {
            int nextIndex = SceneManager.GetActiveScene().buildIndex + 1;
            if (nextIndex < SceneManager.sceneCountInBuildSettings)
                SceneManager.LoadScene(nextIndex);
            else
                Debug.LogWarning($"[BackstoryController] 无可用的下一个场景（当前 buildIndex={SceneManager.GetActiveScene().buildIndex}）。");
        }
    }

    void SetUiVisible(bool visible)
    {
        if (visible)
            RuntimeUiUtility.NormalizeOverlayCanvas(targetCanvas, transform);

        if (panelRoot != null)
            panelRoot.SetActive(visible);
        else if (targetCanvas != null)
            targetCanvas.enabled = visible;
    }

    void OnDestroy()
    {
        if (!_ownsRuntimeCanvas || targetCanvas == null) return;

        GameObject go = targetCanvas.gameObject;
        targetCanvas = null;
        if (go == null) return;

        if (Application.isPlaying)
            Destroy(go);
        else
            DestroyImmediate(go);
    }

    public void EnsureOverlayUiBuilt()
    {
        if (panelRoot != null)
        {
            if (targetCanvas == null)
                targetCanvas = panelRoot.GetComponentInParent<Canvas>();
            RuntimeUiUtility.NormalizeOverlayCanvas(targetCanvas, transform);
            return;
        }

        var canvasGo = new GameObject("BackstoryIntroCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        targetCanvas = canvasGo.GetComponent<Canvas>();
        targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        targetCanvas.sortingOrder = 245;
        RuntimeUiUtility.ConfigureOverlayCanvasScaler(canvasGo.GetComponent<CanvasScaler>());

        panelRoot = CreateFullScreenPanel(canvasGo.transform, "BackstoryPanel");
        panelRoot.SetActive(false);

        titleText = CreateAnchoredTmp(panelRoot.transform, "TitleText",
            new Vector2(0.08f, 0.86f), new Vector2(0.92f, 0.95f),
            52, FontStyles.Bold, TextAlignmentOptions.Center);

        bodyText = CreateAnchoredTmp(panelRoot.transform, "BodyText",
            new Vector2(0.1f, 0.18f), new Vector2(0.9f, 0.82f),
            32, FontStyles.Normal, TextAlignmentOptions.TopLeft);

        progressText = CreateAnchoredTmp(panelRoot.transform, "ProgressText",
            new Vector2(0.08f, 0.12f), new Vector2(0.3f, 0.17f),
            24, FontStyles.Normal, TextAlignmentOptions.BottomLeft);
        if (progressText != null)
            progressText.color = new Color(1f, 1f, 1f, 0.55f);

        nextButton = CreateBottomButton(panelRoot.transform, nextLabel);
        nextButtonLabel = nextButton != null ? nextButton.GetComponentInChildren<TMP_Text>() : null;

        ApplyFont();
        _ownsRuntimeCanvas = Application.isPlaying;
        RuntimeUiUtility.MarkPlayModeOnly(canvasGo);
    }

    void EnsureLegacyReferences()
    {
        if (targetCanvas == null)
        {
            targetCanvas = GetComponentInParent<Canvas>();
            if (targetCanvas == null)
            {
                Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
                foreach (Canvas c in canvases)
                {
                    if (c != null && c.gameObject.activeInHierarchy)
                    {
                        targetCanvas = c;
                        break;
                    }
                }
            }
        }

        if (targetCanvas == null)
        {
            Debug.LogError("[BackstoryController] 场景中未找到 Canvas，无法构建剧情 UI。");
            return;
        }

        if (bodyText == null)
            BuildLegacyBodyText(targetCanvas.transform);

        if (nextButton == null)
            BuildLegacyNextButton(targetCanvas.transform);

        ApplyFont();
        UpdateButtonLabel();
    }

    static GameObject CreateFullScreenPanel(Transform parent, string name)
    {
        var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);

        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.82f);
        return panel;
    }

    static TMP_Text CreateAnchoredTmp(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, float fontSize, FontStyles style, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color = Color.white;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.richText = true;
        tmp.raycastTarget = false;
        return tmp;
    }

    static Button CreateBottomButton(Transform parent, string label)
    {
        var btnGo = new GameObject("NextButton",
            typeof(RectTransform), typeof(Image), typeof(Button));
        btnGo.transform.SetParent(parent, false);

        var rt = btnGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.06f);
        rt.anchorMax = new Vector2(0.5f, 0.06f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(280f, 72f);
        rt.anchoredPosition = Vector2.zero;

        var img = btnGo.GetComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.2f);

        var btn = btnGo.GetComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = img.color;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.34f);
        colors.pressedColor = new Color(1f, 1f, 1f, 0.48f);
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
        tmp.fontSize = 30;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        return btn;
    }

    void BuildLegacyBodyText(Transform parent)
    {
        bodyText = CreateAnchoredTmp(parent, "BodyText",
            new Vector2(0.1f, 0.2f), new Vector2(0.9f, 0.8f),
            36, FontStyles.Normal, TextAlignmentOptions.TopLeft);
    }

    void BuildLegacyNextButton(Transform parent)
    {
        nextButton = CreateBottomButton(parent, nextLabel);
        nextButtonLabel = nextButton != null ? nextButton.GetComponentInChildren<TMP_Text>() : null;
    }

    void ApplyFont()
    {
        TMP_FontAsset font = bodyFont != null ? bodyFont : TMP_Settings.defaultFontAsset;
        if (font == null) return;

        if (titleText != null) titleText.font = font;
        if (bodyText != null) bodyText.font = font;
        if (progressText != null) progressText.font = font;
        if (nextButtonLabel != null) nextButtonLabel.font = font;
    }
}

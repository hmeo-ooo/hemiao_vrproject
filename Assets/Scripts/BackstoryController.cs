using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 按数组分段播放剧情文字。
/// 用法：把脚本挂到 BackstoryCanvas（或场景中任意 GameObject）上，
/// 在 Inspector 里填 Segments 数组；可选择拖入 BodyText / NextButton 引用，
/// 留空则会自动在 BackstoryCanvas 下创建文本框 + 按钮。
/// </summary>
public class BackstoryController : MonoBehaviour
{
    [Header("剧情文本（按顺序播放）")]
    [TextArea(2, 8)]
    public string[] segments = new string[]
    {
        "在这里写第一段背景故事……",
        "第二段：继续介绍世界观。",
        "第三段：交代主角的处境。"
    };

    [Header("UI 引用（留空则自动查找 / 创建）")]
    public Canvas targetCanvas;
    public TMP_Text bodyText;
    public Button nextButton;
    public TMP_Text nextButtonLabel;

    [Header("逐字播放")]
    public bool useTypewriter = true;
    [Range(0.005f, 0.3f)]
    public float charInterval = 0.04f;
    [Tooltip("打字进行中按按钮 / 按键，跳过动画直接显示完整段落")]
    public bool clickSkipsTypewriter = true;

    [Header("快捷键（除按钮外）")]
    public bool advanceWithKeyboard = true;
    public KeyCode[] advanceKeys = new KeyCode[]
    {
        KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter
    };

    [Header("按钮文案")]
    public string nextLabel = "下一步";
    public string finishLabel = "开始";

    [Header("结束行为")]
    [Tooltip("最后一段后是否自动加载 Build Settings 中的下一个场景")]
    public bool loadNextSceneOnFinish = true;
    public UnityEvent onFinished;

    [Header("字体（中文请指定支持中文的 TMP Font Asset）")]
    public TMP_FontAsset bodyFont;

    int _index = -1;
    bool _finished;
    bool _isTyping;
    Coroutine _typingRoutine;

    void Awake()
    {
        EnsureReferences();
    }

    void Start()
    {
        if (segments == null || segments.Length == 0)
        {
            Debug.LogWarning("[BackstoryController] Segments 为空，没有内容可播放。");
            return;
        }
        ShowSegment(0);
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

    public void Restart()
    {
        _finished = false;
        _index = -1;
        ShowSegment(0);
    }

    public void Advance()
    {
        OnNextButtonClicked();
    }

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

        if (_typingRoutine != null) StopCoroutine(_typingRoutine);

        if (useTypewriter && charInterval > 0f)
        {
            _typingRoutine = StartCoroutine(TypewriterRoutine(text));
        }
        else
        {
            bodyText.text = text;
            _isTyping = false;
        }

        UpdateButtonLabel();
    }

    IEnumerator TypewriterRoutine(string full)
    {
        _isTyping = true;
        bodyText.text = string.Empty;

        var wait = new WaitForSeconds(charInterval);
        for (int i = 1; i <= full.Length; i++)
        {
            bodyText.text = full.Substring(0, i);
            yield return wait;
        }
        _isTyping = false;
    }

    void CompleteTypewriter()
    {
        if (_typingRoutine != null)
        {
            StopCoroutine(_typingRoutine);
            _typingRoutine = null;
        }
        if (_index >= 0 && _index < segments.Length)
            bodyText.text = segments[_index] ?? string.Empty;
        _isTyping = false;
    }

    void UpdateButtonLabel()
    {
        if (nextButtonLabel == null) return;
        bool isLast = _index >= segments.Length - 1;
        nextButtonLabel.text = isLast ? finishLabel : nextLabel;
    }

    void Finish()
    {
        if (_finished) return;
        _finished = true;

        onFinished?.Invoke();

        if (loadNextSceneOnFinish)
        {
            int nextIndex = SceneManager.GetActiveScene().buildIndex + 1;
            if (nextIndex < SceneManager.sceneCountInBuildSettings)
            {
                SceneManager.LoadScene(nextIndex);
            }
            else
            {
                Debug.LogWarning($"[BackstoryController] 无可用的下一个场景（当前 buildIndex={SceneManager.GetActiveScene().buildIndex}）。");
            }
        }
    }

    void EnsureReferences()
    {
        if (targetCanvas == null)
        {
            targetCanvas = GetComponentInParent<Canvas>();
            if (targetCanvas == null)
            {
                Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
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
            BuildBodyText(targetCanvas.transform);

        if (nextButton == null)
            BuildNextButton(targetCanvas.transform);

        ApplyFont();
        UpdateButtonLabel();
    }

    void BuildBodyText(Transform parent)
    {
        GameObject go = new GameObject("BodyText", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.1f, 0.2f);
        rt.anchorMax = new Vector2(0.9f, 0.8f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 36;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.enableWordWrapping = true;
        tmp.richText = true;
        tmp.text = string.Empty;

        bodyText = tmp;
    }

    void BuildNextButton(Transform parent)
    {
        GameObject btnGo = new GameObject("NextButton",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnGo.transform.SetParent(parent, false);

        RectTransform rt = btnGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 60f);
        rt.sizeDelta = new Vector2(260f, 72f);

        Image bg = btnGo.GetComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.18f);
        bg.raycastTarget = true;

        Button btn = btnGo.GetComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.normalColor = new Color(1f, 1f, 1f, 0.18f);
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.32f);
        colors.pressedColor = new Color(1f, 1f, 1f, 0.45f);
        colors.selectedColor = colors.highlightedColor;
        btn.colors = colors;

        GameObject labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(btnGo.transform, false);

        RectTransform labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        TextMeshProUGUI label = labelGo.AddComponent<TextMeshProUGUI>();
        label.fontSize = 28;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.text = nextLabel;
        label.raycastTarget = false;

        nextButton = btn;
        nextButtonLabel = label;
    }

    void ApplyFont()
    {
        TMP_FontAsset font = bodyFont;
        if (font == null)
            font = TMP_Settings.defaultFontAsset;
        if (font == null) return;

        if (bodyText != null) bodyText.font = font;
        if (nextButtonLabel != null) nextButtonLabel.font = font;
    }
}

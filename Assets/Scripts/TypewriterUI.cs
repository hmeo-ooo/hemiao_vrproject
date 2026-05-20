using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public class TypewriterUI : MonoBehaviour
{
    [Header("文本源")]
    [Tooltip("如果使用多页，请在 Pages 填写每页文本；若为空则使用 fullText")]
    public List<Page> pages;
    [Tooltip("备用单页文本（pages 为空时使用）")]
    [TextArea]
    public string fullText = "Hello, Typewriter!";

    [System.Serializable]
    public class Page
    {
        [Tooltip("当前页由若干片段组成，每个片段可单独设置颜色/字号/加粗/斜体")]
        public List<Segment> segments = new List<Segment>();

        [Tooltip("可选：覆盖全局的分段分隔符（留空使用全局 Segment Separator）")]
        public string separator = "";
    }

    [System.Serializable]
    public class Segment
    {
        [TextArea(1, 8)]
        public string text;

        [Tooltip("片段文字颜色")]
        public Color color = Color.white;

        [Tooltip("片段字号（<=0 表示使用 Text 组件默认字号）")]
        public int fontSize = 0;

        [Tooltip("是否加粗")]
        public bool bold = false;

        [Tooltip("是否斜体")]
        public bool italic = false;

        [Tooltip("可选：字体名称，仅在 TMP 且支持时有效")]
        public string fontName = "";
    }

    [Header("打字设置（全局）")]
    [Tooltip("每秒字符数；当页面未覆盖时使用")]
    public float charsPerSecond = 30f;
    [Tooltip("是否在 Start 时自动播放第一页（如果 pages 有内容）")]
    public bool playOnStart = false;
    [Tooltip("点击时是否立即显示全部文本（保留以兼容旧选项）")]
    public bool skipOnClick = true;
    [Tooltip("全局：保留完整文本布局（未显示字符用透明色包裹）")]
    public bool preserveLayout = true;

    [Header("分段换行")]
    [Tooltip("每个 segment 之间默认的分隔符（留空表示不自动插入换行；可设置为 \"\\n\" 以插入换行）")]
    public string segmentSeparator = "";

    [Header("声音（可选，全局）")]
    public AudioClip typeSound;
    public AudioSource audioSource;

    [Header("翻页")]
    [Tooltip("自动完成一页后是否自动跳到下一页")]
    public bool autoAdvance = false;
    [Tooltip("自动跳页的延迟（秒）")]
    public float autoAdvanceDelay = 0.5f;

    [Header("目标 UI")]
    [Tooltip("优先使用此 Text；若为空脚本会尝试 GetComponent<Text>() 或 TextMeshPro（反射）")]
    public Text targetText;

    // TMP 反射
    object tmpTextObject;
    PropertyInfo tmpTextProperty;

    // 状态
    Coroutine typingCoroutine;
    bool isTyping;
    int currentPageIndex = 0;
    bool pageCompleted;

    // 输入防抖
    float lastPlayerInputTime = -1f;
    const float inputIgnoreWindow = 0.08f;

    void Awake()
    {
        if (targetText == null) targetText = GetComponent<Text>();
        if (targetText == null)
        {
            var tmpType = System.Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
            if (tmpType != null)
            {
                tmpTextObject = GetComponent(tmpType);
                if (tmpTextObject != null)
                    tmpTextProperty = tmpType.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
            }
        }

        if (preserveLayout)
            SetDisplayedText(MakePreserveStringFromSegments(0, 0));
        else
            SetDisplayedText(string.Empty);

        pageCompleted = false;
    }

    void Start()
    {
        if (playOnStart) StartPage(0);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0))
        {
            lastPlayerInputTime = Time.time;

            if (HasPages())
            {
                if (isTyping)
                {
                    CompleteTyping();
                    pageCompleted = true;
                    return;
                }

                if (pageCompleted)
                {
                    NextPage();
                    return;
                }

                StartPage(currentPageIndex);
            }
            else
            {
                if (isTyping)
                {
                    CompleteTyping();
                    pageCompleted = true;
                }
            }
        }
    }

    bool HasPages() => pages != null && pages.Count > 0;

    // 页面控制
    public void StartPage(int pageIndex)
    {
        StopTypingInternal();
        pageCompleted = false;
        if (HasPages())
        {
            currentPageIndex = Mathf.Clamp(pageIndex, 0, pages.Count - 1);
            StartTypingPage(currentPageIndex);
        }
        else
        {
            currentPageIndex = 0;
            StartTypingFullText();
        }
    }

    public void NextPage()
    {
        if (!HasPages()) return;
        int next = currentPageIndex + 1;
        if (next >= pages.Count) return;
        currentPageIndex = next;
        pageCompleted = false;
        StartTypingPage(currentPageIndex);
    }

    public void PrevPage()
    {
        if (!HasPages()) return;
        int prev = currentPageIndex - 1;
        if (prev < 0) return;
        currentPageIndex = prev;
        pageCompleted = false;
        StartTypingPage(currentPageIndex);
    }

    public void RestartCurrentPage()
    {
        pageCompleted = false;
        StartPage(currentPageIndex);
    }

    void StartTypingPage(int pageIndex)
    {
        StopTypingInternal();
        string plain = GetPagePlainText(pageIndex);
        typingCoroutine = StartCoroutine(TypeRoutineFromSegments(pageIndex, plain));
    }

    void StartTypingFullText()
    {
        StopTypingInternal();
        typingCoroutine = StartCoroutine(TypeRoutineSimple(fullText));
    }

    IEnumerator TypeRoutineFromSegments(int pageIndex, string plainText)
    {
        isTyping = true;
        pageCompleted = false;

        if (charsPerSecond <= 0f)
        {
            SetDisplayedText(BuildFullRichFromSegments(pageIndex));
            isTyping = false;
            pageCompleted = true;
            yield break;
        }

        float delay = 1f / charsPerSecond;
        int length = plainText.Length;

        for (int i = 1; i <= length; i++)
        {
            if (preserveLayout) SetDisplayedText(MakePreserveStringFromSegments(pageIndex, i));
            else SetDisplayedText(BuildVisibleRichFromSegments(pageIndex, i));

            PlayTypeSoundGlobal();

            float elapsed = 0f;
            while (elapsed < delay)
            {
                bool recentInput = (Time.time - lastPlayerInputTime) < inputIgnoreWindow;
                if (skipOnClick && !recentInput &&
                    (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space)))
                {
                    SetDisplayedText(BuildFullRichFromSegments(pageIndex));
                    StopTypingInternal();
                    pageCompleted = true;
                    yield break;
                }
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        StopTypingInternal();
        pageCompleted = true;

        if (autoAdvance)
        {
            int next = currentPageIndex + 1;
            if (HasPages() && next < pages.Count)
            {
                yield return new WaitForSeconds(autoAdvanceDelay);
                currentPageIndex = next;
                pageCompleted = false;
                StartTypingPage(currentPageIndex);
            }
        }
    }

    IEnumerator TypeRoutineSimple(string text)
    {
        isTyping = true;
        pageCompleted = false;

        if (charsPerSecond <= 0f)
        {
            SetDisplayedText(text);
            isTyping = false;
            pageCompleted = true;
            yield break;
        }

        float delay = 1f / charsPerSecond;
        int length = text.Length;
        for (int i = 1; i <= length; i++)
        {
            if (preserveLayout) SetDisplayedText(MakePreserveString(text, i));
            else SetDisplayedText(text.Substring(0, i));

            PlayTypeSoundGlobal();

            float elapsed = 0f;
            while (elapsed < delay)
            {
                bool recentInput = (Time.time - lastPlayerInputTime) < inputIgnoreWindow;
                if (skipOnClick && !recentInput &&
                    (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space)))
                {
                    SetDisplayedText(text);
                    StopTypingInternal();
                    pageCompleted = true;
                    yield break;
                }
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        StopTypingInternal();
        pageCompleted = true;
    }

    void PlayTypeSoundGlobal()
    {
        if (typeSound == null) return;
        if (audioSource != null) audioSource.PlayOneShot(typeSound);
        else AudioSource.PlayClipAtPoint(typeSound, Camera.main != null ? Camera.main.transform.position : Vector3.zero);
    }

    public void CompleteTyping()
    {
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }
        SetDisplayedText(HasPages() ? BuildFullRichFromSegments(currentPageIndex) : (fullText ?? string.Empty));
        isTyping = false;
        pageCompleted = true;
    }

    void StopTypingInternal()
    {
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }
        isTyping = false;
    }

    // 辅助构造函数
    string GetPagePlainText(int index)
    {
        if (!HasPages()) return fullText ?? string.Empty;
        index = Mathf.Clamp(index, 0, pages.Count - 1);
        var segs = pages[index].segments;
        if (segs == null || segs.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        // 不再在片段间自动插入默认分隔符，换行仅由片段文本本身的换行控制
        for (int i = 0; i < segs.Count; i++)
        {
            sb.Append(NormalizeSegmentText(segs[i].text));
        }
        return sb.ToString();
    }

    string BuildFullRichFromSegments(int pageIndex)
    {
        if (!HasPages()) return fullText ?? string.Empty;
        pageIndex = Mathf.Clamp(pageIndex, 0, pages.Count - 1);
        var segs = pages[pageIndex].segments;
        var sb = new StringBuilder();
        // 不再在片段间自动插入默认分隔符，换行仅由片段文本本身的换行控制
        for (int i = 0; i < segs.Count; i++)
        {
            sb.Append(WrapSegmentRich(segs[i], NormalizeSegmentText(segs[i].text)));
        }
        return sb.ToString();
    }

    string BuildVisibleRichFromSegments(int pageIndex, int visibleCount)
    {
        if (!HasPages()) return (fullText ?? string.Empty).Substring(0, Mathf.Clamp(visibleCount, 0, (fullText ?? string.Empty).Length));
        pageIndex = Mathf.Clamp(pageIndex, 0, pages.Count - 1);
        var segs = pages[pageIndex].segments;
        var sb = new StringBuilder();
        int remain = visibleCount;
        // 不再在片段间自动插入默认分隔符，换行仅由片段文本本身的换行控制
        for (int i = 0; i < segs.Count; i++)
        {
            string txt = NormalizeSegmentText(segs[i].text);
            int take = Mathf.Clamp(remain, 0, txt.Length);
            if (take > 0)
            {
                string part = txt.Substring(0, take);
                sb.Append(WrapSegmentRich(segs[i], part));
            }
            remain -= take;
            if (remain <= 0) break;
        }
        return sb.ToString();
    }

    string MakePreserveStringFromSegments(int pageIndex, int visibleCount)
    {
        if (!HasPages()) return MakePreserveString(fullText ?? string.Empty, visibleCount);
        pageIndex = Mathf.Clamp(pageIndex, 0, pages.Count - 1);
        var segs = pages[pageIndex].segments;
        var sb = new StringBuilder();
        int remain = visibleCount;
        // 不再在片段间自动插入默认分隔符，换行仅由片段文本本身的换行控制
        for (int i = 0; i < segs.Count; i++)
        {
            string txt = NormalizeSegmentText(segs[i].text);
            int show = Mathf.Clamp(remain, 0, txt.Length);
            string visible = txt.Substring(0, show);
            string hidden = txt.Substring(show);
            if (!string.IsNullOrEmpty(visible))
            {
                sb.Append(WrapSegmentRich(segs[i], visible));
            }
            if (!string.IsNullOrEmpty(hidden))
            {
                string wrappedHidden = "<color=#00000000>" + EscapeRichText(hidden) + "</color>";
                sb.Append(WrapSegmentRichRawInside(segs[i], wrappedHidden));
            }
            remain -= show;
        }
        return sb.ToString();
    }

    string GetSeparatorForPage(int pageIndex)
    {
        if (!HasPages()) return (segmentSeparator ?? "").Replace("\\n", "\n");
        pageIndex = Mathf.Clamp(pageIndex, 0, pages.Count - 1);
        var sep = pages[pageIndex].separator;
        if (!string.IsNullOrEmpty(sep)) return sep.Replace("\\n", "\n");
        return (segmentSeparator ?? "").Replace("\\n", "\n");
    }

    string WrapSegmentRich(Segment seg, string content)
    {
        string s = EscapeRichText(content);
        string colorHex = ColorToHex(seg.color);
        s = $"<color={colorHex}>{s}</color>";
        if (seg.fontSize > 0) s = $"<size={seg.fontSize}>{s}</size>";
        if (seg.bold) s = $"<b>{s}</b>";
        if (seg.italic) s = $"<i>{s}</i>";
        if (!string.IsNullOrEmpty(seg.fontName)) s = $"<font=\"{seg.fontName}\">{s}</font>";
        return s;
    }

    string WrapSegmentRichRawInside(Segment seg, string innerAlreadyTagged)
    {
        string s = innerAlreadyTagged;
        if (seg.fontSize > 0) s = $"<size={seg.fontSize}>{s}</size>";
        if (seg.bold) s = $"<b>{s}</b>";
        if (seg.italic) s = $"<i>{s}</i>";
        string colorHex = ColorToHex(seg.color);
        s = $"<color={colorHex}>{s}</color>";
        if (!string.IsNullOrEmpty(seg.fontName)) s = $"<font=\"{seg.fontName}\">{s}</font>";
        return s;
    }

    string EscapeRichText(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Replace("<", "&lt;").Replace(">", "&gt;");
    }

    string ColorToHex(Color c)
    {
        Color32 cc = c;
        return $"#{cc.r:X2}{cc.g:X2}{cc.b:X2}";
    }

    string MakePreserveString(string text, int visibleCount)
    {
        visibleCount = Mathf.Clamp(visibleCount, 0, text.Length);
        if (visibleCount >= text.Length) return text;
        string visible = text.Substring(0, visibleCount);
        string remaining = text.Substring(visibleCount);
        return visible + "<color=#00000000>" + EscapeRichText(remaining) + "</color>";
    }

    void SetDisplayedText(string s)
    {
        if (targetText != null)
        {
            if (!targetText.supportRichText && preserveLayout)
                Debug.LogWarning("[TypewriterUI] targetText 未启用 Rich Text，但 preserveLayout 为 true。");
            targetText.text = s;
            return;
        }

        if (tmpTextObject != null && tmpTextProperty != null)
        {
            tmpTextProperty.SetValue(tmpTextObject, s, null);
            return;
        }

        Debug.Log(s);
    }

    // 规范化 segment 文本：只移除 CR（\r），保留真实的换行符 '\n'，并保证 null -> empty
    string NormalizeSegmentText(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        // 只删除 '\r'，保留 '\n'，换行由文本中显式的 '\n' 控制
        return s.Replace("\r", "");
    }
}
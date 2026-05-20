using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.Events;

public class IntroCompleteNotifier : MonoBehaviour
{
    public TMP_Text textField;                 // 指向 Intro 面板上的 TMP 文本
    [Tooltip("文字保持不变超过该时间则认为当前页稳定（秒）")]
    public float stableTime = 0.4f;

    [Tooltip("是否需要用户交互（空格或左键）在最后一页时触发进入下一界面（推荐开启）")]
    public bool requireInputAfterLastPage = true;

    [Tooltip("用于检测按键后文本是否变化的延迟（秒），根据播放脚本可调 0.02-0.1")]
    public float postInputCheckDelay = 0.05f;

    public UnityEvent onComplete;              // 完成时触发的事件

    string lastText = "";
    float lastChangeTime = 0f;
    bool pageStable = false;
    bool triggered = false;

    void Start()
    {
        if (textField == null)
            textField = GetComponentInChildren<TMP_Text>();

        lastText = textField != null ? textField.text : "";
        lastChangeTime = Time.time;
        StartCoroutine(CheckRoutine());
    }

    IEnumerator CheckRoutine()
    {
        const float pollInterval = 0.05f;
        while (true)
        {
            string current = textField != null ? textField.text : "";
            if (current != lastText)
            {
                // 文本变化 -> 取消稳定，更新时间戳
                lastText = current;
                lastChangeTime = Time.time;
                pageStable = false;
            }
            else
            {
                // 文本未变，根据时间判断是否稳定（即当前页已显示完并等待交互）
                if (!pageStable && current.Length > 0 && Time.time - lastChangeTime >= stableTime)
                {
                    pageStable = true;
                }
            }

            yield return new WaitForSeconds(pollInterval);
        }
    }

    void Update()
    {
        if (triggered)
            return;

        // 仅在页面稳定时响应用户按键检测（避免在逐字显示过程中误判）
        if (pageStable && requireInputAfterLastPage)
        {
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0))
            {
                // 开始检测此交互是否导致页面前进
                StartCoroutine(HandleInputAdvance());
            }
        }
    }

    IEnumerator HandleInputAdvance()
    {
        if (textField == null)
            yield break;

        string before = textField.text;
        // 等待播放脚本响应翻页（或不响应表示最后一页）
        yield return new WaitForSeconds(postInputCheckDelay);

        string after = textField != null ? textField.text : "";

        if (after != before)
        {
            // 用户交互导致翻页：更新状态，继续等待下页稳定
            lastText = after;
            lastChangeTime = Time.time;
            pageStable = false;
        }
        else
        {
            // 用户交互没有导致文本变化 -> 认为已在最后一页，触发完成
            TriggerComplete();
        }
    }

    void TriggerComplete()
    {
        if (triggered)
            return;

        triggered = true;
        onComplete?.Invoke();
    }

    // 可由外部手动重置（例如重新播放 Intro）
    public void ResetNotifier()
    {
        triggered = false;
        pageStable = false;
        lastText = textField != null ? textField.text : "";
        lastChangeTime = Time.time;
        StopAllCoroutines();
        StartCoroutine(CheckRoutine());
    }

    // 由逐字播放脚本在播放结束时显式调用 —— 推荐使用（更可靠）
    public void NotifyComplete()
    {
        TriggerComplete();
    }
}
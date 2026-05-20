using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

public class CountDownTimer : MonoBehaviour
{
    [Header("倒计时设置")]
    [Tooltip("总倒计时时长（秒）。在 Inspector 中修改。")]
    [SerializeField]
    private float durationSeconds = 60f;

    [Tooltip("是否在 Awake/进入场景时自动开始")]
    [SerializeField]
    private bool startOnAwake = true;

    [Tooltip("使用不受时间缩放影响的时间（Realtime）")]
    [SerializeField]
    private bool useUnscaledTime = false;

    [Header("绑定的文本（可选择 Text 或 TextMeshPro）")]
    [Tooltip("如果使用 Unity UI 的 Text，请将其拖到这里")]
    [SerializeField]
    private Text uiText;

    [Tooltip("如果使用 TextMeshPro，请将其拖到这里")]
    [SerializeField]
    private TMP_Text tmpText;

    [Tooltip("可手动指定用于缩放动画的 Transform（优先）。如果为空，会使用绑定的文本的 Transform。")]
    [SerializeField]
    private Transform textTargetOverride;

    [Tooltip("是否自动将剩余时间同步到绑定的文本")]
    [SerializeField]
    private bool updateText = true;

    [Header("秒变更时的放大效果")]
    [Tooltip("放大倍数（相对于原始缩放）")]
    [SerializeField]
    private float pulseScale = 1.3f;

    [Tooltip("放大所用时长（秒）")]
    [SerializeField]
    private float pulseUpDuration = 0.08f;

    [Tooltip("回弹还原所用时长（秒）")]
    [SerializeField]
    private float pulseDownDuration = 0.12f;

    [Header("最后 N 秒警告")]
    [Tooltip("进入警告颜色的阈值（秒）")]
    [SerializeField]
    private int warningThresholdSeconds = 5;

    [Tooltip("警告颜色")]
    [SerializeField]
    private Color warningColor = Color.red;

    [Header("事件")]
    [Tooltip("倒计时结束时触发")]
    public UnityEvent OnFinished;

    // 当前剩余时间（秒），对外只读
    public float RemainingTime { get; private set; }

    // 是否正在运行
    public bool IsRunning { get; private set; }

    // 只读暴露总时长（方便外部读取）
    public float Duration => durationSeconds;

    // 用于检测秒数变化（使用向下取整的秒数）
    private int prevTotalSeconds = int.MinValue;

    // 原始颜色缓存（若绑定对应组件）
    private Color? originalUiTextColor;
    private Color? originalTmpTextColor;

    // 缩放目标与原始缩放缓存
    private Transform scaleTarget;
    private Vector3 originalScale;

    // 动画协程句柄（防止重复）
    private Coroutine pulseCoroutine;

    private void Awake()
    {
        // 初始化剩余时间为设定时长
        RemainingTime = Mathf.Max(0f, durationSeconds);

        CacheReferences();
        RestoreOriginalVisuals();
        UpdateDisplay(initial:true);

        if (startOnAwake)
        {
            StartTimer();
        }
    }

    private void CacheReferences()
    {
        // 选择缩放目标：优先使用 override，其次使用 tmpText，再次使用 uiText
        if (textTargetOverride != null)
        {
            scaleTarget = textTargetOverride;
        }
        else if (tmpText != null)
        {
            scaleTarget = tmpText.transform;
        }
        else if (uiText != null)
        {
            scaleTarget = uiText.transform;
        }
        else
        {
            scaleTarget = null;
        }

        if (scaleTarget != null)
        {
            originalScale = scaleTarget.localScale;
        }
        else
        {
            originalScale = Vector3.one;
        }

        // 缓存原始颜色（如果组件存在）
        if (uiText != null)
        {
            originalUiTextColor = uiText.color;
        }

        if (tmpText != null)
        {
            originalTmpTextColor = tmpText.color;
        }
    }

    private void RestoreOriginalVisuals()
    {
        // 恢复文本颜色与缩放（用于 Awake/OnValidate）
        if (uiText != null && originalUiTextColor.HasValue)
        {
            uiText.color = originalUiTextColor.Value;
        }

        if (tmpText != null && originalTmpTextColor.HasValue)
        {
            tmpText.color = originalTmpTextColor.Value;
        }

        if (scaleTarget != null)
        {
            scaleTarget.localScale = originalScale;
        }
    }

    private void Update()
    {
        if (!IsRunning)
        {
            return;
        }

        float delta = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        RemainingTime -= delta;

        if (RemainingTime <= 0f)
        {
            RemainingTime = 0f;
            IsRunning = false;
            UpdateDisplay();
            OnFinished?.Invoke();
            return;
        }

        UpdateDisplay();
    }

    /// <summary>
    /// 开始计时。如果传入 newDuration 则用于覆盖当前时长并重置计时器。
    /// </summary>
    public void StartTimer(float? newDuration = null)
    {
        if (newDuration.HasValue)
        {
            durationSeconds = Mathf.Max(0f, newDuration.Value);
            RemainingTime = durationSeconds;
        }
        else if (RemainingTime <= 0f)
        {
            // 如果之前已经到 0，则重置为设定时长再开始
            RemainingTime = Mathf.Max(0f, durationSeconds);
        }

        if (durationSeconds <= 0f)
        {
            // 直接触发结束
            RemainingTime = 0f;
            IsRunning = false;
            UpdateDisplay();
            OnFinished?.Invoke();
            return;
        }

        IsRunning = true;
        UpdateDisplay();
    }

    /// <summary>
    /// 停止计时（保留当前剩余时间）。
    /// </summary>
    public void StopTimer()
    {
        IsRunning = false;
        UpdateDisplay();
    }

    /// <summary>
    /// 重置计时器为当前设定的时长并停止（如果 wantStart 为 true 则立即开始）。
    /// </summary>
    public void ResetTimer(bool wantStart = false)
    {
        RemainingTime = Mathf.Max(0f, durationSeconds);
        IsRunning = false;
        UpdateDisplay();
        if (wantStart)
        {
            StartTimer();
        }
    }

    /// <summary>
    /// 直接设置新的时长并根据参数决定是否立即开始。
    /// </summary>
    public void SetDuration(float newDuration, bool startImmediately = false)
    {
        durationSeconds = Mathf.Max(0f, newDuration);
        RemainingTime = durationSeconds;
        IsRunning = false;
        UpdateDisplay();
        if (startImmediately)
        {
            StartTimer();
        }
    }

    // 将剩余时间格式化为 "mm:ss" 并写入绑定的文本组件
    private void UpdateDisplay(bool initial = false)
    {
        if (!updateText)
        {
            return;
        }

        // 计算显示的总秒数（向下取整）
        int totalSeconds = Mathf.FloorToInt(Mathf.Max(0f, RemainingTime));

        // 颜色警告处理（优先显示警告色）
        if (totalSeconds <= warningThresholdSeconds)
        {
            ApplyWarningColor();
        }
        else
        {
            RestoreTextColors();
        }

        string text = FormatTime(RemainingTime);

        if (tmpText != null)
        {
            tmpText.text = text;
        }

        if (uiText != null)
        {
            uiText.text = text;
        }

        // 秒数变化时触发放大效果：忽略初始同步时的触发
        if (prevTotalSeconds == int.MinValue)
        {
            // 首次赋值，不触发动画
            prevTotalSeconds = totalSeconds;
            return;
        }

        if (!initial && totalSeconds != prevTotalSeconds && totalSeconds < prevTotalSeconds)
        {
            // 倒计时的秒数向下变化时才触发脉冲动画
            StartPulse();
        }

        prevTotalSeconds = totalSeconds;
    }

    private void ApplyWarningColor()
    {
        if (uiText != null && originalUiTextColor.HasValue)
        {
            uiText.color = warningColor;
        }

        if (tmpText != null && originalTmpTextColor.HasValue)
        {
            tmpText.color = warningColor;
        }
    }

    private void RestoreTextColors()
    {
        if (uiText != null && originalUiTextColor.HasValue)
        {
            uiText.color = originalUiTextColor.Value;
        }

        if (tmpText != null && originalTmpTextColor.HasValue)
        {
            tmpText.color = originalTmpTextColor.Value;
        }
    }

    private void StartPulse()
    {
        if (scaleTarget == null)
        {
            return;
        }

        // 防止启动多个协程
        if (pulseCoroutine != null)
        {
            StopCoroutine(pulseCoroutine);
            pulseCoroutine = null;
            // 确保恢复原始缩放
            scaleTarget.localScale = originalScale;
        }

        pulseCoroutine = StartCoroutine(PulseCoroutine());
    }

    private IEnumerator PulseCoroutine()
    {
        Vector3 targetScale = originalScale * pulseScale;

        // 向上插值
        float t = 0f;
        while (t < pulseUpDuration)
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            t += dt;
            float lerp = Mathf.Clamp01(pulseUpDuration > 0f ? t / pulseUpDuration : 1f);
            scaleTarget.localScale = Vector3.Lerp(originalScale, targetScale, lerp);
            yield return null;
        }

        // 向下插值回原始
        t = 0f;
        while (t < pulseDownDuration)
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            t += dt;
            float lerp = Mathf.Clamp01(pulseDownDuration > 0f ? t / pulseDownDuration : 1f);
            scaleTarget.localScale = Vector3.Lerp(targetScale, originalScale, lerp);
            yield return null;
        }

        scaleTarget.localScale = originalScale;
        pulseCoroutine = null;
    }

    // 将秒数格式化为 mm:ss。使用向下取整显示剩余的完整秒数（例如 59.9 -> 00:59）。
    private static string FormatTime(float secondsFloat)
    {
        int totalSeconds = Mathf.FloorToInt(Mathf.Max(0f, secondsFloat));
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        return $"{minutes:00}:{seconds:00}";
    }

#if UNITY_EDITOR
    // 在编辑器中调整 Inspector 时更新显示，方便调试
    private void OnValidate()
    {
        RemainingTime = Mathf.Max(0f, durationSeconds);
        CacheReferences();
        RestoreOriginalVisuals();
        UpdateDisplay(initial:true);
    }
#endif
}

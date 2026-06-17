using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

public class CountDownTimer : MonoBehaviour
{
    [Header("?????????")]
    [Tooltip("????????????????? Inspector ??????")]
    [SerializeField]
    private float durationSeconds = 60f;

    [Tooltip("????? Awake/?????????????")]
    [SerializeField]
    private bool startOnAwake = true;

    [Tooltip("?????????????????????Realtime??")]
    [SerializeField]
    private bool useUnscaledTime = false;

    [Header("????????????? Text ?? TextMeshPro??")]
    [Tooltip("?????? Unity UI ?? Text?????????????")]
    [SerializeField]
    private Text uiText;

    [Tooltip("?????? TextMeshPro?????????????")]
    [SerializeField]
    private TMP_Text tmpText;

    [Tooltip("????????????????????? Transform???????????????????????????? Transform??")]
    [SerializeField]
    private Transform textTargetOverride;

    [Tooltip("?????????????????????????")]
    [SerializeField]
    private bool updateText = true;

    [Header("?????????????")]
    [Tooltip("???????????????????")]
    [SerializeField]
    private float pulseScale = 1.3f;

    [Tooltip("??????????????")]
    [SerializeField]
    private float pulseUpDuration = 0.08f;

    [Tooltip("?????????????????")]
    [SerializeField]
    private float pulseDownDuration = 0.12f;

    [Header("??? N ????")]
    [Tooltip("??????????????????")]
    [SerializeField]
    private int warningThresholdSeconds = 5;

    [Tooltip("???????")]
    [SerializeField]
    private Color warningColor = Color.red;

    [Header("???")]
    [Tooltip("??????????????")]
    public UnityEvent OnFinished;

    // ????????????????????
    public float RemainingTime { get; private set; }

    // ???????????
    public bool IsRunning { get; private set; }

    public bool IsPaused => isPaused;

    // ????????????????????????
    public float Duration => durationSeconds;

    // ?????????????????????????????????
    private int prevTotalSeconds = int.MinValue;

    // ?????????????????????
    private Color? originalUiTextColor;
    private Color? originalTmpTextColor;

    // ??????????????????
    private Transform scaleTarget;
    private Vector3 originalScale;

    // ????????????????????
    private Coroutine pulseCoroutine;

    // ?????????????????????????????????
    private bool warningSoundPlayed;
    private bool isPaused;

    private void Awake()
    {
        // ?????????????????
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
        // ????????????????? override???????? tmpText???????? uiText
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

        // ??????????????????????
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
        // ???????????????????? Awake/OnValidate??
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
        if (!IsRunning || isPaused)
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
    /// ??????????????? newDuration ???????????????????????????
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
            // ?????????? 0??????????????????
            RemainingTime = Mathf.Max(0f, durationSeconds);
        }

        if (durationSeconds <= 0f)
        {
            // ??????????
            RemainingTime = 0f;
            IsRunning = false;
            UpdateDisplay();
            OnFinished?.Invoke();
            return;
        }

        IsRunning = true;
        warningSoundPlayed = false;
        UpdateDisplay();
    }

    /// <summary>
    /// ??????????????????????
    /// </summary>
    public void StopTimer()
    {
        IsRunning = false;
        isPaused = false;
        UpdateDisplay();
    }

    public void PauseTimer()
    {
        if (!IsRunning) return;
        isPaused = true;
    }

    public void ResumeTimer()
    {
        if (!IsRunning) return;
        isPaused = false;
    }

    /// <summary>
    /// ????????????????????????????? wantStart ? true ?????????????
    /// </summary>
    public void ResetTimer(bool wantStart = false)
    {
        RemainingTime = Mathf.Max(0f, durationSeconds);
        IsRunning = false;
        warningSoundPlayed = false;
        UpdateDisplay();
        if (wantStart)
        {
            StartTimer();
        }
    }

    /// <summary>
    /// ??????????????????????????????????????
    /// </summary>
    public void SetDuration(float newDuration, bool startImmediately = false)
    {
        durationSeconds = Mathf.Max(0f, newDuration);
        RemainingTime = durationSeconds;
        IsRunning = false;
        warningSoundPlayed = false;
        UpdateDisplay();
        if (startImmediately)
        {
            StartTimer();
        }
    }

    // ????????????? "mm:ss" ??????????????
    private void UpdateDisplay(bool initial = false)
    {
        if (!updateText)
        {
            return;
        }

        // ??????????????????????????
        int totalSeconds = Mathf.FloorToInt(Mathf.Max(0f, RemainingTime));

        // ??????????????????????????
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

        // ????????????????????????????????????
        if (prevTotalSeconds == int.MinValue)
        {
            // ??????????????????
            prevTotalSeconds = totalSeconds;
            return;
        }

        if (!initial && totalSeconds != prevTotalSeconds && totalSeconds < prevTotalSeconds)
        {
            StartPulse();
            PlayCountdownSfx(totalSeconds, prevTotalSeconds);
        }

        prevTotalSeconds = totalSeconds;
    }

    void PlayCountdownSfx(int totalSeconds, int previousTotalSeconds)
    {
        if (SfxManager.Instance == null) return;

        if (!warningSoundPlayed
            && totalSeconds <= warningThresholdSeconds
            && previousTotalSeconds > warningThresholdSeconds)
        {
            warningSoundPlayed = true;
            SfxManager.Instance.PlayCountdownWarning();
        }

        SfxManager.Instance.PlayCountdownTick();
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

        // ??????????????
        if (pulseCoroutine != null)
        {
            StopCoroutine(pulseCoroutine);
            pulseCoroutine = null;
            // ????????????
            scaleTarget.localScale = originalScale;
        }

        pulseCoroutine = StartCoroutine(PulseCoroutine());
    }

    private IEnumerator PulseCoroutine()
    {
        Vector3 targetScale = originalScale * pulseScale;

        // ??????
        float t = 0f;
        while (t < pulseUpDuration)
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            t += dt;
            float lerp = Mathf.Clamp01(pulseUpDuration > 0f ? t / pulseUpDuration : 1f);
            scaleTarget.localScale = Vector3.Lerp(originalScale, targetScale, lerp);
            yield return null;
        }

        // ??????????
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

    // ???????????? mm:ss????????????????????????????????? 59.9 -> 00:59????
    private static string FormatTime(float secondsFloat)
    {
        int totalSeconds = Mathf.FloorToInt(Mathf.Max(0f, secondsFloat));
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        return $"{minutes:00}:{seconds:00}";
    }

#if UNITY_EDITOR
    // ??????????? Inspector ?????????????????
    private void OnValidate()
    {
        RemainingTime = Mathf.Max(0f, durationSeconds);
        CacheReferences();
        RestoreOriginalVisuals();
        UpdateDisplay(initial:true);
    }
#endif
}

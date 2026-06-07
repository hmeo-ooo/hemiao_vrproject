using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 协调关卡准备 UI、倒计时、掉落与玩家输入。
/// </summary>
[DefaultExecutionOrder(10)]
public class LevelSessionController : MonoBehaviour
{
    [Header("引用")]
    public LevelManager levelManager;
    public LevelHubUI levelHubUI;
    public LevelTutorialUI levelTutorialUI;
    public CountDownTimer countDownTimer;

    [Tooltip("游戏中显示的面板（关卡号、倒计时、余额等）；准备界面时隐藏。")]
    public GameObject gameplayHudRoot;

    [Tooltip("可选：同步 Billboard 上的 Day 文本。")]
    public TMP_Text gameplayDayText;

    public string dayTextFormat = "Day {0}";

    [Header("回合结束")]
    [Tooltip("倒计时结束后是否自动准备下一关（无下一关则重载当前关）。")]
    public bool advanceLevelAfterRound = true;

    bool _roundActive;
    Canvas _gameplayHudCanvas;
    Vector3 _gameplayHudCanvasScale = Vector3.one;

    // 干扰排程：回合开始后从 0 计时；pending 中元素在到达 triggerAtSeconds 时
    // 被搬运到 active；active 中若 durationSeconds>0 且到时间则自动 Stop。
    float _roundElapsed;
    readonly List<LevelInterferenceConfig> _pendingInterferences = new List<LevelInterferenceConfig>();
    readonly List<LevelInterferenceConfig> _activeInterferences = new List<LevelInterferenceConfig>();

    void Awake()
    {
        if (levelManager == null)
            levelManager = LevelManager.Instance;
        if (levelHubUI == null)
            levelHubUI = GetComponent<LevelHubUI>();
        if (levelTutorialUI == null)
            levelTutorialUI = GetComponent<LevelTutorialUI>();
        if (countDownTimer == null)
            countDownTimer = FindObjectOfType<CountDownTimer>();
    }

    void Start()
    {
        CacheGameplayHudCanvas();

        if (levelHubUI != null)
            levelHubUI.Bind(this);

        if (countDownTimer != null)
        {
            countDownTimer.StopTimer();
            countDownTimer.OnFinished.AddListener(OnCountdownFinished);
        }

        int preferred = levelManager != null ? levelManager.startLevelIndex : 0;
        int index = levelManager != null ? levelManager.ResolveLevelIndex(preferred) : 0;
        PrepareLevelAndShowHub(index);
    }

    void OnDestroy()
    {
        if (countDownTimer != null)
            countDownTimer.OnFinished.RemoveListener(OnCountdownFinished);
    }

    public void OnEnterLevelButtonClicked()
    {
        if (_roundActive) return;
        TryShowTutorialOrBeginRound();
    }

    void TryShowTutorialOrBeginRound()
    {
        if (levelManager == null)
            levelManager = LevelManager.Instance;

        LevelDefinition def = levelManager != null ? levelManager.CurrentLevel : null;
        if (ShouldShowTutorial(def))
        {
            levelHubUI?.Hide();
            levelTutorialUI.Show(def, OnTutorialConfirmed, OnTutorialBack);
            GameplayInputGate.SetBlocked(true);
            return;
        }

        BeginRound();
    }

    void OnTutorialConfirmed()
    {
        levelTutorialUI?.Hide();
        BeginRound();
    }

    void OnTutorialBack()
    {
        levelTutorialUI?.Hide();
        GameplayInputGate.SetBlocked(true);
        levelHubUI?.Show();
    }

    static bool ShouldShowTutorial(LevelDefinition def)
    {
        if (def == null || !def.showTutorialBeforeLevel || !def.HasTutorialContent)
            return false;
        return true;
    }

    /// <summary>
    /// 玩家在「选择关卡」面板中点了某关：加载该关，并停留在准备界面。
    /// </summary>
    public void OnLevelPicked(int levelIndex)
    {
        if (_roundActive) return;
        PrepareLevelAndShowHub(levelIndex);
    }

    public void OnRepayDebtButtonClicked()
    {
        if (DebtManager.Instance == null)
        {
            levelHubUI?.SetStatus("Debt system not found.");
            return;
        }

        if (DebtManager.Instance.TryRepayFromCredits(out int paid))
        {
            levelHubUI?.SetStatus($"Repaid {paid:N0} credits.");
            if (CreditManager.Instance != null)
                CreditManager.Instance.ShowSubtitle($"Debt repaid: {paid:N0}", 2f, new Color(0.4f, 1f, 0.5f));
        }
        else
        {
            levelHubUI?.SetStatus("Not enough credits or debt is cleared.");
        }

        levelHubUI?.Refresh();
    }

    void OnCountdownFinished()
    {
        if (!_roundActive) return;
        if (SfxManager.Instance != null)
            SfxManager.Instance.PlayRoundEnd();
        EndRoundAndShowHub(advanceLevelAfterRound);
    }

    public void PrepareLevelAndShowHub(int levelIndex)
    {
        _roundActive = false;

        if (levelManager == null)
            levelManager = LevelManager.Instance;

        bool loaded = false;
        if (levelManager != null)
        {
            int resolved = levelManager.ResolveLevelIndex(levelIndex);
            loaded = levelManager.LoadLevel(resolved);
        }

        levelManager?.EndLevelGameplay();

        if (countDownTimer != null)
            countDownTimer.StopTimer();

        GameplayInputGate.SetBlocked(true);
        SetGameplayHudVisible(false);
        levelHubUI?.SetStatus(loaded
            ? string.Empty
            : "Failed to load level. Assign LevelDefinition assets on LevelManager.");
        levelHubUI?.Show();
        RefreshGameplayDayText();
    }

    public void BeginRound()
    {
        if (levelManager == null)
            levelManager = LevelManager.Instance;

        if (levelManager == null)
        {
            levelHubUI?.SetStatus("LevelManager not found.");
            return;
        }

        if (levelManager.CurrentLevel == null)
        {
            int index = levelManager.ResolveLevelIndex(
                levelManager.CurrentLevelIndex >= 0 ? levelManager.CurrentLevelIndex : levelManager.startLevelIndex);
            if (!levelManager.LoadLevel(index))
            {
                levelHubUI?.SetStatus("No level data configured. Assign LevelDefinition assets on LevelManager.");
                return;
            }
        }

        _roundActive = true;
        CreditManager.Instance?.ResetThrowCombo();
        levelHubUI?.Hide();
        GameplayInputGate.SetBlocked(false);
        SetGameplayHudVisible(true);
        RefreshGameplayDayText();

        levelManager.BeginLevelGameplay();

        if (countDownTimer != null)
        {
            float duration = Mathf.Max(1f, levelManager.CurrentLevel.levelDurationSeconds);
            countDownTimer.SetDuration(duration, true);
        }

        BeginRoundInterferences(levelManager.CurrentLevel);
    }

    public void EndRoundAndShowHub(bool tryAdvanceLevel)
    {
        _roundActive = false;
        StopAllInterferences();
        levelManager?.EndLevelGameplay();

        if (countDownTimer != null)
            countDownTimer.StopTimer();

        int nextIndex = levelManager != null ? levelManager.CurrentLevelIndex : 0;
        if (tryAdvanceLevel && levelManager != null)
        {
            int candidate = levelManager.CurrentLevelIndex + 1;
            if (candidate < levelManager.LevelCount)
                nextIndex = levelManager.ResolveLevelIndex(candidate);
        }

        PrepareLevelAndShowHub(nextIndex);
    }

    /// <summary>
    /// 场上物品全部处理完毕后，玩家通过 bed 等交互点提前结束本关。
    /// </summary>
    public void EndRoundEarly()
    {
        if (!_roundActive) return;

        LevelManager lm = levelManager != null ? levelManager : LevelManager.Instance;
        if (lm == null || !lm.IsAllItemsProcessed()) return;

        if (SfxManager.Instance != null)
            SfxManager.Instance.PlayRoundEnd();

        EndRoundAndShowHub(advanceLevelAfterRound);
    }

    void Update()
    {
        if (!_roundActive) return;
        _roundElapsed += Time.deltaTime;
        TickInterferences();
    }

    // ------------------------------------------------------------------
    // 关卡干扰排程
    // ------------------------------------------------------------------

    void BeginRoundInterferences(LevelDefinition def)
    {
        _roundElapsed = 0f;
        StopAllInterferences();

        if (def == null || def.interferences == null) return;
        for (int i = 0; i < def.interferences.Length; i++)
        {
            var cfg = def.interferences[i];
            if (cfg == null) continue;
            _pendingInterferences.Add(cfg);
        }
    }

    void TickInterferences()
    {
        for (int i = _pendingInterferences.Count - 1; i >= 0; i--)
        {
            var cfg = _pendingInterferences[i];
            if (_roundElapsed >= cfg.triggerAtSeconds)
            {
                StartInterference(cfg);
                _pendingInterferences.RemoveAt(i);
                _activeInterferences.Add(cfg);
            }
        }

        for (int i = _activeInterferences.Count - 1; i >= 0; i--)
        {
            var cfg = _activeInterferences[i];

            // 玩家提前结束（例如 TVStaticOverlay 连续按 E 取消后），把它从活跃列表里移除
            if (HasInterferenceBeenDismissedExternally(cfg))
            {
                _activeInterferences.RemoveAt(i);
                continue;
            }

            if (cfg.durationSeconds <= 0f) continue; // 持续到回合结束
            if (_roundElapsed >= cfg.triggerAtSeconds + cfg.durationSeconds)
            {
                StopInterference(cfg);
                _activeInterferences.RemoveAt(i);
            }
        }
    }

    bool HasInterferenceBeenDismissedExternally(LevelInterferenceConfig cfg)
    {
        switch (cfg.type)
        {
            case LevelInterferenceConfig.InterferenceType.TVStaticOverlay:
                return !TVStaticOverlay.IsActive;
        }
        return false;
    }

    void StartInterference(LevelInterferenceConfig cfg)
    {
        switch (cfg.type)
        {
            case LevelInterferenceConfig.InterferenceType.TVStaticOverlay:
                TVStaticOverlay.Instance.Show(new TVStaticOverlayParams
                {
                    intensity = cfg.intensity,
                    noiseFps = cfg.noiseFps,
                    textureSize = cfg.noiseTextureSize,
                    tint = cfg.tint,
                    centerSprite = cfg.centerPatternSprite,
                    centerSize = cfg.centerPatternSize,
                    centerPulseScale = cfg.centerPatternPulseScale,
                    centerPulseFrequencyHz = cfg.centerPatternPulseFrequencyHz,
                    centerRestColor = cfg.centerPatternColor,
                    cancelKey = cfg.cancelKey,
                    pressesToCancel = cfg.pressesToCancel,
                    flashColor = cfg.flashColor,
                    flashDuration = cfg.flashDuration,
                });
                break;
        }
    }

    void StopInterference(LevelInterferenceConfig cfg)
    {
        switch (cfg.type)
        {
            case LevelInterferenceConfig.InterferenceType.TVStaticOverlay:
                if (TVStaticOverlay.IsActive)
                    TVStaticOverlay.Instance.Hide();
                break;
        }
    }

    void StopAllInterferences()
    {
        for (int i = 0; i < _activeInterferences.Count; i++)
            StopInterference(_activeInterferences[i]);
        _activeInterferences.Clear();
        _pendingInterferences.Clear();
    }

    void CacheGameplayHudCanvas()
    {
        if (gameplayHudRoot == null) return;
        _gameplayHudCanvas = gameplayHudRoot.GetComponentInParent<Canvas>();
        if (_gameplayHudCanvas != null)
            _gameplayHudCanvasScale = _gameplayHudCanvas.transform.localScale;
    }

    void SetGameplayHudVisible(bool visible)
    {
        if (gameplayHudRoot != null)
            gameplayHudRoot.SetActive(visible);

        if (_gameplayHudCanvas == null)
            CacheGameplayHudCanvas();

        if (_gameplayHudCanvas != null && visible && _gameplayHudCanvas.transform.localScale.sqrMagnitude < 0.0001f)
            _gameplayHudCanvas.transform.localScale = Vector3.one;
    }

    void RefreshGameplayDayText()
    {
        if (gameplayDayText == null || levelManager == null) return;

        LevelDefinition def = levelManager.CurrentLevel;
        int day = def != null ? def.levelNumber : levelManager.CurrentLevelIndex + 1;
        gameplayDayText.text = string.Format(dayTextFormat, day);
    }
}

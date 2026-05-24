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

    void Awake()
    {
        if (levelManager == null)
            levelManager = LevelManager.Instance;
        if (levelHubUI == null)
            levelHubUI = GetComponent<LevelHubUI>();
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
        BeginRound();
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
    }

    public void EndRoundAndShowHub(bool tryAdvanceLevel)
    {
        _roundActive = false;
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
            _gameplayHudCanvas.transform.localScale = _gameplayHudCanvasScale.sqrMagnitude > 0.0001f
                ? _gameplayHudCanvasScale
                : Vector3.one;
    }

    void RefreshGameplayDayText()
    {
        if (gameplayDayText == null || levelManager == null) return;

        LevelDefinition def = levelManager.CurrentLevel;
        int day = def != null ? def.levelNumber : levelManager.CurrentLevelIndex + 1;
        gameplayDayText.text = string.Format(dayTextFormat, day);
    }
}

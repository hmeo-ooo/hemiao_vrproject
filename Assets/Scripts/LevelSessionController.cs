using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 协调关卡准备 UI、倒计时、掉落与玩家输入。
/// </summary>
[DefaultExecutionOrder(10)]
[RequireComponent(typeof(StartScreenUI))]
public class LevelSessionController : MonoBehaviour
{
    [Header("引用")]
    public LevelManager levelManager;
    public StartScreenUI startScreenUI;
    public BackstoryController backstoryIntroUI;
    public LevelHubUI levelHubUI;
    public LevelTutorialUI levelTutorialUI;
    public PauseMenuUI pauseMenuUI;
    public CountDownTimer countDownTimer;

    [Tooltip("游戏中显示的面板（关卡号、倒计时、余额等）；准备界面时隐藏。")]
    public GameObject gameplayHudRoot;

    [Tooltip("可选：同步 Billboard 上的 Day 文本。")]
    public TMP_Text gameplayDayText;

    public string dayTextFormat = "Day {0}";

    [Header("背景故事")]
    [Tooltip("点击 Start 后、进入关卡选择前，是否播放背景故事导入 UI。")]
    public bool showBackstoryIntroOnStart = true;

    [Header("回合结束")]
    [Tooltip("倒计时结束后是否自动准备下一关（无下一关则重载当前关）。")]
    public bool advanceLevelAfterRound = true;

    [Tooltip("回合结束（含早退、倒计时结束）后，是否把玩家瞬移回 playerSpawnPoint。")]
    public bool resetPlayerOnRoundEnd = true;

    [Tooltip("玩家根节点（带 CharacterMove）。留空则在 Start 时自动查找 CharacterInteraction 所在物体。")]
    public Transform playerTransform;

    [Tooltip("回合结束后玩家被瞬移到该 Transform 的 position/rotation。\n" +
             "留空则在 Start 时自动用玩家初始位置作为出生点。")]
    public Transform playerSpawnPoint;

    [Tooltip("正式进入关卡时玩家朝向（Y 轴欧拉角，度）。")]
    public float playerRoundStartRotationY = 180f;

    bool _roundActive;
    bool _gamePaused;
    float _savedTimeScale = 1f;
    Vector3 _autoSpawnPosition;
    Quaternion _autoSpawnRotation = Quaternion.identity;
    bool _autoSpawnCaptured;
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
        if (startScreenUI == null)
            startScreenUI = GetComponent<StartScreenUI>();
        if (backstoryIntroUI == null)
            backstoryIntroUI = GetComponent<BackstoryController>();
        if (levelHubUI == null)
            levelHubUI = GetComponent<LevelHubUI>();
        if (levelTutorialUI == null)
            levelTutorialUI = GetComponent<LevelTutorialUI>();
        if (pauseMenuUI == null)
            pauseMenuUI = GetComponent<PauseMenuUI>();
        if (countDownTimer == null)
            countDownTimer = FindObjectOfType<CountDownTimer>();

        CacheGameplayHudCanvas();
        SetGameplayHudVisible(false);
    }

    void Start()
    {
        ResolvePlayerTransform();
        CapturePlayerSpawnPose();

        if (levelHubUI != null)
            levelHubUI.Bind(this);

        if (countDownTimer != null)
        {
            countDownTimer.StopTimer();
            countDownTimer.OnFinished.AddListener(OnCountdownFinished);
        }

        SetGameplayHudVisible(false);

        if (startScreenUI != null)
            startScreenUI.Show(OnStartScreenDismissed);
        else
            ShowInitialHub();
    }

    void OnStartScreenDismissed()
    {
        if (ShouldShowBackstoryIntro())
            backstoryIntroUI.Show(OnBackstoryIntroFinished);
        else
            ShowInitialHub();
    }

    bool ShouldShowBackstoryIntro()
    {
        if (!showBackstoryIntroOnStart || backstoryIntroUI == null)
            return false;
        return backstoryIntroUI.HasContent;
    }

    void OnBackstoryIntroFinished()
    {
        ShowInitialHub();
    }

    void ShowInitialHub()
    {
        int preferred = levelManager != null ? levelManager.startLevelIndex : 0;
        int index = levelManager != null ? levelManager.ResolveLevelIndex(preferred) : 0;
        PrepareLevelAndShowHub(index);
    }

    void OnDestroy()
    {
        if (countDownTimer != null)
            countDownTimer.OnFinished.RemoveListener(OnCountdownFinished);
        ForceResumeIfPaused();
    }

    public bool IsGamePaused => _gamePaused;

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
        ForceResumeIfPaused();

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
        ApplyPlayerRoundStartPose();
        CreditManager.Instance?.ResetThrowCombo();
        levelHubUI?.Hide();
        GameplayInputGate.SetBlocked(false);
        SetGameplayHudVisible(true);
        RefreshGameplayDayText();

        levelManager.BeginLevelGameplay();

        CharacterInteraction character = FindObjectOfType<CharacterInteraction>();
        character?.ForceRefreshInteractionVisuals();

        if (countDownTimer != null)
        {
            float duration = Mathf.Max(1f, levelManager.CurrentLevel.levelDurationSeconds);
            countDownTimer.SetDuration(duration, true);
        }

        BeginRoundInterferences(levelManager.CurrentLevel);
    }

    public void EndRoundAndShowHub(bool tryAdvanceLevel)
    {
        ForceResumeIfPaused();
        _roundActive = false;
        StopAllInterferences();

        // 关卡结束：强制清空场上所有垃圾/碎片/可拾取物，避免遗留到下一关。
        if (levelManager != null)
        {
            levelManager.EndLevelGameplay();
            levelManager.ClearAllGameplayItems();
        }

        if (countDownTimer != null)
            countDownTimer.StopTimer();

        if (resetPlayerOnRoundEnd)
            ResetPlayerToSpawn();

        int nextIndex = levelManager != null ? levelManager.CurrentLevelIndex : 0;
        if (tryAdvanceLevel && levelManager != null)
        {
            int candidate = levelManager.CurrentLevelIndex + 1;
            if (candidate < levelManager.LevelCount)
                nextIndex = levelManager.ResolveLevelIndex(candidate);
        }

        PrepareLevelAndShowHub(nextIndex);
    }

    // ------------------------------------------------------------------
    // 玩家出生点
    // ------------------------------------------------------------------

    void ResolvePlayerTransform()
    {
        if (playerTransform != null) return;

        CharacterInteraction character = FindObjectOfType<CharacterInteraction>();
        if (character != null)
            playerTransform = character.transform;
    }

    void CapturePlayerSpawnPose()
    {
        if (playerTransform == null) return;
        if (playerSpawnPoint != null) return;

        _autoSpawnPosition = playerTransform.position;
        _autoSpawnRotation = GetRoundStartRotation();
        _autoSpawnCaptured = true;
    }

    Quaternion GetRoundStartRotation()
    {
        return Quaternion.Euler(0f, playerRoundStartRotationY, 0f);
    }

    void SyncCharacterMoveYaw(Transform target, Quaternion rotation)
    {
        if (target == null) return;
        CharacterMove move = target.GetComponent<CharacterMove>();
        if (move != null)
            move.SetYaw(rotation.eulerAngles.y);
    }

    /// <summary>正式进入关卡：回到出生点并应用关卡起始朝向。</summary>
    void ApplyPlayerRoundStartPose()
    {
        ResetPlayerToSpawn();
    }

    /// <summary>把玩家瞬移回 playerSpawnPoint（优先）或开局自动捕获的初始姿态，并清零物理速度。</summary>
    public void ResetPlayerToSpawn()
    {
        if (playerTransform == null)
            ResolvePlayerTransform();
        if (playerTransform == null) return;

        Vector3 pos;
        Quaternion rot;
        if (playerSpawnPoint != null)
        {
            pos = playerSpawnPoint.position;
            rot = GetRoundStartRotation();
        }
        else if (_autoSpawnCaptured)
        {
            pos = _autoSpawnPosition;
            rot = _autoSpawnRotation;
        }
        else
        {
            return;
        }

        // 玩家手里的物品已在 ClearAllGameplayItems 内被释放+销毁，这里无需再处理。
        Rigidbody rb = playerTransform.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = pos;
            rb.rotation = rot;
        }

        playerTransform.SetPositionAndRotation(pos, rot);
        SyncCharacterMoveYaw(playerTransform, rot);

        Physics.SyncTransforms();
    }

    /// <summary>
    /// 场上垃圾全部处理完毕、垃圾堆不再补充后，玩家通过 bed 等交互点提前结束本关。
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
        if (Input.GetKeyDown(KeyCode.Escape))
            TryTogglePause();

        if (!_roundActive) return;
        if (_gamePaused) return;

        _roundElapsed += Time.deltaTime;
        TickInterferences();
    }

    void TryTogglePause()
    {
        if (_gamePaused)
        {
            ResumeGame();
            return;
        }

        if (!_roundActive || !CanOpenPauseMenu())
            return;

        PauseGame();
    }

    bool CanOpenPauseMenu()
    {
        InspectionView inspection = InspectionView.Instance;
        if (inspection != null && inspection.IsInspecting)
            return false;

        if (TVStaticOverlay.IsActive)
            return false;

        return true;
    }

    void PauseGame()
    {
        if (_gamePaused || !_roundActive) return;

        _gamePaused = true;
        _savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        GameplayInputGate.SetBlocked(true);
        countDownTimer?.PauseTimer();

        if (SfxManager.Instance != null)
            SfxManager.Instance.PauseBgm();

        pauseMenuUI?.Show(ResumeGame);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void ResumeGame()
    {
        if (!_gamePaused) return;

        _gamePaused = false;
        Time.timeScale = _savedTimeScale > 0f ? _savedTimeScale : 1f;

        pauseMenuUI?.Hide();

        if (!_roundActive)
            return;

        GameplayInputGate.SetBlocked(false);
        countDownTimer?.ResumeTimer();

        if (SfxManager.Instance != null)
            SfxManager.Instance.ResumeBgm();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void ForceResumeIfPaused()
    {
        if (!_gamePaused) return;

        _gamePaused = false;
        Time.timeScale = _savedTimeScale > 0f ? _savedTimeScale : 1f;
        pauseMenuUI?.Hide();

        if (SfxManager.Instance != null)
            SfxManager.Instance.ResumeBgm();
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
        if (cfg == null) return;
        cfg.SanitizeDefaults();
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

        if (_gameplayHudCanvas != null && visible)
            _gameplayHudCanvas.transform.localScale = Vector3.one;

        if (visible)
        {
            BillboardUI billboard = gameplayHudRoot != null
                ? gameplayHudRoot.GetComponentInParent<BillboardUI>()
                : null;
            billboard?.RefreshHudLayout();
        }
    }

    void RefreshGameplayDayText()
    {
        if (gameplayDayText == null || levelManager == null) return;

        LevelDefinition def = levelManager.CurrentLevel;
        int day = def != null ? def.levelNumber : levelManager.CurrentLevelIndex + 1;
        gameplayDayText.text = string.Format(dayTextFormat, day);
    }
}

using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 全局音效管理器。在首个场景中放置一个带本组件的空物体（建议与 CreditManager 同级），
/// 并在 Inspector 中绑定 AudioClip。场景切换后通过 DontDestroyOnLoad 保留。
/// </summary>
[DefaultExecutionOrder(-50)]
public class SfxManager : MonoBehaviour
{
    public static SfxManager Instance { get; private set; }

    [Header("音源")]
    [Tooltip("2D UI 音效（积分、倒计时等）")]
    [SerializeField] AudioSource uiSource;

    [Tooltip("3D 世界音效（可选备用音源）")]
    [SerializeField] AudioSource worldSource;

    [Header("音量")]
    [Range(0f, 1f)]
    [SerializeField] float masterVolume = 1f;

    [Range(0f, 1f)]
    [SerializeField] float uiVolume = 1f;

    [Range(0f, 1f)]
    [SerializeField] float worldVolume = 1f;

    [Header("BGM")]
    [Tooltip("背景音乐音源（循环播放，2D）")]
    [SerializeField] AudioSource bgmSource;

    [Tooltip("默认背景音乐")]
    [SerializeField] AudioClip bgm;

    [Tooltip("进入游戏后自动播放 BGM")]
    [SerializeField] bool playBgmOnStart = true;

    [Tooltip("BGM 是否循环")]
    [SerializeField] bool loopBgm = true;

    [Range(0f, 1f)]
    [SerializeField] float bgmVolume = 0.5f;

    [Tooltip("关闭后 BGM 静音且不播放")]
    [SerializeField] bool bgmEnabled = true;

    [Tooltip("切换场景时按场景名自动换 BGM")]
    [SerializeField] bool switchBgmOnSceneLoad;

    [SerializeField] SceneBgmEntry[] sceneBgms;

    [System.Serializable]
    public class SceneBgmEntry
    {
        public string sceneName;
        public AudioClip clip;
    }

    AudioClip _currentBgmClip;

    public bool BgmEnabled
    {
        get => bgmEnabled;
        set => SetBgmEnabled(value);
    }

    public float BgmVolume
    {
        get => bgmVolume;
        set
        {
            bgmVolume = Mathf.Clamp01(value);
            ApplyBgmVolume();
        }
    }

    [Header("分拣")]
    [SerializeField] AudioClip correctThrow;
    [SerializeField] AudioClip wrongThrow;

    [Header("切割")]
    [SerializeField] AudioClip cut;

    [Header("危险品")]
    [SerializeField] AudioClip explosion;

    [Tooltip("场上存在任意危险品时持续循环的报警声。")]
    [SerializeField] AudioClip dangerousGoodsAlarmLoop;

    [Range(0f, 1f)]
    [SerializeField] float dangerousAlarmVolume = 0.6f;

    AudioSource _dangerAlarmSource;
    int _dangerousGoodsCount;

    [Header("倒计时")]
    [SerializeField] AudioClip countdownTick;
    [SerializeField] AudioClip countdownWarning;

    [Header("回合")]
    [SerializeField] AudioClip roundEnd;

    [Header("积分")]
    [SerializeField] AudioClip coin;

    [Header("交互")]
    [SerializeField] AudioClip grab;
    [SerializeField] AudioClip throwItem;

    CharacterInteraction _character;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        EnsureAudioSources();

        if (playBgmOnStart && bgmEnabled)
            PlayBgm();
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        BindCharacterInteraction();
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        UnbindCharacterInteraction();
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        BindCharacterInteraction();

        if (switchBgmOnSceneLoad && bgmEnabled)
        {
            AudioClip sceneClip = ResolveSceneBgm(scene.name);
            if (sceneClip != null)
                PlayBgm(sceneClip);
        }
    }

    AudioClip ResolveSceneBgm(string sceneName)
    {
        if (sceneBgms == null || sceneBgms.Length == 0) return null;

        for (int i = 0; i < sceneBgms.Length; i++)
        {
            SceneBgmEntry entry = sceneBgms[i];
            if (entry != null && entry.clip != null && entry.sceneName == sceneName)
                return entry.clip;
        }

        return null;
    }

    void EnsureAudioSources()
    {
        if (uiSource == null)
        {
            var uiGo = new GameObject("UI Source");
            uiGo.transform.SetParent(transform, false);
            uiSource = uiGo.AddComponent<AudioSource>();
            uiSource.playOnAwake = false;
            uiSource.spatialBlend = 0f;
        }

        if (worldSource == null)
        {
            var worldGo = new GameObject("World Source");
            worldGo.transform.SetParent(transform, false);
            worldSource = worldGo.AddComponent<AudioSource>();
            worldSource.playOnAwake = false;
            worldSource.spatialBlend = 1f;
            worldSource.minDistance = 1f;
            worldSource.maxDistance = 25f;
        }

        if (bgmSource == null)
        {
            var bgmGo = new GameObject("BGM Source");
            bgmGo.transform.SetParent(transform, false);
            bgmSource = bgmGo.AddComponent<AudioSource>();
            bgmSource.playOnAwake = false;
            bgmSource.spatialBlend = 0f;
            bgmSource.loop = loopBgm;
        }

        if (_dangerAlarmSource == null)
        {
            var alarmGo = new GameObject("Danger Alarm Source");
            alarmGo.transform.SetParent(transform, false);
            _dangerAlarmSource = alarmGo.AddComponent<AudioSource>();
            _dangerAlarmSource.playOnAwake = false;
            _dangerAlarmSource.loop = true;
            _dangerAlarmSource.spatialBlend = 0f;
        }
    }

    void BindCharacterInteraction()
    {
        UnbindCharacterInteraction();
        _character = FindObjectOfType<CharacterInteraction>();
        if (_character == null) return;

        _character.Grabbed += HandleGrabbed;
        _character.Thrown += HandleThrown;
    }

    void UnbindCharacterInteraction()
    {
        if (_character == null) return;
        _character.Grabbed -= HandleGrabbed;
        _character.Thrown -= HandleThrown;
        _character = null;
    }

    void HandleGrabbed(GameObject _) => PlayGrab();

    void HandleThrown() => PlayThrow();

    public void PlayUI(AudioClip clip, float volumeScale = 1f)
    {
        if (clip == null || uiSource == null) return;
        uiSource.PlayOneShot(clip, masterVolume * uiVolume * volumeScale);
    }

    public void PlayAt(AudioClip clip, Vector3 position, float volumeScale = 1f)
    {
        if (clip == null) return;
        AudioSource.PlayClipAtPoint(clip, position, masterVolume * worldVolume * volumeScale);
    }

    public void PlayCorrectThrow() => PlayAt(correctThrow, GetListenerPosition());

    public void PlayWrongThrow() => PlayUI(wrongThrow);

    public void PlayCut(Vector3 position) => PlayAt(cut, position);

    public void PlayExplosion(Vector3 position) => PlayAt(explosion, position);

    public void RegisterDangerousGoods()
    {
        _dangerousGoodsCount++;
        if (_dangerousGoodsCount == 1)
            StartDangerousAlarm();
    }

    public void UnregisterDangerousGoods()
    {
        if (_dangerousGoodsCount <= 0) return;
        _dangerousGoodsCount--;
        if (_dangerousGoodsCount == 0)
            StopDangerousAlarm();
    }

    public void ResetDangerousGoodsAlarm()
    {
        _dangerousGoodsCount = 0;
        StopDangerousAlarm();
    }

    void StartDangerousAlarm()
    {
        if (dangerousGoodsAlarmLoop == null || _dangerAlarmSource == null) return;
        if (_dangerAlarmSource.clip != dangerousGoodsAlarmLoop)
            _dangerAlarmSource.clip = dangerousGoodsAlarmLoop;
        _dangerAlarmSource.volume = masterVolume * dangerousAlarmVolume;
        if (!_dangerAlarmSource.isPlaying)
            _dangerAlarmSource.Play();
    }

    void StopDangerousAlarm()
    {
        if (_dangerAlarmSource != null && _dangerAlarmSource.isPlaying)
            _dangerAlarmSource.Stop();
    }

    public void PlayCountdownTick() => PlayUI(countdownTick);

    public void PlayCountdownWarning() => PlayUI(countdownWarning);

    public void PlayRoundEnd() => PlayUI(roundEnd);

    public void PlayCoin() => PlayUI(coin);

    public void PlayGrab() => PlayUI(grab);

    public void PlayThrow() => PlayAt(throwItem, GetListenerPosition());

    public void PlayBgm(AudioClip clip = null)
    {
        if (!bgmEnabled || bgmSource == null) return;

        clip ??= bgm;
        if (clip == null) return;

        if (_currentBgmClip == clip && bgmSource.isPlaying)
        {
            ApplyBgmVolume();
            return;
        }

        _currentBgmClip = clip;
        bgmSource.clip = clip;
        bgmSource.loop = loopBgm;
        ApplyBgmVolume();
        bgmSource.Play();
    }

    public void StopBgm()
    {
        if (bgmSource == null) return;
        bgmSource.Stop();
        _currentBgmClip = null;
    }

    public void PauseBgm()
    {
        if (bgmSource != null && bgmSource.isPlaying)
            bgmSource.Pause();
    }

    public void ResumeBgm()
    {
        if (!bgmEnabled || bgmSource == null) return;

        if (bgmSource.clip != null)
        {
            bgmSource.UnPause();
            if (!bgmSource.isPlaying)
                bgmSource.Play();
            return;
        }

        PlayBgm();
    }

    public void SetBgmEnabled(bool enabled)
    {
        bgmEnabled = enabled;
        if (!bgmEnabled)
            PauseBgm();
        else
            ResumeBgm();
    }

    void ApplyBgmVolume()
    {
        if (bgmSource != null)
            bgmSource.volume = masterVolume * bgmVolume;
    }

    static Vector3 GetListenerPosition()
    {
        if (Camera.main != null)
            return Camera.main.transform.position;
        var listener = FindObjectOfType<AudioListener>();
        return listener != null ? listener.transform.position : Vector3.zero;
    }
}

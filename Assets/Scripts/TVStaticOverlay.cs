using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 全屏“电视雪花点”叠加效果 + 屏幕中央跳动图案。按需懒加载创建自己的 Canvas，
/// 每帧根据配置的帧率重新生成一张噪声纹理，并按 intensity 控制整体不透明度。
///
/// 玩家可以连续点击 <see cref="TVStaticOverlayParams.cancelKey"/>
/// <see cref="TVStaticOverlayParams.pressesToCancel"/> 次提前结束该干扰；
/// 每按一次中央图案会短暂闪烁为 <see cref="TVStaticOverlayParams.flashColor"/>。
///
/// 由 <see cref="LevelSessionController"/> 在干扰触发/结束时调用
/// <see cref="Show"/> / <see cref="Hide"/>。<see cref="IsActive"/> 是无副作用的
/// 静态查询，外部代码（如 CharacterInteraction）可用它判断当前是否正在干扰中。
/// </summary>
public class TVStaticOverlay : MonoBehaviour
{
    static TVStaticOverlay _instance;

    public static TVStaticOverlay Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("TVStaticOverlay");
                _instance = go.AddComponent<TVStaticOverlay>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    /// <summary>无副作用的当前可见状态查询，避免外部代码意外创建实例。</summary>
    public static bool IsActive => _instance != null && _instance.isShowing;

    Canvas overlayCanvas;
    RawImage noiseImage;
    Texture2D noiseTexture;

    Image centerImage;
    RectTransform centerRt;
    TMP_Text centerLabel;

    int textureSize;
    int targetFps;
    float frameInterval;
    float timeSinceLastFrame;

    float intensity;
    Color tint = Color.white;

    // 中央图案 / 取消
    Color centerRestColor = Color.white;
    Color centerFlashColor = Color.red;
    Color centerLabelRestColor = Color.black;
    Color centerLabelFlashColor = Color.white;
    float flashDuration;
    float flashRemaining;

    float pulseScale;
    float pulseFreqHz;

    KeyCode cancelKey = KeyCode.E;
    int pressesToCancel;
    int pressesSoFar;

    bool isShowing;

    Color32[] pixelBuffer;

    public bool IsShowing => isShowing;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
        if (noiseTexture != null)
        {
            Destroy(noiseTexture);
            noiseTexture = null;
        }
    }

    /// <summary>开启或更新雪花叠加层。</summary>
    public void Show(in TVStaticOverlayParams p)
    {
        EnsureOverlay();
        ConfigureTexture(Mathf.Clamp(p.textureSize, 32, 1024));

        targetFps = Mathf.Max(1, p.noiseFps);
        frameInterval = 1f / targetFps;
        intensity = Mathf.Clamp01(p.intensity);
        tint = p.tint;

        centerRestColor = p.centerRestColor;
        if (centerRestColor.a <= 0f)
            centerRestColor = Color.white;
        centerFlashColor = p.flashColor;
        flashDuration = Mathf.Max(0f, p.flashDuration);
        pulseScale = Mathf.Max(1f, p.centerPulseScale);
        pulseFreqHz = Mathf.Max(0f, p.centerPulseFrequencyHz);

        cancelKey = p.cancelKey;
        pressesToCancel = Mathf.Max(1, p.pressesToCancel);
        pressesSoFar = 0;
        flashRemaining = 0f;

        ApplyImageTint();
        ApplyCenterAppearance(p.centerSprite, p.centerSize);

        // 立即生成一帧噪声，避免出现一瞬间黑屏
        GenerateNoiseFrame();
        timeSinceLastFrame = 0f;

        overlayCanvas.gameObject.SetActive(true);
        isShowing = true;
    }

    public void Hide()
    {
        if (overlayCanvas != null) overlayCanvas.gameObject.SetActive(false);
        isShowing = false;
        pressesSoFar = 0;
        flashRemaining = 0f;
    }

    void Update()
    {
        if (!isShowing || noiseImage == null) return;

        // 噪声纹理刷新（用 unscaledTime，让暂停画面也能滚动）
        timeSinceLastFrame += Time.unscaledDeltaTime;
        if (timeSinceLastFrame >= frameInterval)
        {
            timeSinceLastFrame -= frameInterval;
            if (timeSinceLastFrame > frameInterval) timeSinceLastFrame = 0f;
            GenerateNoiseFrame();
        }

        UpdateCenterPattern();
        HandleCancelInput();
    }

    void HandleCancelInput()
    {
        if (cancelKey == KeyCode.None) return;
        if (!Input.GetKeyDown(cancelKey)) return;

        pressesSoFar++;
        flashRemaining = flashDuration;

        if (pressesSoFar >= pressesToCancel)
            Hide();
    }

    void UpdateCenterPattern()
    {
        if (centerRt == null || centerImage == null) return;

        // Pulse：以 sine 波在 1 与 pulseScale 之间往复
        float scale = 1f;
        if (pulseFreqHz > 0f && pulseScale > 1f)
        {
            float s = (Mathf.Sin(Time.unscaledTime * pulseFreqHz * 2f * Mathf.PI) + 1f) * 0.5f;
            scale = Mathf.Lerp(1f, pulseScale, s);
        }
        centerRt.localScale = new Vector3(scale, scale, 1f);

        // 闪烁：每次按键把 flashRemaining 拉满，期间显示 flashColor，结束后回到 restColor
        if (flashRemaining > 0f)
        {
            flashRemaining -= Time.unscaledDeltaTime;
            centerImage.color = centerFlashColor;
            if (centerLabel != null)
                centerLabel.color = centerLabelFlashColor;
        }
        else
        {
            centerImage.color = centerRestColor;
            if (centerLabel != null)
                centerLabel.color = centerLabelRestColor;
        }
    }

    void EnsureOverlay()
    {
        if (overlayCanvas != null) return;

        var canvasGo = new GameObject("TVStaticCanvas");
        canvasGo.transform.SetParent(transform, false);
        overlayCanvas = canvasGo.AddComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // 显示在 InspectionView (800) 之上，覆盖所有 HUD
        overlayCanvas.sortingOrder = 900;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        RuntimeUiUtility.ConfigureOverlayCanvasScaler(scaler);

        var imgGo = new GameObject("Noise");
        imgGo.transform.SetParent(canvasGo.transform, false);
        var rt = imgGo.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        noiseImage = imgGo.AddComponent<RawImage>();
        noiseImage.raycastTarget = false; // 不阻挡输入
        noiseImage.uvRect = new Rect(0f, 0f, 1f, 1f);

        // 屏幕中央跳动图案
        var centerGo = new GameObject("CenterPattern");
        centerGo.transform.SetParent(canvasGo.transform, false);
        centerRt = centerGo.AddComponent<RectTransform>();
        centerRt.anchorMin = new Vector2(0.5f, 0.5f);
        centerRt.anchorMax = new Vector2(0.5f, 0.5f);
        centerRt.pivot = new Vector2(0.5f, 0.5f);
        centerRt.anchoredPosition = Vector2.zero;
        centerRt.sizeDelta = new Vector2(220f, 220f);
        centerImage = centerGo.AddComponent<Image>();
        centerImage.color = Color.white;
        centerImage.raycastTarget = false;
        centerImage.preserveAspect = true;

        // 图案正中显示大写 E，随父节点一起跳动
        var labelGo = new GameObject("LabelE");
        labelGo.transform.SetParent(centerGo.transform, false);
        var labelRt = labelGo.AddComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        centerLabel = labelGo.AddComponent<TextMeshProUGUI>();
        centerLabel.text = "E";
        centerLabel.fontSize = 120f;
        centerLabel.fontStyle = FontStyles.Bold;
        centerLabel.alignment = TextAlignmentOptions.Center;
        centerLabel.color = centerLabelRestColor;
        centerLabel.raycastTarget = false;

        overlayCanvas.gameObject.SetActive(false);
    }

    void ApplyCenterAppearance(Sprite sprite, Vector2 size)
    {
        if (centerImage == null || centerRt == null) return;
        if (size.x < 8f || size.y < 8f)
            size = new Vector2(220f, 220f);

        centerImage.sprite = sprite;
        centerRt.sizeDelta = size;
        centerRt.localScale = Vector3.one;

        if (centerLabel != null)
            centerLabel.fontSize = Mathf.Max(48f, size.y * 0.55f);
    }

    void ConfigureTexture(int size)
    {
        if (noiseTexture != null && textureSize == size) return;

        if (noiseTexture != null)
        {
            Destroy(noiseTexture);
            noiseTexture = null;
        }

        textureSize = size;
        noiseTexture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
        {
            name = "TVStaticNoise",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
        };

        pixelBuffer = new Color32[size * size];

        if (noiseImage != null)
            noiseImage.texture = noiseTexture;
    }

    void GenerateNoiseFrame()
    {
        if (noiseTexture == null || pixelBuffer == null) return;

        // 灰度噪声，模拟传统模拟电视雪花点
        for (int i = 0; i < pixelBuffer.Length; i++)
        {
            byte v = (byte)Random.Range(0, 256);
            pixelBuffer[i] = new Color32(v, v, v, 255);
        }

        noiseTexture.SetPixels32(pixelBuffer);
        noiseTexture.Apply(false);
    }

    void ApplyImageTint()
    {
        if (noiseImage == null) return;
        // intensity 为主透明度；tint.a 为额外乘子。Inspector 里 tint 的 Alpha 常被误设为 0 导致雪花不可见。
        float tintAlpha = tint.a > 0f ? tint.a : 1f;
        float alpha = Mathf.Clamp01(intensity * tintAlpha);
        Color c = tint;
        if (c.r + c.g + c.b < 0.01f)
            c = Color.white;
        c.a = alpha;
        noiseImage.color = c;
    }
}

/// <summary>
/// <see cref="TVStaticOverlay.Show"/> 的参数包，由 <see cref="LevelInterferenceConfig"/>
/// 在 <see cref="LevelSessionController.StartInterference"/> 中拼装传入。
/// </summary>
public struct TVStaticOverlayParams
{
    public float intensity;
    public int noiseFps;
    public int textureSize;
    public Color tint;

    public Sprite centerSprite;
    public Vector2 centerSize;
    public float centerPulseScale;
    public float centerPulseFrequencyHz;
    public Color centerRestColor;

    public KeyCode cancelKey;
    public int pressesToCancel;
    public Color flashColor;
    public float flashDuration;
}

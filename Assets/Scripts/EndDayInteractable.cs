using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 挂在 bed 等休息点物体上：玩家靠近时显示白色描边与「按 E 提前结束这一天」提示；
/// 当场景内没有待处理的垃圾、且垃圾堆（Garbage dump / TrashHeap）不会再补充新垃圾时，
/// 按 E 可提前结束本关。
/// </summary>
[RequireComponent(typeof(Collider))]
public class EndDayInteractable : MonoBehaviour
{
    [Header("交互")]
    public KeyCode interactKey = KeyCode.E;

    [Tooltip("玩家 Transform；留空则自动查找 CharacterInteraction。")]
    public Transform playerTransform;

    [Tooltip("进入此距离后显示描边与提示。")]
    public float proximityDistance = 3f;

    [Header("描边")]
    public Color outlineColor = Color.white;

    [Tooltip("自定义描边材质，留空则自动使用 Hemiao/ItemOutline。")]
    public Material outlineMaterial;

    [Range(0f, 0.5f)]
    public float outlineWidthScale = 0.03f;

    [Header("提示 UI")]
    public string promptFormat = "按 [{0}] 提前结束这一天";

    [Tooltip("提示文字相对 bed 中心的本地偏移。")]
    public Vector3 promptLocalOffset = new Vector3(0f, 0.8f, 0f);

    public float promptFontSize = 0.22f;

    public Color promptReadyColor = Color.white;

    public Color promptWaitingColor = new Color(0.75f, 0.75f, 0.75f, 0.85f);

    [Tooltip("提示面板宽度（世界单位）。")]
    public float promptPanelWidth = 2.4f;

    [Tooltip("提示面板高度（世界单位）。")]
    public float promptPanelHeight = 0.45f;

    [Header("无法提前结束时的字幕")]
    [Tooltip("场上仍有待处理垃圾时按 E 显示的字幕。{0} = 剩余垃圾数量。")]
    public string cannotEndDayGarbageMessage = "场上仍有 {0} 件垃圾待处理，无法结束这一天。";

    [Tooltip("垃圾堆还会继续补充时按 E 显示的字幕。")]
    public string cannotEndDayMoreSpawningMessage = "垃圾堆还会继续产出垃圾，无法结束这一天。";

    [Tooltip("无法判断具体原因时的兜底字幕。")]
    public string cannotEndDayGenericMessage = "仍有工作未完成，无法结束这一天。";

    public float cannotEndDaySubtitleDuration = 2.5f;

    public Color cannotEndDaySubtitleColor = new Color(1f, 0.75f, 0.4f, 1f);

    [Header("结束回合")]
    public LevelSessionController sessionController;

    /// <summary>玩家在 bed 交互范围内时，E 键优先用于结束/提示，避免误触审视。</summary>
    public static bool ShouldConsumeInteractKey =>
        Instance != null && Instance._playerInRange;

    static EndDayInteractable Instance { get; set; }

    Collider _collider;
    bool _playerInRange;
    bool _outlineActive;
    bool _canEndDay;

    readonly System.Collections.Generic.List<GameObject> _outlineRenderers =
        new System.Collections.Generic.List<GameObject>();

    Material _outlineMaterialInstance;

    Canvas _promptCanvas;
    RectTransform _promptPanel;
    TMP_Text _promptText;
    Camera _playerCamera;

    void Awake()
    {
        Instance = this;
        _collider = GetComponent<Collider>();
        if (_collider != null)
            _collider.isTrigger = true;

        if (sessionController == null)
            sessionController = FindObjectOfType<LevelSessionController>();

        BuildPromptUi();
    }

    void Start()
    {
        ResolvePlayerReferences();
    }

    void ResolvePlayerReferences()
    {
        if (playerTransform != null)
        {
            _playerCamera = playerTransform.GetComponentInChildren<Camera>();
            return;
        }

        CharacterInteraction character = FindObjectOfType<CharacterInteraction>();
        if (character == null) return;

        playerTransform = character.transform;
        _playerCamera = character.cameraTransform != null
            ? character.cameraTransform.GetComponent<Camera>()
            : character.GetComponentInChildren<Camera>();
    }

    void Update()
    {
        if (GameplayInputGate.IsBlocked || playerTransform == null)
        {
            SetPlayerInRange(false);
            return;
        }

        float dist = Vector3.Distance(playerTransform.position, transform.position);
        SetPlayerInRange(dist <= proximityDistance);

        if (!_playerInRange) return;

        _canEndDay = CanEndDayNow();
        RefreshPromptVisuals();

        if (Input.GetKeyDown(interactKey))
        {
            if (_canEndDay)
                TryEndDayEarly();
            else
                ShowCannotEndDaySubtitle();
        }
    }

    void LateUpdate()
    {
        if (!_playerInRange || _promptCanvas == null) return;
        UpdatePromptFacing();
    }

    bool CanEndDayNow()
    {
        LevelManager lm = LevelManager.Instance;
        return lm != null && lm.IsAllItemsProcessed();
    }

    void TryEndDayEarly()
    {
        if (!_canEndDay) return;

        if (sessionController == null)
            sessionController = FindObjectOfType<LevelSessionController>();

        if (sessionController != null)
            sessionController.EndRoundEarly();
    }

    void ShowCannotEndDaySubtitle()
    {
        string message = BuildCannotEndDayMessage();
        if (string.IsNullOrEmpty(message)) return;

        CreditManager credits = CreditManager.Instance;
        if (credits != null)
            credits.ShowSubtitle(message, cannotEndDaySubtitleDuration, cannotEndDaySubtitleColor);
        else
            Debug.Log($"[EndDayInteractable] {message}", this);
    }

    string BuildCannotEndDayMessage()
    {
        LevelManager lm = LevelManager.Instance;
        if (lm == null || !lm.IsGameplayActive)
            return cannotEndDayGenericMessage;

        int remaining = lm.CountGameplayGarbageInScene();
        if (remaining > 0)
        {
            if (string.IsNullOrEmpty(cannotEndDayGarbageMessage))
                return cannotEndDayGenericMessage;
            return cannotEndDayGarbageMessage.Replace("{0}", remaining.ToString());
        }

        if (!lm.IsAllItemsProcessed())
        {
            if (!string.IsNullOrEmpty(cannotEndDayMoreSpawningMessage))
                return cannotEndDayMoreSpawningMessage;
        }

        return cannotEndDayGenericMessage;
    }

    void SetPlayerInRange(bool inRange)
    {
        if (_playerInRange == inRange) return;
        _playerInRange = inRange;

        SetOutlineActive(inRange);

        if (_promptCanvas != null)
            _promptCanvas.gameObject.SetActive(inRange);

        if (!inRange)
            _canEndDay = false;
    }

    void RefreshPromptVisuals()
    {
        if (_promptText == null) return;

        _promptText.text = BuildPromptText(interactKey);
        _promptText.color = _canEndDay ? promptReadyColor : promptWaitingColor;
    }

    string BuildPromptText(KeyCode key)
    {
        string keyLabel = GetKeyDisplayName(key);
        if (string.IsNullOrEmpty(promptFormat))
            return keyLabel;

        // 用 Replace 而非 string.Format，避免 Inspector 里写 [{E}] 等字面量大括号时抛异常。
        return promptFormat.Replace("{0}", keyLabel);
    }

    static string GetKeyDisplayName(KeyCode key)
    {
        string name = key.ToString();
        if (name.StartsWith("Alpha"))
            return name.Substring(5);
        return name;
    }

    void BuildPromptUi()
    {
        var canvasGo = new GameObject("EndDayPromptCanvas");
        canvasGo.transform.SetParent(transform, false);
        canvasGo.transform.localPosition = promptLocalOffset;
        canvasGo.transform.localRotation = Quaternion.identity;

        _promptCanvas = canvasGo.AddComponent<Canvas>();
        _promptCanvas.renderMode = RenderMode.WorldSpace;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 100f;

        var canvasRect = canvasGo.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(promptPanelWidth * 100f, promptPanelHeight * 100f);
        canvasRect.localScale = Vector3.one * 0.01f;

        var panelGo = new GameObject("PromptPanel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        _promptPanel = panelGo.AddComponent<RectTransform>();
        _promptPanel.anchorMin = Vector2.zero;
        _promptPanel.anchorMax = Vector2.one;
        _promptPanel.offsetMin = Vector2.zero;
        _promptPanel.offsetMax = Vector2.zero;

        var bg = panelGo.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.08f, 0.72f);

        var textGo = new GameObject("PromptText");
        textGo.transform.SetParent(panelGo.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 4f);
        textRect.offsetMax = new Vector2(-8f, -4f);

        _promptText = textGo.AddComponent<TextMeshProUGUI>();
        _promptText.alignment = TextAlignmentOptions.Center;
        _promptText.fontSize = promptFontSize * 100f;
        _promptText.enableWordWrapping = true;
        _promptText.richText = true;
        _promptText.color = promptWaitingColor;

        _promptCanvas.gameObject.SetActive(false);
    }

    void UpdatePromptFacing()
    {
        if (_playerCamera == null)
        {
            ResolvePlayerReferences();
            if (_playerCamera == null) return;
        }

        Transform t = _promptCanvas.transform;
        Vector3 toCam = _playerCamera.transform.position - t.position;
        toCam.y = 0f;
        if (toCam.sqrMagnitude > 0.0001f)
            t.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
    }

    void SetOutlineActive(bool active)
    {
        if (_outlineActive == active) return;
        _outlineActive = active;
        if (active) ApplyOutline();
        else ClearOutlineRenderers();
    }

    void ApplyOutline()
    {
        ClearOutlineRenderers();

        Material baseMat = GetOutlineMaterial();
        if (baseMat == null) return;

        float width = ComputeOutlineWidth();

        MeshRenderer[] meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < meshRenderers.Length; i++)
            TryAddOutlineForRenderer(meshRenderers[i], baseMat, outlineColor, width);

        SkinnedMeshRenderer[] skinned = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinned.Length; i++)
            TryAddOutlineForSkinned(skinned[i], baseMat, outlineColor, width);
    }

    float ComputeOutlineWidth()
    {
        Bounds b = _collider != null
            ? _collider.bounds
            : ItemInfoWorldUI.CalculateWorldBounds(gameObject);
        float maxExtent = Mathf.Max(b.extents.x, b.extents.y, b.extents.z);
        return outlineWidthScale * Mathf.Max(maxExtent, 0.2f);
    }

    Material GetOutlineMaterial()
    {
        if (outlineMaterial != null && outlineMaterial.shader != null && outlineMaterial.shader.isSupported)
            return outlineMaterial;

        Shader shader = Shader.Find("Hemiao/ItemOutline");
        if (shader == null || !shader.isSupported) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null || !shader.isSupported) shader = Shader.Find("Unlit/Color");
        if (shader == null) return null;

        _outlineMaterialInstance = new Material(shader);
        outlineMaterial = _outlineMaterialInstance;
        return outlineMaterial;
    }

    void TryAddOutlineForRenderer(MeshRenderer mr, Material baseMat, Color color, float width)
    {
        if (mr == null || !mr.enabled) return;
        if (mr.gameObject.name.EndsWith("_EndDayOutline")) return;

        MeshFilter mf = mr.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;

        GameObject go = new GameObject(mr.gameObject.name + "_EndDayOutline");
        go.transform.SetParent(mr.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.layer = mr.gameObject.layer;
        go.hideFlags = HideFlags.DontSave;

        MeshFilter cloneMf = go.AddComponent<MeshFilter>();
        cloneMf.sharedMesh = mf.sharedMesh;

        MeshRenderer cloneMr = go.AddComponent<MeshRenderer>();
        SetupOutlineRenderer(cloneMr, baseMat, color, width);
        _outlineRenderers.Add(go);
    }

    void TryAddOutlineForSkinned(SkinnedMeshRenderer smr, Material baseMat, Color color, float width)
    {
        if (smr == null || !smr.enabled || smr.sharedMesh == null) return;
        if (smr.gameObject.name.EndsWith("_EndDayOutline")) return;

        GameObject go = new GameObject(smr.gameObject.name + "_EndDayOutline");
        go.transform.SetParent(smr.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.layer = smr.gameObject.layer;
        go.hideFlags = HideFlags.DontSave;

        SkinnedMeshRenderer cloneSmr = go.AddComponent<SkinnedMeshRenderer>();
        cloneSmr.sharedMesh = smr.sharedMesh;
        cloneSmr.bones = smr.bones;
        cloneSmr.rootBone = smr.rootBone;
        SetupOutlineRenderer(cloneSmr, baseMat, color, width);
        _outlineRenderers.Add(go);
    }

    static void SetupOutlineRenderer(Renderer renderer, Material baseMat, Color color, float width)
    {
        renderer.material = CreateOutlineMaterialInstance(baseMat, color, width);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    static Material CreateOutlineMaterialInstance(Material baseMat, Color color, float width)
    {
        Material inst = new Material(baseMat);
        if (inst.HasProperty("_Color"))
            inst.SetColor("_Color", color);
        else if (inst.HasProperty("_BaseColor"))
            inst.SetColor("_BaseColor", color);
        if (inst.HasProperty("_OutlineWidth"))
            inst.SetFloat("_OutlineWidth", width);
        return inst;
    }

    void ClearOutlineRenderers()
    {
        for (int i = _outlineRenderers.Count - 1; i >= 0; i--)
        {
            if (_outlineRenderers[i] != null)
                Destroy(_outlineRenderers[i]);
        }
        _outlineRenderers.Clear();
    }

    void OnDisable()
    {
        SetPlayerInRange(false);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        ClearOutlineRenderers();
        if (_outlineMaterialInstance != null)
            Destroy(_outlineMaterialInstance);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, proximityDistance);
    }
}

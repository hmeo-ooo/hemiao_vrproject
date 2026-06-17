using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 手持具有 InspectableItem 组件的物品时按 E，进入审视界面：
/// - 屏幕叠加 60% 透明黑色遮罩
/// - 一个独立相机将物品渲染到 RenderTexture，并通过 RawImage 显示在遮罩之上
/// - ESC 退出，物品在玩家面前自然掉落
/// - DragDetach 模式：左键按住物品任意位置拖拽，位移达到 detachScreenRatio 阈值即触发整体分离
/// - KnifeCut 模式：拾起切割刀后长按左键划过所有切割锚点/线条后触发整体分离
/// - HammerSmash 模式：左键点击锤子拾起，对物品累计敲击 hammerHitsRequired 下后触发整体分离
///
/// 整体分离的语义：销毁原物体，按 InspectableItem.dropEntries 在玩家手部位置实例化所有 prefab。
/// 把物品临时放到"审视舱"（远离主场景的位置），避免主相机看到。
/// 使用 RenderTexture + SSO Canvas 的方案，可在 Built-in / URP 中正常工作。
/// </summary>
public class InspectionView : MonoBehaviour
{
    static InspectionView _instance;
    public static InspectionView Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("InspectionView");
                _instance = go.AddComponent<InspectionView>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    [Header("外观")]
    [Range(0f, 1f)] public float dimAlpha = 0.9f;
    public Color dimColor = Color.black;

    [Header("操作提示样式")]
    [Tooltip("审视界面顶部操作说明距屏幕上边缘的内边距（参考分辨率 1920×1080 像素）。")]
    public float instructionTopMargin = 28f;

    public float instructionPanelWidth = 960f;

    public float instructionPanelHeight = 52f;

    public int instructionFontSize = 28;

    public Color instructionTextColor = new Color(0.95f, 0.95f, 0.95f, 1f);

    public Color instructionPanelColor = new Color(0.06f, 0.06f, 0.08f, 0.72f);

    [Header("KnifeCut 默认资源")]
    [Tooltip("物品未指定 knifeSprite 时使用的全局默认切割刀图标。两者都留空则使用内置占位图。")]
    public Sprite defaultKnifeSprite;

    [Header("展示")]
    [Tooltip("审视相机距离物品的距离（米）。建议与 CharacterInteraction.holdDistance 接近，避免物体被放大。")]
    public float displayDistance = 1.2f;

    [Tooltip("按包围盒自动推远相机时的额外留白系数。")]
    public float displayDistancePadding = 1.1f;

    [Tooltip("审视相机视野（度）。为 0 则沿用主相机 FOV。")]
    public float fieldOfView = 0f;

    [Tooltip("审视舱在世界中的位置，需远离主场景，避免被主相机看到。")]
    public Vector3 inspectionRoomCenter = new Vector3(10000f, 10000f, 10000f);

    Camera mainCamera;
    Camera inspectionCamera;
    CharacterInteraction characterInteraction;
    CharacterMove characterMove;

    Canvas overlayCanvas;
    RectTransform canvasRt;
    Image dimImage;
    RawImage itemImage;
    Image knifeImage;
    RectTransform knifeRt;
    Image hammerImage;
    RectTransform hammerRt;
    RectTransform instructionRt;
    TMP_Text instructionText;
    RenderTexture renderTexture;
    int rtWidth, rtHeight;

    GameObject inspectedItem;
    InspectableItem inspectable;
    Transform originalParent;
    Quaternion originalRotation;
    Vector3 originalScale;
    int originalSiblingIndex;
    Vector3 inspectionDisplayPosition;

    struct RbState
    {
        public Rigidbody rb;
        public bool kinematic;
        public bool gravity;
        public bool detect;
    }
    readonly List<RbState> rbStates = new List<RbState>();

    bool isInspecting;
    bool gateWasBlocked;
    bool mouseLookPushed;
    CursorLockMode prevLockMode;
    bool prevCursorVisible;

    // DragDetach 模式状态
    Vector2 dragMouseStart;
    bool isDragging;

    // KnifeCut 模式状态
    Vector2 knifeIdleCanvasPos;
    bool isHoldingKnife;
    Vector2 knifeReturnVelocity;
    bool[] cutAnchorDone;
    bool[] cutLineDone;
    InspectionCutMarkerRenderer cutMarkerRenderer;
    Vector3 knifeSwipePrevWorld;
    bool knifeSwipeHasPrev;

    // HammerSmash 模式状态
    Vector2 hammerIdleCanvasPos;
    bool isHoldingHammer;
    Vector2 hammerReturnVelocity;
    int hammerHitCount;
    float hammerShakeTimeLeft;
    const float HammerShakeDuration = 0.12f;
    const float HammerShakeMagnitude = 0.03f;

    public bool IsInspecting => isInspecting;

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
        ReleaseRenderTexture();
    }

    public void BeginInspection(GameObject item, CharacterInteraction interaction)
    {
        if (isInspecting || item == null) return;
        var insp = item.GetComponent<InspectableItem>();
        if (insp == null) return;

        // 没有分离产物或 KnifeCut 未配置切割锚点时，进入审视没有意义。
        if (!insp.CanEnterInspection) return;

        characterInteraction = interaction;
        if (characterInteraction != null)
        {
            characterMove = characterInteraction.GetComponent<CharacterMove>();
            if (characterInteraction.cameraTransform != null)
                mainCamera = characterInteraction.cameraTransform.GetComponent<Camera>();
        }
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogWarning("[InspectionView] 找不到主相机，无法进入审视。");
            return;
        }

        if (characterInteraction != null)
            characterInteraction.ForceReleaseIfHolding(item);

        inspectedItem = item;
        inspectable = insp;
        originalParent = item.transform.parent;
        originalRotation = item.transform.rotation;
        originalScale = item.transform.localScale;
        originalSiblingIndex = item.transform.GetSiblingIndex();

        rbStates.Clear();
        CaptureRigidbodiesUnder(item);
        FreezeAllCapturedRigidbodies();

        item.transform.SetParent(null, true);
        item.transform.position = inspectionRoomCenter;
        inspectionDisplayPosition = inspectionRoomCenter;

        EnsureInspectionCamera();
        EnsureOverlay();
        EnsureRenderTexture();

        // 使用 InspectableItem 上配置的固定欧拉角，使物品姿态与玩家持物时的角度无关。
        item.transform.rotation = Quaternion.Euler(insp.inspectionDisplayEulers);

        FrameInspectionCamera(item);

        inspectionCamera.targetTexture = renderTexture;
        inspectionCamera.gameObject.SetActive(true);

        overlayCanvas.gameObject.SetActive(true);

        ConfigureKnifeForCurrentMode();
        ConfigureHammerForCurrentMode();
        ConfigureInstructionHint();

        gateWasBlocked = GameplayInputGate.IsBlocked;
        GameplayInputGate.SetBlocked(true);
        if (characterMove != null)
        {
            characterMove.PushMouseLookSuspend();
            mouseLookPushed = true;
        }
        prevLockMode = Cursor.lockState;
        prevCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        isInspecting = true;
    }

    void Update()
    {
        if (!isInspecting) return;

        MaintainRenderTextureSize();

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CancelInspection();
            return;
        }

        if (inspectable != null)
        {
            switch (inspectable.interactionMode)
            {
                case InspectableItem.InspectionInteraction.KnifeCut:
                    HandleKnifeCut();
                    return;
                case InspectableItem.InspectionInteraction.HammerSmash:
                    HandleHammerSmash();
                    return;
            }
        }

        HandleDrag();
    }

    // ------------------------------------------------------------------
    // DragDetach 模式
    // ------------------------------------------------------------------

    void HandleDrag()
    {
        if (inspectable == null || inspectionCamera == null || inspectedItem == null) return;

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = InspectionScreenRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore))
            {
                Transform t = hit.collider != null ? hit.collider.transform : null;
                if (t != null && (t == inspectedItem.transform || t.IsChildOf(inspectedItem.transform)))
                {
                    dragMouseStart = Input.mousePosition;
                    isDragging = true;
                    return;
                }
            }
        }

        if (isDragging && Input.GetMouseButton(0))
        {
            Vector2 mouseDelta = (Vector2)Input.mousePosition - dragMouseStart;

            // 视觉反馈：让物品轻微跟随鼠标方向偏移，让玩家感知到"在用力撕扯"。
            float sens = inspectable.dragWorldSensitivity;
            Vector3 worldOffset =
                inspectionCamera.transform.right * (mouseDelta.x * sens) +
                inspectionCamera.transform.up * (mouseDelta.y * sens);
            inspectedItem.transform.position = inspectionDisplayPosition + worldOffset;

            float ratio = mouseDelta.magnitude / Mathf.Max(1f, Screen.height);
            if (ratio >= inspectable.detachScreenRatio)
            {
                DetachAndEnd();
                return;
            }
        }

        if (Input.GetMouseButtonUp(0) && isDragging)
        {
            if (inspectedItem != null)
                inspectedItem.transform.position = inspectionDisplayPosition;
            isDragging = false;
        }
    }

    /// <summary>
    /// 鼠标屏幕坐标 → 通过审视相机的视图换算射线（RenderTexture 内的对应像素）。
    /// </summary>
    Ray InspectionScreenRay(Vector3 mouseScreenPos)
    {
        // RawImage 撑满全屏，因此屏幕坐标与 RenderTexture 坐标一一对应；按比例换算到 RT 像素。
        float u = mouseScreenPos.x / Mathf.Max(1f, Screen.width);
        float v = mouseScreenPos.y / Mathf.Max(1f, Screen.height);
        Vector3 rtPoint = new Vector3(u * rtWidth, v * rtHeight, 0f);
        return inspectionCamera.ScreenPointToRay(rtPoint);
    }

    // ------------------------------------------------------------------
    // KnifeCut 模式
    // ------------------------------------------------------------------

    void ConfigureKnifeForCurrentMode()
    {
        EnsureToolOverlayWidgets();
        if (knifeRt == null || knifeImage == null) return;

        bool useKnife = inspectable != null
            && inspectable.interactionMode == InspectableItem.InspectionInteraction.KnifeCut;

        knifeRt.gameObject.SetActive(useKnife);
        if (!useKnife) return;

        Sprite sprite = InspectionUiSprites.ResolveKnifeSprite(inspectable.knifeSprite, defaultKnifeSprite);
        knifeImage.sprite = sprite;
        knifeImage.color = Color.white;
        knifeImage.preserveAspect = true;
        knifeImage.raycastTarget = true;

        knifeRt.sizeDelta = inspectable.knifeUISize;
        knifeRt.pivot = ClampVec01(inspectable.knifeTipPivot);
        knifeRt.localRotation = Quaternion.Euler(0f, 0f, inspectable.knifeUIRotation);
        knifeRt.localScale = Vector3.one;

        knifeIdleCanvasPos = ScreenAnchorToCanvasLocal(inspectable.knifeIdleAnchor);
        knifeRt.anchoredPosition = knifeIdleCanvasPos;
        knifeReturnVelocity = Vector2.zero;
        isHoldingKnife = false;
        knifeSwipeHasPrev = false;

        knifeRt.SetAsLastSibling();

        InitKnifeCutProgress();
        BuildKnifeCutMarkers();
    }

    void InitKnifeCutProgress()
    {
        int anchorCount = inspectable != null && inspectable.cutAnchors != null
            ? inspectable.cutAnchors.Count : 0;
        int lineCount = inspectable != null && inspectable.cutLines != null
            ? inspectable.cutLines.Count : 0;

        cutAnchorDone = anchorCount > 0 ? new bool[anchorCount] : null;
        cutLineDone = lineCount > 0 ? new bool[lineCount] : null;
    }

    void BuildKnifeCutMarkers()
    {
        if (cutMarkerRenderer != null)
        {
            Destroy(cutMarkerRenderer.gameObject);
            cutMarkerRenderer = null;
        }

        if (inspectedItem == null || inspectable == null) return;
        cutMarkerRenderer = InspectionCutMarkerRenderer.Build(
            inspectedItem.transform, inspectable, cutAnchorDone, cutLineDone);
    }

    /// <summary>
    /// KnifeCut 交互：
    /// 1. 左键点击切割刀 → 拾起（刀尖跟随鼠标）。
    /// 2. 拾起后长按左键在物品上划过，刀尖轨迹经过锚点/线条即切开（红色标记消失）。
    /// 3. 全部切开后触发整体分离。
    /// </summary>
    void HandleKnifeCut()
    {
        if (knifeRt == null || inspectable == null || inspectedItem == null) return;

        Vector2 mousePos = Input.mousePosition;

        if (Input.GetMouseButtonDown(0) && !isHoldingKnife)
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(knifeRt, mousePos, null))
            {
                isHoldingKnife = true;
                knifeReturnVelocity = Vector2.zero;
                knifeSwipeHasPrev = false;
            }
        }

        if (isHoldingKnife)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRt, mousePos, null, out Vector2 localPos))
            {
                knifeRt.anchoredPosition = localPos;
            }

            if (Input.GetMouseButton(0))
                ProcessKnifeSwipe(mousePos);
            else
                knifeSwipeHasPrev = false;
        }
        else
        {
            float smooth = Mathf.Max(0.0001f, inspectable.knifeReturnSmoothTime);
            Vector2 cur = knifeRt.anchoredPosition;
            cur.x = Mathf.SmoothDamp(cur.x, knifeIdleCanvasPos.x, ref knifeReturnVelocity.x, smooth);
            cur.y = Mathf.SmoothDamp(cur.y, knifeIdleCanvasPos.y, ref knifeReturnVelocity.y, smooth);
            knifeRt.anchoredPosition = cur;
        }
    }

    void ProcessKnifeSwipe(Vector2 mouseScreenPos)
    {
        if (!TryGetKnifeTipOnItem(mouseScreenPos, out Vector3 currWorld))
            return;

        Vector3 prevWorld = knifeSwipeHasPrev ? knifeSwipePrevWorld : currWorld;
        bool anyCut = ApplyKnifeCutsAlongSwipe(prevWorld, currWorld);

        knifeSwipePrevWorld = currWorld;
        knifeSwipeHasPrev = true;

        if (anyCut && InspectableCutUtility.CountRemaining(
                inspectable.cutAnchors, cutAnchorDone,
                inspectable.cutLines, cutLineDone) <= 0)
        {
            DetachAndEnd();
        }
    }

    bool ApplyKnifeCutsAlongSwipe(Vector3 prevWorld, Vector3 currWorld)
    {
        if (inspectable == null || inspectedItem == null) return false;

        Transform root = inspectedItem.transform;
        bool any = false;

        if (inspectable.cutAnchors != null && cutAnchorDone != null)
        {
            for (int i = 0; i < inspectable.cutAnchors.Count; i++)
            {
                if (i >= cutAnchorDone.Length || cutAnchorDone[i]) continue;
                InspectableCutAnchor anchor = inspectable.cutAnchors[i];
                if (anchor == null) continue;

                if (!InspectableCutUtility.IsAnchorCutBySwipe(root, anchor, prevWorld, currWorld))
                    continue;

                cutAnchorDone[i] = true;
                cutMarkerRenderer?.SetAnchorDone(i, true);
                any = true;
            }
        }

        if (inspectable.cutLines != null && cutLineDone != null)
        {
            for (int i = 0; i < inspectable.cutLines.Count; i++)
            {
                if (i >= cutLineDone.Length || cutLineDone[i]) continue;
                InspectableCutLine line = inspectable.cutLines[i];
                if (line == null) continue;

                if (!InspectableCutUtility.IsLineCutBySwipe(root, line, prevWorld, currWorld))
                    continue;

                cutLineDone[i] = true;
                cutMarkerRenderer?.SetLineDone(i, true);
                any = true;
            }
        }

        return any;
    }

    /// <summary>
    /// 刀尖（knifeTipPivot = 鼠标位置）沿审视相机射线落在物品表面的世界坐标。
    /// </summary>
    bool TryGetKnifeTipOnItem(Vector2 mouseScreenPos, out Vector3 worldPoint)
    {
        worldPoint = Vector3.zero;
        if (inspectedItem == null || inspectionCamera == null) return false;

        Ray ray = InspectionScreenRay(mouseScreenPos);
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;

        for (int i = 0; i < hits.Length; i++)
        {
            Transform t = hits[i].collider != null ? hits[i].collider.transform : null;
            if (t == null) continue;
            if (t != inspectedItem.transform && !t.IsChildOf(inspectedItem.transform)) continue;
            if (hits[i].distance < best)
            {
                best = hits[i].distance;
                worldPoint = hits[i].point;
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// 在屏幕坐标 <paramref name="mouseScreenPos"/> 处通过审视相机做射线投射，
    /// 判断是否命中当前被审视的物品（或其后代 Collider）。Knife / Hammer 共用。
    /// </summary>
    bool TryToolHitInspectedItem(Vector2 mouseScreenPos)
    {
        if (inspectedItem == null || inspectionCamera == null) return false;

        Ray ray = InspectionScreenRay(mouseScreenPos);
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            Transform t = hits[i].collider != null ? hits[i].collider.transform : null;
            if (t == null) continue;
            if (t == inspectedItem.transform || t.IsChildOf(inspectedItem.transform))
                return true;
        }
        return false;
    }

    // ------------------------------------------------------------------
    // HammerSmash 模式
    // ------------------------------------------------------------------

    void ConfigureInstructionHint()
    {
        EnsureInstructionHintWidget();
        if (instructionRt == null || instructionText == null) return;

        string text = inspectable != null
            ? inspectable.GetInspectionInstructionText()
            : string.Empty;

        instructionText.text = text;
        bool show = !string.IsNullOrWhiteSpace(text);
        instructionRt.gameObject.SetActive(show);
        if (show)
            instructionRt.SetAsLastSibling();
    }

    void ConfigureHammerForCurrentMode()
    {
        EnsureToolOverlayWidgets();
        if (hammerRt == null || hammerImage == null) return;

        bool useHammer = inspectable != null
            && inspectable.interactionMode == InspectableItem.InspectionInteraction.HammerSmash;

        hammerRt.gameObject.SetActive(useHammer);
        if (!useHammer) return;

        hammerImage.sprite = InspectionUiSprites.ResolveHammerSprite(inspectable.hammerSprite, null);
        hammerImage.color = inspectable.hammerSprite != null
            ? Color.white
            : new Color(0.7f, 0.5f, 0.3f, 1f);
        hammerImage.preserveAspect = true;
        hammerImage.raycastTarget = true;

        hammerRt.sizeDelta = inspectable.hammerUISize;
        hammerRt.pivot = ClampVec01(inspectable.hammerHeadPivot);
        hammerRt.localRotation = Quaternion.Euler(0f, 0f, inspectable.hammerUIRotation);
        hammerRt.localScale = Vector3.one;

        hammerIdleCanvasPos = ScreenAnchorToCanvasLocal(inspectable.hammerIdleAnchor);
        hammerRt.anchoredPosition = hammerIdleCanvasPos;
        hammerReturnVelocity = Vector2.zero;
        isHoldingHammer = false;
        hammerHitCount = 0;
        hammerShakeTimeLeft = 0f;

        hammerRt.SetAsLastSibling();
    }

    /// <summary>
    /// HammerSmash 三段式（或 N 段式）交互：
    /// 1. 未拾起时，左键点击锤子图标 → 拾起锤子（之后跟随鼠标）。
    /// 2. 已拾起时，每次左键点击命中物品都算一下敲击，叠加到 hammerHitCount。
    /// 3. 达到 inspectable.hammerHitsRequired 即触发整体分离。
    /// 未命中物品的点击不计数，玩家可继续移动鼠标重试。
    /// </summary>
    void HandleHammerSmash()
    {
        if (hammerRt == null || inspectable == null) return;

        Vector2 mousePos = Input.mousePosition;

        if (Input.GetMouseButtonDown(0))
        {
            if (!isHoldingHammer)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(hammerRt, mousePos, null))
                {
                    isHoldingHammer = true;
                    hammerReturnVelocity = Vector2.zero;
                }
            }
            else if (TryToolHitInspectedItem(mousePos))
            {
                hammerHitCount++;
                int required = Mathf.Max(1, inspectable.hammerHitsRequired);
                if (hammerHitCount >= required)
                {
                    DetachAndEnd();
                    return;
                }
                BeginHammerHitShake();
            }
        }

        if (isHoldingHammer)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRt, mousePos, null, out Vector2 localPos))
            {
                hammerRt.anchoredPosition = localPos;
            }
        }
        else
        {
            float smooth = Mathf.Max(0.0001f, inspectable.hammerReturnSmoothTime);
            Vector2 cur = hammerRt.anchoredPosition;
            cur.x = Mathf.SmoothDamp(cur.x, hammerIdleCanvasPos.x, ref hammerReturnVelocity.x, smooth);
            cur.y = Mathf.SmoothDamp(cur.y, hammerIdleCanvasPos.y, ref hammerReturnVelocity.y, smooth);
            hammerRt.anchoredPosition = cur;
        }

        UpdateHammerHitShake();
    }

    void BeginHammerHitShake()
    {
        hammerShakeTimeLeft = HammerShakeDuration;
    }

    /// <summary>每帧把"被敲击的微抖动"叠加到 inspectedItem 上，让玩家感受到敲击反馈。</summary>
    void UpdateHammerHitShake()
    {
        if (inspectedItem == null) return;

        if (hammerShakeTimeLeft <= 0f)
        {
            inspectedItem.transform.position = inspectionDisplayPosition;
            return;
        }

        hammerShakeTimeLeft -= Time.deltaTime;
        if (hammerShakeTimeLeft <= 0f)
        {
            hammerShakeTimeLeft = 0f;
            inspectedItem.transform.position = inspectionDisplayPosition;
            return;
        }

        float t = hammerShakeTimeLeft / HammerShakeDuration;
        float mag = HammerShakeMagnitude * t;
        Vector3 off = new Vector3(
            Random.Range(-mag, mag),
            Random.Range(-mag, mag),
            Random.Range(-mag, mag));
        inspectedItem.transform.position = inspectionDisplayPosition + off;
    }

    // ------------------------------------------------------------------
    // 分离 / 取消 / 收尾
    // ------------------------------------------------------------------

    /// <summary>
    /// 触发整体分离：在玩家手部位置实例化所有 dropEntries，销毁原物体，退出审视。
    /// </summary>
    void DetachAndEnd()
    {
        if (!isInspecting)
        {
            TeardownInspectionUI();
            return;
        }

        Vector3 dropAnchor = mainCamera != null
            ? mainCamera.transform.position + mainCamera.transform.forward * GetDropDistance()
            : Vector3.zero;

        if (inspectable != null)
            inspectable.SpawnDropEntries(dropAnchor);

        // 原物体在分离后不再保留：销毁前清空 rbStates，避免后续 TeardownInspectionUI
        // 试图去恢复已被销毁的 Rigidbody。
        rbStates.Clear();
        if (inspectedItem != null)
        {
            Destroy(inspectedItem);
            inspectedItem = null;
        }

        TeardownInspectionUI();
    }

    /// <summary>
    /// 玩家按 Esc 主动取消：物品复位到原父级，掉落在玩家面前，物理状态还原。
    /// </summary>
    void CancelInspection()
    {
        if (!isInspecting) return;

        Vector3 dropAnchor = mainCamera != null
            ? mainCamera.transform.position + mainCamera.transform.forward * GetDropDistance()
            : Vector3.zero;

        if (inspectedItem != null)
        {
            inspectedItem.transform.SetParent(originalParent, false);
            inspectedItem.transform.position = dropAnchor;
            inspectedItem.transform.rotation = originalRotation;
            inspectedItem.transform.localScale = originalScale;
            if (originalParent != null)
                inspectedItem.transform.SetSiblingIndex(originalSiblingIndex);
        }

        RestoreCapturedRigidbodies();

        TeardownInspectionUI();
    }

    /// <summary>
    /// 关 UI、还原输入门、清空状态。DetachAndEnd / CancelInspection 共享。
    /// </summary>
    void TeardownInspectionUI()
    {
        if (overlayCanvas != null) overlayCanvas.gameObject.SetActive(false);
        if (knifeRt != null) knifeRt.gameObject.SetActive(false);
        if (hammerRt != null) hammerRt.gameObject.SetActive(false);
        if (instructionRt != null) instructionRt.gameObject.SetActive(false);
        if (inspectionCamera != null)
        {
            inspectionCamera.targetTexture = null;
            inspectionCamera.gameObject.SetActive(false);
        }

        GameplayInputGate.SetBlocked(gateWasBlocked);
        if (characterMove != null && mouseLookPushed)
        {
            characterMove.PopMouseLookSuspend();
            mouseLookPushed = false;
        }
        Cursor.lockState = prevLockMode;
        Cursor.visible = prevCursorVisible;

        CharacterInteraction interaction = characterInteraction;
        isInspecting = false;
        inspectedItem = null;
        inspectable = null;
        characterInteraction = null;
        isDragging = false;
        isHoldingKnife = false;
        knifeSwipeHasPrev = false;
        cutAnchorDone = null;
        cutLineDone = null;
        if (cutMarkerRenderer != null)
        {
            Destroy(cutMarkerRenderer.gameObject);
            cutMarkerRenderer = null;
        }
        isHoldingHammer = false;
        hammerHitCount = 0;
        hammerShakeTimeLeft = 0f;

        interaction?.ForceRefreshInteractionVisuals();
    }

    /// <summary>
    /// 给外部强制结束审视的钩子（如玩家死亡 / 切场景等）。仅做取消语义。
    /// </summary>
    public void EndInspection()
    {
        if (isInspecting) CancelInspection();
    }

    float GetDropDistance()
    {
        if (characterInteraction != null)
            return Mathf.Max(0.4f, characterInteraction.holdDistance);
        return Mathf.Max(0.4f, displayDistance);
    }

    float GetInspectionFieldOfView()
    {
        if (fieldOfView > 0f) return fieldOfView;
        if (mainCamera != null) return mainCamera.fieldOfView;
        return 60f;
    }

    float GetInspectionCameraDistance()
    {
        if (inspectable != null)
            return inspectable.ResolveInspectionDisplayDistance(GetDropDistance());
        return GetDropDistance();
    }

    void FrameInspectionCamera(GameObject item)
    {
        if (inspectionCamera == null || item == null) return;

        Bounds bounds = ItemInfoWorldUI.CalculateWorldBounds(item);
        float fov = GetInspectionFieldOfView();
        inspectionCamera.fieldOfView = fov;

        float distance = GetInspectionCameraDistance();
        float radius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
        if (radius > 0.001f)
        {
            float fitDistance = radius / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            distance = Mathf.Max(distance, fitDistance * displayDistancePadding);
        }

        Vector3 lookTarget = bounds.center;
        inspectionCamera.transform.position = lookTarget + Vector3.back * distance;
        inspectionCamera.transform.rotation = Quaternion.identity;
    }

    // ------------------------------------------------------------------
    // Rigidbody 暂存 / 冻结 / 还原
    // ------------------------------------------------------------------

    void CaptureRigidbodiesUnder(GameObject root)
    {
        if (root == null) return;
        Rigidbody[] rbs = root.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rbs.Length; i++)
            CaptureRigidbody(rbs[i]);
    }

    void CaptureRigidbody(Rigidbody rb)
    {
        if (rb == null) return;
        rbStates.Add(new RbState
        {
            rb = rb,
            kinematic = rb.isKinematic,
            gravity = rb.useGravity,
            detect = rb.detectCollisions
        });
    }

    void FreezeAllCapturedRigidbodies()
    {
        for (int i = 0; i < rbStates.Count; i++)
        {
            var s = rbStates[i];
            if (s.rb == null) continue;
            if (!s.rb.isKinematic)
            {
                s.rb.velocity = Vector3.zero;
                s.rb.angularVelocity = Vector3.zero;
            }
            s.rb.isKinematic = true;
            s.rb.useGravity = false;
            // 注意：不要把 detectCollisions 设为 false，否则 Unity 会把整个刚体
            // 排除在所有物理查询之外（包括 Physics.Raycast），导致审视界面
            // 左键拖拽时打不到物品。
            // kinematic + useGravity=false 已足以避免它在审视舱里被物理推动。
        }
    }

    void RestoreCapturedRigidbodies()
    {
        for (int i = 0; i < rbStates.Count; i++)
        {
            var s = rbStates[i];
            if (s.rb == null) continue;

            s.rb.isKinematic = s.kinematic;
            s.rb.useGravity = s.gravity;
            s.rb.detectCollisions = s.detect;

            if (!s.rb.isKinematic)
            {
                s.rb.velocity = Vector3.zero;
                s.rb.angularVelocity = Vector3.zero;
            }
        }
        rbStates.Clear();
    }

    // ------------------------------------------------------------------
    // 工具
    // ------------------------------------------------------------------

    Vector2 ScreenAnchorToCanvasLocal(Vector2 anchor01)
    {
        if (canvasRt == null) return Vector2.zero;
        Rect r = canvasRt.rect;
        float x = Mathf.Lerp(r.xMin, r.xMax, Mathf.Clamp01(anchor01.x));
        float y = Mathf.Lerp(r.yMin, r.yMax, Mathf.Clamp01(anchor01.y));
        return new Vector2(x, y);
    }

    static Vector2 ClampVec01(Vector2 v) =>
        new Vector2(Mathf.Clamp01(v.x), Mathf.Clamp01(v.y));

    void EnsureInspectionCamera()
    {
        if (inspectionCamera != null) return;

        var camGo = new GameObject("InspectionCamera");
        camGo.transform.SetParent(transform, false);
        inspectionCamera = camGo.AddComponent<Camera>();
        inspectionCamera.clearFlags = CameraClearFlags.SolidColor;
        inspectionCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        inspectionCamera.cullingMask = ~0;
        inspectionCamera.nearClipPlane = 0.05f;
        inspectionCamera.farClipPlane = 100f;
        inspectionCamera.useOcclusionCulling = false;
        inspectionCamera.allowMSAA = true;
        inspectionCamera.allowHDR = false;
        var listener = camGo.GetComponent<AudioListener>();
        if (listener != null) Destroy(listener);
        camGo.SetActive(false);
    }

    void EnsureOverlay()
    {
        if (overlayCanvas != null)
        {
            EnsureToolOverlayWidgets();
            return;
        }

        var canvasGo = new GameObject("InspectionOverlayCanvas");
        canvasGo.transform.SetParent(transform, false);
        overlayCanvas = canvasGo.AddComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.sortingOrder = 800;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();
        canvasRt = canvasGo.GetComponent<RectTransform>();

        var dimGo = new GameObject("Dim");
        dimGo.transform.SetParent(canvasGo.transform, false);
        var dimRt = dimGo.AddComponent<RectTransform>();
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = Vector2.zero;
        dimRt.offsetMax = Vector2.zero;
        dimImage = dimGo.AddComponent<Image>();
        Color c = dimColor;
        c.a = dimAlpha;
        dimImage.color = c;
        dimImage.raycastTarget = true;

        var imgGo = new GameObject("ItemRender");
        imgGo.transform.SetParent(canvasGo.transform, false);
        var imgRt = imgGo.AddComponent<RectTransform>();
        imgRt.anchorMin = Vector2.zero;
        imgRt.anchorMax = Vector2.one;
        imgRt.offsetMin = Vector2.zero;
        imgRt.offsetMax = Vector2.zero;
        itemImage = imgGo.AddComponent<RawImage>();
        itemImage.color = Color.white;
        itemImage.raycastTarget = false;

        var knifeGo = new GameObject("InspectionKnife");
        knifeGo.transform.SetParent(canvasGo.transform, false);
        knifeRt = knifeGo.AddComponent<RectTransform>();
        knifeRt.anchorMin = new Vector2(0.5f, 0.5f);
        knifeRt.anchorMax = new Vector2(0.5f, 0.5f);
        knifeRt.sizeDelta = new Vector2(220f, 220f);
        knifeRt.pivot = new Vector2(0.15f, 0.85f);
        knifeImage = knifeGo.AddComponent<Image>();
        knifeImage.color = Color.white;
        knifeImage.raycastTarget = false;
        knifeImage.preserveAspect = true;
        knifeGo.SetActive(false);

        var hammerGo = new GameObject("InspectionHammer");
        hammerGo.transform.SetParent(canvasGo.transform, false);
        hammerRt = hammerGo.AddComponent<RectTransform>();
        hammerRt.anchorMin = new Vector2(0.5f, 0.5f);
        hammerRt.anchorMax = new Vector2(0.5f, 0.5f);
        hammerRt.sizeDelta = new Vector2(240f, 240f);
        hammerRt.pivot = new Vector2(0.2f, 0.85f);
        hammerImage = hammerGo.AddComponent<Image>();
        hammerImage.color = Color.white;
        hammerImage.raycastTarget = false;
        hammerImage.preserveAspect = true;
        hammerGo.SetActive(false);

        BuildInstructionHintWidget(overlayCanvas.transform);

        overlayCanvas.gameObject.SetActive(false);
    }

    /// <summary>
    /// 旧版审视 UI 可能缺少刀/锤/提示控件；在已有 Canvas 上补建缺失节点。
    /// </summary>
    void EnsureToolOverlayWidgets()
    {
        if (overlayCanvas == null) return;

        EnsureInstructionHintWidget();

        if (knifeRt == null || knifeImage == null)
        {
            Transform existing = overlayCanvas.transform.Find("InspectionKnife");
            if (existing != null)
            {
                knifeRt = existing as RectTransform;
                knifeImage = existing.GetComponent<Image>();
            }
        }

        if (knifeRt == null || knifeImage == null)
        {
            var knifeGo = new GameObject("InspectionKnife");
            knifeGo.transform.SetParent(overlayCanvas.transform, false);
            knifeRt = knifeGo.AddComponent<RectTransform>();
            knifeRt.anchorMin = new Vector2(0.5f, 0.5f);
            knifeRt.anchorMax = new Vector2(0.5f, 0.5f);
            knifeRt.sizeDelta = new Vector2(220f, 220f);
            knifeRt.pivot = new Vector2(0.15f, 0.85f);
            knifeImage = knifeGo.AddComponent<Image>();
            knifeImage.color = Color.white;
            knifeImage.preserveAspect = true;
            knifeGo.SetActive(false);
        }

        if (hammerRt == null || hammerImage == null)
        {
            Transform existing = overlayCanvas.transform.Find("InspectionHammer");
            if (existing != null)
            {
                hammerRt = existing as RectTransform;
                hammerImage = existing.GetComponent<Image>();
            }
        }

        if (hammerRt == null || hammerImage == null)
        {
            var hammerGo = new GameObject("InspectionHammer");
            hammerGo.transform.SetParent(overlayCanvas.transform, false);
            hammerRt = hammerGo.AddComponent<RectTransform>();
            hammerRt.anchorMin = new Vector2(0.5f, 0.5f);
            hammerRt.anchorMax = new Vector2(0.5f, 0.5f);
            hammerRt.sizeDelta = new Vector2(240f, 240f);
            hammerRt.pivot = new Vector2(0.2f, 0.85f);
            hammerImage = hammerGo.AddComponent<Image>();
            hammerImage.color = Color.white;
            hammerImage.preserveAspect = true;
            hammerGo.SetActive(false);
        }
    }

    void EnsureInstructionHintWidget()
    {
        if (overlayCanvas == null) return;

        if (instructionRt == null || instructionText == null)
        {
            Transform existing = overlayCanvas.transform.Find("InspectionInstruction");
            if (existing != null)
            {
                instructionRt = existing as RectTransform;
                instructionText = existing.GetComponentInChildren<TMP_Text>(true);
            }
        }

        if (instructionRt == null || instructionText == null)
            BuildInstructionHintWidget(overlayCanvas.transform);
    }

    void BuildInstructionHintWidget(Transform canvasRoot)
    {
        var rootGo = new GameObject("InspectionInstruction");
        rootGo.transform.SetParent(canvasRoot, false);
        instructionRt = rootGo.AddComponent<RectTransform>();
        instructionRt.anchorMin = new Vector2(0.5f, 1f);
        instructionRt.anchorMax = new Vector2(0.5f, 1f);
        instructionRt.pivot = new Vector2(0.5f, 1f);
        instructionRt.sizeDelta = new Vector2(instructionPanelWidth, instructionPanelHeight);
        instructionRt.anchoredPosition = new Vector2(0f, -instructionTopMargin);

        var bg = rootGo.AddComponent<Image>();
        bg.color = instructionPanelColor;
        bg.raycastTarget = false;

        var textGo = new GameObject("InstructionText");
        textGo.transform.SetParent(rootGo.transform, false);
        var textRt = textGo.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(16f, 6f);
        textRt.offsetMax = new Vector2(-16f, -6f);

        instructionText = textGo.AddComponent<TextMeshProUGUI>();
        instructionText.alignment = TextAlignmentOptions.Center;
        instructionText.fontSize = instructionFontSize;
        instructionText.color = instructionTextColor;
        instructionText.enableWordWrapping = true;
        instructionText.richText = true;
        instructionText.raycastTarget = false;

        rootGo.SetActive(false);
    }

    void EnsureRenderTexture()
    {
        int w = Mathf.Max(64, Screen.width);
        int h = Mathf.Max(64, Screen.height);
        if (renderTexture != null && rtWidth == w && rtHeight == h) return;

        ReleaseRenderTexture();
        renderTexture = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
        {
            name = "InspectionRT",
            antiAliasing = 1,
            useMipMap = false,
            autoGenerateMips = false,
        };
        renderTexture.Create();
        rtWidth = w;
        rtHeight = h;
        if (itemImage != null) itemImage.texture = renderTexture;
        if (inspectionCamera != null) inspectionCamera.targetTexture = renderTexture;
    }

    void MaintainRenderTextureSize()
    {
        if (renderTexture == null) return;
        if (rtWidth != Screen.width || rtHeight != Screen.height)
            EnsureRenderTexture();
    }

    void ReleaseRenderTexture()
    {
        if (renderTexture == null) return;
        if (inspectionCamera != null && inspectionCamera.targetTexture == renderTexture)
            inspectionCamera.targetTexture = null;
        if (itemImage != null && itemImage.texture == renderTexture)
            itemImage.texture = null;
        renderTexture.Release();
        Destroy(renderTexture);
        renderTexture = null;
        rtWidth = 0;
        rtHeight = 0;
    }
}

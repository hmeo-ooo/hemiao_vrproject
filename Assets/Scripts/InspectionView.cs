using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 手持具有 InspectableItem 组件的物品时按 E，进入审视界面：
/// - 屏幕叠加 60% 透明黑色遮罩
/// - 一个独立相机将物品渲染到 RenderTexture，并通过 RawImage 显示在遮罩之上
/// - ESC 退出，物品在玩家面前自然掉落
/// - DragDetach 模式：左键按住 detachableParts 中的子物体拖拽，达到阈值即分离两个物体并退出
/// - KnifeCut 模式：屏幕右侧出现一把切割刀，左键拖动它到中心物品上，
///   即把外壳与所有 detachableParts 一并分离，退出审视，所有部件自然掉落
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
    [Range(0f, 1f)] public float dimAlpha = 0.6f;
    public Color dimColor = Color.black;

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
    RenderTexture renderTexture;
    int rtWidth, rtHeight;

    GameObject inspectedItem;
    InspectableItem inspectable;
    Transform originalParent;
    Quaternion originalRotation;
    Vector3 originalScale;
    int originalSiblingIndex;

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

    Transform dragTarget;
    Vector3 dragLocalStart;
    Vector2 dragMouseStart;
    bool isDragging;

    // KnifeCut 模式状态
    Vector2 knifeIdleCanvasPos;
    bool isDraggingKnife;
    Vector2 knifeReturnVelocity;

    readonly List<GameObject> detachableOutlineRenderers = new List<GameObject>();
    Material detachableOutlineMaterial;

    struct PartTransformSnapshot
    {
        public Vector3 worldScale;
        public Vector3 localPosition;
    }

    readonly Dictionary<Transform, PartTransformSnapshot> partSnapshots = new Dictionary<Transform, PartTransformSnapshot>();

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
        ClearDetachableOutlines();
        ReleaseRenderTexture();
    }

    public void BeginInspection(GameObject item, CharacterInteraction interaction)
    {
        if (isInspecting || item == null) return;
        var insp = item.GetComponent<InspectableItem>();
        if (insp == null) return;

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
        CaptureRigidbody(item.GetComponent<Rigidbody>());
        for (int i = 0; i < insp.detachableParts.Count; i++)
        {
            Transform part = insp.detachableParts[i];
            if (part == null) continue;
            CaptureRigidbody(part.GetComponent<Rigidbody>());
            if (part.GetComponent<Rigidbody>() == null)
                CaptureRigidbody(part.GetComponentInChildren<Rigidbody>());
        }
        FreezeAllCapturedRigidbodies();
        CapturePartSnapshots(insp);

        item.transform.SetParent(null, true);
        item.transform.position = inspectionRoomCenter;

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
        ApplyDetachableOutlines();

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
            EndInspection(null);
            return;
        }

        if (inspectable != null
            && inspectable.interactionMode == InspectableItem.InspectionInteraction.KnifeCut)
        {
            HandleKnifeCut();
        }
        else
        {
            HandleDrag();
        }
    }

    void HandleDrag()
    {
        if (inspectable == null || inspectionCamera == null) return;

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = InspectionScreenRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (inspectable.TryResolveDetachable(hit.collider.transform, out Transform partRoot))
                {
                    dragTarget = partRoot;
                    dragLocalStart = partRoot.localPosition;
                    dragMouseStart = Input.mousePosition;
                    isDragging = true;
                    return;
                }
            }
        }

        if (isDragging && Input.GetMouseButton(0) && dragTarget != null)
        {
            Vector2 mouseDelta = (Vector2)Input.mousePosition - dragMouseStart;
            float sens = inspectable.dragWorldSensitivity;
            Vector3 worldOffset =
                inspectionCamera.transform.right * (mouseDelta.x * sens) +
                inspectionCamera.transform.up * (mouseDelta.y * sens);

            Transform parent = dragTarget.parent;
            Vector3 localOffset = parent != null
                ? parent.InverseTransformVector(worldOffset)
                : worldOffset;
            dragTarget.localPosition = dragLocalStart + localOffset;

            float ratio = mouseDelta.magnitude / Mathf.Max(1f, Screen.height);
            if (ratio >= inspectable.detachScreenRatio)
            {
                // 把鼠标拖拽方向映射到"玩家当前视角"的世界方向，让被分离件朝玩家拖动的方向飞出。
                Vector3 dir = Vector3.up;
                if (mainCamera != null)
                {
                    Vector3 mapped = mainCamera.transform.right * mouseDelta.x
                                   + mainCamera.transform.up * mouseDelta.y;
                    if (mapped.sqrMagnitude > 1e-4f) dir = mapped.normalized;
                }
                DetachAndEnd(dragTarget, dir);
                return;
            }
        }

        if (Input.GetMouseButtonUp(0) && isDragging)
        {
            if (dragTarget != null)
                dragTarget.localPosition = dragLocalStart;
            isDragging = false;
            dragTarget = null;
        }
    }

    /// <summary>
    /// 鼠标屏幕坐标 → 通过审视相机的视图换算射线（RenderTexture 内的对应像素）。
    /// </summary>
    Ray InspectionScreenRay(Vector3 mouseScreenPos)
    {
        // 我们让 RawImage 撑满全屏，因此屏幕坐标与 RenderTexture 坐标一一对应。
        // 即使屏幕尺寸与 RT 不一致，按比例换算到 RT 像素坐标。
        float u = mouseScreenPos.x / Mathf.Max(1f, Screen.width);
        float v = mouseScreenPos.y / Mathf.Max(1f, Screen.height);
        Vector3 rtPoint = new Vector3(u * rtWidth, v * rtHeight, 0f);
        return inspectionCamera.ScreenPointToRay(rtPoint);
    }

    void DetachAndEnd(Transform partRoot, Vector3 dragWorldDir)
    {
        if (partRoot == null) { EndInspection(null); return; }
        // 恢复父物体与该可分离件之间被 InspectableItem.Awake 关掉的碰撞，
        // 这样分离后两者再相遇会正常碰撞、不会互相穿透。
        if (inspectable != null)
        {
            inspectable.ResolvePartInfo(partRoot)?.ApplyTo(partRoot);
            inspectable.shellInfo?.ApplyTo(inspectable.transform);
        }
        partRoot.SetParent(null, true);
        RestorePartWorldScale(partRoot);
        inspectable?.ReleaseAttachedPart(partRoot);
        EndInspection(partRoot, dragWorldDir);
    }

    void FinalizeDetachedPart(Transform part, Vector3 dropAnchor, Vector3 detachDir)
    {
        if (part == null) return;

        RestorePartWorldScale(part);
        ItemSpawner.FinalizeLooseItem(part.gameObject);

        Rigidbody rb = part.GetComponent<Rigidbody>();
        if (rb == null) return;

        Vector3 dir = detachDir.sqrMagnitude > 1e-4f ? detachDir.normalized : Vector3.up;
        part.position = dropAnchor + dir * 0.15f;

        float speed = inspectable != null ? inspectable.detachVelocity : 1f;
        rb.velocity = dir * speed;
        rb.angularVelocity = Vector3.zero;
    }

    static bool IsTransformUnderDetachedPart(Transform t, Transform detachedPart)
    {
        if (t == null || detachedPart == null) return false;
        return t == detachedPart || t.IsChildOf(detachedPart);
    }

    // ------------------------------------------------------------------
    // KnifeCut 模式
    // ------------------------------------------------------------------

    void ConfigureKnifeForCurrentMode()
    {
        if (knifeRt == null || knifeImage == null) return;

        bool useKnife = inspectable != null
            && inspectable.interactionMode == InspectableItem.InspectionInteraction.KnifeCut;

        knifeRt.gameObject.SetActive(useKnife);
        if (!useKnife) return;

        // 设置外观
        knifeImage.sprite = inspectable.knifeSprite;
        knifeImage.color = inspectable.knifeSprite != null
            ? Color.white
            : new Color(0.85f, 0.85f, 0.9f, 1f); // 占位灰色矩形
        knifeImage.preserveAspect = true;

        // 尺寸与“刀尖”枢轴：把 pivot 设为刀尖位置，使该点跟随鼠标
        knifeRt.sizeDelta = inspectable.knifeUISize;
        knifeRt.pivot = ClampVec01(inspectable.knifeTipPivot);
        knifeRt.localRotation = Quaternion.Euler(0f, 0f, inspectable.knifeUIRotation);
        knifeRt.localScale = Vector3.one;

        // 计算初始位置（屏幕比例 → Canvas local）
        knifeIdleCanvasPos = ScreenAnchorToCanvasLocal(inspectable.knifeIdleAnchor);
        knifeRt.anchoredPosition = knifeIdleCanvasPos;
        knifeReturnVelocity = Vector2.zero;
        isDraggingKnife = false;
    }

    void HandleKnifeCut()
    {
        if (knifeRt == null || inspectable == null) return;

        Vector2 mousePos = Input.mousePosition;

        // 开始拖拽：左键按下且鼠标在刀的矩形内
        if (Input.GetMouseButtonDown(0))
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(knifeRt, mousePos, null))
            {
                isDraggingKnife = true;
                knifeReturnVelocity = Vector2.zero;
            }
        }

        if (isDraggingKnife && Input.GetMouseButton(0))
        {
            // 把鼠标位置（屏幕坐标）映射到 Canvas 本地坐标，并赋给刀
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRt, mousePos, null, out Vector2 localPos))
            {
                knifeRt.anchoredPosition = localPos;
            }

            // 命中检测：从审视相机沿鼠标方向发射射线，若打到当前被审视物品则触发切割
            if (TryKnifeHitInspectedItem(mousePos))
            {
                DoKnifeCutAndEnd();
                return;
            }
        }

        if (Input.GetMouseButtonUp(0) && isDraggingKnife)
        {
            isDraggingKnife = false;
        }

        // 未拖拽时平滑回到初始锚点
        if (!isDraggingKnife)
        {
            float smooth = Mathf.Max(0.0001f, inspectable.knifeReturnSmoothTime);
            Vector2 cur = knifeRt.anchoredPosition;
            cur.x = Mathf.SmoothDamp(cur.x, knifeIdleCanvasPos.x, ref knifeReturnVelocity.x, smooth);
            cur.y = Mathf.SmoothDamp(cur.y, knifeIdleCanvasPos.y, ref knifeReturnVelocity.y, smooth);
            knifeRt.anchoredPosition = cur;
        }
    }

    bool TryKnifeHitInspectedItem(Vector2 mouseScreenPos)
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

    void DoKnifeCutAndEnd()
    {
        if (!isInspecting || inspectable == null || inspectedItem == null)
        {
            EndInspection(null);
            return;
        }

        Vector3 shellCenter = ItemInfoWorldUI.CalculateWorldBounds(inspectedItem).center;

        // 在解除父子关系前，记录每个子件相对外壳的世界偏移与“方向特征”，
        // 用于切割完成后在主世界里给各部件铺开掉落位置和远离冲量。
        var partsInfo = new List<PartKnifeInfo>(inspectable.detachableParts.Count);
        for (int i = 0; i < inspectable.detachableParts.Count; i++)
        {
            Transform p = inspectable.detachableParts[i];
            if (p == null) continue;
            inspectable.RestoreCollisionFor(p);

            Vector3 toPart = p.position - shellCenter;
            // 把审视空间中的方向投影到玩家相机空间，作为掉落时的世界方向
            Vector3 worldDir;
            if (inspectionCamera != null && mainCamera != null && toPart.sqrMagnitude > 1e-6f)
            {
                Vector3 camLocal = inspectionCamera.transform.InverseTransformDirection(toPart.normalized);
                worldDir = mainCamera.transform.TransformDirection(camLocal);
                if (worldDir.sqrMagnitude > 1e-6f) worldDir.Normalize();
                else worldDir = Vector3.down;
            }
            else
            {
                worldDir = Vector3.down;
            }

            partsInfo.Add(new PartKnifeInfo { part = p, dropDir = worldDir });
        }

        // 解除父子关系，把子件从外壳剥离到世界根
        for (int i = 0; i < partsInfo.Count; i++)
            partsInfo[i].part.SetParent(null, true);

        // 写入分离后的 ItemInformation（外壳 + 各子件）
        inspectable.shellInfo?.ApplyTo(inspectedItem.transform);
        if (inspectedItem != null)
            ItemSpawner.FinalizeLooseItem(inspectedItem);
        for (int i = 0; i < partsInfo.Count; i++)
        {
            Transform p = partsInfo[i].part;
            if (p == null) continue;
            inspectable.ResolvePartInfo(p)?.ApplyTo(p);
            ItemSpawner.FinalizeLooseItem(p.gameObject);
            inspectable.ReleaseAttachedPart(p);
        }

        FinalizeKnifeCut(partsInfo);
    }

    struct PartKnifeInfo
    {
        public Transform part;
        public Vector3 dropDir;
    }

    void FinalizeKnifeCut(List<PartKnifeInfo> parts)
    {
        Vector3 dropAnchor = mainCamera != null
            ? mainCamera.transform.position + mainCamera.transform.forward * GetDropDistance()
            : Vector3.zero;

        // 复位外壳的父子层级与位姿（与 EndInspection 中相同的还原逻辑）
        if (inspectedItem != null)
        {
            inspectedItem.transform.SetParent(originalParent, false);
            inspectedItem.transform.position = dropAnchor;
            inspectedItem.transform.rotation = originalRotation;
            inspectedItem.transform.localScale = originalScale;
            if (originalParent != null)
                inspectedItem.transform.SetSiblingIndex(originalSiblingIndex);
        }

        float impulse = inspectable.knifeCutSeparateImpulse;
        float dropSpeed = inspectable.knifeCutInitialDropSpeed;
        float spread = inspectable.knifeCutDropSpread;

        // 把所有被捕获的刚体（外壳 + 各子件）解冻为受重力的自由刚体
        for (int i = 0; i < rbStates.Count; i++)
        {
            var s = rbStates[i];
            if (s.rb == null) continue;

            s.rb.isKinematic = false;
            s.rb.useGravity = true;
            s.rb.detectCollisions = true;

            int idx = IndexOfPartRigidbody(parts, s.rb);
            if (idx >= 0)
            {
                Vector3 dir = parts[idx].dropDir;
                Transform part = parts[idx].part;
                part.position = dropAnchor + dir * spread;
                RestorePartWorldScale(part);
                s.rb.velocity = Vector3.down * dropSpeed + dir * impulse;
                s.rb.angularVelocity = Vector3.zero;
            }
            else
            {
                // 外壳本体或其他附带的刚体
                s.rb.velocity = Vector3.down * dropSpeed;
                s.rb.angularVelocity = Vector3.zero;
            }
        }

        // 兜底：若某子件没有 Rigidbody（未被 rbStates 捕获），给它补一个并入位
        for (int i = 0; i < parts.Count; i++)
        {
            Transform p = parts[i].part;
            if (p == null) continue;
            Rigidbody rb = p.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = p.gameObject.AddComponent<Rigidbody>();
                p.position = dropAnchor + parts[i].dropDir * spread;
                rb.velocity = Vector3.down * dropSpeed + parts[i].dropDir * impulse;
            }
        }

        rbStates.Clear();
        TeardownInspectionUI();
    }

    int IndexOfPartRigidbody(List<PartKnifeInfo> parts, Rigidbody rb)
    {
        if (rb == null) return -1;
        Transform t = rb.transform;
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].part == t) return i;
        }
        return -1;
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

    public void EndInspection(Transform detachedPart = null, Vector3 detachDir = default)
    {
        if (!isInspecting) return;

        RestoreDetachedPartLocalPositions(detachedPart);

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

        if (detachedPart != null)
            FinalizeDetachedPart(detachedPart, dropAnchor, detachDir);

        for (int i = 0; i < rbStates.Count; i++)
        {
            var s = rbStates[i];
            if (s.rb == null) continue;
            if (IsTransformUnderDetachedPart(s.rb.transform, detachedPart))
                continue;

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

        TeardownInspectionUI();
    }

    /// <summary>
    /// EndInspection / FinalizeKnifeCut 共享的“关 UI、还原输入门、清空状态”尾段。
    /// </summary>
    void TeardownInspectionUI()
    {
        ClearDetachableOutlines();

        if (overlayCanvas != null) overlayCanvas.gameObject.SetActive(false);
        if (knifeRt != null) knifeRt.gameObject.SetActive(false);
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
        dragTarget = null;
        isDragging = false;
        isDraggingKnife = false;
        partSnapshots.Clear();

        interaction?.ForceRefreshInteractionVisuals();
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

    void CapturePartSnapshots(InspectableItem insp)
    {
        partSnapshots.Clear();
        if (insp == null) return;

        for (int i = 0; i < insp.detachableParts.Count; i++)
        {
            Transform part = insp.detachableParts[i];
            if (part == null) continue;
            partSnapshots[part] = new PartTransformSnapshot
            {
                worldScale = part.lossyScale,
                localPosition = part.localPosition
            };
        }
    }

    void FrameInspectionCamera(GameObject item)
    {
        if (inspectionCamera == null || item == null) return;

        Bounds bounds = ItemInfoWorldUI.CalculateWorldBounds(item);
        float fov = GetInspectionFieldOfView();
        inspectionCamera.fieldOfView = fov;

        float distance = GetDropDistance();
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

    void RestorePartWorldScale(Transform part)
    {
        if (part == null) return;
        if (!partSnapshots.TryGetValue(part, out PartTransformSnapshot snap)) return;
        SetTransformWorldScale(part, snap.worldScale);
    }

    void RestoreDetachedPartLocalPositions(Transform detachedPart)
    {
        if (inspectable == null || inspectedItem == null) return;

        for (int i = 0; i < inspectable.detachableParts.Count; i++)
        {
            Transform part = inspectable.detachableParts[i];
            if (part == null || part == detachedPart) continue;
            if (!part.IsChildOf(inspectedItem.transform)) continue;
            if (!partSnapshots.TryGetValue(part, out PartTransformSnapshot snap)) continue;
            part.localPosition = snap.localPosition;
        }
    }

    static void SetTransformWorldScale(Transform t, Vector3 worldScale)
    {
        if (t == null) return;

        Transform parent = t.parent;
        if (parent == null)
        {
            t.localScale = worldScale;
            return;
        }

        Vector3 ps = parent.lossyScale;
        t.localScale = new Vector3(
            worldScale.x / Mathf.Max(Mathf.Abs(ps.x), 1e-6f),
            worldScale.y / Mathf.Max(Mathf.Abs(ps.y), 1e-6f),
            worldScale.z / Mathf.Max(Mathf.Abs(ps.z), 1e-6f));
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
            // 左键拖拽时打不到 detachable 子物体。
            // kinematic + useGravity=false 已足以避免它在审视舱里被物理推动。
        }
    }

    void EnsureInspectionCamera()
    {
        if (inspectionCamera != null) return;

        var camGo = new GameObject("InspectionCamera");
        camGo.transform.SetParent(transform, false);
        inspectionCamera = camGo.AddComponent<Camera>();
        inspectionCamera.clearFlags = CameraClearFlags.SolidColor;
        inspectionCamera.backgroundColor = new Color(0f, 0f, 0f, 0f); // 透明背景
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
        if (overlayCanvas != null) return;

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

        // KnifeCut 模式用的切割刀 UI（默认隐藏，仅在 KnifeCut 模式进入审视时显示）
        var knifeGo = new GameObject("InspectionKnife");
        knifeGo.transform.SetParent(canvasGo.transform, false);
        knifeRt = knifeGo.AddComponent<RectTransform>();
        // 锚点设为 Canvas 中心，方便用 anchoredPosition 直接表达 Canvas 本地坐标
        knifeRt.anchorMin = new Vector2(0.5f, 0.5f);
        knifeRt.anchorMax = new Vector2(0.5f, 0.5f);
        knifeRt.sizeDelta = new Vector2(220f, 220f);
        knifeRt.pivot = new Vector2(0.15f, 0.85f);
        knifeImage = knifeGo.AddComponent<Image>();
        knifeImage.color = Color.white;
        knifeImage.raycastTarget = false;
        knifeImage.preserveAspect = true;
        knifeGo.SetActive(false);

        overlayCanvas.gameObject.SetActive(false);
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

    // ------------------------------------------------------------------
    // DragDetach 可撕扯件描边
    // ------------------------------------------------------------------

    void ApplyDetachableOutlines()
    {
        ClearDetachableOutlines();
        if (inspectable == null) return;
        if (inspectable.interactionMode != InspectableItem.InspectionInteraction.DragDetach) return;
        if (!inspectable.showDetachableOutline) return;

        Material baseMat = GetDetachableOutlineMaterial();
        if (baseMat == null) return;

        Color color = inspectable.detachableOutlineColor;
        float widthScale = inspectable.detachableOutlineWidthScale;

        for (int i = 0; i < inspectable.detachableParts.Count; i++)
        {
            Transform part = inspectable.detachableParts[i];
            if (part == null) continue;
            AddDetachableOutline(part.gameObject, baseMat, color, widthScale);
        }
    }

    void AddDetachableOutline(GameObject target, Material baseMat, Color color, float widthScale)
    {
        if (target == null) return;

        MeshRenderer[] meshRenderers = target.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < meshRenderers.Length; i++)
        {
            MeshRenderer mr = meshRenderers[i];
            float width = ComputeDetachableOutlineWidth(mr.transform, target, widthScale);
            TryAddDetachableOutlineForRenderer(mr, baseMat, color, width);
        }

        SkinnedMeshRenderer[] skinned = target.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinned.Length; i++)
        {
            SkinnedMeshRenderer smr = skinned[i];
            float width = ComputeDetachableOutlineWidth(smr.transform, target, widthScale);
            TryAddDetachableOutlineForSkinned(smr, baseMat, color, width);
        }
    }

    float ComputeDetachableOutlineWidth(Transform rendererTransform, GameObject partRoot, float widthScale)
    {
        Bounds bounds = ItemInfoWorldUI.CalculateWorldBounds(partRoot);
        float maxExtent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
        float worldWidth = widthScale * Mathf.Max(maxExtent, 0.05f);

        Vector3 lossy = rendererTransform.lossyScale;
        float avgScale = (Mathf.Abs(lossy.x) + Mathf.Abs(lossy.y) + Mathf.Abs(lossy.z)) / 3f;
        return worldWidth / Mathf.Max(avgScale, 0.001f);
    }

    void TryAddDetachableOutlineForRenderer(MeshRenderer mr, Material baseMat, Color color, float width)
    {
        if (mr == null || !mr.enabled) return;
        if (mr.gameObject.name.EndsWith("_InspectOutline")) return;

        MeshFilter mf = mr.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;

        GameObject go = new GameObject(mr.gameObject.name + "_InspectOutline");
        go.transform.SetParent(mr.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.layer = mr.gameObject.layer;
        go.hideFlags = HideFlags.DontSave;

        MeshFilter cloneMf = go.AddComponent<MeshFilter>();
        cloneMf.sharedMesh = mf.sharedMesh;

        MeshRenderer cloneMr = go.AddComponent<MeshRenderer>();
        SetupDetachableOutlineRenderer(cloneMr, baseMat, color, width);
        cloneMr.sortingOrder = 1;
        detachableOutlineRenderers.Add(go);
    }

    void TryAddDetachableOutlineForSkinned(SkinnedMeshRenderer smr, Material baseMat, Color color, float width)
    {
        if (smr == null || !smr.enabled || smr.sharedMesh == null) return;
        if (smr.gameObject.name.EndsWith("_InspectOutline")) return;

        GameObject go = new GameObject(smr.gameObject.name + "_InspectOutline");
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
        SetupDetachableOutlineRenderer(cloneSmr, baseMat, color, width);
        cloneSmr.sortingOrder = 1;
        detachableOutlineRenderers.Add(go);
    }

    static void SetupDetachableOutlineRenderer(Renderer renderer, Material baseMat, Color color, float width)
    {
        renderer.material = CreateDetachableOutlineMaterial(baseMat, color, width);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    static Material CreateDetachableOutlineMaterial(Material baseMat, Color color, float width)
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

    Material GetDetachableOutlineMaterial()
    {
        if (characterInteraction != null && characterInteraction.outlineMaterial != null
            && characterInteraction.outlineMaterial.shader != null
            && characterInteraction.outlineMaterial.shader.isSupported)
            return characterInteraction.outlineMaterial;

        if (detachableOutlineMaterial != null
            && detachableOutlineMaterial.shader != null
            && detachableOutlineMaterial.shader.isSupported)
            return detachableOutlineMaterial;

        Shader shader = Shader.Find("Hemiao/ItemOutline");
        if (shader == null || !shader.isSupported)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null || !shader.isSupported)
            shader = Shader.Find("Unlit/Color");
        if (shader == null) return null;

        detachableOutlineMaterial = new Material(shader);
        return detachableOutlineMaterial;
    }

    void ClearDetachableOutlines()
    {
        for (int i = detachableOutlineRenderers.Count - 1; i >= 0; i--)
        {
            GameObject go = detachableOutlineRenderers[i];
            if (go != null)
                Destroy(go);
        }
        detachableOutlineRenderers.Clear();
    }
}

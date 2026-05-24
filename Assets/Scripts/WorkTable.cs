using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 工作台。挂在带 Collider 的桌面物体上。
///
/// 行为：
/// 1. 玩家手持物体的包围盒与桌面 Collider 重叠时，桌面亮起黄色描边。
/// 2. 在重叠状态下松开左键（CharacterInteraction.Released 事件） → 物体自动吸附到 AnchorPoint。
/// 3. 玩家准星指向桌面上的物体并长按左键 → 物体回到玩家手里（依靠 CharacterInteraction 现有抓取逻辑）。
/// 4. 玩家准星指向桌面上的物体时，鼠标滚轮可旋转该物体。
/// </summary>
[RequireComponent(typeof(Collider))]
public class WorkTable : MonoBehaviour
{
    [Header("锚点")]
    [Tooltip("放置时物体自动吸附到这个位置。留空会自动查找名为 \"AnchorPoint\" 的子物体。")]
    public Transform anchorPoint;

    [Header("交互玩家")]
    [Tooltip("场景中的 CharacterInteraction，留空则运行时自动查找。")]
    public CharacterInteraction character;

    [Header("放置检测")]
    [Tooltip("用于判断手持物体是否压在桌面上的包围盒外扩量。")]
    public float placementCheckPadding = 0.05f;

    [Header("描边")]
    [Tooltip("描边颜色，默认黄色。")]
    public Color highlightColor = new Color(1f, 0.85f, 0.1f, 1f);

    [Tooltip("自定义描边材质，留空则自动使用 Hemiao/ItemOutline。")]
    public Material outlineMaterial;

    [Tooltip("描边宽度系数，会乘以桌面包围盒最大半径。")]
    [Range(0f, 0.5f)]
    public float outlineWidthScale = 0.03f;

    [Header("吸附动画")]
    [Tooltip("吸附到锚点的平滑时间。")]
    public float snapSmoothTime = 0.1f;

    [Header("取出后状态")]
    [Tooltip("玩家把物品从 AnchorPoint 拖出后，强制让物品变回普通可投掷状态（非 kinematic + 启用重力）。\n关闭则保留物品被放置前的原始 Rigidbody 状态。")]
    public bool forceThrowableOnRetrieval = true;

    [Header("右键拖拽旋转")]
    [Tooltip("鼠标水平拖拽的旋转灵敏度（每单位 Mouse X 旋转的角度）。")]
    public float dragRotateYawSpeed = 6f;

    [Tooltip("鼠标垂直拖拽的旋转灵敏度（每单位 Mouse Y 旋转的角度）。")]
    public float dragRotatePitchSpeed = 6f;

    [Tooltip("拖拽时是否暂停玩家视角（推荐开启，否则视角也会跟着转）。")]
    public bool suspendCameraWhileDragging = true;

    Collider tableCollider;

    public bool HasPlacedItem => placedItem != null;
    public GameObject PlacedItem => placedItem;

    GameObject heldByPlayer;
    GameObject currentCandidate;
    bool outlineActive;

    GameObject placedItem;
    Rigidbody placedRb;
    Transform placedOriginalParent;
    bool placedOriginalKinematic;
    bool placedOriginalUseGravity;
    RigidbodyInterpolation placedOriginalInterpolation;
    RigidbodyConstraints placedOriginalConstraints;

    /// <summary>刚从桌面取走、等待松手/扔出后恢复物理状态的物体。</summary>
    GameObject pendingRetrieval;
    bool pendingOriginalKinematic;
    bool pendingOriginalUseGravity;

    Vector3 placedSnapVelocity;
    Quaternion placedDesiredRotation = Quaternion.identity;
    bool armedForRelease;
    bool draggingRotate;
    CharacterMove suspendedMover;

    readonly List<GameObject> activeOutlineRenderers = new List<GameObject>();
    Material outlineMaterialInstance;

    void Awake()
    {
        tableCollider = GetComponent<Collider>();
    }

    void Start()
    {
        if (character == null)
            character = FindObjectOfType<CharacterInteraction>();

        if (character != null)
        {
            character.Grabbed += OnPlayerGrabbed;
            character.Released += OnPlayerReleased;
            character.Thrown += OnPlayerThrown;
        }

        if (anchorPoint == null)
        {
            Transform t = transform.Find("AnchorPoint");
            if (t != null) anchorPoint = t;
        }

        if (anchorPoint == null)
            Debug.LogWarning($"[WorkTable] {name} 未设置 AnchorPoint，将无法放置物体。", this);
    }

    void OnDestroy()
    {
        if (character != null)
        {
            character.Grabbed -= OnPlayerGrabbed;
            character.Released -= OnPlayerReleased;
            character.Thrown -= OnPlayerThrown;
        }

        if (draggingRotate) EndDragRotation();

        ClearOutlineRenderers();
        if (outlineMaterialInstance != null)
            Destroy(outlineMaterialInstance);
    }

    void Update()
    {
        UpdateHeldOverlapDetection();
        UpdatePickupInteraction();
        UpdateDragRotation();
    }

    void LateUpdate()
    {
        if (placedItem == null) return;

        if (armedForRelease)
        {
            placedItem.transform.position = anchorPoint != null ? anchorPoint.position : placedItem.transform.position;
            if (placedRb != null)
            {
                placedRb.velocity = Vector3.zero;
                placedRb.angularVelocity = Vector3.zero;
            }
        }
        else
        {
            SnapPlacedToAnchor();
        }
    }

    // ---------- CharacterInteraction 事件 ----------

    void OnPlayerGrabbed(GameObject obj)
    {
        heldByPlayer = obj;
        if (obj != null && obj == placedItem)
        {
            pendingRetrieval = obj;
            pendingOriginalKinematic = placedOriginalKinematic;
            pendingOriginalUseGravity = placedOriginalUseGravity;
            FinalizeRetrieval();
        }
    }

    void OnPlayerReleased()
    {
        ApplyRetrievalPhysics();
        TryPlaceOnRelease();
    }

    void OnPlayerThrown()
    {
        ApplyRetrievalPhysics();
        heldByPlayer = null;
        currentCandidate = null;
    }

    void TryPlaceOnRelease()
    {
        GameObject toPlace = currentCandidate;
        if (toPlace != null && placedItem == null && anchorPoint != null && IsPlaceable(toPlace))
            PlaceOnTable(toPlace);

        heldByPlayer = null;
        currentCandidate = null;
    }

    static bool IsPlaceable(GameObject go)
    {
        if (go == null) return false;
        // 螺丝刀等"工具类"物体不应被放置到锚点上，它们有自己的归位逻辑。
        if (go.GetComponent<Screwdriver>() != null) return false;
        if (go.GetComponent<Knife>() != null) return false;
        return true;
    }

    // ---------- 每帧检测 ----------

    void UpdateHeldOverlapDetection()
    {
        if (tableCollider == null) return;

        if (heldByPlayer == null || heldByPlayer == placedItem || anchorPoint == null || !IsPlaceable(heldByPlayer))
        {
            currentCandidate = null;
        }
        else
        {
            Bounds heldBounds = ItemInfoWorldUI.CalculateWorldBounds(heldByPlayer);
            Bounds tableBounds = tableCollider.bounds;
            tableBounds.Expand(placementCheckPadding * 2f);
            currentCandidate = tableBounds.Intersects(heldBounds) ? heldByPlayer : null;
        }

        SetOutlineActive(currentCandidate != null || placedItem != null);
    }

    void UpdatePickupInteraction()
    {
        if (placedItem == null) return;
        if (character == null) return;

        bool aimingAtPlaced = IsAimingAtPlaced();
        bool mouseHeld = Input.GetMouseButton(0);
        bool canArm = aimingAtPlaced && mouseHeld && heldByPlayer == null;

        if (canArm && !armedForRelease)
        {
            armedForRelease = true;
            RestoreOriginalRbState();
            if (placedItem.transform.parent == anchorPoint)
                placedItem.transform.SetParent(placedOriginalParent, true);
        }
        else if (!canArm && armedForRelease)
        {
            // 玩家没等到抓取阈值就松手或移开了准星，把物体重新锁回桌面。
            armedForRelease = false;
            ApplyPlacedRbState();
            if (placedItem.transform.parent != anchorPoint)
                placedItem.transform.SetParent(anchorPoint, true);
        }
    }

    void UpdateDragRotation()
    {
        if (placedItem == null)
        {
            if (draggingRotate) EndDragRotation();
            return;
        }

        bool rightHeld = Input.GetMouseButton(1);

        if (!draggingRotate)
        {
            if (Input.GetMouseButtonDown(1) && IsAimingAtPlaced())
                BeginDragRotation();
        }
        else
        {
            if (!rightHeld)
            {
                EndDragRotation();
                return;
            }

            float mx = Input.GetAxis("Mouse X");
            float my = Input.GetAxis("Mouse Y");
            if (Mathf.Abs(mx) < 1e-4f && Mathf.Abs(my) < 1e-4f) return;

            Vector3 yawAxis = Vector3.up;
            Vector3 pitchAxis = character != null && character.cameraTransform != null
                ? character.cameraTransform.right
                : Vector3.right;

            Quaternion delta = Quaternion.AngleAxis(-mx * dragRotateYawSpeed, yawAxis)
                             * Quaternion.AngleAxis(-my * dragRotatePitchSpeed, pitchAxis);

            placedDesiredRotation = delta * placedDesiredRotation;
            placedItem.transform.rotation = delta * placedItem.transform.rotation;
        }
    }

    void BeginDragRotation()
    {
        draggingRotate = true;
        if (suspendCameraWhileDragging && character != null)
        {
            CharacterMove mover = character.GetComponent<CharacterMove>();
            if (mover == null) mover = character.GetComponentInParent<CharacterMove>();
            if (mover == null) mover = FindObjectOfType<CharacterMove>();
            if (mover != null)
            {
                mover.PushMouseLookSuspend();
                suspendedMover = mover;
            }
        }
    }

    void EndDragRotation()
    {
        draggingRotate = false;
        if (suspendedMover != null)
        {
            suspendedMover.PopMouseLookSuspend();
            suspendedMover = null;
        }
    }

    bool IsAimingAtPlaced()
    {
        if (placedItem == null || character == null || character.cameraTransform == null)
            return false;

        Camera cam = character.cameraTransform.GetComponent<Camera>();
        Ray ray = cam != null
            ? cam.ScreenPointToRay(new Vector2(Screen.width / 2f, Screen.height / 2f))
            : new Ray(character.cameraTransform.position, character.cameraTransform.forward);

        if (!Physics.Raycast(ray, out RaycastHit hit, character.maxGrabDistance, character.interactMask))
            return false;

        Rigidbody rb = hit.rigidbody != null ? hit.rigidbody : hit.collider.GetComponentInParent<Rigidbody>();
        return rb != null && rb.gameObject == placedItem;
    }

    // ---------- 放置 / 取出 ----------

    void PlaceOnTable(GameObject item)
    {
        Rigidbody rb = item.GetComponent<Rigidbody>();
        if (rb == null) rb = item.GetComponentInParent<Rigidbody>();
        if (rb == null) return;

        placedItem = rb.gameObject;
        placedRb = rb;
        placedOriginalParent = placedItem.transform.parent;
        placedOriginalKinematic = rb.isKinematic;
        placedOriginalUseGravity = rb.useGravity;
        placedOriginalInterpolation = rb.interpolation;
        placedOriginalConstraints = rb.constraints;

        ApplyPlacedRbState();

        placedItem.transform.SetParent(anchorPoint, true);
        placedSnapVelocity = Vector3.zero;
        placedDesiredRotation = anchorPoint != null ? anchorPoint.rotation : placedItem.transform.rotation;
    }

    void ApplyPlacedRbState()
    {
        if (placedRb == null) return;
        placedRb.velocity = Vector3.zero;
        placedRb.angularVelocity = Vector3.zero;
        placedRb.isKinematic = true;
        placedRb.useGravity = false;
    }

    void RestoreOriginalRbState()
    {
        if (placedRb == null) return;
        ApplyThrowableState(
            placedRb,
            placedOriginalKinematic,
            placedOriginalUseGravity,
            placedOriginalInterpolation,
            placedOriginalConstraints);
    }

    void ApplyRetrievalPhysics()
    {
        if (pendingRetrieval == null) return;

        Rigidbody rb = pendingRetrieval.GetComponent<Rigidbody>();
        if (rb != null)
        {
            ApplyThrowableState(
                rb,
                pendingOriginalKinematic,
                pendingOriginalUseGravity,
                rb.interpolation,
                rb.constraints);
        }

        pendingRetrieval = null;
    }

    void ApplyThrowableState(
        Rigidbody rb,
        bool originalKinematic,
        bool originalUseGravity,
        RigidbodyInterpolation originalInterpolation,
        RigidbodyConstraints originalConstraints)
    {
        if (rb == null) return;

        bool kinematic = forceThrowableOnRetrieval ? false : originalKinematic;
        bool useGravity = forceThrowableOnRetrieval ? true : originalUseGravity;

        rb.isKinematic = kinematic;
        rb.useGravity = useGravity;
        rb.interpolation = originalInterpolation;
        rb.constraints = originalConstraints;
        rb.detectCollisions = true;
        if (!rb.isKinematic)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    void SnapPlacedToAnchor()
    {
        if (anchorPoint == null) return;

        Transform t = placedItem.transform;
        if (snapSmoothTime <= 0f)
        {
            t.SetPositionAndRotation(anchorPoint.position, placedDesiredRotation);
            return;
        }

        t.position = Vector3.SmoothDamp(t.position, anchorPoint.position, ref placedSnapVelocity, snapSmoothTime);

        float lerp = 1f - Mathf.Exp(-Time.deltaTime / snapSmoothTime);
        t.rotation = Quaternion.Slerp(t.rotation, placedDesiredRotation, lerp);
    }

    /// <summary>
    /// Cuttable 切割时调用：停止桌面吸附，否则刚体会与 SnapPlacedToAnchor 冲突导致震动且子物体无法掉落。
    /// </summary>
    public void ReleasePlacedItemForCut(GameObject item)
    {
        if (placedItem == null || item == null) return;
        if (item != placedItem && item.transform.root != placedItem.transform.root)
            return;

        if (placedItem.transform.parent == anchorPoint)
            placedItem.transform.SetParent(placedOriginalParent, true);

        ClearPlacedState();
    }

    void FinalizeRetrieval()
    {
        if (placedItem != null && placedItem.transform.parent == anchorPoint)
            placedItem.transform.SetParent(placedOriginalParent, true);

        ClearPlacedState();
    }

    void ClearPlacedState()
    {
        placedItem = null;
        placedRb = null;
        placedOriginalParent = null;
        placedSnapVelocity = Vector3.zero;
        armedForRelease = false;
        if (draggingRotate) EndDragRotation();
    }

    // ---------- 描边 ----------

    void SetOutlineActive(bool active)
    {
        if (active == outlineActive) return;
        outlineActive = active;
        if (active) ApplyTableOutline();
        else ClearOutlineRenderers();
    }

    void ApplyTableOutline()
    {
        ClearOutlineRenderers();

        Material baseMat = GetOutlineMaterial();
        if (baseMat == null) return;

        float width = ComputeOutlineWidth();

        var meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < meshRenderers.Length; i++)
            TryAddOutlineForRenderer(meshRenderers[i], baseMat, highlightColor, width);

        var skinned = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinned.Length; i++)
            TryAddOutlineForSkinned(skinned[i], baseMat, highlightColor, width);
    }

    float ComputeOutlineWidth()
    {
        Bounds b = tableCollider != null
            ? tableCollider.bounds
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

        outlineMaterialInstance = new Material(shader);
        outlineMaterial = outlineMaterialInstance;
        return outlineMaterial;
    }

    void TryAddOutlineForRenderer(MeshRenderer mr, Material baseMat, Color color, float width)
    {
        if (mr == null || !mr.enabled) return;
        if (mr.gameObject.name.EndsWith("_TableOutline")) return;

        MeshFilter mf = mr.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;

        GameObject go = new GameObject(mr.gameObject.name + "_TableOutline");
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
        activeOutlineRenderers.Add(go);
    }

    void TryAddOutlineForSkinned(SkinnedMeshRenderer smr, Material baseMat, Color color, float width)
    {
        if (smr == null || !smr.enabled || smr.sharedMesh == null) return;
        if (smr.gameObject.name.EndsWith("_TableOutline")) return;

        GameObject go = new GameObject(smr.gameObject.name + "_TableOutline");
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
        activeOutlineRenderers.Add(go);
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
        for (int i = activeOutlineRenderers.Count - 1; i >= 0; i--)
        {
            if (activeOutlineRenderers[i] != null)
                Destroy(activeOutlineRenderers[i]);
        }
        activeOutlineRenderers.Clear();
    }
}

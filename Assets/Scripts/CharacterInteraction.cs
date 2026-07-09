using System;
using System.Collections;
using System.Collections.Generic;
using Hemiao.Rendering;
using UnityEngine;

public class CharacterInteraction : MonoBehaviour
{
    [Header("\u4EA4\u4E92")]
    public Transform cameraTransform;
    public float maxGrabDistance = 3f;
    public LayerMask interactMask = ~0;

    [Header("\u51C6\u661F")]
    public Texture2D crosshairTexture;
    public float crosshairSize = 16f;
    public Color crosshairDefaultColor = Color.white;
    public Color crosshairAimColor = Color.green;

    [Header("\u6293\u53D6")]
    [Tooltip("\u9ED8\u8BA4\u6301\u63E1\u8DDD\u79BB\uFF08\u76F8\u673A\u524D\u65B9\uFF09\u3002\u6293\u53D6\u65F6\u4F1A\u5148\u4FDD\u6301\u5F53\u524D\u8DDD\u79BB\u3002")]
    public float holdDistance = 1.8f;

    [Tooltip("\u6301\u63E1\u70B9\u6700\u8FD1\u8DDD\u79BB\uFF0C\u9632\u6B62\u7A7F\u5165\u76F8\u673A\u3002")]
    public float minHoldDistance = 0.6f;

    [Tooltip("\u6293\u53D6\u540E\u8DDF\u968F\u6301\u63E1\u70B9\u7684\u5E73\u6ED1\u65F6\u95F4\uFF0C\u8D8A\u5C0F\u8D8A\u8DDF\u624B\uFF0C\u8D8A\u5927\u8D8A\u67D4\u548C\u3002")]
    public float grabFollowSmoothTime = 0.08f;

    [Header("远程召唤 - 钩爪")]
    [Tooltip("准星对准物体后按下此键，发射钩爪把物体勾回到玩家身前。")]
    public KeyCode summonKey = KeyCode.F;

    [Tooltip("钩爪射线最大距离（米）。<=0 表示无限远。")]
    public float summonMaxDistance = 0f;

    [Tooltip("钩爪射线检测的 LayerMask。")]
    public LayerMask summonMask = ~0;

    [Tooltip("被勾回的物体最终落在相机前方多远处（米）。")]
    public float summonDropDistance = 1.5f;

    [Tooltip("两次发射之间的最小冷却时间（秒）。0 = 无冷却。")]
    public float summonCooldown = 0.4f;

    [Header("钩爪外观")]
    [Tooltip("钩爪发射起点。留空时使用相机本地空间下的 hookMuzzleLocalOffset 计算。")]
    public Transform hookOrigin;

    [Tooltip("hookOrigin 为空时使用：相机本地空间下的发射点偏移（默认右下伸出）。")]
    public Vector3 hookMuzzleLocalOffset = new Vector3(0.35f, -0.3f, 0.3f);

    [Tooltip("线缆材质。留空则运行时自动创建 Unlit 材质。")]
    public Material hookLineMaterial;

    [Tooltip("线缆颜色（含 Alpha）。")]
    public Color hookLineColor = new Color(0.95f, 0.85f, 0.55f, 1f);

    [Tooltip("线缆宽度（米）。")]
    public float hookLineWidth = 0.02f;

    [Header("钩爪时序")]
    [Tooltip("钩爪飞出速度（米/秒）。")]
    public float hookFlySpeed = 32f;

    [Tooltip("钩住物体后的回拉速度（米/秒）。")]
    public float hookPullSpeed = 22f;

    [Tooltip("线缆收回的时长（秒）。")]
    public float hookRetractDuration = 0.12f;

    [Tooltip("回拉过程中暂时关闭物体碰撞，让其穿越中途障碍。")]
    public bool hookPassThroughObstacles = true;

    [Header("持物碰撞")]
    [Tooltip("持物时从相机向前做射线检测，避免 HoldPoint 落入墙/地内部。")]
    public bool clampHoldPointAgainstWalls = true;

    [Tooltip("持物碰撞检测使用的 LayerMask。")]
    public LayerMask holdCollisionMask = ~0;

    [Tooltip("HoldPoint 与墙面/地面保持的最小间距（米）。")]
    public float holdWallPadding = 0.08f;

    [Tooltip("移动 Sweep 与推出重叠时预留的皮肤宽度（米）。")]
    public float holdCollisionSkin = 0.02f;

    [Tooltip("\u6293\u53D6\u540E\u662F\u5426\u7F13\u6162\u6536\u62DB\u5230 holdDistance\u3002")]
    public bool easeToHoldDistance = false;

    [Tooltip("\u6536\u62DB\u5230 holdDistance \u7684\u901F\u5EA6\uFF08\u4EC5 easeToHoldDistance \u5F00\u542F\u65F6\uFF09\u3002")]
    public float holdDistanceEaseSpeed = 2f;

    public float grabHoldThreshold = 0.2f;

    public float throwForce = 8f;

    [Tooltip("开启后，滚轮在持物时旋转物体；关闭则由手部动画处理滚轮（双手上下交错）。")]
    public bool scrollWheelRotatesObject = false;

    public float rotateSpeed = 360f;

    public bool IsHoldingObject => grabbedObject != null;
    public bool IsGrabCharging => leftPressedCandidate && grabbedObject == null;
    public Transform HoldPoint => holdPoint;

    public event Action<GameObject> Grabbed;
    public event Action Released;
    public event Action Thrown;
    public event Action<float> ScrollWheel;

    [Header("物品描边")]
    [Tooltip("可交互物品默认描边颜色（白）。")]
    public Color defaultOutlineColor = Color.white;

    [Tooltip("玩家抓取物品时的高亮描边颜色（黄）。")]
    public Color heldOutlineColor = new Color(1f, 0.85f, 0.1f, 1f);

    [Tooltip("描边宽度（世界空间米，0.005 ≈ 5mm）。")]
    [Range(0f, 0.05f)] public float outlineWidthMeters = 0.005f;

    [Tooltip("能拆解物品（Composite / Cuttable / 含 Screw 子件）的加粗描边宽度（米）。")]
    [Range(0f, 0.05f)] public float decomposableOutlineWidthMeters = 0.012f;

    Transform holdPoint;
    GameObject grabbedObject;
    Rigidbody grabbedRb;
    bool grabbedWasKinematic;
    bool grabbedUseGravity;
    RigidbodyInterpolation grabbedInterpolation;
    RigidbodyConstraints grabbedOriginalConstraints;
    CollisionDetectionMode grabbedCollisionDetection;

    GameObject aimedObject;
    RaycastHit aimHit;

    float leftPressTime;
    bool leftPressedCandidate;
    RaycastHit candidateHit;

    float currentHoldDistance;
    Vector3 grabLocalOffset;
    Vector3 grabFollowVelocity;
    float _lastSummonTime = -999f;

    Coroutine _hookRoutine;
    LineRenderer _hookLine;
    bool _hookActive;

    readonly List<(Collider held, Collider player)> _ignoredPlayerCollisions = new List<(Collider, Collider)>();

    ItemInfoWorldUI itemInfoUI;

    void Start()
    {
        if (cameraTransform == null)
        {
            var cam = GetComponentInChildren<Camera>();
            if (cam != null) cameraTransform = cam.transform;
        }

        itemInfoUI = GetComponent<ItemInfoWorldUI>();
        if (itemInfoUI == null)
            itemInfoUI = gameObject.AddComponent<ItemInfoWorldUI>();
        if (cameraTransform != null)
            itemInfoUI.Initialize(cameraTransform.GetComponent<Camera>());

        holdPoint = new GameObject("HoldPoint").transform;
        if (cameraTransform != null)
            holdPoint.SetParent(cameraTransform, false);
        holdPoint.localPosition = Vector3.forward * holdDistance;
        holdPoint.localRotation = Quaternion.identity;

        if (LevelManager.Instance != null)
            LevelManager.Instance.LevelGameplayStarted += OnLevelGameplayStarted;

        ItemOutlineSystem.DefaultColor            = defaultOutlineColor;
        ItemOutlineSystem.HeldColor               = heldOutlineColor;
        ItemOutlineSystem.DefaultWidthMeters      = outlineWidthMeters;
        ItemOutlineSystem.DecomposableWidthMeters = decomposableOutlineWidthMeters;
        ItemOutlineSystem.ScanScene();

        Grabbed  += OnGrabbedForOutline;
        Released += OnReleasedForOutline;
        Thrown   += OnReleasedForOutline;
    }

    void OnGrabbedForOutline(GameObject go) => ItemOutlineSystem.SetHeld(go);
    void OnReleasedForOutline()            => ItemOutlineSystem.ClearHeld();

    void Update()
    {
        if (cameraTransform == null) return;

        if (!GameplayInputGate.IsBlocked)
        {
            UpdateHoldPointDistance();
            UpdateAimTarget();
            HandleLeftPressLogic();
            HandleThrow();
            HandleScrollRotate();
            HandleInspectionInput();
            HandleSummonInput();
        }

        RefreshItemInfoUI();
    }

    void OnLevelGameplayStarted()
    {
        ForceRefreshInteractionVisuals();
    }

    /// <summary>审视结束等时机立即刷新交互视觉（审视期间 Update 被门控跳过）。</summary>
    public void ForceRefreshInteractionVisuals()
    {
        if (cameraTransform == null) return;
        UpdateAimTarget();
        RefreshInteractionVisuals();
    }

    void HandleInspectionInput()
    {
        if (_hookActive) return;
        if (grabbedObject == null) return;
        var insp = grabbedObject.GetComponent<InspectableItem>();
        if (insp == null) return;
        if (!Input.GetKeyDown(insp.inspectKey)) return;
        if (EndDayInteractable.ShouldConsumeInteractKey) return;
        // 干扰（如 TVStaticOverlay）正在显示时，把 E 让给“取消干扰”计数，
        // 避免一边按 E 一边意外进入审视。
        if (TVStaticOverlay.IsActive) return;
        InspectionView.Instance.BeginInspection(grabbedObject, this);
    }

    void FixedUpdate()
    {
        if (GameplayInputGate.IsBlocked) return;
        if (_hookActive) return; // 钩爪 coroutine 接管 grabbedRb 的位置
        if (grabbedRb == null || holdPoint == null) return;
        UpdateGrabbedTransformPhysics();
    }

    void UpdateHoldPointDistance()
    {
        if (holdPoint == null || cameraTransform == null) return;

        if (grabbedObject != null)
        {
            if (easeToHoldDistance)
            {
                currentHoldDistance = Mathf.MoveTowards(
                    currentHoldDistance,
                    holdDistance,
                    holdDistanceEaseSpeed * Time.deltaTime);
            }
        }
        else
        {
            currentHoldDistance = holdDistance;
        }

        currentHoldDistance = Mathf.Clamp(currentHoldDistance, minHoldDistance, maxGrabDistance);

        float holdDistanceForPoint = currentHoldDistance;
        if (grabbedObject != null && clampHoldPointAgainstWalls)
            holdDistanceForPoint = ClampHoldDistanceAgainstGeometry(currentHoldDistance);

        holdPoint.localPosition = Vector3.forward * holdDistanceForPoint;
    }

    float ClampHoldDistanceAgainstGeometry(float desiredDistance)
    {
        if (cameraTransform == null) return desiredDistance;

        Vector3 origin = cameraTransform.position;
        Vector3 dir = cameraTransform.forward;
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            dir,
            desiredDistance,
            holdCollisionMask,
            QueryTriggerInteraction.Ignore);

        float closest = desiredDistance;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null || IsHeldCollider(col)) continue;

            closest = Mathf.Min(closest, hits[i].distance);
        }

        if (closest < desiredDistance)
            return Mathf.Max(minHoldDistance, closest - holdWallPadding);

        return desiredDistance;
    }

    bool IsHeldCollider(Collider col)
    {
        if (col == null || grabbedObject == null) return false;
        Transform t = col.transform;
        return t == grabbedObject.transform || t.IsChildOf(grabbedObject.transform);
    }

    /// <summary>
    /// 外部脚本（如 Screwdriver）调用，把持物点沿相机前方推近或拉远。delta > 0 表示更远。
    /// </summary>
    public void AdjustHoldDistance(float delta)
    {
        if (holdPoint == null) return;
        currentHoldDistance = Mathf.Clamp(currentHoldDistance + delta, minHoldDistance, maxGrabDistance);

        float holdDistanceForPoint = currentHoldDistance;
        if (grabbedObject != null && clampHoldPointAgainstWalls)
            holdDistanceForPoint = ClampHoldDistanceAgainstGeometry(currentHoldDistance);

        holdPoint.localPosition = Vector3.forward * holdDistanceForPoint;
    }

    void UpdateGrabbedTransformPhysics()
    {
        Vector3 targetPos = holdPoint.TransformPoint(grabLocalOffset);
        Quaternion targetRot = holdPoint.rotation;

        if (grabFollowSmoothTime > 0f)
        {
            targetPos = Vector3.SmoothDamp(
                grabbedRb.position,
                targetPos,
                ref grabFollowVelocity,
                grabFollowSmoothTime,
                Mathf.Infinity,
                Time.fixedDeltaTime);

            float rotLerp = 1f - Mathf.Exp(-Time.fixedDeltaTime / grabFollowSmoothTime);
            targetRot = Quaternion.Slerp(grabbedRb.rotation, targetRot, rotLerp);
        }

        Vector3 safePos = ComputeSweptHeldPosition(grabbedRb, targetPos);
        grabbedRb.MovePosition(safePos);
        grabbedRb.MoveRotation(targetRot);
        ResolveRigidbodyPenetration(grabbedRb);

        grabbedRb.velocity = Vector3.zero;
        grabbedRb.angularVelocity = Vector3.zero;
    }

    Vector3 ComputeSweptHeldPosition(Rigidbody rb, Vector3 targetPos)
    {
        Vector3 start = rb.position;
        Vector3 delta = targetPos - start;
        float distance = delta.magnitude;
        if (distance <= 1e-6f) return targetPos;

        Vector3 dir = delta / distance;
        float allowedDistance = distance;

        Collider[] ownColliders = rb.GetComponentsInChildren<Collider>(false);
        bool hasSolidCollider = false;
        for (int i = 0; i < ownColliders.Length; i++)
        {
            if (!TryGetColliderSweepDistance(ownColliders[i], dir, distance, out float colliderAllowed))
                continue;

            hasSolidCollider = true;
            allowedDistance = Mathf.Min(allowedDistance, colliderAllowed);
        }

        if (!hasSolidCollider
            && rb.SweepTest(dir, out RaycastHit sweepHit, distance, QueryTriggerInteraction.Ignore)
            && !IsHeldCollider(sweepHit.collider))
        {
            allowedDistance = Mathf.Min(allowedDistance, sweepHit.distance);
        }

        allowedDistance = Mathf.Max(0f, allowedDistance - holdCollisionSkin);
        return start + dir * allowedDistance;
    }

    bool TryGetColliderSweepDistance(Collider col, Vector3 dir, float maxDistance, out float allowedDistance)
    {
        allowedDistance = maxDistance;
        if (col == null || !col.enabled || col.isTrigger) return false;

        QueryTriggerInteraction triggerQuery = QueryTriggerInteraction.Ignore;

        switch (col)
        {
            case SphereCollider sphere:
            {
                Transform t = sphere.transform;
                Vector3 center = t.TransformPoint(sphere.center);
                float radius = sphere.radius * GetMaxAbsComponent(t.lossyScale);
                if (Physics.SphereCast(center, radius, dir, out RaycastHit hit, maxDistance, holdCollisionMask, triggerQuery)
                    && !IsHeldCollider(hit.collider))
                {
                    allowedDistance = hit.distance;
                }
                return true;
            }
            case CapsuleCollider capsule:
            {
                if (TryGetCapsuleWorld(capsule, out Vector3 p1, out Vector3 p2, out float radius)
                    && Physics.CapsuleCast(p1, p2, radius, dir, out RaycastHit hit, maxDistance, holdCollisionMask, triggerQuery)
                    && !IsHeldCollider(hit.collider))
                {
                    allowedDistance = hit.distance;
                }
                return true;
            }
            case BoxCollider box:
            {
                Transform t = box.transform;
                Vector3 center = t.TransformPoint(box.center);
                Vector3 halfExtents = Vector3.Scale(box.size * 0.5f, t.lossyScale);
                if (Physics.BoxCast(center, halfExtents, dir, out RaycastHit hit, t.rotation, maxDistance, holdCollisionMask, triggerQuery)
                    && !IsHeldCollider(hit.collider))
                {
                    allowedDistance = hit.distance;
                }
                return true;
            }
            default:
            {
                Bounds bounds = col.bounds;
                Vector3 extents = bounds.extents;
                if (extents.sqrMagnitude <= 1e-8f) return false;

                if (Physics.BoxCast(bounds.center, extents, dir, out RaycastHit hit, col.transform.rotation, maxDistance, holdCollisionMask, triggerQuery)
                    && !IsHeldCollider(hit.collider))
                {
                    allowedDistance = hit.distance;
                }
                return true;
            }
        }
    }

    static bool TryGetCapsuleWorld(CapsuleCollider capsule, out Vector3 point1, out Vector3 point2, out float radius)
    {
        point1 = point2 = Vector3.zero;
        radius = 0f;
        if (capsule == null) return false;

        Transform t = capsule.transform;
        float scale = GetMaxAbsComponent(t.lossyScale);
        radius = capsule.radius * scale;
        float height = Mathf.Max(capsule.height * scale, radius * 2f);
        float halfHeight = Mathf.Max(0f, height * 0.5f - radius);

        Vector3 localDir = capsule.direction switch
        {
            0 => Vector3.right,
            2 => Vector3.forward,
            _ => Vector3.up,
        };

        Vector3 worldDir = t.TransformDirection(localDir).normalized;
        Vector3 center = t.TransformPoint(capsule.center);
        point1 = center - worldDir * halfHeight;
        point2 = center + worldDir * halfHeight;
        return true;
    }

    static float GetMaxAbsComponent(Vector3 v) =>
        Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    void UpdateAimTarget()
    {
        aimedObject = null;
        aimHit = new RaycastHit();

        if (cameraTransform == null) return;

        Camera cam = cameraTransform.GetComponent<Camera>();
        Ray ray = cam != null
            ? cam.ScreenPointToRay(new Vector2(Screen.width / 2f, Screen.height / 2f))
            : new Ray(cameraTransform.position, cameraTransform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, maxGrabDistance, interactMask))
        {
            GameObject interactTarget = ResolveInteractableRoot(hit.collider);
            if (interactTarget != null && (grabbedObject == null || interactTarget != grabbedObject))
            {
                aimedObject = interactTarget;
                aimHit = hit;
            }
        }

    }

    void RefreshInteractionVisuals()
    {
        RefreshItemInfoUI();
    }

    ItemInformation _lastShownItemInfo;
    Transform _lastShownItemAnchor;

    void RefreshItemInfoUI()
    {
        if (itemInfoUI == null) return;

        if (aimedObject != null && grabbedObject == null)
        {
            var info = aimedObject.GetComponent<ItemInformation>();
            if (info == null)
                info = aimedObject.GetComponentInParent<ItemInformation>();
            if (info != null)
            {
                Transform anchor = GetItemRoot(info).transform;
                if (_lastShownItemInfo != info || _lastShownItemAnchor != anchor)
                {
                    itemInfoUI.Show(info, anchor);
                    _lastShownItemInfo = info;
                    _lastShownItemAnchor = anchor;
                }
            }
            else
            {
                itemInfoUI.Hide();
                _lastShownItemInfo = null;
                _lastShownItemAnchor = null;
            }
        }
        else
        {
            itemInfoUI.Hide();
            _lastShownItemInfo = null;
            _lastShownItemAnchor = null;
        }
    }

    static GameObject GetItemRoot(ItemInformation info)
    {
        if (info == null) return null;

        // 如果该 info 所在物体属于某个 InspectableItem 的层级（无论是 InspectableItem 本身还是
        // 其某个 detachable 子物体），统一返回 InspectableItem 所挂 Rigidbody 的节点，
        // 这样准星对子部件时也会把"整个可审视组合"作为抓取目标。
        InspectableItem insp = info.GetComponentInParent<InspectableItem>();
        if (insp != null)
        {
            Rigidbody iRb = insp.GetComponent<Rigidbody>();
            if (iRb == null) iRb = insp.GetComponentInParent<Rigidbody>();
            return iRb != null ? iRb.gameObject : insp.gameObject;
        }

        Rigidbody rb = info.GetComponentInParent<Rigidbody>();
        return rb != null ? rb.gameObject : info.gameObject;
    }

    static GameObject ResolveInteractableRoot(Collider collider)
    {
        if (collider == null) return null;

        // 优先按 InspectableItem 解析：碰到 detachable 子件或父体的 collider 都视为整体。
        InspectableItem insp = collider.GetComponentInParent<InspectableItem>();
        if (insp != null)
        {
            Rigidbody iRb = insp.GetComponent<Rigidbody>();
            if (iRb == null) iRb = insp.GetComponentInParent<Rigidbody>();
            return iRb != null ? iRb.gameObject : insp.gameObject;
        }

        var info = collider.GetComponentInParent<ItemInformation>();
        if (info != null)
            return GetItemRoot(info);

        Rigidbody hitRb = collider.attachedRigidbody != null
            ? collider.attachedRigidbody
            : collider.GetComponentInParent<Rigidbody>();
        if (hitRb != null)
            return hitRb.gameObject;

        if (collider.CompareTag("Screw") || collider.CompareTag("Screwdriver") || collider.CompareTag("Knife"))
            return collider.gameObject;

        return null;
    }

    void OnGUI()
    {
        if (cameraTransform == null) return;
        if (GameplayInputGate.IsBlocked) return;

        Color c = (aimedObject != null) ? crosshairAimColor : crosshairDefaultColor;

        float size = GameDisplaySettings.ScaleDesignPixels(crosshairSize);

        if (crosshairTexture == null)
        {
            DrawCrosshair(c, size);
            return;
        }

        GUI.color = c;
        float px = (Screen.width - size) / 2f;
        float py = (Screen.height - size) / 2f;
        GUI.DrawTexture(new Rect(px, py, size, size), crosshairTexture);
        GUI.color = Color.white;
    }

    static void DrawCrosshair(Color color, float size)
    {
        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;
        float armLength = size * 0.45f;
        float gap = size * 0.12f;
        float thickness = Mathf.Max(2f, size * 0.06f);
        float halfThickness = thickness * 0.5f;

        Color old = GUI.color;
        GUI.color = color;
        Texture2D tex = Texture2D.whiteTexture;

        GUI.DrawTexture(new Rect(cx - gap - armLength, cy - halfThickness, armLength, thickness), tex);
        GUI.DrawTexture(new Rect(cx + gap, cy - halfThickness, armLength, thickness), tex);
        GUI.DrawTexture(new Rect(cx - halfThickness, cy - gap - armLength, thickness, armLength), tex);
        GUI.DrawTexture(new Rect(cx - halfThickness, cy + gap, thickness, armLength), tex);

        GUI.color = old;
    }

    void HandleLeftPressLogic()
    {
        if (cameraTransform == null) return;
        if (_hookActive) return;

        if (Input.GetMouseButtonDown(0))
        {
            leftPressTime = 0f;
            leftPressedCandidate = false;

            if (GetInteractableRigidbody(aimedObject) != null)
            {
                leftPressedCandidate = true;
                candidateHit = aimHit;
            }
            else
            {
                Camera cam = cameraTransform.GetComponent<Camera>();
                Ray ray = cam != null
                    ? cam.ScreenPointToRay(new Vector2(Screen.width / 2f, Screen.height / 2f))
                    : new Ray(cameraTransform.position, cameraTransform.forward);

                if (Physics.Raycast(ray, out candidateHit, maxGrabDistance, interactMask))
                {
                    if (GetInteractableRigidbody(candidateHit.collider.gameObject) != null)
                        leftPressedCandidate = true;
                }
            }
        }

        if (Input.GetMouseButton(0) && leftPressedCandidate && grabbedObject == null)
        {
            leftPressTime += Time.deltaTime;
            if (leftPressTime >= grabHoldThreshold)
            {
                GameObject grabTarget = GetGrabTargetFromHit(candidateHit);
                if (grabTarget != null && grabTarget == aimedObject)
                    TryGrab(grabTarget);
                else if (grabTarget != null && aimedObject == null)
                    TryGrab(grabTarget);
                leftPressedCandidate = false;
            }
        }

        if (Input.GetMouseButtonUp(0))
        {
            leftPressedCandidate = false;
            leftPressTime = 0f;
            if (grabbedObject != null)
                Drop();
        }
    }

    void TryGrab(GameObject target)
    {
        if (target == null) return;
        var rb = GetInteractableRigidbody(target);
        if (rb == null) return;

        if (aimedObject != null && aimedObject != rb.gameObject)
            return;

        if (holdPoint == null)
        {
            Debug.LogWarning("HoldPoint missing, cannot grab.");
            return;
        }

        grabbedObject = rb.gameObject;
        grabbedRb = rb;
        grabbedWasKinematic = rb.isKinematic;
        grabbedUseGravity = rb.useGravity;
        grabbedInterpolation = rb.interpolation;
        grabbedOriginalConstraints = rb.constraints;
        grabbedCollisionDetection = rb.collisionDetectionMode;

        SuspendFromConveyorBelts(rb);

        // 保持 Dynamic + MovePosition 跟随准星；勿冻结 Position（会锁死在世界坐标无法跟随）。
        // 必须先切到 Dynamic，再清零速度（kinematic 上设置 velocity 会报错）。
        rb.isKinematic = false;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.useGravity = false;
        rb.detectCollisions = true;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        IgnorePlayerCollisionsWhileHeld(rb);

        currentHoldDistance = GetGrabDistance(rb);
        grabFollowVelocity = Vector3.zero;
        if (holdPoint != null)
        {
            holdPoint.localPosition = Vector3.forward * currentHoldDistance;
            holdPoint.rotation = rb.rotation;
            grabLocalOffset = holdPoint.InverseTransformPoint(rb.position);
        }

        if (itemInfoUI != null) itemInfoUI.Hide();
        aimedObject = null;
        Grabbed?.Invoke(grabbedObject);
    }

    float GetGrabDistance(Rigidbody rb)
    {
        if (cameraTransform == null || rb == null) return holdDistance;
        float alongView = Vector3.Dot(rb.position - cameraTransform.position, cameraTransform.forward);
        return Mathf.Clamp(alongView, minHoldDistance, maxGrabDistance);
    }

    void Drop()
    {
        ReleaseGrabbedObject(false, Vector3.zero);
    }

    void HandleThrow()
    {
        if (_hookActive) return;
        if (grabbedObject == null || cameraTransform == null) return;
        if (Input.GetMouseButtonDown(1))
        {
            Vector3 dir = cameraTransform.forward.normalized;
            ReleaseGrabbedObject(true, dir);
        }
    }

    // ------------------------------------------------------------------
    // 钩爪：按 F 发射线缆把准星瞄准的物体勾回到玩家身前掉落
    // ------------------------------------------------------------------

    void HandleSummonInput()
    {
        if (cameraTransform == null) return;
        if (!Input.GetKeyDown(summonKey)) return;
        if (_hookActive) return;
        if (grabbedObject != null) return;
        if (TVStaticOverlay.IsActive) return;
        if (EndDayInteractable.ShouldConsumeInteractKey) return;
        if (Time.unscaledTime - _lastSummonTime < summonCooldown) return;

        TrySummonAimedTarget();
    }

    /// <summary>
    /// 朝准星方向做一次无视 maxGrabDistance 的射线，找到第一个带 Rigidbody 的目标后
    /// 启动钩爪流程：飞出 → 钩住 → 回拉 → 落地。
    /// </summary>
    public bool TrySummonAimedTarget()
    {
        if (cameraTransform == null) return false;
        if (_hookActive || grabbedObject != null) return false;

        Camera cam = cameraTransform.GetComponent<Camera>();
        Ray ray = cam != null
            ? cam.ScreenPointToRay(new Vector2(Screen.width / 2f, Screen.height / 2f))
            : new Ray(cameraTransform.position, cameraTransform.forward);

        float dist = summonMaxDistance > 0f ? summonMaxDistance : Mathf.Infinity;
        if (!Physics.Raycast(ray, out RaycastHit hit, dist, summonMask, QueryTriggerInteraction.Ignore))
            return false;

        GameObject target = ResolveInteractableRoot(hit.collider);
        Rigidbody rb = GetInteractableRigidbody(target);
        if (rb == null) return false;
        if (rb.transform == transform || rb.transform.IsChildOf(transform)) return false;

        _lastSummonTime = Time.unscaledTime;
        if (_hookRoutine != null) StopCoroutine(_hookRoutine);
        _hookRoutine = StartCoroutine(HookSequence(rb));
        return true;
    }

    IEnumerator HookSequence(Rigidbody target)
    {
        _hookActive = true;
        EnsureHookLine();
        _hookLine.enabled = true;

        // ---- Phase 1: 钩爪飞出 ----
        Vector3 muzzle = GetHookMuzzleWorld();
        Vector3 currentTarget = target != null ? target.worldCenterOfMass : muzzle;
        float flyDistance = Vector3.Distance(muzzle, currentTarget);
        float flyDuration = Mathf.Max(0.04f, flyDistance / Mathf.Max(0.1f, hookFlySpeed));

        float t = 0f;
        while (t < flyDuration)
        {
            if (target == null) { FinishHook(); yield break; }

            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / flyDuration);
            muzzle = GetHookMuzzleWorld();
            currentTarget = target.worldCenterOfMass;
            Vector3 tip = Vector3.Lerp(muzzle, currentTarget, a);
            UpdateHookLine(muzzle, tip);
            yield return null;
        }

        if (target == null) { FinishHook(); yield break; }

        // ---- Phase 2: 钩住目标 ----
        // 通过 TryGrab 让 WorkTable / EmbeddedTrashItem / 传送带等监听者按正常流程响应。
        aimedObject = target.gameObject;
        TryGrab(target.gameObject);
        if (grabbedRb != target)
        {
            FinishHook();
            yield break;
        }

        PromoteHeldItemPermanently();
        if (hookPassThroughObstacles)
            grabbedRb.detectCollisions = false;

        // ---- Phase 3: 沿线缆回拉到掉落点 ----
        Vector3 startPos = grabbedRb.position;
        Vector3 ComputeDropPos() =>
            cameraTransform.position + cameraTransform.forward * Mathf.Max(minHoldDistance, summonDropDistance);
        float pullDistance = Vector3.Distance(startPos, ComputeDropPos());
        float pullDuration = Mathf.Max(0.08f, pullDistance / Mathf.Max(0.1f, hookPullSpeed));

        float pt = 0f;
        while (pt < pullDuration)
        {
            if (grabbedRb == null) { FinishHook(); yield break; }

            pt += Time.deltaTime;
            float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(pt / pullDuration));
            Vector3 pos = Vector3.Lerp(startPos, ComputeDropPos(), a);
            grabbedRb.position = pos;
            grabbedRb.velocity = Vector3.zero;
            grabbedRb.angularVelocity = Vector3.zero;
            UpdateHookLine(GetHookMuzzleWorld(), pos);
            yield return null;
        }

        // ---- Phase 4: 落地（恢复碰撞 + 释放） ----
        if (grabbedRb != null)
        {
            if (hookPassThroughObstacles)
                grabbedRb.detectCollisions = true;
            Physics.SyncTransforms();
        }

        ReleaseGrabbedObject(false, Vector3.zero);

        // ---- Phase 5: 线缆收回 ----
        Vector3 retractStart = GetHookMuzzleWorld();
        Vector3 retractEnd = _hookLine != null && _hookLine.positionCount >= 2
            ? _hookLine.GetPosition(1)
            : retractStart;
        float rt = 0f;
        while (rt < hookRetractDuration)
        {
            rt += Time.deltaTime;
            float a = Mathf.Clamp01(rt / hookRetractDuration);
            Vector3 muz = GetHookMuzzleWorld();
            Vector3 tip = Vector3.Lerp(retractEnd, muz, a);
            UpdateHookLine(muz, tip);
            yield return null;
        }

        FinishHook();
    }

    void FinishHook()
    {
        if (_hookLine != null)
            _hookLine.enabled = false;
        _hookActive = false;
        _hookRoutine = null;
    }

    void EnsureHookLine()
    {
        if (_hookLine != null) return;

        var go = new GameObject("HookLine");
        go.transform.SetParent(transform, false);
        go.hideFlags = HideFlags.DontSave;

        _hookLine = go.AddComponent<LineRenderer>();
        _hookLine.useWorldSpace = true;
        _hookLine.positionCount = 2;
        _hookLine.numCapVertices = 4;
        _hookLine.numCornerVertices = 0;
        _hookLine.startWidth = hookLineWidth;
        _hookLine.endWidth = hookLineWidth * 1.3f;
        _hookLine.alignment = LineAlignment.View;
        _hookLine.textureMode = LineTextureMode.Stretch;
        _hookLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _hookLine.receiveShadows = false;
        _hookLine.material = GetHookLineMaterial();
        _hookLine.startColor = hookLineColor;
        _hookLine.endColor = hookLineColor;
        _hookLine.enabled = false;
    }

    Material GetHookLineMaterial()
    {
        if (hookLineMaterial != null) return hookLineMaterial;

        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        if (sh == null) return null;

        hookLineMaterial = new Material(sh);
        if (hookLineMaterial.HasProperty("_Color"))
            hookLineMaterial.SetColor("_Color", hookLineColor);
        else if (hookLineMaterial.HasProperty("_BaseColor"))
            hookLineMaterial.SetColor("_BaseColor", hookLineColor);
        return hookLineMaterial;
    }

    void UpdateHookLine(Vector3 from, Vector3 to)
    {
        if (_hookLine == null) return;
        _hookLine.SetPosition(0, from);
        _hookLine.SetPosition(1, to);
    }

    Vector3 GetHookMuzzleWorld()
    {
        if (hookOrigin != null) return hookOrigin.position;
        if (cameraTransform != null) return cameraTransform.TransformPoint(hookMuzzleLocalOffset);
        return transform.position;
    }

    /// <summary>
    /// 外部脚本（如 DangerousGoodsBehavior）在物品即将被销毁前调用，先安全释放。
    /// </summary>
    public void ForceReleaseIfHolding(GameObject target)
    {
        if (grabbedObject == null || target == null) return;
        if (grabbedObject != target) return;
        ReleaseGrabbedObject(false, Vector3.zero);
    }

    /// <summary>
    /// 当当前握持的物品需要"被抓取后永久脱离 Kinematic / 锁定状态"时调用（例如 TrashHeap 上嵌入的垃圾）。
    /// 改写抓取时缓存的物理状态，让 <see cref="ReleaseGrabbedObject"/> 不再把物体还原成 Kinematic。
    /// 调用方需保证此时确实正握住一个物体，否则本调用无效。
    /// </summary>
    public void PromoteHeldItemPermanently()
    {
        if (grabbedRb == null) return;
        grabbedWasKinematic = false;
        grabbedUseGravity = true;
        grabbedOriginalConstraints = RigidbodyConstraints.None;
        grabbedInterpolation = RigidbodyInterpolation.Interpolate;
        grabbedCollisionDetection = CollisionDetectionMode.ContinuousDynamic;
    }

    void ReleaseGrabbedObject(bool applyThrow, Vector3 throwDirection)
    {
        if (grabbedRb == null)
        {
            grabbedObject = null;
            return;
        }

        Rigidbody releasedRb = grabbedRb;
        bool wasKinematic = grabbedWasKinematic;
        bool useGravity = grabbedUseGravity;
        RigidbodyInterpolation interpolation = grabbedInterpolation;
        RigidbodyConstraints constraints = grabbedOriginalConstraints;
        CollisionDetectionMode collisionDetection = grabbedCollisionDetection;

        ResolveRigidbodyPenetration(releasedRb);
        RestorePlayerCollisionsWhileHeld();

        // 投掷时需要 Dynamic 才能施加力；松手后由 Released/Thrown 监听方（如 Knife）决定是否回到 Kinematic。
        if (applyThrow && wasKinematic)
            releasedRb.isKinematic = false;
        else
            releasedRb.isKinematic = wasKinematic;

        releasedRb.useGravity = useGravity;
        releasedRb.detectCollisions = true;
        releasedRb.interpolation = interpolation;
        releasedRb.constraints = constraints;
        releasedRb.collisionDetectionMode = wasKinematic
            ? collisionDetection
            : CollisionDetectionMode.ContinuousDynamic;

        if (applyThrow)
        {
            releasedRb.AddForce(throwDirection * throwForce, ForceMode.VelocityChange);
            Thrown?.Invoke();
        }
        else
        {
            Released?.Invoke();
        }

        if (holdPoint != null)
            holdPoint.localRotation = Quaternion.identity;

        grabbedObject = null;
        grabbedRb = null;
        grabFollowVelocity = Vector3.zero;
        UnsuspendFromConveyorBelts(releasedRb);
    }

    void IgnorePlayerCollisionsWhileHeld(Rigidbody rb)
    {
        RestorePlayerCollisionsWhileHeld();
        if (rb == null) return;

        Collider[] heldColliders = rb.GetComponentsInChildren<Collider>(false);
        Collider[] playerColliders = GetComponentsInChildren<Collider>(false);
        if (heldColliders.Length == 0 || playerColliders.Length == 0) return;

        for (int i = 0; i < heldColliders.Length; i++)
        {
            Collider held = heldColliders[i];
            if (held == null || !held.enabled || held.isTrigger) continue;

            for (int j = 0; j < playerColliders.Length; j++)
            {
                Collider player = playerColliders[j];
                if (player == null || !player.enabled) continue;

                Physics.IgnoreCollision(held, player, true);
                _ignoredPlayerCollisions.Add((held, player));
            }
        }
    }

    void RestorePlayerCollisionsWhileHeld()
    {
        for (int i = 0; i < _ignoredPlayerCollisions.Count; i++)
        {
            (Collider held, Collider player) = _ignoredPlayerCollisions[i];
            if (held != null && player != null)
                Physics.IgnoreCollision(held, player, false);
        }

        _ignoredPlayerCollisions.Clear();
    }

    /// <summary>
    /// 把刚体从重叠的碰撞体中推出（持物每帧 / 松手前调用）。
    /// </summary>
    void ResolveRigidbodyPenetration(Rigidbody rb)
    {
        if (rb == null) return;

        Collider[] ownColliders = rb.GetComponentsInChildren<Collider>(false);
        if (ownColliders.Length == 0) return;

        const int maxIterations = 8;
        float skin = holdCollisionSkin;

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            bool resolvedAny = false;

            for (int i = 0; i < ownColliders.Length; i++)
            {
                Collider own = ownColliders[i];
                if (own == null || !own.enabled || own.isTrigger) continue;

                Bounds bounds = own.bounds;
                Collider[] overlaps = Physics.OverlapBox(
                    bounds.center,
                    bounds.extents,
                    own.transform.rotation,
                    holdCollisionMask,
                    QueryTriggerInteraction.Ignore);

                for (int j = 0; j < overlaps.Length; j++)
                {
                    Collider other = overlaps[j];
                    if (other == null || other.isTrigger) continue;
                    if (other.transform.IsChildOf(rb.transform)) continue;
                    if (IsHeldCollider(other)) continue;

                    if (!Physics.ComputePenetration(
                            own, own.transform.position, own.transform.rotation,
                            other, other.transform.position, other.transform.rotation,
                            out Vector3 direction, out float distance))
                        continue;

                    if (distance <= 1e-5f) continue;

                    rb.position += direction * (distance + skin);
                    resolvedAny = true;
                }
            }

            if (!resolvedAny) break;
        }

        Physics.SyncTransforms();
    }

    void HandleScrollRotate()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) <= 1e-4f) return;

        ScrollWheel?.Invoke(scroll);

        if (!scrollWheelRotatesObject || grabbedObject == null || holdPoint == null || cameraTransform == null)
            return;

        holdPoint.Rotate(cameraTransform.forward, scroll * rotateSpeed, Space.World);
    }

    void OnDisable()
    {
        Grabbed  -= OnGrabbedForOutline;
        Released -= OnReleasedForOutline;
        Thrown   -= OnReleasedForOutline;
        ItemOutlineSystem.ClearHeld();

        if (LevelManager.Instance != null)
            LevelManager.Instance.LevelGameplayStarted -= OnLevelGameplayStarted;

        if (_hookRoutine != null)
        {
            StopCoroutine(_hookRoutine);
            _hookRoutine = null;
        }
        _hookActive = false;
        if (_hookLine != null)
            _hookLine.enabled = false;

        if (grabbedRb != null)
            ReleaseGrabbedObject(false, Vector3.zero);

        if (itemInfoUI != null) itemInfoUI.Hide();
    }

    static Rigidbody GetInteractableRigidbody(GameObject go)
    {
        if (go == null) return null;
        return go.GetComponent<Rigidbody>() ?? go.GetComponentInParent<Rigidbody>();
    }

    static GameObject GetGrabTargetFromHit(RaycastHit hit)
    {
        if (hit.collider == null) return null;

        GameObject root = ResolveInteractableRoot(hit.collider);
        if (root == null) return null;

        Rigidbody rb = GetInteractableRigidbody(root);
        return rb != null ? rb.gameObject : null;
    }

    static void SuspendFromConveyorBelts(Rigidbody rb)
    {
        if (rb == null) return;
        var belts = FindObjectsOfType<ConveyorBelt>();
        for (int i = 0; i < belts.Length; i++)
            belts[i].SuspendRigidbody(rb);
    }

    static void UnsuspendFromConveyorBelts(Rigidbody rb)
    {
        if (rb == null) return;
        var belts = FindObjectsOfType<ConveyorBelt>();
        for (int i = 0; i < belts.Length; i++)
            belts[i].UnsuspendRigidbody(rb);
    }
}

using UnityEngine;

/// <summary>
/// 螺丝刀。挂在带 Rigidbody（默认 isKinematic）和 Collider 的螺丝刀物体上，必须带 Tag "Screwdriver"。
///
/// 行为：
/// 1. 只有当关联的 WorkTable 上吸附了物品时，螺丝刀的 collider 才会启用，CharacterInteraction 才能 raycast 命中并长按拾取。
/// 2. 拾取后，螺丝刀的 Tip 朝向某个 Tag 为 "Screw" 的 collider 且距离/角度满足条件时，自动吸附到该 Screw 上。
/// 3. 吸附期间，玩家通过滚轮顺时针累计旋转 720° 即可拆下 Screw（Screw 被解除 parent，加上 Rigidbody+重力，碰到桌面销毁）。
/// 4. 玩家松开（左键松开 / 右键扔出）后，螺丝刀自动平滑归位到启动时的位置/旋转（或指定的 Home Point）。
/// </summary>
[DefaultExecutionOrder(200)]
public class Screwdriver : MonoBehaviour
{
    [Header("归位")]
    [Tooltip("螺丝刀归位的位置/旋转参考点。留空则使用启动时的世界位置与旋转。")]
    public Transform homePoint;

    [Tooltip("归位平滑时间。")]
    public float returnSmoothTime = 0.15f;

    [Header("交互门控")]
    [Tooltip("关联的 WorkTable。只有当其上吸附了物品时螺丝刀才能被拾取。留空则不门控、始终可拾取。")]
    public WorkTable workTable;

    [Header("Tip")]
    [Tooltip("螺丝刀的 Tip 子物体名称。其本地 +Z 方向应朝向螺丝刀尖端。")]
    public string tipChildName = "Tip";

    [Header("Screw 吸附")]
    [Tooltip("从 Tip 向前检测 Screw 的距离。")]
    public float screwDetectDistance = 0.35f;

    [Tooltip("Tip 朝向与 Screw 法线的最大夹角（度）。")]
    public float alignAngleThreshold = 35f;

    [Tooltip("吸附时位置/旋转平滑过渡的时间。")]
    public float lockSnapSmoothTime = 0.06f;

    [Tooltip("被检测为可拆卸的 Screw 必须带的 Tag。")]
    public string screwTag = "Screw";

    [Header("持握姿势")]
    [Tooltip("玩家持有螺丝刀但未吸附 Screw 时，强制摆成 \"Tip 朝向相机前方\" 的姿势。")]
    public bool enforceNaturalHoldPose = true;

    [Tooltip("螺丝刀上充当握把的本地位置（相对 transform.position）。默认 0 = 直接以 transform 自身作为握把点。")]
    public Vector3 gripLocalOffset = Vector3.zero;

    [Tooltip("自然持握姿势的平滑过渡时间。")]
    public float naturalPoseSmoothTime = 0.06f;

    [Header("拆卸条件")]
    [Tooltip("吸附 Screw 后，每单位 Mouse X 拖拽量旋转的角度，正值代表玩家视角下的顺时针。")]
    public float rotateSpeedPerMouseX = 30f;

    [Tooltip("拆下螺丝所需的累积顺时针角度（默认 720° = 两圈）。")]
    public float requiredClockwiseDegrees = 720f;

    [Tooltip("Screw 被拆下时沿原本法线方向的初始速度。")]
    public float ejectSpeed = 1.2f;

    [Tooltip("吸附 Screw 时是否挂起玩家相机鼠标视角。否则鼠标横移会同时转视角。")]
    public bool suspendCameraWhileLocked = true;

    [Header("持物距离")]
    [Tooltip("持有螺丝刀且未锁定 Screw 时，每单位滚轮量改变的持物距离（米）。正值表示向上滚轮拉远。")]
    public float distancePerScroll = 3f;

    Rigidbody rb;
    Transform tip;
    Vector3 tipLocalOffset;
    Quaternion tipLocalRotOffset;
    bool tipResolved;

    CharacterInteraction character;

    Vector3 homePosition;
    Quaternion homeRotation;
    Vector3 returnVelocity;

    bool grabbedNow;
    GameObject lockedScrew;
    Vector3 screwAnchorPos;
    Vector3 screwAnchorNormal;
    Quaternion screwAnchorRot;
    float clockwiseAccum;

    Collider[] cachedColliders;
    bool collidersAllowed = true;

    CharacterMove suspendedMover;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        cachedColliders = GetComponentsInChildren<Collider>(true);
        ResolveTip();
    }

    void ResolveTip()
    {
        if (tipResolved && tip != null) return;

        tip = transform.Find(tipChildName);
        if (tip == null)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name == tipChildName) { tip = t; break; }
            }
        }

        if (tip != null)
        {
            tipLocalOffset = transform.InverseTransformPoint(tip.position);
            tipLocalRotOffset = Quaternion.Inverse(transform.rotation) * tip.rotation;
            tipResolved = true;
        }
    }

    void Start()
    {
        if (homePoint != null)
        {
            homePosition = homePoint.position;
            homeRotation = homePoint.rotation;
        }
        else
        {
            homePosition = transform.position;
            homeRotation = transform.rotation;
        }

        character = FindObjectOfType<CharacterInteraction>();
        if (character != null)
        {
            character.Grabbed += OnGrabbed;
            character.Released += OnReleased;
            character.Thrown += OnThrown;
        }

        EnsureKinematicHomeState();
        UpdatePickabilityGate();
    }

    void OnDestroy()
    {
        BreakScrewLock();
        if (character != null)
        {
            character.Grabbed -= OnGrabbed;
            character.Released -= OnReleased;
            character.Thrown -= OnThrown;
        }
    }

    void EnsureKinematicHomeState()
    {
        if (rb == null) return;
        rb.useGravity = false;
        rb.isKinematic = true;
    }

    // ---------- CharacterInteraction 事件 ----------

    void OnGrabbed(GameObject obj)
    {
        if (obj != gameObject) return;
        grabbedNow = true;
        clockwiseAccum = 0f;
        lockedScrew = null;
        SetCollidersEnabled(true);
    }

    void OnReleased() { HandleLetGo(); }

    void OnThrown() { HandleLetGo(); }

    void HandleLetGo()
    {
        if (!grabbedNow) return;
        grabbedNow = false;
        BreakScrewLock();
        EnsureKinematicHomeState();
    }

    void OnDisable()
    {
        BreakScrewLock();
    }

    // ---------- 主循环 ----------

    void Update()
    {
        if (!grabbedNow)
            UpdatePickabilityGate();
    }

    void LateUpdate()
    {
        ResolveTip();
        if (grabbedNow)
        {
            UpdateScrewLock();
        }
        else
        {
            ReturnHomeStep();
        }
    }

    void ReturnHomeStep()
    {
        EnsureKinematicHomeState();

        if (returnSmoothTime <= 0f)
        {
            transform.SetPositionAndRotation(homePosition, homeRotation);
            returnVelocity = Vector3.zero;
            return;
        }

        transform.position = Vector3.SmoothDamp(
            transform.position, homePosition, ref returnVelocity, returnSmoothTime);

        float lerp = 1f - Mathf.Exp(-Time.deltaTime / returnSmoothTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, homeRotation, lerp);
    }

    // ---------- Screw 吸附 ----------

    void UpdateScrewLock()
    {
        if (tip != null && lockedScrew == null)
            TryAcquireScrewLock();

        if (lockedScrew == null)
        {
            UpdateHoldDistanceScroll();
            ApplyNaturalHoldPose();
            return;
        }

        // 鼠标横向拖拽 → 累计顺时针角度（视角已经被挂起，不会同时转相机）
        float mx = Input.GetAxis("Mouse X");
        if (Mathf.Abs(mx) > 1e-4f)
        {
            float deltaDeg = mx * rotateSpeedPerMouseX;
            clockwiseAccum = Mathf.Max(0f, clockwiseAccum + deltaDeg);
        }

        // 计算螺丝刀应该处于的位姿：Tip 贴在 screw 上、绕 screw 法线累积旋转 clockwiseAccum
        Quaternion twist = Quaternion.AngleAxis(clockwiseAccum, Vector3.forward);
        Quaternion desiredTipWorldRot = screwAnchorRot * twist;
        Quaternion targetScrewdriverRot = desiredTipWorldRot * Quaternion.Inverse(tipLocalRotOffset);
        Vector3 targetScrewdriverPos = screwAnchorPos - targetScrewdriverRot * tipLocalOffset;

        float t = lockSnapSmoothTime <= 0f
            ? 1f
            : 1f - Mathf.Exp(-Time.deltaTime / lockSnapSmoothTime);
        transform.position = Vector3.Lerp(transform.position, targetScrewdriverPos, t);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetScrewdriverRot, t);

        if (clockwiseAccum >= requiredClockwiseDegrees)
        {
            GameObject toDetach = lockedScrew;
            BreakScrewLock();
            DetachScrew(toDetach);
        }
    }

    void UpdateHoldDistanceScroll()
    {
        if (character == null) return;
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 1e-4f) return;
        character.AdjustHoldDistance(scroll * distancePerScroll);
    }

    void ApplyNaturalHoldPose()
    {
        if (!enforceNaturalHoldPose) return;
        if (tip == null || character == null) return;

        Transform hp = character.HoldPoint;
        if (hp == null) return;

        // 让 Tip 的世界旋转对齐 HoldPoint，等价于 Tip 朝着相机前方
        Quaternion targetScrewdriverRot = hp.rotation * Quaternion.Inverse(tipLocalRotOffset);

        // 让 gripLocalOffset 这个本地点贴在 HoldPoint 上
        Vector3 gripWorldOffset = targetScrewdriverRot * gripLocalOffset;
        Vector3 targetScrewdriverPos = hp.position - gripWorldOffset;

        float lerp = naturalPoseSmoothTime <= 0f
            ? 1f
            : 1f - Mathf.Exp(-Time.deltaTime / naturalPoseSmoothTime);
        transform.position = Vector3.Lerp(transform.position, targetScrewdriverPos, lerp);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetScrewdriverRot, lerp);
    }

    void TryAcquireScrewLock()
    {
        if (tip == null) return;

        Vector3 tipPos = tip.position;
        Vector3 tipDir = tip.forward;

        // 注意：Rigidbody.detectCollisions = false 不会让 Physics.Raycast 跳过自身 collider，
        // 所以这里改用 RaycastAll + 过滤掉螺丝刀自身的命中。
        RaycastHit[] hits = Physics.RaycastAll(tipPos, tipDir, screwDetectDistance);
        if (hits == null || hits.Length == 0) return;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null) continue;

            // 跳过螺丝刀自身和其子物体
            if (hit.collider.transform == transform) continue;
            if (hit.collider.transform.IsChildOf(transform)) continue;

            if (!hit.collider.CompareTag(screwTag))
            {
                // 第一个非自身的命中不是 Screw，说明前面有东西挡住，吸附失败
                return;
            }

            float ang = Vector3.Angle(tipDir, -hit.normal);
            if (ang > alignAngleThreshold) return;

            lockedScrew = hit.collider.gameObject;
            screwAnchorPos = hit.point;
            screwAnchorNormal = hit.normal;

            Vector3 upHint = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(screwAnchorNormal, upHint)) > 0.95f)
                upHint = Vector3.right;
            screwAnchorRot = Quaternion.LookRotation(-screwAnchorNormal, upHint);
            clockwiseAccum = 0f;
            BeginScrewing();
            return;
        }
    }

    void BeginScrewing()
    {
        if (!suspendCameraWhileLocked) return;
        if (suspendedMover != null) return;

        CharacterMove mover = null;
        if (character != null)
        {
            mover = character.GetComponent<CharacterMove>();
            if (mover == null) mover = character.GetComponentInParent<CharacterMove>();
        }
        if (mover == null) mover = FindObjectOfType<CharacterMove>();
        if (mover != null)
        {
            mover.PushMouseLookSuspend();
            suspendedMover = mover;
        }
    }

    void BreakScrewLock()
    {
        lockedScrew = null;
        clockwiseAccum = 0f;
        if (suspendedMover != null)
        {
            suspendedMover.PopMouseLookSuspend();
            suspendedMover = null;
        }
    }

    void DetachScrew(GameObject screw)
    {
        if (screw == null) return;

        Transform parent = screw.transform.parent;
        Rigidbody parentRb = parent != null ? parent.GetComponentInParent<Rigidbody>() : null;

        // 解除父子关系，独立成顶级物体
        screw.transform.SetParent(null, true);

        // 防止 Screw 跟原本所在的物体相互碰撞、卡住
        if (parentRb != null)
            IgnoreCollisionsBetween(screw, parentRb.gameObject);

        // 给 Screw 加上物理
        Rigidbody srb = screw.GetComponent<Rigidbody>();
        if (srb == null) srb = screw.AddComponent<Rigidbody>();
        srb.isKinematic = false;
        srb.useGravity = true;
        srb.detectCollisions = true;
        srb.interpolation = RigidbodyInterpolation.Interpolate;
        srb.velocity = screwAnchorNormal * ejectSpeed;
        srb.angularVelocity = Vector3.zero;

        // 确保所有 collider 都是实体（不是 trigger），否则会直接穿过桌面
        Collider[] cols = screw.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
            if (cols[i] != null) cols[i].isTrigger = false;

        // 移除 Tag 防止后续被 CharacterInteraction 当作可交互目标
        try { screw.tag = "Untagged"; } catch { /* tag 可能不存在 */ }

        // 附加自毁脚本：碰到桌面销毁自己
        ScrewDestroyOnTable destroyer = screw.GetComponent<ScrewDestroyOnTable>();
        if (destroyer == null) destroyer = screw.AddComponent<ScrewDestroyOnTable>();
        destroyer.workTable = workTable;
    }

    static void IgnoreCollisionsBetween(GameObject a, GameObject b)
    {
        Collider[] aCols = a.GetComponentsInChildren<Collider>(true);
        Collider[] bCols = b.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < aCols.Length; i++)
        {
            if (aCols[i] == null) continue;
            for (int j = 0; j < bCols.Length; j++)
            {
                if (bCols[j] == null) continue;
                Physics.IgnoreCollision(aCols[i], bCols[j], true);
            }
        }
    }

    // ---------- 门控 ----------

    void UpdatePickabilityGate()
    {
        bool allow = workTable == null || workTable.HasPlacedItem;
        SetCollidersEnabled(allow);
    }

    void SetCollidersEnabled(bool enabled)
    {
        if (collidersAllowed == enabled && cachedColliders != null) return;
        collidersAllowed = enabled;
        if (cachedColliders == null) return;
        for (int i = 0; i < cachedColliders.Length; i++)
        {
            if (cachedColliders[i] != null)
                cachedColliders[i].enabled = enabled;
        }
    }
}

/// <summary>
/// 被 Screwdriver.DetachScrew 临时挂到掉落的 Screw 上：碰到 WorkTable 即销毁自己。
/// </summary>
public class ScrewDestroyOnTable : MonoBehaviour
{
    public WorkTable workTable;

    [Tooltip("无论是否匹配 workTable，碰到任何 WorkTable 都销毁。")]
    public bool destroyOnAnyWorkTable = true;

    [Tooltip("生存时间上限（秒）。超时仍未碰到桌面也销毁，避免漏接的螺丝在场景里堆积。")]
    public float maxLifetime = 8f;

    float spawnTime;

    void Start()
    {
        spawnTime = Time.time;
    }

    void Update()
    {
        if (maxLifetime > 0f && Time.time - spawnTime > maxLifetime)
            Destroy(gameObject);
    }

    void OnCollisionEnter(Collision col)
    {
        WorkTable wt = col.collider.GetComponentInParent<WorkTable>();
        if (wt == null) return;

        if (destroyOnAnyWorkTable || wt == workTable)
            Destroy(gameObject);
    }
}

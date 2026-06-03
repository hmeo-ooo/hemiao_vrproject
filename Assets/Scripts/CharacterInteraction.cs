using System;
using System.Collections.Generic;
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

    [Header("\u7269\u54C1\u63CF\u8FB9")]
    [Tooltip("\u63CF\u8FB9\u6750\u8D28\uFF08Hemiao/ItemOutline\uFF09\u3002\u7559\u7A7A\u5219\u8FD0\u884C\u65F6\u81EA\u52A8\u67E5\u627E\u3002")]
    public Material outlineMaterial;

    [Tooltip("\u63CF\u8FB9\u5BBD\u5EA6\uFF08\u5C4F\u5E55\u7A7A\u95F4\uFF09\u3002")]
    public float outlineWidth = 0.02f;

    [Tooltip("\u65E0 ItemInformation \u7684\u53EF\u4EA4\u4E92\u7269\u4F53\u4F7F\u7528\u7684\u63CF\u8FB9\u989C\u8272\u3002")]
    public Color defaultOutlineColor = Color.white;

    [Tooltip("\u6309 ItemCategory\uFF1AMetal, OrganicMatter, CoreEnergy, DangerousGoods")]
    public Color[] outlineColorsByCategory = new Color[]
    {
        new Color(0.78f, 0.82f, 0.88f),
        new Color(0.35f, 0.92f, 0.42f),
        new Color(0.35f, 0.72f, 1f),
        new Color(1f, 0.38f, 0.22f),
    };

    Transform holdPoint;
    GameObject grabbedObject;
    Rigidbody grabbedRb;
    bool grabbedWasKinematic;
    bool grabbedUseGravity;
    bool grabbedDetectCollisions;
    RigidbodyInterpolation grabbedInterpolation;
    RigidbodyConstraints grabbedOriginalConstraints;

    GameObject aimedObject;
    RaycastHit aimHit;

    float leftPressTime;
    bool leftPressedCandidate;
    RaycastHit candidateHit;

    float currentHoldDistance;
    Vector3 grabLocalOffset;
    Vector3 grabFollowVelocity;

    readonly List<GameObject> activeOutlines = new List<GameObject>();
    readonly HashSet<GameObject> outlinedRoots = new HashSet<GameObject>();

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
    }

    void Update()
    {
        if (GameplayInputGate.IsBlocked) return;
        if (cameraTransform == null) return;

        UpdateHoldPointDistance();
        UpdateAimTarget();
        RefreshInteractionVisuals();
        HandleLeftPressLogic();
        HandleThrow();
        HandleScrollRotate();
        HandleInspectionInput();
    }

    void HandleInspectionInput()
    {
        if (grabbedObject == null) return;
        var insp = grabbedObject.GetComponent<InspectableItem>();
        if (insp == null) return;
        if (!Input.GetKeyDown(insp.inspectKey)) return;
        // 干扰（如 TVStaticOverlay）正在显示时，把 E 让给“取消干扰”计数，
        // 避免一边按 E 一边意外进入审视。
        if (TVStaticOverlay.IsActive) return;
        InspectionView.Instance.BeginInspection(grabbedObject, this);
    }

    void LateUpdate()
    {
        if (GameplayInputGate.IsBlocked) return;
        if (grabbedRb == null || holdPoint == null) return;
        UpdateGrabbedTransform();
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
        holdPoint.localPosition = Vector3.forward * currentHoldDistance;
    }

    /// <summary>
    /// 外部脚本（如 Screwdriver）调用，把持物点沿相机前方推近或拉远。delta > 0 表示更远。
    /// </summary>
    public void AdjustHoldDistance(float delta)
    {
        if (holdPoint == null) return;
        currentHoldDistance = Mathf.Clamp(currentHoldDistance + delta, minHoldDistance, maxGrabDistance);
        holdPoint.localPosition = Vector3.forward * currentHoldDistance;
    }

    void UpdateGrabbedTransform()
    {
        Transform t = grabbedRb.transform;
        if (grabFollowSmoothTime <= 0f)
        {
            t.SetPositionAndRotation(holdPoint.position, holdPoint.rotation);
            return;
        }

        Vector3 targetPos = holdPoint.TransformPoint(grabLocalOffset);
        t.position = Vector3.SmoothDamp(
            t.position,
            targetPos,
            ref grabFollowVelocity,
            grabFollowSmoothTime);

        float rotLerp = 1f - Mathf.Exp(-Time.deltaTime / grabFollowSmoothTime);
        t.rotation = Quaternion.Slerp(t.rotation, holdPoint.rotation, rotLerp);
    }

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
        HashSet<GameObject> wantOutline = new HashSet<GameObject>();
        var itemsInRange = CollectItemRootsInRange();
        for (int i = 0; i < itemsInRange.Count; i++)
        {
            GameObject item = itemsInRange[i];
            if (item == null || item == aimedObject || item == grabbedObject) continue;
            wantOutline.Add(item);
        }

        var toRemove = new List<GameObject>();
        foreach (GameObject root in outlinedRoots)
        {
            if (root == null || !wantOutline.Contains(root))
                toRemove.Add(root);
        }
        for (int i = 0; i < toRemove.Count; i++)
            RemoveOutline(toRemove[i]);

        foreach (GameObject root in wantOutline)
            EnsureOutline(root);

        if (itemInfoUI == null) return;

        if (aimedObject != null && grabbedObject == null)
        {
            var info = aimedObject.GetComponent<ItemInformation>();
            if (info == null)
                info = aimedObject.GetComponentInParent<ItemInformation>();
            if (info != null)
                itemInfoUI.Show(info, GetItemRoot(info).transform);
            else
                itemInfoUI.Hide();
        }
        else
        {
            itemInfoUI.Hide();
        }
    }

    List<GameObject> CollectItemRootsInRange()
    {
        var results = new List<GameObject>();
        if (cameraTransform == null) return results;

        var seen = new HashSet<int>();
        float maxDistSqr = maxGrabDistance * maxGrabDistance;
        Vector3 camPos = cameraTransform.position;

        ItemInformation[] allItems = FindObjectsOfType<ItemInformation>();
        for (int i = 0; i < allItems.Length; i++)
        {
            ItemInformation info = allItems[i];
            if (info == null) continue;

            GameObject root = GetItemRoot(info);
            if (root == null || !root.activeInHierarchy) continue;

            if ((root.transform.position - camPos).sqrMagnitude > maxDistSqr)
                continue;

            int id = root.GetInstanceID();
            if (!seen.Add(id)) continue;
            results.Add(root);
        }

        // 工具类物体（例如 Screwdriver / Knife）没有 ItemInformation，也希望参与描边显示。
        CollectToolRootsInRange<Screwdriver>(camPos, maxDistSqr, seen, results);
        CollectToolRootsInRange<Knife>(camPos, maxDistSqr, seen, results);

        return results;
    }

    static void CollectToolRootsInRange<T>(Vector3 camPos, float maxDistSqr, HashSet<int> seen, List<GameObject> results)
        where T : Component
    {
        T[] tools = FindObjectsOfType<T>();
        for (int i = 0; i < tools.Length; i++)
        {
            T tool = tools[i];
            if (tool == null) continue;
            GameObject root = tool.gameObject;
            if (!root.activeInHierarchy) continue;
            if ((root.transform.position - camPos).sqrMagnitude > maxDistSqr) continue;

            int id = root.GetInstanceID();
            if (!seen.Add(id)) continue;
            results.Add(root);
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

    void EnsureOutline(GameObject target)
    {
        if (target == null || outlinedRoots.Contains(target)) return;
        AddOutline(target);
    }

    void RemoveOutline(GameObject target)
    {
        if (target == null) return;
        ClearOutline(target);
        outlinedRoots.Remove(target);
    }

    void OnGUI()
    {
        if (cameraTransform == null) return;

        if (crosshairTexture == null)
        {
            Color old = GUI.color;
            GUI.color = (aimedObject != null) ? crosshairAimColor : crosshairDefaultColor;
            float x = (Screen.width - 4f) / 2f;
            float y = (Screen.height - 4f) / 2f;
            GUI.DrawTexture(new Rect(x, y, 4f, 4f), Texture2D.whiteTexture);
            GUI.color = old;
            return;
        }

        Color c = (aimedObject != null) ? crosshairAimColor : crosshairDefaultColor;
        GUI.color = c;
        float size = crosshairSize;
        float px = (Screen.width - size) / 2f;
        float py = (Screen.height - size) / 2f;
        GUI.DrawTexture(new Rect(px, py, size, size), crosshairTexture);
        GUI.color = Color.white;
    }

    void HandleLeftPressLogic()
    {
        if (cameraTransform == null) return;

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
        grabbedDetectCollisions = rb.detectCollisions;
        grabbedInterpolation = rb.interpolation;
        grabbedOriginalConstraints = rb.constraints;

        SuspendFromConveyorBelts(rb);
        rb.constraints &= ~RigidbodyConstraints.FreezeRotation;
        if (!rb.isKinematic)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        rb.isKinematic = true;
        rb.detectCollisions = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        currentHoldDistance = GetGrabDistance(rb);
        grabFollowVelocity = Vector3.zero;
        if (holdPoint != null)
        {
            holdPoint.localPosition = Vector3.forward * currentHoldDistance;
            holdPoint.rotation = rb.rotation;
            grabLocalOffset = holdPoint.InverseTransformPoint(rb.position);
        }

        RemoveOutline(grabbedObject);
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
        if (grabbedObject == null || cameraTransform == null) return;
        if (Input.GetMouseButtonDown(1))
        {
            Vector3 dir = cameraTransform.forward.normalized;
            ReleaseGrabbedObject(true, dir);
        }
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
        bool detectCollisions = grabbedDetectCollisions;
        RigidbodyInterpolation interpolation = grabbedInterpolation;
        RigidbodyConstraints constraints = grabbedOriginalConstraints;

        releasedRb.isKinematic = wasKinematic;
        releasedRb.useGravity = useGravity;
        releasedRb.detectCollisions = detectCollisions;
        releasedRb.interpolation = interpolation;
        releasedRb.constraints = constraints;

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

    void HandleScrollRotate()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) <= 1e-4f) return;

        ScrollWheel?.Invoke(scroll);

        if (!scrollWheelRotatesObject || grabbedObject == null || holdPoint == null || cameraTransform == null)
            return;

        holdPoint.Rotate(cameraTransform.forward, scroll * rotateSpeed, Space.World);
    }

    void AddOutline(GameObject target)
    {
        if (target == null || outlinedRoots.Contains(target)) return;

        Material baseMat = GetOutlineBaseMaterial();
        if (baseMat == null) return;

        Color outlineColor = GetOutlineColor(target);
        float width = ComputeOutlineWidthForTarget(target);
        int created = 0;

        var meshRenderers = target.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < meshRenderers.Length; i++)
            created += TryAddOutlineForRenderer(meshRenderers[i], baseMat, outlineColor, width);

        var skinnedRenderers = target.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinnedRenderers.Length; i++)
            created += TryAddOutlineForSkinnedRenderer(skinnedRenderers[i], baseMat, outlineColor, width);

        if (created == 0)
            Debug.LogWarning($"[CharacterInteraction] No renderers found for outline on {target.name}.", target);
        else
            outlinedRoots.Add(target);
    }

    float ComputeOutlineWidthForTarget(GameObject target)
    {
        if (target == null) return outlineWidth;

        Bounds bounds = ItemInfoWorldUI.CalculateWorldBounds(target);
        float maxExtent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
        return outlineWidth * Mathf.Max(maxExtent, 0.2f);
    }

    int TryAddOutlineForRenderer(MeshRenderer mr, Material baseMat, Color outlineColor, float width)
    {
        if (mr == null || !mr.enabled || mr.gameObject.name.EndsWith("_Outline")) return 0;

        var mf = mr.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return 0;

        var outlineGo = new GameObject(mr.gameObject.name + "_Outline");
        outlineGo.transform.SetParent(mr.transform, false);
        outlineGo.transform.localPosition = Vector3.zero;
        outlineGo.transform.localRotation = Quaternion.identity;
        outlineGo.transform.localScale = Vector3.one;
        outlineGo.layer = mr.gameObject.layer;
        outlineGo.hideFlags = HideFlags.DontSave;

        var cloneMf = outlineGo.AddComponent<MeshFilter>();
        cloneMf.sharedMesh = mf.sharedMesh;

        var cloneMr = outlineGo.AddComponent<MeshRenderer>();
        SetupOutlineRenderer(cloneMr, baseMat, outlineColor, width);
        activeOutlines.Add(outlineGo);
        return 1;
    }

    int TryAddOutlineForSkinnedRenderer(SkinnedMeshRenderer smr, Material baseMat, Color outlineColor, float width)
    {
        if (smr == null || !smr.enabled || smr.sharedMesh == null || smr.gameObject.name.EndsWith("_Outline"))
            return 0;

        var outlineGo = new GameObject(smr.gameObject.name + "_Outline");
        outlineGo.transform.SetParent(smr.transform, false);
        outlineGo.transform.localPosition = Vector3.zero;
        outlineGo.transform.localRotation = Quaternion.identity;
        outlineGo.transform.localScale = Vector3.one;
        outlineGo.layer = smr.gameObject.layer;
        outlineGo.hideFlags = HideFlags.DontSave;

        var cloneSmr = outlineGo.AddComponent<SkinnedMeshRenderer>();
        cloneSmr.sharedMesh = smr.sharedMesh;
        cloneSmr.bones = smr.bones;
        cloneSmr.rootBone = smr.rootBone;
        SetupOutlineRenderer(cloneSmr, baseMat, outlineColor, width);
        activeOutlines.Add(outlineGo);
        return 1;
    }

    void SetupOutlineRenderer(Renderer renderer, Material baseMat, Color outlineColor, float width)
    {
        renderer.material = CreateOutlineMaterialInstance(baseMat, outlineColor, width);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    void ClearOutline(GameObject target)
    {
        for (int i = activeOutlines.Count - 1; i >= 0; i--)
        {
            GameObject go = activeOutlines[i];
            if (go == null)
            {
                activeOutlines.RemoveAt(i);
                continue;
            }

            bool shouldRemove = target == null || go.transform.IsChildOf(target.transform);
            if (!shouldRemove) continue;

            Destroy(go);
            activeOutlines.RemoveAt(i);
        }

        if (target == null)
            outlinedRoots.Clear();
        else
            outlinedRoots.Remove(target);
    }

    Material GetOutlineBaseMaterial()
    {
        if (outlineMaterial != null && outlineMaterial.shader != null && outlineMaterial.shader.isSupported)
            return outlineMaterial;

        Shader shader = Shader.Find("Hemiao/ItemOutline");
        if (shader == null || !shader.isSupported)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null || !shader.isSupported)
            shader = Shader.Find("Unlit/Color");
        if (shader == null) return null;

        outlineMaterial = new Material(shader);
        return outlineMaterial;
    }

    static Material CreateOutlineMaterialInstance(Material baseMat, Color color, float width)
    {
        var inst = new Material(baseMat);
        if (inst.HasProperty("_Color"))
            inst.SetColor("_Color", color);
        else if (inst.HasProperty("_BaseColor"))
            inst.SetColor("_BaseColor", color);
        if (inst.HasProperty("_OutlineWidth"))
            inst.SetFloat("_OutlineWidth", width);
        return inst;
    }

    Color GetOutlineColor(GameObject target)
    {
        var info = target.GetComponentInParent<ItemInformation>();
        if (info == null) return defaultOutlineColor;
        if (info.overrideOutlineColor) return info.outlineColor;
        return GetCategoryOutlineColor(info.category);
    }

    Color GetCategoryOutlineColor(ItemInformation.ItemCategory category)
    {
        int index = (int)category;
        if (outlineColorsByCategory != null && index >= 0 && index < outlineColorsByCategory.Length)
            return outlineColorsByCategory[index];
        return defaultOutlineColor;
    }

    void OnDisable()
    {
        if (grabbedRb != null)
            ReleaseGrabbedObject(false, Vector3.zero);

        ClearOutline(null);
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

        // 同 ResolveInteractableRoot：命中 InspectableItem 层级时统一返回组合根。
        InspectableItem insp = hit.collider.GetComponentInParent<InspectableItem>();
        if (insp != null)
        {
            Rigidbody iRb = insp.GetComponent<Rigidbody>();
            if (iRb == null) iRb = insp.GetComponentInParent<Rigidbody>();
            return iRb != null ? iRb.gameObject : insp.gameObject;
        }

        var rb = hit.rigidbody != null
            ? hit.rigidbody
            : hit.collider.GetComponentInParent<Rigidbody>();
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

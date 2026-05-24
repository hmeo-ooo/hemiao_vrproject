using System;
using UnityEngine;

/// <summary>
/// 切割刀。刀刃 Collider 碰到 Cuttable 时触发分离。
/// </summary>
[DefaultExecutionOrder(200)]
public class Knife : MonoBehaviour
{
    [Header("归位")]
    public Transform homePoint;
    public float returnSmoothTime = 0.15f;

    [Header("交互门控")]
    [Tooltip("关联 WorkTable。只有其 HasPlacedItem 为 true 时才允许拾取。留空则始终可拾取。")]
    public WorkTable workTable;

    [Header("刀刃")]
    public string bladeStartChildName = "BladeStart";
    public string bladeEndChildName = "BladeEnd";

    [Header("切割检测")]
    [Tooltip("刀刃胶囊体检测半径（米）。")]
    public float cutContactRadius = 0.2f;

    [Tooltip("靠近可切物体时，用于判断是否跳过强制握姿的距离系数（× cutContactRadius）。")]
    public float nearCuttablePoseSkipMultiplier = 6f;

    [Tooltip("挥砍时额外容差：鼠标移动超过此值（像素量级轴输入）即视为在挥砍。")]
    public float minMouseSwingInput = 0.02f;

    [Tooltip("仍要求挥砍动作才切割；关闭后准星对准即可切。")]
    public bool requireBladeMotion = false;

    [Tooltip("requireBladeMotion 开启时，刀刃最低速度（米/秒）。")]
    public float minBladeSpeed = 0.05f;

    [Tooltip("准星射线命中 Cuttable 时，刀尖距命中点的最大距离（米）。")]
    public float crosshairCutMaxBladeDistance = 1.5f;

    [Range(2, 9)]
    public int bladeSampleCount = 5;

    [Header("持握姿势")]
    [Tooltip("持握时强制刀尖朝相机前方。关闭后刀随 HoldPoint 移动，更易对准桌面物体。")]
    public bool enforceNaturalHoldPose = false;

    public Vector3 gripLocalOffset = Vector3.zero;
    public float naturalPoseSmoothTime = 0.06f;

    [Header("持物距离")]
    public float distancePerScroll = 3f;

    Rigidbody rb;
    Transform bladeStart;
    Transform bladeEnd;
    Vector3 bladeEndLocalOffset;
    Quaternion bladeEndLocalRotOffset;
    bool bladesResolved;

    CharacterInteraction character;

    Vector3 homePosition;
    Quaternion homeRotation;
    Vector3 returnVelocity;

    bool grabbedNow;
    Vector3 prevBladeStartPos;
    Vector3 prevBladeEndPos;
    bool hasPrevBladePos;

    Collider[] cachedColliders;
    bool collidersAllowed = true;

    static readonly Collider[] OverlapBuffer = new Collider[32];

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        cachedColliders = GetComponentsInChildren<Collider>(true);
        ResolveBlades();
    }

    void ResolveBlades()
    {
        if (bladesResolved && bladeEnd != null) return;

        bladeStart = FindChildByName(bladeStartChildName);
        if (bladeStart == null)
            bladeStart = FindChildByName("BladeStar");

        bladeEnd = FindChildByName(bladeEndChildName);
        if (bladeEnd == null)
            bladeEnd = ResolveBladeEndFallback();

        if (bladeEnd != null)
        {
            bladeEndLocalOffset = transform.InverseTransformPoint(bladeEnd.position);
            bladeEndLocalRotOffset = Quaternion.Inverse(transform.rotation) * bladeEnd.rotation;
            bladesResolved = true;
        }
    }

    Transform FindChildByName(string childName)
    {
        if (string.IsNullOrEmpty(childName)) return null;

        string trimmed = childName.Trim();
        Transform t = transform.Find(childName);
        if (t == null) t = transform.Find(trimmed);
        if (t != null) return t;

        Transform bestPrefix = null;
        foreach (Transform c in GetComponentsInChildren<Transform>(true))
        {
            if (c == null || c == transform) continue;
            string n = c.name.Trim();
            if (string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase))
                return c;
            if (bestPrefix == null
                && n.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
                bestPrefix = c;
        }
        return bestPrefix;
    }

    Transform ResolveBladeEndFallback()
    {
        Transform sawBlade = FindChildByName("SM_Prop_Tool_Neon_Saw_Blade_01");
        if (sawBlade != null) return sawBlade;

        Transform tip = FindChildByName("Tip");
        if (tip != null) return tip;

        Transform farthest = null;
        float maxDist = 0f;
        foreach (Transform c in GetComponentsInChildren<Transform>(true))
        {
            if (c == null || c == transform) continue;
            float d = Vector3.Distance(transform.position, c.position);
            if (d > maxDist)
            {
                maxDist = d;
                farthest = c;
            }
        }
        return farthest;
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
        // 勿对 kinematic 刚体写 velocity/angularVelocity（Unity 会报错）。
        // 归位时只需保持 kinematic；速度在抓取时由 CharacterInteraction 清零。
        rb.useGravity = false;
        rb.isKinematic = true;
    }

    void OnGrabbed(GameObject obj)
    {
        if (obj != gameObject) return;
        grabbedNow = true;
        hasPrevBladePos = false;
        SetCollidersEnabled(true);
    }

    void OnReleased() { HandleLetGo(); }
    void OnThrown() { HandleLetGo(); }

    void HandleLetGo()
    {
        if (!grabbedNow) return;
        grabbedNow = false;
        hasPrevBladePos = false;
        EnsureKinematicHomeState();
    }

    void Update()
    {
        if (!grabbedNow)
            UpdatePickabilityGate();
    }

    void LateUpdate()
    {
        ResolveBlades();
        if (!grabbedNow)
        {
            ReturnHomeStep();
            return;
        }

        UpdateHoldDistanceScroll();

        Vector3 bladeStartPos = GetBladeStartPos();
        Vector3 bladeEndPos = GetBladeEndPos();

        if (!IsNearCuttable(bladeStartPos, bladeEndPos))
            ApplyNaturalHoldPose();

        UpdateCutting(bladeStartPos, bladeEndPos);
    }

    Vector3 GetBladeStartPos() =>
        bladeStart != null ? bladeStart.position : transform.position;

    Vector3 GetBladeEndPos() =>
        bladeEnd != null ? bladeEnd.position : transform.position + transform.forward * 0.6f;

    bool IsNearCuttable(Vector3 start, Vector3 end)
    {
        float range = cutContactRadius * nearCuttablePoseSkipMultiplier;
        Vector3 mid = (start + end) * 0.5f;
        foreach (Cuttable cuttable in Cuttable.AllActive)
        {
            if (cuttable == null || cuttable.IsCut) continue;
            if (cuttable.GetDistanceToBounds(mid) <= range
                || cuttable.GetDistanceToBounds(end) <= range)
                return true;
        }
        return false;
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

    void ApplyNaturalHoldPose()
    {
        if (!enforceNaturalHoldPose || bladeEnd == null || character == null) return;

        Transform hp = character.HoldPoint;
        if (hp == null) return;

        Quaternion targetRot = hp.rotation * Quaternion.Inverse(bladeEndLocalRotOffset);
        Vector3 gripWorldOffset = targetRot * gripLocalOffset;
        Vector3 targetPos = hp.position - gripWorldOffset;

        float lerp = naturalPoseSmoothTime <= 0f
            ? 1f
            : 1f - Mathf.Exp(-Time.deltaTime / naturalPoseSmoothTime);
        transform.position = Vector3.Lerp(transform.position, targetPos, lerp);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, lerp);
    }

    void UpdateHoldDistanceScroll()
    {
        if (character == null) return;
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 1e-4f) return;
        character.AdjustHoldDistance(scroll * distancePerScroll);
    }

    void UpdateCutting(Vector3 start, Vector3 end)
    {
        float bladeSpeed = 0f;
        if (hasPrevBladePos && Time.deltaTime > 1e-5f)
        {
            float endSpeed = (end - prevBladeEndPos).magnitude / Time.deltaTime;
            float startSpeed = (start - prevBladeStartPos).magnitude / Time.deltaTime;
            bladeSpeed = Mathf.Max(endSpeed, startSpeed);
        }
        prevBladeStartPos = start;
        prevBladeEndPos = end;
        hasPrevBladePos = true;

        if (!HasCutMotion(bladeSpeed)) return;

        if (TryCutWithCrosshairRay(end)) return;
        if (TryCutWithOverlap(start, end)) return;
        TryCutWithBoundsFallback(start, end);
    }

    bool HasCutMotion(float bladeSpeed)
    {
        if (!requireBladeMotion) return true;
        float mouseDelta = Mathf.Abs(Input.GetAxis("Mouse X")) + Mathf.Abs(Input.GetAxis("Mouse Y"));
        return bladeSpeed >= minBladeSpeed || mouseDelta >= minMouseSwingInput;
    }

    bool TryCutWithCrosshairRay(Vector3 bladeEndPos)
    {
        if (character == null || character.cameraTransform == null) return false;

        float maxDist = character.maxGrabDistance + cutContactRadius;
        Camera cam = character.cameraTransform.GetComponent<Camera>();
        Ray ray = cam != null
            ? cam.ScreenPointToRay(new Vector2(Screen.width / 2f, Screen.height / 2f))
            : new Ray(character.cameraTransform.position, character.cameraTransform.forward);

        RaycastHit[] hits = Physics.RaycastAll(ray, maxDist, ~0, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0) return false;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null || IsKnifeCollider(col)) continue;

            Cuttable cuttable = col.GetComponentInParent<Cuttable>();
            if (cuttable == null || cuttable.IsCut) continue;

            float bladeToHit = Vector3.Distance(bladeEndPos, hits[i].point);
            if (bladeToHit > crosshairCutMaxBladeDistance) continue;

            cuttable.CutFromBlade();
            return true;
        }
        return false;
    }

    bool TryCutWithOverlap(Vector3 start, Vector3 end)
    {
        Vector3 seg = end - start;
        float segLen = seg.magnitude;
        if (segLen < 1e-4f)
        {
            int n = Physics.OverlapSphereNonAlloc(
                end, cutContactRadius, OverlapBuffer, ~0, QueryTriggerInteraction.Collide);
            return TryCutFromOverlapBuffer(n);
        }

        int count = Physics.OverlapCapsuleNonAlloc(
            start, end, cutContactRadius, OverlapBuffer, ~0, QueryTriggerInteraction.Collide);
        return TryCutFromOverlapBuffer(count);
    }

    bool TryCutFromOverlapBuffer(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Collider col = OverlapBuffer[i];
            if (col == null || IsKnifeCollider(col)) continue;

            Cuttable cuttable = col.GetComponentInParent<Cuttable>();
            if (cuttable == null || cuttable.IsCut) continue;

            cuttable.CutFromBlade();
            return true;
        }
        return false;
    }

    bool TryCutWithBoundsFallback(Vector3 start, Vector3 end)
    {
        foreach (Cuttable cuttable in Cuttable.AllActive)
        {
            if (cuttable == null || cuttable.IsCut) continue;
            if (!cuttable.IsBladeSegmentNear(start, end, cutContactRadius)) continue;

            cuttable.CutFromBlade();
            return true;
        }
        return false;
    }

    bool IsKnifeCollider(Collider col)
    {
        if (col == null) return true;
        Transform t = col.transform;
        return t == transform || t.IsChildOf(transform);
    }

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

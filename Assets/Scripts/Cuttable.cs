using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 挂在可切割物体上。刀刃碰到 Collider 时分离外壳与内容物，各自变为可抓取物体。
/// </summary>
[DisallowMultipleComponent]
public class Cuttable : MonoBehaviour
{
    static readonly HashSet<Cuttable> sActive = new HashSet<Cuttable>();

    public static IReadOnlyCollection<Cuttable> AllActive => sActive;

    [Header("结构")]
    [Tooltip("外壳根物体。切割后会解除父子关系。留空则使用本物体。")]
    public Transform shellRoot;

    [Tooltip("内容物。可以是容器（切其子物体）或单个物体（直接分离）。留空则分离外壳下所有子物体。")]
    public Transform contentsRoot;

    [Header("分离效果")]
    public GameObject shatterEffectPrefab;

    [Tooltip("内容物分离时沿远离外壳方向的冲量（牛·秒）。")]
    public float contentSeparateImpulse = 0.35f;

    [Tooltip("分离后各部分的初始向下速度（米/秒）。0 则纯靠重力。")]
    public float separateDropInitialSpeed = 0.5f;

    public bool destroySelfAfterCut = false;

    [Header("未切开投入通道")]
    [Tooltip("仍保持外壳与子物体组合时扔进通道，显示的黄色字幕。")]
    public string abandonedMixtureMessage = "Abandoned mixture";

    [Tooltip("未切开投入通道时增加的信用点。")]
    public int abandonedMixtureCredits = 1;

    public UnityEvent onCut;

    bool cut;

    public bool IsCut => cut;

    /// <summary>尚未切割且外壳与子物体仍保持组合状态。</summary>
    public bool IsStillAssembled
    {
        get
        {
            if (cut) return false;

            Transform shell = shellRoot != null ? shellRoot : transform;

            if (contentsRoot != null)
                return contentsRoot.parent == shell || contentsRoot.IsChildOf(shell);

            return shell.childCount > 0;
        }
    }

    void OnEnable() => sActive.Add(this);

    void OnDisable() => sActive.Remove(this);

    public void CutFromBlade()
    {
        if (cut) return;
        Separate();
    }

    /// <summary>整件未切开时投入分拣通道：黄色字幕 + 少量信用点，并销毁整组物体。</summary>
    public void HandleAbandonedMixtureInAisle()
    {
        if (CreditManager.Instance != null)
        {
            CreditManager.Instance.AddCredits(abandonedMixtureCredits);
            string msg = string.IsNullOrEmpty(abandonedMixtureMessage)
                ? $"+{abandonedMixtureCredits} credits"
                : $"{abandonedMixtureMessage} (+{abandonedMixtureCredits})";
            CreditManager.Instance.ShowSubtitle(
                msg,
                2f,
                new Color(1f, 0.92f, 0.2f, 1f));
        }

        Destroy(gameObject);
    }

    public float GetDistanceToBounds(Vector3 worldPoint)
    {
        Bounds bounds = ItemInfoWorldUI.CalculateWorldBounds(gameObject);
        return Vector3.Distance(worldPoint, bounds.ClosestPoint(worldPoint));
    }

    public bool IsBladeSegmentNear(Vector3 bladeStart, Vector3 bladeEnd, float radius)
    {
        Collider[] cols = GetComponentsInChildren<Collider>(true);
        if (cols.Length == 0)
            return GetDistanceToBounds(Vector3.Lerp(bladeStart, bladeEnd, 0.5f)) <= radius;

        float radiusSqr = radius * radius;
        for (int i = 0; i < cols.Length; i++)
        {
            Collider col = cols[i];
            if (col == null || !col.enabled) continue;

            Vector3 closestOnBlade = ClosestPointOnSegment(bladeStart, bladeEnd, col.bounds.center);
            if ((col.ClosestPoint(closestOnBlade) - closestOnBlade).sqrMagnitude <= radiusSqr)
                return true;
        }
        return false;
    }

    [ContextMenu("Force Separate")]
    public void ForceSeparate() => Separate();

    static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float lenSqr = ab.sqrMagnitude;
        if (lenSqr < 1e-8f) return a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / lenSqr);
        return a + ab * t;
    }

    void Separate()
    {
        if (cut) return;
        cut = true;

        ReleaseFromWorkTables();

        if (shatterEffectPrefab != null)
            Instantiate(shatterEffectPrefab, transform.position, transform.rotation);

        Transform shell = shellRoot != null ? shellRoot : transform;
        List<Transform> contents = CollectContents();
        Vector3 shellCenter = ItemInfoWorldUI.CalculateWorldBounds(shell.gameObject).center;

        for (int i = 0; i < contents.Count; i++)
        {
            Transform child = contents[i];
            if (child == null || child == shell) continue;

            Vector3 pushDir = child.position - shellCenter;
            if (pushDir.sqrMagnitude < 1e-6f) pushDir = Vector3.down;
            else pushDir.Normalize();

            MakeInteractablePart(child, pushDir * contentSeparateImpulse);
            IgnoreCollidersBetween(child.gameObject, shell.gameObject);
        }

        DetachRemainingChildren(shell, contents);
        MakeInteractablePart(shell);

        onCut?.Invoke();

        if (destroySelfAfterCut)
            Destroy(gameObject);
        else
            enabled = false;
    }

    void ReleaseFromWorkTables()
    {
        WorkTable[] tables = FindObjectsOfType<WorkTable>();
        for (int i = 0; i < tables.Length; i++)
        {
            if (tables[i] != null)
                tables[i].ReleasePlacedItemForCut(gameObject);
        }
    }

    List<Transform> CollectContents()
    {
        var contents = new List<Transform>();
        Transform shell = shellRoot != null ? shellRoot : transform;

        if (contentsRoot != null)
        {
            if (contentsRoot.childCount > 0)
            {
                for (int i = contentsRoot.childCount - 1; i >= 0; i--)
                    contents.Add(contentsRoot.GetChild(i));
            }
            else if (contentsRoot != shell)
                contents.Add(contentsRoot);
            return contents;
        }

        if (shell != null && shell.childCount > 0)
        {
            for (int i = shell.childCount - 1; i >= 0; i--)
                contents.Add(shell.GetChild(i));
            return contents;
        }

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform c = transform.GetChild(i);
            if (shell != null && (c == shell || c.IsChildOf(shell))) continue;
            contents.Add(c);
        }
        return contents;
    }

    void DetachRemainingChildren(Transform shell, List<Transform> alreadyHandled)
    {
        if (shell == null) return;

        Vector3 shellCenter = ItemInfoWorldUI.CalculateWorldBounds(shell.gameObject).center;
        for (int i = shell.childCount - 1; i >= 0; i--)
        {
            Transform child = shell.GetChild(i);
            if (child == null || alreadyHandled.Contains(child)) continue;

            Vector3 pushDir = child.position - shellCenter;
            if (pushDir.sqrMagnitude < 1e-6f) pushDir = Vector3.down;
            else pushDir.Normalize();

            MakeInteractablePartStatic(child, pushDir * contentSeparateImpulse);
            IgnoreCollidersBetween(child.gameObject, shell.gameObject);
        }
    }

    void MakeInteractablePart(Transform part, Vector3 impulse)
    {
        MakeInteractablePartStatic(part, impulse);
    }

    static void MakeInteractablePartStatic(Transform part, Vector3 impulse, bool applyImpulseAsVelocity = false)
    {
        if (part == null) return;

        part.SetParent(null, true);

        Rigidbody body = part.GetComponent<Rigidbody>();
        if (body == null) body = part.gameObject.AddComponent<Rigidbody>();

        body.isKinematic = false;
        body.useGravity = true;
        body.detectCollisions = true;
        body.constraints = RigidbodyConstraints.None;
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;

        Physics.SyncTransforms();
        body.WakeUp();

        if (impulse.sqrMagnitude > 1e-6f)
        {
            if (applyImpulseAsVelocity)
                body.velocity = impulse;
            else
                body.AddForce(impulse, ForceMode.Impulse);
        }

        Cuttable other = part.GetComponent<Cuttable>();
        if (other != null && other.enabled)
            other.enabled = false;
    }

    void MakeInteractablePart(Transform part)
    {
        Vector3 vel = separateDropInitialSpeed > 0f
            ? Vector3.down * separateDropInitialSpeed
            : Vector3.zero;
        MakeInteractablePartStatic(part, vel, applyImpulseAsVelocity: true);
    }

    static void IgnoreCollidersBetween(GameObject a, GameObject b)
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
}

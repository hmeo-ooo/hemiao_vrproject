using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 挂在可切割物体上。刀刃碰到 Collider 时销毁本物体，并按 <see cref="dropEntries"/>
/// 在物体位置实例化对应数量的 prefab。每个 prefab 自带的 ItemInformation / Rigidbody /
/// Collider / 外观等会被沿用，无需在此重复配置。
/// </summary>
[DisallowMultipleComponent]
public class Cuttable : MonoBehaviour
{
    static readonly HashSet<Cuttable> sActive = new HashSet<Cuttable>();

    public static IReadOnlyCollection<Cuttable> AllActive => sActive;

    [Header("分离效果")]
    public GameObject shatterEffectPrefab;

    [Tooltip("分离后所有 prefab 实例的初始向下速度（米/秒）。0 即纯靠重力。")]
    public float dropInitialSpeed = 0.5f;

    [Header("未切开投入通道")]
    [Tooltip("仍未被切割就被投入通道时显示的字幕。")]
    public string abandonedMixtureMessage = "Abandoned mixture";

    [Tooltip("未切开投入通道时的信用点变化（填负数表示惩罚）。")]
    public int abandonedMixtureCredits = -50;

    [Header("分离后生成 - 必出")]
    [Tooltip("切割触发后销毁本物品，并按此列表在本物体位置实例化 prefab（必定生成）。\n" +
             "每条可设置 prefab + 数量。prefab 自带的 ItemInformation / Rigidbody /\n" +
             "Collider / 外观等信息会被沿用，无需在此重复配置。")]
    public List<DetachSpawnEntry> dropEntries = new List<DetachSpawnEntry>();

    [Header("分离后生成 - 可能出")]
    [Tooltip("切割时每条独立按 spawnChance 摇一次：命中则该条所有 count 一起出，\n" +
             "未命中则一个都不出。用于配置低概率掉落的彩蛋/惊喜物品。")]
    public List<OptionalDetachSpawnEntry> optionalDropEntries = new List<OptionalDetachSpawnEntry>();

    public UnityEvent onCut;

    bool cut;

    public bool IsCut => cut;

    /// <summary>尚未被刀切开（投入分拣通道时仍按"未拆混合物"惩罚）。</summary>
    public bool IsStillAssembled => !cut;

    void OnEnable() => sActive.Add(this);

    void OnDisable() => sActive.Remove(this);

    public void CutFromBlade()
    {
        if (cut) return;
        Separate();
    }

    /// <summary>整件未切开时投入分拣通道：惩罚信用点并销毁整组物体。</summary>
    public void HandleAbandonedMixtureInAisle()
    {
        if (CreditManager.Instance != null)
        {
            if (abandonedMixtureCredits != 0)
                CreditManager.Instance.AddCredits(abandonedMixtureCredits);

            string creditText = abandonedMixtureCredits >= 0
                ? $"+{abandonedMixtureCredits} credits"
                : $"{abandonedMixtureCredits} credits";
            string msg = string.IsNullOrEmpty(abandonedMixtureMessage)
                ? creditText
                : $"{abandonedMixtureMessage} ({creditText})";
            Color subtitleColor = abandonedMixtureCredits >= 0
                ? new Color(1f, 0.92f, 0.2f, 1f)
                : new Color(1f, 0.3f, 0.3f, 1f);
            CreditManager.Instance.ShowSubtitle(msg, 2f, subtitleColor);
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

        if (SfxManager.Instance != null)
            SfxManager.Instance.PlayCut(transform.position);

        ReleaseFromWorkTables();

        Vector3 anchor = ItemInfoWorldUI.CalculateWorldBounds(gameObject).center;

        if (shatterEffectPrefab != null)
            Instantiate(shatterEffectPrefab, anchor, transform.rotation);

        DetachSpawnUtility.SpawnEntries(dropEntries, anchor, dropInitialSpeed);
        DetachSpawnUtility.SpawnOptionalEntries(optionalDropEntries, anchor, dropInitialSpeed);

        onCut?.Invoke();

        Destroy(gameObject);
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
}

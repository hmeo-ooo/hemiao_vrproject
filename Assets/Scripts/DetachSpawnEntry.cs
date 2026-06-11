using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "分离后必定生成"的一条配置：哪一个 prefab、生成几个。
/// InspectableItem 与 Cuttable 共用。prefab 上自带的 ItemInformation / Rigidbody /
/// Collider / 外观等信息会被沿用，不需要在此重复填写。
/// </summary>
[System.Serializable]
public class DetachSpawnEntry
{
    [Tooltip("要实例化的 prefab。prefab 上自带的 ItemInformation / Rigidbody / Collider 等会随实例化保留。")]
    public GameObject prefab;

    [Tooltip("实例化的数量。")]
    [Min(0)]
    public int count = 1;
}

/// <summary>
/// "分离后可能生成"的一条配置：在 <see cref="DetachSpawnEntry"/> 的基础上加一个独立摇出的概率。
/// 命中概率时本条所有 count 一起生成，否则一个都不出（即"全有或全无"，每条独立摇）。
/// </summary>
[System.Serializable]
public class OptionalDetachSpawnEntry
{
    [Tooltip("要实例化的 prefab。prefab 上自带的 ItemInformation / Rigidbody / Collider 等会随实例化保留。")]
    public GameObject prefab;

    [Tooltip("命中本条概率时，实例化的数量。")]
    [Min(0)]
    public int count = 1;

    [Tooltip("本条被生成的概率。0 = 永不生成；1 = 必然生成。每条独立摇一次：" +
             "命中则该条全部 count 个一起生成，未命中则一个都不出。")]
    [Range(0f, 1f)]
    public float spawnChance = 0.5f;
}

/// <summary>
/// 把一组 <see cref="DetachSpawnEntry"/> / <see cref="OptionalDetachSpawnEntry"/>
/// 在指定锚点处实例化出来的工具。所有实例都生成在同一点，初始速度向下，
/// 靠物理自然下落堆叠。
/// </summary>
public static class DetachSpawnUtility
{
    /// <summary>
    /// 在 <paramref name="anchor"/> 点按 <paramref name="entries"/> 实例化所有 prefab（必定生成）。
    /// </summary>
    /// <param name="entries">分离后要生成的 prefab 列表。</param>
    /// <param name="anchor">所有 prefab 实例的生成位置（世界坐标）。</param>
    /// <param name="initialDropSpeed">生成后立即施加的向下速度（米/秒），用于自然下落、堆叠。0 表示纯靠重力。</param>
    public static void SpawnEntries(IList<DetachSpawnEntry> entries, Vector3 anchor, float initialDropSpeed)
    {
        if (entries == null) return;

        for (int i = 0; i < entries.Count; i++)
        {
            DetachSpawnEntry e = entries[i];
            if (e == null || e.prefab == null || e.count <= 0) continue;

            for (int n = 0; n < e.count; n++)
                InstantiateOne(e.prefab, anchor, initialDropSpeed);
        }
    }

    /// <summary>
    /// 按 <paramref name="entries"/> 中每条独立摇 <see cref="OptionalDetachSpawnEntry.spawnChance"/>：
    /// 命中即把该条所有 count 个一起在 <paramref name="anchor"/> 处实例化，未命中则跳过该条。
    /// </summary>
    public static void SpawnOptionalEntries(IList<OptionalDetachSpawnEntry> entries, Vector3 anchor, float initialDropSpeed)
    {
        if (entries == null) return;

        for (int i = 0; i < entries.Count; i++)
        {
            OptionalDetachSpawnEntry e = entries[i];
            if (e == null || e.prefab == null || e.count <= 0) continue;
            if (e.spawnChance <= 0f) continue;
            if (e.spawnChance < 1f && Random.value > e.spawnChance) continue;

            for (int n = 0; n < e.count; n++)
                InstantiateOne(e.prefab, anchor, initialDropSpeed);
        }
    }

    static void InstantiateOne(GameObject prefab, Vector3 anchor, float initialDropSpeed)
    {
        GameObject inst = Object.Instantiate(prefab, anchor, prefab.transform.rotation);

        // 与 ItemSpawner 流程保持一致：保证有 Rigidbody + Collider，并按类别上色描边。
        ItemSpawner.EnsurePhysics(inst);
        ItemSpawner.FinalizeLooseItem(inst);

        if (initialDropSpeed > 0f)
        {
            Rigidbody rb = inst.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.down * initialDropSpeed;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }
}

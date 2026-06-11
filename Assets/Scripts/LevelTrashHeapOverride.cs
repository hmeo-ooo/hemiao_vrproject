using System;
using UnityEngine;

/// <summary>
/// 关卡级垃圾堆配置：在 <see cref="LevelDefinition.trashHeapOverrides"/> 中按 heapId 指向场景里的
/// <see cref="TrashHeap"/>。关卡开始时由 <see cref="LevelManager"/> 将下列字段写入对应堆并重新生成。
/// 生成概率统一使用 <see cref="LevelDefinition.complexityComposition"/>。
/// </summary>
[Serializable]
public class LevelTrashHeapOverride
{
    [Serializable]
    public class Entry
    {
        [Tooltip("候选垃圾预制体（建议挂 ItemInformation 以标注 complexity）。")]
        public GameObject prefab;
    }

    [Tooltip("要配置的 TrashHeap.heapId。区分大小写；留空则本条被忽略。")]
    public string heapId;

    [Tooltip("可选：仅用于在 Inspector 中标注，例如「主堆 / 角落堆」。")]
    public string label;

    [Header("候选垃圾")]
    [Tooltip("替换场景 TrashHeap.entries 的候选预制体列表；留空数组表示不生成任何垃圾。" +
             "若保持 null 则沿用场景里已配置的 entries。")]
    public Entry[] entries;

    [Header("数量")]
    [Min(0)] public int initialTrashCount = 12;

    [Header("去重策略")]
    public bool allowDuplicates = true;

    [Header("再生（按时间补充新垃圾）")]
    [Tooltip("启用后，关卡进行中按 respawnInterval 周期补充新垃圾。")]
    public bool respawnEnabled = false;

    [Tooltip("再生节拍间隔（秒）。值越小再生得越快。")]
    [Min(0.1f)] public float respawnInterval = 5f;

    [Tooltip("每个再生节拍最多生成多少件。")]
    [Min(1)] public int respawnPerBurst = 1;

    [Tooltip("再生希望保持的堆上活跃数量。0 = 沿用 initialTrashCount。")]
    [Min(0)] public int respawnTargetActiveCount = 0;

    [Header("整关累计上限")]
    [Tooltip("本关该堆累计生成上限（初始 + 再生）。0 不限制。")]
    [Min(0)] public int maxTotalSpawned = 0;
}

using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 每关复杂度构成。以百分比表示 Basic / Composite / Dangerous 的生成概率。
/// 建议三项之和为 100；若合计不为 100，运行时按各项占合计的比例归一化抽取。
/// 全部为 0 时退化为在候选池中均匀随机。
/// </summary>
[System.Serializable]
public class LevelComplexityComposition
{
    [Tooltip("基础物（Basic）生成概率（%）。")]
    [Range(0f, 100f)]
    [FormerlySerializedAs("basicWeight")]
    public float basicProbability = 100f;

    [Tooltip("复合物（Composite）生成概率（%）。")]
    [Range(0f, 100f)]
    [FormerlySerializedAs("compositeWeight")]
    public float compositeProbability = 0f;

    [Tooltip("高危品（Dangerous）生成概率（%）。")]
    [Range(0f, 100f)]
    [FormerlySerializedAs("dangerousWeight")]
    public float dangerousProbability = 0f;

    /// <summary>三项概率之和（Inspector 填写的原始值，未归一化）。</summary>
    public float TotalProbability => basicProbability + compositeProbability + dangerousProbability;

    public bool HasAnyProbability => TotalProbability > 0f;

    public float GetProbability(ItemInformation.ItemComplexity complexity)
    {
        switch (complexity)
        {
            case ItemInformation.ItemComplexity.Basic: return basicProbability;
            case ItemInformation.ItemComplexity.Composite: return compositeProbability;
            case ItemInformation.ItemComplexity.Dangerous: return dangerousProbability;
            default: return 0f;
        }
    }

    /// <summary>
    /// 在「仅有可用桶」的前提下按概率抽取复杂度。
    /// <paramref name="isBucketAvailable"/> 返回 false 的复杂度不参与本次 roll（例如对应 prefab 桶为空）。
    /// </summary>
    public bool TryPickComplexity(
        System.Func<ItemInformation.ItemComplexity, bool> isBucketAvailable,
        out ItemInformation.ItemComplexity pick)
    {
        pick = ItemInformation.ItemComplexity.Basic;

        float total = 0f;
        if (isBucketAvailable(ItemInformation.ItemComplexity.Basic))
            total += basicProbability;
        if (isBucketAvailable(ItemInformation.ItemComplexity.Composite))
            total += compositeProbability;
        if (isBucketAvailable(ItemInformation.ItemComplexity.Dangerous))
            total += dangerousProbability;

        if (total <= 0f)
            return false;

        float roll = Random.value * total;
        float acc = 0f;

        if (isBucketAvailable(ItemInformation.ItemComplexity.Basic))
        {
            acc += basicProbability;
            if (roll < acc)
            {
                pick = ItemInformation.ItemComplexity.Basic;
                return true;
            }
        }

        if (isBucketAvailable(ItemInformation.ItemComplexity.Composite))
        {
            acc += compositeProbability;
            if (roll < acc)
            {
                pick = ItemInformation.ItemComplexity.Composite;
                return true;
            }
        }

        pick = ItemInformation.ItemComplexity.Dangerous;
        return true;
    }
}

/// <summary>
/// 单关配置：掉落物列表、生成参数、场上静态道具摆放。
/// 在 Project 窗口：Create → Hemiao → Level Definition
/// </summary>
[CreateAssetMenu(fileName = "Level_01", menuName = "Hemiao/Level Definition")]
public class LevelDefinition : ScriptableObject
{
    [Header("标识")]
    [Tooltip("关卡序号（仅用于显示/排序，加载以 LevelManager 数组顺序为准）。")]
    public int levelNumber = 1;

    public string displayName = "Level 1";

    [Header("关卡时长")]
    [Tooltip("本关倒计时（秒）。由 LevelSessionController 在开始游戏时应用到 CountDownTimer。")]
    [Min(1f)]
    public float levelDurationSeconds = 120f;

    [Header("物品生成")]
    [Tooltip("本关掉落（ItemSpawner）与垃圾堆（TrashHeap）共用的 Basic/Composite/Dangerous 生成概率（%）。建议三项之和为 100。" +
             "若某概率对应的预制体不在候选列表里，该档会被跳过并在剩余档位间重新归一化。")]
    public LevelComplexityComposition complexityComposition = new LevelComplexityComposition();

    [Header("掉落生成（ItemSpawner）")]
    [Tooltip("本关候选掉落物预制体（应包含 ItemInformation 组件）。")]
    public GameObject[] spawnPrefabs;

    public float spawnInterval = 1f;

    public int itemsPerBurst = 5;

    public float spawnSpreadRadius = 0.3f;

    public float burstImpulse = 2f;

    public float burstImpulseRandomness = 0.5f;

    [Tooltip("同时在场数量上限，0 不限制。")]
    public int maxActiveItems;

    [Tooltip("本关累计生成上限，0 不限制。")]
    public int maxTotalItems;

    [Tooltip("应用本关配置后是否自动开始掉落。")]
    public bool autoStartSpawning = true;

    [Header("场上道具")]
    [Tooltip("进入关卡时生成的静态道具（工作台物品、可切割物等）。")]
    public LevelPropPlacement[] sceneProps;

    [Header("分拣通道")]
    [Tooltip("本关启用的分拣通道列表。展开每条后可从场景拖入物体，自动写入 localPosition / localEulerAngles。")]
    public LevelAislePlacement[] aisles;

    [Header("场上垃圾堆")]
    [Tooltip("本关对场景里 TrashHeap 的配置（按 heapId 匹配）。关卡开始时写入 entries / 数量 / 再生等字段，覆盖场景默认值。" +
             "生成概率统一使用上方 complexityComposition。")]
    public LevelTrashHeapOverride[] trashHeapOverrides;

    [Header("关卡干扰")]
    [Tooltip("本关在指定时间触发的干扰列表（如电视雪花叠加层）。每条由 LevelSessionController 按 triggerAtSeconds 排程。")]
    public LevelInterferenceConfig[] interferences;

    [TextArea(2, 4)]
    public string designNotes;

    [Header("关卡指引 / 教程")]
    [Tooltip("进入本关前是否显示指引 UI（在 Level Hub 点「进入关卡」之后、倒计时开始之前）。")]
    public bool showTutorialBeforeLevel = true;

    [Tooltip("指引页标题；留空则使用 displayName。")]
    public string tutorialTitle;

    [TextArea(4, 12)]
    [Tooltip("指引与教程正文，支持 TMP 富文本标签。")]
    public string tutorialBody;

    public bool HasTutorialContent =>
        !string.IsNullOrWhiteSpace(tutorialTitle) || !string.IsNullOrWhiteSpace(tutorialBody);

    public string ResolveTutorialTitle()
    {
        if (!string.IsNullOrWhiteSpace(tutorialTitle))
            return tutorialTitle.Trim();
        return !string.IsNullOrWhiteSpace(displayName) ? displayName : $"Level {levelNumber}";
    }
}

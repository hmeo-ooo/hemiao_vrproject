using UnityEngine;

/// <summary>
/// 每关复杂度构成。权重越大该类物品越易被抽到；全部为 0 时退化为随机抽取。
/// </summary>
[System.Serializable]
public class LevelComplexityComposition
{
    [Tooltip("基础物（Basic）权重。")]
    [Min(0f)] public float basicWeight = 1f;

    [Tooltip("复合物（Composite）权重。")]
    [Min(0f)] public float compositeWeight = 0f;

    [Tooltip("高危品（Dangerous）权重。")]
    [Min(0f)] public float dangerousWeight = 0f;

    public float TotalWeight => basicWeight + compositeWeight + dangerousWeight;

    public float GetWeight(ItemInformation.ItemComplexity c)
    {
        switch (c)
        {
            case ItemInformation.ItemComplexity.Basic: return basicWeight;
            case ItemInformation.ItemComplexity.Composite: return compositeWeight;
            case ItemInformation.ItemComplexity.Dangerous: return dangerousWeight;
            default: return 0f;
        }
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

    [Header("掉落生成（ItemSpawner）")]
    [Tooltip("本关候选掉落物预制体（应包含 ItemInformation 组件）。")]
    public GameObject[] spawnPrefabs;

    [Header("复杂度构成")]
    [Tooltip("按 Basic/Composite/Dangerous 权重从 spawnPrefabs 中抽取。若某权重对应的预制体不在列表里，会跳过该权重。")]
    public LevelComplexityComposition complexityComposition = new LevelComplexityComposition();

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

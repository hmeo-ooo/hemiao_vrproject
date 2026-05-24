using UnityEngine;

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

    [TextArea(2, 4)]
    public string designNotes;
}

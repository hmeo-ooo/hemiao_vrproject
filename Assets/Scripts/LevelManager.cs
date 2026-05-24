using System;
using UnityEngine;

/// <summary>
/// 读取 LevelDefinition 列表，切换关卡时清理并重建掉落配置与场上道具。
/// </summary>
[DefaultExecutionOrder(-100)]
public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    [Header("关卡表（按顺序第 1～10 关）")]
    public LevelDefinition[] levels;

    [Header("引用")]
    public ItemSpawner itemSpawner;

    [Tooltip("本关静态道具生成到此节点下；留空则使用自身 Transform。")]
    public Transform propsRoot;

    [Header("启动")]
    [Tooltip("进入场景时仅准备关卡数据（不开始掉落）；通常由 LevelSessionController 接管流程。")]
    public bool loadLevelOnStart;

    public int startLevelIndex;

    public event Action<LevelDefinition> LevelLoaded;
    public event Action LevelGameplayStarted;
    public event Action LevelGameplayEnded;

    public bool IsGameplayActive { get; private set; }

    public int CurrentLevelIndex { get; private set; } = -1;

    public LevelDefinition CurrentLevel =>
        levels != null && CurrentLevelIndex >= 0 && CurrentLevelIndex < levels.Length
            ? levels[CurrentLevelIndex]
            : null;

    public int LevelCount => levels != null ? levels.Length : 0;

    public int ResolveLevelIndex(int preferredIndex)
    {
        if (levels == null || levels.Length == 0)
            return 0;

        preferredIndex = Mathf.Clamp(preferredIndex, 0, levels.Length - 1);
        if (levels[preferredIndex] != null)
            return preferredIndex;

        for (int i = preferredIndex + 1; i < levels.Length; i++)
        {
            if (levels[i] != null)
                return i;
        }

        for (int i = preferredIndex - 1; i >= 0; i--)
        {
            if (levels[i] != null)
                return i;
        }

        return 0;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[LevelManager] Duplicate instance removed.", this);
            Destroy(this);
            return;
        }

        Instance = this;

        if (propsRoot == null)
            propsRoot = transform;

        if (itemSpawner == null)
            itemSpawner = FindObjectOfType<ItemSpawner>();

        if (loadLevelOnStart && levels != null && levels.Length > 0)
        {
            int index = ResolveLevelIndex(startLevelIndex);
            LoadLevel(index);
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public bool LoadLevel(int index)
    {
        if (levels == null || levels.Length == 0)
        {
            Debug.LogWarning("[LevelManager] No levels configured.", this);
            return false;
        }

        if (index < 0 || index >= levels.Length)
        {
            Debug.LogWarning($"[LevelManager] Level index {index} out of range [0, {levels.Length - 1}].", this);
            return false;
        }

        LevelDefinition def = levels[index];
        if (def == null)
        {
            Debug.LogWarning($"[LevelManager] Level slot {index} is null.", this);
            return false;
        }

        ClearRuntimeContent();
        ApplyDefinition(def);
        CurrentLevelIndex = index;

        Debug.Log($"[LevelManager] Loaded level {index + 1}/{levels.Length}: {def.displayName}");
        LevelLoaded?.Invoke(def);
        return true;
    }

    /// <summary>开始本关掉落（需已 LoadLevel）。</summary>
    public void BeginLevelGameplay()
    {
        if (CurrentLevel == null)
        {
            Debug.LogWarning("[LevelManager] BeginLevelGameplay called with no level loaded.", this);
            return;
        }

        if (IsGameplayActive) return;

        if (itemSpawner != null && CurrentLevel.autoStartSpawning)
            itemSpawner.StartSpawning();

        IsGameplayActive = true;
        LevelGameplayStarted?.Invoke();
    }

    /// <summary>停止掉落并清理已生成物品。</summary>
    public void EndLevelGameplay()
    {
        if (!IsGameplayActive && itemSpawner == null) return;

        if (itemSpawner != null)
        {
            itemSpawner.StopSpawning();
            itemSpawner.ClearAllSpawnedItems();
        }

        if (IsGameplayActive)
        {
            IsGameplayActive = false;
            LevelGameplayEnded?.Invoke();
        }
    }

    public bool LoadNextLevel()
    {
        if (levels == null || levels.Length == 0) return false;
        int next = CurrentLevelIndex < 0 ? 0 : CurrentLevelIndex + 1;
        if (next >= levels.Length) return false;
        return LoadLevel(next);
    }

    public void ReloadCurrentLevel()
    {
        if (CurrentLevelIndex >= 0)
            LoadLevel(CurrentLevelIndex);
    }

    void ClearRuntimeContent()
    {
        if (itemSpawner != null)
            itemSpawner.ClearAllSpawnedItems();

        ClearPropsRoot();
    }

    void ClearPropsRoot()
    {
        if (propsRoot == null) return;

        for (int i = propsRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = propsRoot.GetChild(i);
            if (child == null) continue;
            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
    }

    void ApplyDefinition(LevelDefinition def)
    {
        if (itemSpawner != null)
            itemSpawner.ApplyLevelSettings(def);

        SpawnSceneProps(def);
    }

    void SpawnSceneProps(LevelDefinition def)
    {
        if (def.sceneProps == null || def.sceneProps.Length == 0 || propsRoot == null)
            return;

        for (int i = 0; i < def.sceneProps.Length; i++)
        {
            LevelPropPlacement placement = def.sceneProps[i];
            if (placement == null || placement.prefab == null) continue;

            Vector3 position;
            Quaternion rotation;

            if (placement.spawnPoint != null)
            {
                position = placement.spawnPoint.position;
                rotation = placement.spawnPoint.rotation;
            }
            else
            {
                position = propsRoot.TransformPoint(placement.localPosition);
                rotation = propsRoot.rotation * Quaternion.Euler(placement.localEulerAngles);
            }

            GameObject instance = Instantiate(placement.prefab, position, rotation, propsRoot);
            instance.name = placement.prefab.name;
        }
    }
}

using System;
using System.Collections.Generic;
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

    [Header("分拣通道")]
    [Tooltip("按关卡配置生成的通道实例挂在此节点下；留空则使用 LevelManager 自身 Transform。")]
    public Transform aislesRoot;

    [Tooltip("默认通道预制体（需含 AisleDetection + Collider）。留空时改为 reposition 场景中已有的通道物体。")]
    public GameObject defaultAislePrefab;

    [Tooltip("关卡定义了通道列表时，是否隐藏场景中原本摆好的通道（避免重复）。")]
    public bool hideSceneAislesWhenUsingLevelLayout = true;

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

    AisleDetection[] sceneBakedAisles;
    readonly List<GameObject> spawnedAisles = new List<GameObject>();

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

        if (aislesRoot == null)
            aislesRoot = transform;

        if (itemSpawner == null)
            itemSpawner = FindObjectOfType<ItemSpawner>();

        CacheSceneBakedAisles();

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
        ClearSpawnedAisles();
        SetSceneBakedAislesActive(true);
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
        ApplyLevelAisles(def);
    }

    void SpawnSceneProps(LevelDefinition def)
    {
        if (def.sceneProps == null || def.sceneProps.Length == 0 || propsRoot == null)
            return;

        for (int i = 0; i < def.sceneProps.Length; i++)
        {
            LevelPropPlacement placement = def.sceneProps[i];
            if (placement == null || placement.prefab == null) continue;

            Vector3 position = propsRoot.TransformPoint(placement.localPosition);
            Quaternion rotation = propsRoot.rotation * Quaternion.Euler(placement.localEulerAngles);

            GameObject instance = Instantiate(placement.prefab, position, rotation, propsRoot);
            instance.name = placement.prefab.name;
        }
    }

    // ------------------------------------------------------------------
    // 分拣通道
    // ------------------------------------------------------------------

    void CacheSceneBakedAisles()
    {
        AisleDetection[] all = FindObjectsOfType<AisleDetection>(true);
        var baked = new List<AisleDetection>(all.Length);
        for (int i = 0; i < all.Length; i++)
        {
            AisleDetection aisle = all[i];
            if (aisle == null) continue;
            if (aislesRoot != null && aisle.transform.IsChildOf(aislesRoot)) continue;
            baked.Add(aisle);
        }
        sceneBakedAisles = baked.ToArray();
    }

    void ApplyLevelAisles(LevelDefinition def)
    {
        ClearSpawnedAisles();

        if (def == null || def.aisles == null || def.aisles.Length == 0)
        {
            SetSceneBakedAislesActive(true);
            return;
        }

        bool spawnedAny = TrySpawnLevelAisles(def);
        if (spawnedAny)
        {
            if (hideSceneAislesWhenUsingLevelLayout)
                SetSceneBakedAislesActive(false);
            return;
        }

        ConfigureSceneBakedAisles(def);
    }

    bool TrySpawnLevelAisles(LevelDefinition def)
    {
        bool spawnedAny = false;
        for (int i = 0; i < def.aisles.Length; i++)
        {
            LevelAislePlacement placement = def.aisles[i];
            if (placement == null) continue;

            GameObject prefab = placement.prefab != null ? placement.prefab : defaultAislePrefab;
            if (prefab == null) continue;

            if (!TryResolveAisleTransform(placement, out Vector3 position, out Quaternion rotation, out Vector3 scale))
                continue;

            GameObject instance = Instantiate(prefab, position, rotation, aislesRoot);
            instance.transform.localScale = scale;

            string suffix = string.IsNullOrEmpty(placement.label)
                ? placement.category.ToString()
                : placement.label;
            instance.name = $"Aisle_{suffix}";

            AisleDetection detection = instance.GetComponent<AisleDetection>();
            if (detection != null)
                detection.aisleCategory = placement.category;

            spawnedAisles.Add(instance);
            spawnedAny = true;
        }
        return spawnedAny;
    }

    void ConfigureSceneBakedAisles(LevelDefinition def)
    {
        if (sceneBakedAisles == null || sceneBakedAisles.Length == 0)
            CacheSceneBakedAisles();

        SetSceneBakedAislesActive(false);
        var used = new HashSet<AisleDetection>();

        for (int i = 0; i < def.aisles.Length; i++)
        {
            LevelAislePlacement placement = def.aisles[i];
            if (placement == null) continue;

            AisleDetection target = FindSceneAisleForPlacement(placement, used, i);
            if (target == null)
            {
                Debug.LogWarning(
                    $"[LevelManager] No scene aisle available for category {placement.category} " +
                    $"(index {i}). Assign defaultAislePrefab to spawn at runtime.");
                continue;
            }

            used.Add(target);
            target.aisleCategory = placement.category;
            target.gameObject.SetActive(true);

            if (TryResolveAisleTransform(placement, out Vector3 position, out Quaternion rotation, out Vector3 scale))
            {
                Transform t = target.transform;
                t.SetPositionAndRotation(position, rotation);
                t.localScale = scale;
            }
        }
    }

    AisleDetection FindSceneAisleForPlacement(
        LevelAislePlacement placement,
        HashSet<AisleDetection> used,
        int index)
    {
        if (sceneBakedAisles == null) return null;

        // 优先：场景中已有同 category 且未被占用的通道
        for (int i = 0; i < sceneBakedAisles.Length; i++)
        {
            AisleDetection aisle = sceneBakedAisles[i];
            if (aisle == null || used.Contains(aisle)) continue;
            if (aisle.aisleCategory == placement.category)
                return aisle;
        }

        // 其次：按列表顺序取第一个未使用的场景通道
        for (int i = 0; i < sceneBakedAisles.Length; i++)
        {
            AisleDetection aisle = sceneBakedAisles[i];
            if (aisle == null || used.Contains(aisle)) continue;
            return aisle;
        }

        // 最后：按 index 兜底
        if (index >= 0 && index < sceneBakedAisles.Length)
        {
            AisleDetection aisle = sceneBakedAisles[index];
            if (aisle != null && !used.Contains(aisle))
                return aisle;
        }

        return null;
    }

    bool TryResolveAisleTransform(
        LevelAislePlacement placement,
        out Vector3 position,
        out Quaternion rotation,
        out Vector3 scale)
    {
        if (aislesRoot == null)
        {
            position = placement.localPosition;
            rotation = Quaternion.Euler(placement.localEulerAngles);
            scale = placement.localScale;
            return true;
        }

        position = aislesRoot.TransformPoint(placement.localPosition);
        rotation = aislesRoot.rotation * Quaternion.Euler(placement.localEulerAngles);
        scale = Vector3.Scale(aislesRoot.lossyScale, placement.localScale);
        return true;
    }

    void ClearSpawnedAisles()
    {
        for (int i = spawnedAisles.Count - 1; i >= 0; i--)
        {
            GameObject go = spawnedAisles[i];
            if (go == null) continue;
            if (Application.isPlaying)
                Destroy(go);
            else
                DestroyImmediate(go);
        }
        spawnedAisles.Clear();
    }

    void SetSceneBakedAislesActive(bool active)
    {
        if (sceneBakedAisles == null) return;
        for (int i = 0; i < sceneBakedAisles.Length; i++)
        {
            if (sceneBakedAisles[i] != null)
                sceneBakedAisles[i].gameObject.SetActive(active);
        }
    }
}

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

    [Tooltip("可选：除主 ItemSpawner 之外、同样接受关卡掉落配置覆盖的额外 Spawner。")]
    public ItemSpawner[] additionalItemSpawners;

    [Tooltip("启用后，所有被本 LevelManager 管理的 Spawner 共享 maxActiveItems / maxTotalItems。")]
    public bool shareSpawnQuotaAcrossSpawners = true;

    readonly ItemSpawner.SpawnQuotaGroup _sharedSpawnQuota = new ItemSpawner.SpawnQuotaGroup();

    [Tooltip("本关静态道具生成到此节点下；留空则使用自身 Transform。")]
    public Transform propsRoot;

    [Header("分拣通道")]
    [Tooltip("按关卡配置生成的通道实例挂在此节点下；留空则使用 LevelManager 自身 Transform。")]
    public Transform aislesRoot;

    [Tooltip("默认通道预制体（需含 AisleDetection + Collider）。留空时改为 reposition 场景中已有的通道物体。")]
    public GameObject defaultAislePrefab;

    [Tooltip("关卡定义了通道列表时，是否隐藏场景中原本摆好的通道（避免重复）。")]
    public bool hideSceneAislesWhenUsingLevelLayout = true;

    [Header("回合结束清理")]
    [Tooltip("关卡结束或切换时，销毁场上所有带 ItemInformation 的物体（含切割碎片、传送带上、玩家手中的物品）。")]
    public bool clearGameplayItemsOnEnd = true;

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

        ForEachManagedSpawner(s => s.StartSpawning());

        IsGameplayActive = true;
        LevelGameplayStarted?.Invoke();
    }

    /// <summary>停止掉落并清理已生成物品。</summary>
    public void EndLevelGameplay()
    {
        if (!IsGameplayActive && itemSpawner == null
            && (additionalItemSpawners == null || additionalItemSpawners.Length == 0))
            return;

        ForEachManagedSpawner(s =>
        {
            s.StopSpawning();
            s.ClearAllSpawnedItems();
        });

        if (clearGameplayItemsOnEnd)
            ClearAllGameplayItems();

        _sharedSpawnQuota.Reset();
        SfxManager.Instance?.ResetDangerousGoodsAlarm();

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

    /// <summary>
    /// 当前关卡是否已经达成"场上零物品 + 不会再生成新物品"的清场状态。
    /// 用于早结束流程（例如玩家走到床前按 E）。需要 IsGameplayActive 为 true。
    /// </summary>
    public bool IsAllItemsProcessed()
    {
        if (!IsGameplayActive) return false;

        int active = 0;
        bool hasMoreToSpawn = false;

        if (shareSpawnQuotaAcrossSpawners)
        {
            active = _sharedSpawnQuota.CurrentActive;
            hasMoreToSpawn = _sharedSpawnQuota.maxTotalItems > 0
                ? _sharedSpawnQuota.HasTotalQuota()
                : AnyManagedSpawnerStillSpawning();
        }
        else
        {
            ForEachManagedSpawner(s =>
            {
                active += s.GetActiveItemCount();
                if (s.maxTotalItems > 0)
                {
                    if (s.GetRemainingTotalSpawnQuota() > 0)
                        hasMoreToSpawn = true;
                }
                else if (s.IsSpawningActive)
                {
                    hasMoreToSpawn = true;
                }
            });
        }

        return active == 0 && !hasMoreToSpawn;
    }

    bool AnyManagedSpawnerStillSpawning()
    {
        bool running = false;
        ForEachManagedSpawner(s =>
        {
            if (s != null && s.IsSpawningActive)
                running = true;
        });
        return running;
    }

    public void ReloadCurrentLevel()
    {
        if (CurrentLevelIndex >= 0)
            LoadLevel(CurrentLevelIndex);
    }

    void ClearRuntimeContent()
    {
        ForEachManagedSpawner(s => s.ClearAllSpawnedItems());

        if (clearGameplayItemsOnEnd)
            ClearAllGameplayItems();

        ClearPropsRoot();
        ClearSpawnedAisles();
        SetSceneBakedAislesActive(true);
    }

    /// <summary>
    /// 销毁场上所有分拣物品（含 Spawner 未跟踪的切割碎片、遗留物等）。
    /// 不会移除螺丝刀、工作台等无 ItemInformation 的场景设施。
    /// </summary>
    public void ClearAllGameplayItems()
    {
        if (InspectionView.Instance != null)
            InspectionView.Instance.EndInspection(null);

        CharacterInteraction character = FindObjectOfType<CharacterInteraction>();

        WorkTable[] tables = FindObjectsOfType<WorkTable>();
        for (int i = 0; i < tables.Length; i++)
        {
            if (tables[i] != null)
                tables[i].ClearForLevelEnd();
        }

        ItemInformation[] items = FindObjectsOfType<ItemInformation>();
        var roots = new HashSet<GameObject>();
        for (int i = 0; i < items.Length; i++)
        {
            ItemInformation info = items[i];
            if (info == null) continue;

            GameObject root = GetGameplayItemRoot(info);
            if (root != null)
                roots.Add(root);
        }

        foreach (GameObject root in roots)
        {
            if (root == null) continue;
            character?.ForceReleaseIfHolding(root);
            Destroy(root);
        }
    }

    static GameObject GetGameplayItemRoot(ItemInformation info)
    {
        if (info == null) return null;

        InspectableItem insp = info.GetComponentInParent<InspectableItem>();
        if (insp != null)
        {
            Rigidbody iRb = insp.GetComponent<Rigidbody>();
            if (iRb == null) iRb = insp.GetComponentInParent<Rigidbody>();
            return iRb != null ? iRb.gameObject : insp.gameObject;
        }

        Rigidbody rb = info.GetComponentInParent<Rigidbody>();
        return rb != null ? rb.gameObject : info.gameObject;
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
        if (shareSpawnQuotaAcrossSpawners)
        {
            _sharedSpawnQuota.Configure(def.maxActiveItems, def.maxTotalItems);
            _sharedSpawnQuota.Reset();
        }

        ForEachManagedSpawner(s =>
        {
            s.ApplyLevelSettings(def);
            s.SetSharedQuota(shareSpawnQuotaAcrossSpawners ? _sharedSpawnQuota : null);
        });

        SpawnSceneProps(def);
        ApplyLevelAisles(def);
    }

    void ForEachManagedSpawner(System.Action<ItemSpawner> action)
    {
        if (action == null) return;

        if (itemSpawner != null)
            action(itemSpawner);

        if (additionalItemSpawners == null) return;

        for (int i = 0; i < additionalItemSpawners.Length; i++)
        {
            ItemSpawner extra = additionalItemSpawners[i];
            if (extra == null || extra == itemSpawner) continue;
            action(extra);
        }
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

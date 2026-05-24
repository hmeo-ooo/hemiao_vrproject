using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 物品生成器：定期从一组可配置的预制体中涌出生成物品，提供开启/关闭接口。
/// - itemPrefabs: 可生成的物品预制体列表
/// - spawnInterval: 每次涌出生成间隔（秒）
/// - itemsPerBurst: 每次一波涌出生成的物品数量
/// - StartSpawning / StopSpawning: 开启和停止生成
/// - maxActiveItems: 同时在场数量上限；maxTotalItems: 本关卡累计生成总数上限
/// 生成时会自动确保有 Rigidbody，并尽可能添加 Collider。
/// </summary>
public class ItemSpawner : MonoBehaviour
{
    [Tooltip("可生成的物品预制体列表")]
    public GameObject[] itemPrefabs;

    [Tooltip("每次涌出生成间隔（秒），必须大于 0")]
    public float spawnInterval = 1f;

    [Tooltip("每次一波涌出生成的物品数量")]
    public int itemsPerBurst = 5;

    [Tooltip("是否在 Start 时自动开始生成")]
    public bool autoStart = true;

    [Header("涌出效果")]
    [Tooltip("生成位置在生成点周围的随机半径")]
    public float spawnSpreadRadius = 0.3f;

    [Tooltip("涌出冲量速度大小，0 表示不额外施加冲量")]
    public float burstImpulse = 2f;

    [Tooltip("冲量方向的随机扰动")]
    public float burstImpulseRandomness = 0.5f;

    [Header("生成控制")]
    [Tooltip("同时存在的物品数量上限，0 表示不限制")]
    public int maxActiveItems = 0;

    [Tooltip("本关卡累计生成物品总数上限，0 表示不限制；达到后自动停止生成")]
    public int maxTotalItems = 0;

    [Tooltip("达到关卡生成上限后是否自动停止生成协程")]
    public bool stopSpawningWhenTotalLimitReached = true;

    private readonly HashSet<GameObject> _activeItems = new HashSet<GameObject>();
    private int _totalSpawnedCount;

    private Coroutine _spawnRoutine;

    private void Awake()
    {
        if (itemPrefabs == null || itemPrefabs.Length == 0)
            Debug.LogWarning($"[{nameof(ItemSpawner)}] 未配置任何 itemPrefabs：{name}");

        if (spawnInterval <= 0f)
        {
            Debug.LogWarning($"[{nameof(ItemSpawner)}] spawnInterval 无效，已重置为 1：{name}");
            spawnInterval = 1f;
        }

        if (itemsPerBurst < 1)
        {
            Debug.LogWarning($"[{nameof(ItemSpawner)}] itemsPerBurst 小于 1，已重置为 1：{name}");
            itemsPerBurst = 1;
        }

        if (maxActiveItems < 0)
        {
            Debug.LogWarning($"[{nameof(ItemSpawner)}] maxActiveItems 为负数，已重置为 0（不限制）：{name}");
            maxActiveItems = 0;
        }

        if (maxTotalItems < 0)
        {
            Debug.LogWarning($"[{nameof(ItemSpawner)}] maxTotalItems 为负数，已重置为 0（不限制）：{name}");
            maxTotalItems = 0;
        }
    }

    private void Start()
    {
        if (autoStart)
            StartSpawning();
    }

    public void StartSpawning()
    {
        if (_spawnRoutine != null) return;
        _spawnRoutine = StartCoroutine(SpawnLoop());
    }

    public void StopSpawning()
    {
        if (_spawnRoutine == null) return;
        StopCoroutine(_spawnRoutine);
        _spawnRoutine = null;
    }

    private IEnumerator SpawnLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(spawnInterval);
            SpawnBurst();
        }
    }

    /// <summary>
    /// 一次涌出生成多个物品，受 maxActiveItems 与 maxTotalItems 限制。
    /// </summary>
    private void SpawnBurst()
    {
        CleanupActiveItems();

        if (itemPrefabs == null || itemPrefabs.Length == 0) return;
        if (IsTotalSpawnLimitReached()) return;

        int count = itemsPerBurst;
        if (maxActiveItems > 0)
        {
            int room = maxActiveItems - _activeItems.Count;
            if (room <= 0) return;
            count = Mathf.Min(count, room);
        }

        if (maxTotalItems > 0)
            count = Mathf.Min(count, maxTotalItems - _totalSpawnedCount);

        if (count <= 0) return;

        for (int i = 0; i < count; i++)
        {
            if (!SpawnOne())
                break;
        }

        if (stopSpawningWhenTotalLimitReached && IsTotalSpawnLimitReached())
            StopSpawning();
    }

    private bool SpawnOne()
    {
        if (IsTotalSpawnLimitReached())
            return false;

        int idx = Random.Range(0, itemPrefabs.Length);
        var prefab = itemPrefabs[idx];
        if (prefab == null) return false;

        Vector3 offset = spawnSpreadRadius > 0f
            ? Random.insideUnitSphere * spawnSpreadRadius
            : Vector3.zero;
        Vector3 position = transform.position + offset;
        Quaternion rotation = Random.rotation;

        GameObject instance = Instantiate(prefab, position, rotation);
        if (instance == null) return false;

        EnsureItemInformation(instance, prefab);
        EnsurePhysics(instance);
        ApplyBurstImpulse(instance);

        _activeItems.Add(instance);
        _totalSpawnedCount++;
        var notifier = instance.AddComponent<ItemLifetimeNotifier>();
        notifier.Initialize(this, instance);
        return true;
    }

    bool IsTotalSpawnLimitReached()
    {
        return maxTotalItems > 0 && _totalSpawnedCount >= maxTotalItems;
    }

    /// <summary>
    /// 从预制体复制 ItemInformation；若实例上没有则自动添加。
    /// </summary>
    static void EnsureItemInformation(GameObject instance, GameObject prefab)
    {
        if (instance == null) return;

        var info = instance.GetComponent<ItemInformation>();
        if (info == null)
            info = instance.AddComponent<ItemInformation>();

        if (prefab == null) return;

        var source = prefab.GetComponent<ItemInformation>();
        if (source == null) return;

        info.category = source.category;
        info.creditsOnCorrectThrow = source.creditsOnCorrectThrow;
        info.itemDisplayName = source.itemDisplayName;
        info.itemDescription = source.itemDescription;
        info.overrideOutlineColor = source.overrideOutlineColor;
        info.outlineColor = source.outlineColor;
    }

    /// <summary>
    /// 确保实例有 Rigidbody，并尽可能添加 Collider，不修改 prefab 资源本身。
    /// </summary>
    private static void EnsurePhysics(GameObject instance)
    {
        var rb = instance.GetComponent<Rigidbody>();
        if (rb == null)
            rb = instance.AddComponent<Rigidbody>();

        rb.useGravity = true;
        rb.isKinematic = false;

        if (instance.GetComponentInChildren<Collider>() != null)
            return;

        var box = instance.AddComponent<BoxCollider>();
        var renderer = instance.GetComponentInChildren<Renderer>();
        if (renderer == null) return;

        Bounds bounds = renderer.bounds;
        box.center = instance.transform.InverseTransformPoint(bounds.center);
        Vector3 lossy = instance.transform.lossyScale;
        box.size = new Vector3(
            bounds.size.x / Mathf.Max(Mathf.Abs(lossy.x), 0.001f),
            bounds.size.y / Mathf.Max(Mathf.Abs(lossy.y), 0.001f),
            bounds.size.z / Mathf.Max(Mathf.Abs(lossy.z), 0.001f));
    }

    private void ApplyBurstImpulse(GameObject instance)
    {
        if (burstImpulse <= 0f) return;

        var rb = instance.GetComponent<Rigidbody>();
        if (rb == null) return;

        Vector3 baseDir = transform.forward.sqrMagnitude > 0.001f
            ? transform.forward
            : Vector3.up;
        Vector3 randomOffset = burstImpulseRandomness > 0f
            ? Random.insideUnitSphere * burstImpulseRandomness
            : Vector3.zero;
        Vector3 dir = (baseDir + randomOffset).normalized;
        rb.AddForce(dir * burstImpulse, ForceMode.Impulse);
    }

    private void CleanupActiveItems()
    {
        var toRemove = new List<GameObject>();
        foreach (var go in _activeItems)
        {
            if (go == null) toRemove.Add(go);
        }
        foreach (var go in toRemove)
            _activeItems.Remove(go);
    }

    internal void NotifyItemDestroyed(GameObject item)
    {
        if (item == null) return;
        _activeItems.Remove(item);
    }

    public int GetActiveItemCount()
    {
        CleanupActiveItems();
        return _activeItems.Count;
    }

    public void SetMaxActiveItems(int max)
    {
        maxActiveItems = Mathf.Max(0, max);
    }

    public int GetTotalSpawnedCount() => _totalSpawnedCount;

    public int GetRemainingTotalSpawnQuota()
    {
        if (maxTotalItems <= 0) return int.MaxValue;
        return Mathf.Max(0, maxTotalItems - _totalSpawnedCount);
    }

    public void SetMaxTotalItems(int max)
    {
        maxTotalItems = Mathf.Max(0, max);
    }

    /// <summary>重置关卡累计生成计数（例如新一局开始时调用）。</summary>
    public void ResetTotalSpawnedCount()
    {
        _totalSpawnedCount = 0;
    }

    /// <summary>停止生成并销毁本 Spawner 跟踪的所有掉落物。</summary>
    public void ClearAllSpawnedItems()
    {
        StopSpawning();

        if (_activeItems.Count > 0)
        {
            var snapshot = new List<GameObject>(_activeItems);
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i] != null)
                    Destroy(snapshot[i]);
            }
            _activeItems.Clear();
        }

        ResetTotalSpawnedCount();
    }

    /// <summary>应用关卡表中的掉落配置。</summary>
    public void ApplyLevelSettings(LevelDefinition level)
    {
        if (level == null) return;

        StopSpawning();

        itemPrefabs = level.spawnPrefabs ?? System.Array.Empty<GameObject>();
        spawnInterval = Mathf.Max(0.01f, level.spawnInterval);
        itemsPerBurst = Mathf.Max(1, level.itemsPerBurst);
        spawnSpreadRadius = level.spawnSpreadRadius;
        burstImpulse = level.burstImpulse;
        burstImpulseRandomness = level.burstImpulseRandomness;
        maxActiveItems = Mathf.Max(0, level.maxActiveItems);
        maxTotalItems = Mathf.Max(0, level.maxTotalItems);
        autoStart = level.autoStartSpawning;

        ResetTotalSpawnedCount();
    }

    private void OnDisable()
    {
        StopSpawning();
    }

    private class ItemLifetimeNotifier : MonoBehaviour
    {
        private ItemSpawner _owner;
        private GameObject _tracked;

        public void Initialize(ItemSpawner owner, GameObject tracked)
        {
            _owner = owner;
            _tracked = tracked;
        }

        private void OnDestroy()
        {
            if (_owner != null && _tracked != null)
                _owner.NotifyItemDestroyed(_tracked);
        }

        private void OnDisable()
        {
            if (!Application.isPlaying) return;

            if (_owner != null && _tracked != null && !_tracked.activeInHierarchy)
                _owner.NotifyItemDestroyed(_tracked);
        }
    }
}

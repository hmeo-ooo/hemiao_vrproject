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

    LevelComplexityComposition _levelComposition = new LevelComplexityComposition();
    readonly ComplexitySpawnPicker _spawnPicker = new ComplexitySpawnPicker();

    private readonly HashSet<GameObject> _activeItems = new HashSet<GameObject>();
    private int _totalSpawnedCount;

    private SpawnQuotaGroup _sharedQuota;

    /// <summary>设置共享配额。设置后，本 Spawner 的 maxActiveItems / maxTotalItems 检查会使用共享值。传 null 解除共享。</summary>
    public void SetSharedQuota(SpawnQuotaGroup quota)
    {
        _sharedQuota = quota;
    }

    public SpawnQuotaGroup SharedQuota => _sharedQuota;

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

        EnsureActiveInHierarchy();
        if (!isActiveAndEnabled)
        {
            Debug.LogWarning(
                $"[{nameof(ItemSpawner)}] StartSpawning skipped because {name} is inactive in hierarchy.",
                this);
            return;
        }

        _spawnRoutine = StartCoroutine(SpawnLoop());
        Debug.Log($"[ItemSpawner] StartSpawning: prefabs={itemPrefabs?.Length ?? 0}, " +
                  $"interval={spawnInterval}, burst={itemsPerBurst}, " +
                  $"maxActive={maxActiveItems}, maxTotal={maxTotalItems}", this);
    }

    /// <summary>
    /// 若本物体因父节点未激活而不可用，则自根向下激活整条未激活链（常见于关卡开始前 Spawner 根节点被禁用）。
    /// </summary>
    void EnsureActiveInHierarchy()
    {
        if (gameObject.activeInHierarchy) return;

        var inactiveChain = new List<Transform>();
        for (Transform t = transform; t != null; t = t.parent)
        {
            if (!t.gameObject.activeSelf)
                inactiveChain.Add(t);
        }

        for (int i = inactiveChain.Count - 1; i >= 0; i--)
            inactiveChain[i].gameObject.SetActive(true);
    }

    [ContextMenu("Force Spawn Burst (Debug)")]
    void DebugForceSpawnBurst()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[ItemSpawner] Force spawn only works in Play mode.", this);
            return;
        }
        SpawnBurst();
    }

    public void StopSpawning()
    {
        if (_spawnRoutine == null) return;
        StopCoroutine(_spawnRoutine);
        _spawnRoutine = null;
    }

    public bool IsSpawningActive => _spawnRoutine != null;

    private IEnumerator SpawnLoop()
    {
        while (true)
        {
            SpawnBurst();
            yield return new WaitForSeconds(spawnInterval);
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

        int activeRoom = GetActiveRoom();
        if (activeRoom <= 0) return;
        count = Mathf.Min(count, activeRoom);

        int totalRoom = GetTotalRoom();
        if (totalRoom <= 0) return;
        count = Mathf.Min(count, totalRoom);

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

        var prefab = PickPrefab();
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
        Hemiao.Rendering.ItemOutlineSystem.Register(instance);

        _activeItems.Add(instance);
        _totalSpawnedCount++;
        _sharedQuota?.OnItemSpawned();
        var notifier = instance.AddComponent<ItemLifetimeNotifier>();
        notifier.Initialize(this, instance);
        return true;
    }

    bool IsTotalSpawnLimitReached()
    {
        if (_sharedQuota != null) return !_sharedQuota.HasTotalQuota();
        return maxTotalItems > 0 && _totalSpawnedCount >= maxTotalItems;
    }

    int GetActiveRoom()
    {
        if (_sharedQuota != null) return _sharedQuota.RoomForActive();
        if (maxActiveItems <= 0) return int.MaxValue;
        return Mathf.Max(0, maxActiveItems - _activeItems.Count);
    }

    int GetTotalRoom()
    {
        if (_sharedQuota != null) return _sharedQuota.RoomForTotal();
        if (maxTotalItems <= 0) return int.MaxValue;
        return Mathf.Max(0, maxTotalItems - _totalSpawnedCount);
    }

    /// <summary>
    /// 按关卡复杂度概率抽取一个 prefab；概率全 0 时退化为均匀随机。
    /// </summary>
    GameObject PickPrefab()
    {
        if (itemPrefabs == null || itemPrefabs.Length == 0) return null;
        return _spawnPicker.PickPrefab(_levelComposition);
    }

    void ConfigureSpawnPicker()
    {
        int count = itemPrefabs != null ? itemPrefabs.Length : 0;
        _spawnPicker.Configure(i => itemPrefabs[i], count);
    }

    /// <summary>
    /// 从预制体复制 ItemInformation；若实例上没有则自动添加。
    /// </summary>
    public static void EnsureItemInformation(GameObject instance, GameObject prefab)
    {
        if (instance == null) return;

        var info = instance.GetComponent<ItemInformation>();
        if (info == null)
            info = instance.AddComponent<ItemInformation>();

        if (prefab == null) return;

        var source = prefab.GetComponent<ItemInformation>();
        if (source == null) return;

        info.category = source.category;
        info.complexity = source.complexity;
        info.creditsOnCorrectThrow = source.creditsOnCorrectThrow;
        info.itemDisplayName = source.itemDisplayName;
        info.itemDescription = source.itemDescription;
    }

    /// <summary>
    /// 将撕扯/切割后独立在场的物品初始化成与 Spawner 生成物相同的状态（物理）。
    /// </summary>
    public static void FinalizeLooseItem(GameObject instance)
    {
        if (instance == null) return;

        EnsurePhysics(instance);
    }

    /// <summary>
    /// 确保实例有 Rigidbody 与 Collider，与 Spawner 生成物一致。
    /// </summary>
    public static void EnsurePhysics(GameObject instance)
    {
        var rb = instance.GetComponent<Rigidbody>();
        if (rb == null)
            rb = instance.AddComponent<Rigidbody>();

        rb.useGravity = true;
        rb.isKinematic = false;
        rb.detectCollisions = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        SanitizeOversizedConvexMeshColliders(instance);

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

    /// <summary>
    /// 部分导入网格（如 Gas.fbx）面数超过 Unity 凸包上限 256，会生成残缺碰撞体并刷警告。
    /// 遇到这类已知网格时，改为按 Renderer 包围盒生成简单碰撞体。
    /// </summary>
    static void SanitizeOversizedConvexMeshColliders(GameObject instance)
    {
        var meshColliders = instance.GetComponentsInChildren<MeshCollider>(true);
        for (int i = 0; i < meshColliders.Length; i++)
        {
            MeshCollider mc = meshColliders[i];
            if (mc == null || !mc.convex || mc.sharedMesh == null) continue;
            if (!IsKnownOversizedConvexMesh(mc.sharedMesh)) continue;

            Renderer renderer = mc.GetComponent<Renderer>();
            GameObject go = mc.gameObject;
            string meshName = mc.sharedMesh.name;
            Object.Destroy(mc);

            if (renderer == null)
            {
                go.AddComponent<BoxCollider>();
                continue;
            }

            Bounds bounds = renderer.bounds;
            Vector3 center = go.transform.InverseTransformPoint(bounds.center);
            Vector3 lossy = go.transform.lossyScale;
            Vector3 size = new Vector3(
                bounds.size.x / Mathf.Max(Mathf.Abs(lossy.x), 0.001f),
                bounds.size.y / Mathf.Max(Mathf.Abs(lossy.y), 0.001f),
                bounds.size.z / Mathf.Max(Mathf.Abs(lossy.z), 0.001f));

            // 气瓶类圆柱网格优先用 CapsuleCollider。
            if (meshName == "Gas")
            {
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.direction = 1;
                capsule.center = center;
                capsule.height = Mathf.Max(size.y, 0.01f);
                capsule.radius = Mathf.Max(size.x, size.z) * 0.5f;
                continue;
            }

            var box = go.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
        }
    }

    static bool IsKnownOversizedConvexMesh(Mesh mesh) =>
        mesh != null && mesh.name == "Gas";

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
        if (_activeItems.Remove(item))
            _sharedQuota?.OnItemDestroyed();
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
            _activeItems.Clear();

            for (int i = 0; i < snapshot.Count; i++)
            {
                GameObject go = snapshot[i];
                if (go == null) continue;

                // 先同步共享配额：Clear 后 OnDestroy 里 Remove 会失败，导致计数泄漏。
                _sharedQuota?.OnItemDestroyed();
                Destroy(go);
            }
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

        _levelComposition = level.complexityComposition ?? new LevelComplexityComposition();
        ConfigureSpawnPicker();

        ResetTotalSpawnedCount();
    }

    private void OnDisable()
    {
        StopSpawning();
    }

    /// <summary>多 Spawner 共享的活跃数 / 累计上限。由外部（如 LevelManager）创建并下发到每个 Spawner。</summary>
    public class SpawnQuotaGroup
    {
        public int maxActiveItems;
        public int maxTotalItems;

        public int CurrentActive { get; private set; }
        public int TotalSpawned { get; private set; }

        public bool HasTotalQuota() => maxTotalItems <= 0 || TotalSpawned < maxTotalItems;
        public bool HasActiveRoom() => maxActiveItems <= 0 || CurrentActive < maxActiveItems;

        public int RoomForActive()
        {
            if (maxActiveItems <= 0) return int.MaxValue;
            return Mathf.Max(0, maxActiveItems - CurrentActive);
        }

        public int RoomForTotal()
        {
            if (maxTotalItems <= 0) return int.MaxValue;
            return Mathf.Max(0, maxTotalItems - TotalSpawned);
        }

        public void Configure(int maxActive, int maxTotal)
        {
            maxActiveItems = Mathf.Max(0, maxActive);
            maxTotalItems = Mathf.Max(0, maxTotal);
        }

        public void Reset()
        {
            CurrentActive = 0;
            TotalSpawned = 0;
        }

        public void OnItemSpawned()
        {
            CurrentActive++;
            TotalSpawned++;
        }

        public void OnItemDestroyed()
        {
            if (CurrentActive > 0) CurrentActive--;
        }
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

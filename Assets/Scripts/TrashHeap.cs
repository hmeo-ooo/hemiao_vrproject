using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 垃圾堆：在垃圾堆模型表面按复杂度概率随机生成若干可交互的垃圾物品。
///
/// 工作流程：
/// 1. Start 时按关卡 <see cref="LevelDefinition.complexityComposition"/> 先抽复杂度，再从 <see cref="entries"/> 对应桶里均匀随机一件；
/// 2. 在 <see cref="surfaceCollider"/>（垃圾堆 Mesh/复合体的碰撞体）上方采样，向下射线找一个合法落点；
/// 3. 实例化预制体，挂上 ItemInformation/Rigidbody/Collider，以 Kinematic 形式"嵌"在堆上；
/// 4. 物品被玩家抓取后由 <see cref="EmbeddedTrashItem"/> 永久晋升为自由物理。
///
/// 用法（场景配置）：
///   - 场景里放好垃圾堆模型，给它（或其子节点）配 MeshCollider/碰撞体，拖到 surfaceCollider 字段；
///   - 在 entries 里填入候选垃圾预制体（需挂 ItemInformation 以标注 complexity）；
///   - 生成概率在 LevelDefinition.complexityComposition 统一配置。
/// </summary>
public class TrashHeap : MonoBehaviour
{
    [Serializable]
    public class TrashEntry
    {
        [Tooltip("候选垃圾预制体（建议挂 ItemInformation 以标注 complexity）。")]
        public GameObject prefab;
    }

    [Header("标识")]
    [Tooltip("场景里多个垃圾堆相互区分用的稳定 ID。LevelDefinition 里的 trashHeapOverrides 用此字段定位。留空则不可被关卡覆盖。")]
    public string heapId;

    [Header("候选垃圾")]
    [Tooltip("本堆可生成的预制体列表。抽取概率由 LevelDefinition.complexityComposition 按 ItemInformation.complexity 分桶决定。")]
    public TrashEntry[] entries;

    [Header("生成参数")]
    [Tooltip("开局在堆上生成多少个垃圾。")]
    [Min(0)] public int initialTrashCount = 12;

    [Tooltip("Start 时是否自动生成；false 则需要外部脚本调用 Spawn()。被 LevelManager 接管后会自动置 false。")]
    public bool spawnOnStart = true;

    [Tooltip("是否允许同一种预制体重复出现。关闭后单个预制体最多只会出现一次（直到被消耗完）。")]
    public bool allowDuplicates = true;

    public enum SpawnPlacementMode
    {
        /// <summary>在 surfaceCollider 上方随机采样落点（默认）。</summary>
        RandomSurface,
        /// <summary>仅在 spawnAnchors 列表里的锚点上生成。</summary>
        Anchors,
    }

    [Header("生成位置")]
    [Tooltip("RandomSurface = 在 surfaceCollider 上方随机采样落点（默认）；" +
             "Anchors = 仅在 spawnAnchors 指定的锚点上生成（一个锚点同一时间最多挂一件垃圾）。")]
    public SpawnPlacementMode placementMode = SpawnPlacementMode.RandomSurface;

    [Tooltip("Anchors 模式使用的锚点列表。每个 Transform 的 position 作为生成位置，transform.up 作为表面法线。" +
             "推荐把锚点做成垃圾堆模型的子节点，并按需要旋转 up 方向。")]
    public Transform[] spawnAnchors;

    [Tooltip("Anchors 模式：锚点上的垃圾被销毁后，是否允许该锚点被再次使用。" +
             "true = 节拍式 / 事件式再生可以反复使用同一锚点；" +
             "false = 一次性，每个锚点最多产出一件垃圾。")]
    public bool reuseAnchorsAfterDestroy = true;

    [Header("位置采样")]
    [Tooltip("RandomSurface 模式使用的垃圾堆碰撞体。留空则在自身/子节点上自动查找一个非 Trigger 的 Collider。" +
             "Anchors 模式下可不需要。")]
    public Collider surfaceCollider;

    [Tooltip("射线起点相对碰撞体最高点再向上抬升的距离，避免起点恰好嵌在 mesh 内部。")]
    public float raycastStartLift = 1f;

    [Tooltip("水平采样使用的 XZ 半径占碰撞体半尺寸的比例（0~1）。0.85 表示集中在堆中央 85% 范围内。")]
    [Range(0f, 1f)] public float horizontalSampleRatio = 0.85f;

    [Tooltip("埋入垃圾堆的比例（占物品自身高度的比例，按物品 up 轴测量）。" +
             "0 = 物品底部恰好贴住表面（看起来落在堆上）；" +
             "0.5 = 物品中部到表面（看起来一半埋进去）；" +
             "1 = 物品顶部贴住表面（看起来完全埋进去）。")]
    [Range(0f, 1f)] public float embedRatio = 0.3f;

    [Tooltip("法线与世界 Y 轴的夹角超过此值（度）的表面将被视为太陡而拒绝放置。")]
    [Range(0f, 89f)] public float maxSlopeAngle = 60f;

    [Tooltip("每次采样最多重试次数（避免堆形状刁钻时死循环）。")]
    [Min(1)] public int maxSampleAttempts = 12;

    [Header("姿态")]
    [Tooltip("物品 up 方向相对于地表法线的最大随机偏角（度）。" +
             "0 = 严格沿表面法线（最规整）；" +
             "30~60 = 自然杂乱；" +
             "90 = 可平躺；180 = 任意方向（含倒置）。")]
    [Range(0f, 180f)] public float maxTiltFromSurfaceNormal = 45f;

    [Tooltip("绕物体自身 up 轴的随机旋转。")]
    public bool randomYaw = true;

    [Header("生命周期")]
    [Tooltip("可选：生成的垃圾整理到此节点下，便于在 Hierarchy 中组织。" +
             "留空则放在世界根下（推荐，避免被 TrashHeap 自身 scale/rotation 干扰，并与项目 ItemSpawner 行为一致）。" +
             "若一定要指定，建议用一个 scale=(1,1,1) 的空物体。")]
    public Transform itemsRoot;

    [Tooltip("被关卡清理逻辑销毁后是否自动补满到 initialTrashCount（事件式：发生销毁立刻补；不受 respawnInterval 限制，但仍受 maxTotalSpawned 限制）。")]
    public bool refillToInitialOnItemDestroyed = false;

    [Header("再生（按时间补充）")]
    [Tooltip("启用后，关卡进行中按 respawnInterval 周期补充新垃圾，让堆上活跃数量趋近 respawnTargetActiveCount。" +
             "再生与 refillToInitialOnItemDestroyed 互不冲突，可以同时开。")]
    public bool respawnEnabled = false;

    [Tooltip("再生节拍间隔（秒）。值越小再生得越快。")]
    [Min(0.1f)] public float respawnInterval = 5f;

    [Tooltip("每个再生节拍最多生成多少件。多个时仍受 respawnTargetActiveCount / maxTotalSpawned 限制。")]
    [Min(1)] public int respawnPerBurst = 1;

    [Tooltip("再生希望保持的堆上活跃数量。0 = 沿用 initialTrashCount。" +
             "活跃数 < 此值时再生会触发；达到或超过时本节拍跳过。")]
    [Min(0)] public int respawnTargetActiveCount = 0;

    [Tooltip("整个关卡内（初始 + 再生）累计最多可生成数量。0 = 不限制。达到上限后再生会停止。")]
    [Min(0)] public int maxTotalSpawned = 0;

    [Tooltip("勾选后，生成的垃圾保持预制体自身缩放，不受 itemsRoot 节点 scale 的影响。" +
             "（仅在配置了 itemsRoot 时生效；实现：SetParent 时 worldPositionStays:true 自动反向补偿 localScale。）")]
    public bool keepPrefabScale = true;

    [Header("拔出动效（被玩家抓出时播放）")]
    [Tooltip("拔出时通过 SfxManager.PlayAt 播放的音效。留空则不播。")]
    public AudioClip pullOutSfx;

    [Range(0f, 2f)]
    [Tooltip("音效相对于 SfxManager 全局音量的额外缩放。")]
    public float pullOutSfxVolumeScale = 1f;

    [Tooltip("拔出时在物品位置实例化的粒子/特效预制体。留空则不播。")]
    public GameObject pullOutVfxPrefab;

    [Tooltip("VFX 预制体的存活时长（秒）。0 表示不自动销毁，由特效自行处理。")]
    [Min(0f)] public float pullOutVfxLifetime = 2f;

    [Tooltip("VFX 是否跟随物品（设为 true 时挂到物品下）；否则在世界位置独立播放。")]
    public bool pullOutVfxParentToItem = false;

    readonly List<GameObject> _activeItems = new List<GameObject>();
    readonly List<int> _remainingPrefabUses = new List<int>();
    readonly ComplexitySpawnPicker _spawnPicker = new ComplexitySpawnPicker();
    LevelComplexityComposition _levelComposition = new LevelComplexityComposition();
    bool _isLevelManaged;
    int _totalSpawnedCount;
    Coroutine _respawnRoutine;

    // 锚点占用追踪：Anchors 模式下使用。
    readonly Dictionary<GameObject, Transform> _instanceToAnchor = new Dictionary<GameObject, Transform>();
    readonly HashSet<Transform> _occupiedAnchors = new HashSet<Transform>();
    readonly HashSet<Transform> _retiredAnchors = new HashSet<Transform>();

    public IReadOnlyList<GameObject> ActiveItems => _activeItems;
    public bool IsLevelManaged => _isLevelManaged;
    public int TotalSpawnedCount => _totalSpawnedCount;
    public bool IsRespawning => _respawnRoutine != null;

    /// <summary>当前仍由本堆跟踪的场上垃圾数量（含未拔出与已拔出但未销毁的）。</summary>
    public int GetActiveItemCount()
    {
        PurgeNullActiveItems();
        return _activeItems.Count;
    }

    /// <summary>
    /// 本堆是否还会补充新垃圾（时间再生 / 销毁补满 / 活跃数低于目标且总量未达上限）。
    /// </summary>
    public bool CanSpawnMoreGarbage()
    {
        if (!isActiveAndEnabled) return false;
        if (entries == null || entries.Length == 0) return false;
        if (IsTotalQuotaExhausted()) return false;

        int active = GetActiveItemCount();
        int target = GetEffectiveTargetActiveCount();
        if (active >= target) return false;

        return respawnEnabled || refillToInitialOnItemDestroyed;
    }

    void PurgeNullActiveItems()
    {
        for (int i = _activeItems.Count - 1; i >= 0; i--)
        {
            if (_activeItems[i] == null)
                _activeItems.RemoveAt(i);
        }
    }

    /// <summary>当前希望维持的堆上活跃数量（用 respawnTargetActiveCount，<=0 时回退到 initialTrashCount）。</summary>
    public int GetEffectiveTargetActiveCount() =>
        respawnTargetActiveCount > 0 ? respawnTargetActiveCount : Mathf.Max(0, initialTrashCount);

    /// <summary>本关还能再生多少件（受 maxTotalSpawned 限制）。</summary>
    public int GetRemainingTotalQuota() =>
        maxTotalSpawned <= 0 ? int.MaxValue : Mathf.Max(0, maxTotalSpawned - _totalSpawnedCount);

    public bool IsTotalQuotaExhausted() =>
        maxTotalSpawned > 0 && _totalSpawnedCount >= maxTotalSpawned;

    void Awake()
    {
        if (surfaceCollider == null)
            surfaceCollider = ResolveSurfaceCollider();
        ConfigureSpawnPicker();
    }

    void Start()
    {
        if (spawnOnStart && !_isLevelManaged)
            ResetAndRespawn();
    }

    void OnDisable()
    {
        StopRespawn();
    }

    /// <summary>
    /// 由 <see cref="LevelManager"/> 在 Awake 阶段调用。设为 true 后，本组件不会再在 Start 里自动生成；
    /// 由 LevelManager 在 LoadLevel 时统一调用 <see cref="ResetAndRespawn"/> / <see cref="ApplyOverride"/>。
    /// </summary>
    public void SetLevelManaged(bool managed)
    {
        _isLevelManaged = managed;
    }

    /// <summary>
    /// 主动生成 count 个垃圾。会与 <see cref="allowDuplicates"/> / 复杂度构成共同决定抽取规则；
    /// 同时受 <see cref="maxTotalSpawned"/> 限制（可能少于 count）。
    /// </summary>
    public void Spawn(int count)
    {
        if (entries == null || entries.Length == 0)
        {
            Debug.LogWarning($"[TrashHeap] {name} 没有配置 entries，跳过生成。", this);
            return;
        }

        bool useAnchors = placementMode == SpawnPlacementMode.Anchors;
        if (useAnchors)
        {
            if (spawnAnchors == null || spawnAnchors.Length == 0)
            {
                Debug.LogWarning($"[TrashHeap] {name} 选择了 Anchors 模式但 spawnAnchors 为空，跳过生成。", this);
                return;
            }
        }
        else if (surfaceCollider == null)
        {
            Debug.LogWarning($"[TrashHeap] {name} 找不到 surfaceCollider，跳过生成。", this);
            return;
        }

        if (count <= 0) return;

        count = Mathf.Min(count, GetRemainingTotalQuota());
        if (count <= 0) return;

        if (!allowDuplicates)
            ResetRemainingUses();

        for (int i = 0; i < count; i++)
        {
            if (IsTotalQuotaExhausted()) break;
            GameObject prefab = PickPrefab();
            if (prefab == null) break;
            SpawnOne(prefab);
        }
    }

    /// <summary>清空所有当前由本堆生成的垃圾，并停止再生协程（不影响已被玩家拔出后销毁的物品；不重置累计计数）。</summary>
    public void ClearAll()
    {
        StopRespawn();
        for (int i = _activeItems.Count - 1; i >= 0; i--)
        {
            GameObject go = _activeItems[i];
            if (go != null) Destroy(go);
        }
        _activeItems.Clear();
        _occupiedAnchors.Clear();
        _retiredAnchors.Clear();
        _instanceToAnchor.Clear();
    }

    /// <summary>清空当前所有垃圾、重置累计计数，按当前 entries / initialTrashCount 重新生成，并按需启动再生循环。</summary>
    public void ResetAndRespawn()
    {
        ClearAll();
        _totalSpawnedCount = 0;
        Spawn(initialTrashCount);
        if (respawnEnabled) StartRespawn();
    }

    /// <summary>启动按时间再生的协程（已启动则忽略；respawnEnabled=false 时不启动）。</summary>
    public void StartRespawn()
    {
        if (_respawnRoutine != null) return;
        if (!respawnEnabled) return;
        if (!isActiveAndEnabled) return;
        _respawnRoutine = StartCoroutine(RespawnLoop());
    }

    /// <summary>停止按时间再生的协程。</summary>
    public void StopRespawn()
    {
        if (_respawnRoutine == null) return;
        StopCoroutine(_respawnRoutine);
        _respawnRoutine = null;
    }

    IEnumerator RespawnLoop()
    {
        // 每个节拍：等待 → 检查活跃 / 总量 → 按需生成最多 respawnPerBurst 件。
        while (true)
        {
            float wait = Mathf.Max(0.1f, respawnInterval);
            yield return new WaitForSeconds(wait);

            if (!respawnEnabled) break;
            if (IsTotalQuotaExhausted()) break;

            int target = GetEffectiveTargetActiveCount();
            int activeRoom = Mathf.Max(0, target - _activeItems.Count);
            if (activeRoom <= 0) continue;

            int totalRoom = GetRemainingTotalQuota();
            int batch = Mathf.Min(respawnPerBurst, Mathf.Min(activeRoom, totalRoom));
            if (batch <= 0) continue;

            // 不重置 _remainingPrefabUses：respawn 各节拍间允许独立抽取（同 ItemSpawner 行为）。
            for (int i = 0; i < batch; i++)
            {
                if (IsTotalQuotaExhausted()) break;
                GameObject prefab = PickPrefab();
                if (prefab == null) break;
                SpawnOne(prefab);
            }
        }
        _respawnRoutine = null;
    }

    /// <summary>
    /// 应用关卡级覆盖配置，并绑定关卡复杂度构成。会按需替换 entries / initialTrashCount，然后清空并重新生成。
    /// </summary>
    public void ApplyOverride(LevelTrashHeapOverride overrideData, LevelComplexityComposition levelComposition)
    {
        _levelComposition = levelComposition ?? new LevelComplexityComposition();

        if (overrideData == null)
        {
            ResetAndRespawn();
            return;
        }

        if (overrideData.entries != null)
        {
            int n = overrideData.entries.Length;
            entries = new TrashEntry[n];
            for (int i = 0; i < n; i++)
            {
                LevelTrashHeapOverride.Entry src = overrideData.entries[i];
                entries[i] = new TrashEntry
                {
                    prefab = src != null ? src.prefab : null,
                };
            }
            ConfigureSpawnPicker();
        }

        initialTrashCount = Mathf.Max(0, overrideData.initialTrashCount);
        allowDuplicates = overrideData.allowDuplicates;
        respawnEnabled = overrideData.respawnEnabled;
        respawnInterval = Mathf.Max(0.1f, overrideData.respawnInterval);
        respawnPerBurst = Mathf.Max(1, overrideData.respawnPerBurst);
        respawnTargetActiveCount = Mathf.Max(0, overrideData.respawnTargetActiveCount);
        maxTotalSpawned = Mathf.Max(0, overrideData.maxTotalSpawned);

        ResetAndRespawn();
    }

    void ConfigureSpawnPicker()
    {
        int count = entries != null ? entries.Length : 0;
        _spawnPicker.Configure(i => entries[i]?.prefab, count);
    }

    void OnValidate()
    {
        ConfigureSpawnPicker();
    }

    /// <summary>
    /// 由 <see cref="EmbeddedTrashItem"/> 在玩家把垃圾抓出时调用：播放音效 + 特效。
    /// </summary>
    internal void PlayPullOutEffects(Vector3 worldPosition)
    {
        if (pullOutSfx != null)
        {
            SfxManager sfx = SfxManager.Instance;
            if (sfx != null)
                sfx.PlayAt(pullOutSfx, worldPosition, pullOutSfxVolumeScale);
            else
                AudioSource.PlayClipAtPoint(pullOutSfx, worldPosition, pullOutSfxVolumeScale);
        }

        if (pullOutVfxPrefab != null)
        {
            Transform parent = pullOutVfxParentToItem ? itemsRoot : null;
            GameObject vfx = Instantiate(pullOutVfxPrefab, worldPosition, Quaternion.identity, parent);
            if (pullOutVfxLifetime > 0f)
                Destroy(vfx, pullOutVfxLifetime);
        }
    }

    void ResetRemainingUses()
    {
        _remainingPrefabUses.Clear();
        for (int i = 0; i < entries.Length; i++)
            _remainingPrefabUses.Add(1);
    }

    GameObject PickPrefab()
    {
        if (entries == null || entries.Length == 0) return null;

        int index = _spawnPicker.PickEntryIndex(_levelComposition, IsEntryAvailable);
        if (index < 0) return null;
        return PickEntryPrefab(index);
    }

    bool IsEntryAvailable(int index)
    {
        if (index < 0 || index >= entries.Length) return false;
        TrashEntry entry = entries[index];
        if (entry == null || entry.prefab == null) return false;
        if (allowDuplicates) return true;
        if (_remainingPrefabUses.Count != entries.Length) return true;
        return _remainingPrefabUses[index] > 0;
    }

    GameObject PickEntryPrefab(int entryIndex)
    {
        if (entryIndex < 0 || entryIndex >= entries.Length) return null;
        var e = entries[entryIndex];
        if (e == null || e.prefab == null) return null;
        ConsumeUse(entryIndex);
        return e.prefab;
    }

    void ConsumeUse(int index)
    {
        if (allowDuplicates) return;
        if (_remainingPrefabUses.Count != entries.Length) return;
        _remainingPrefabUses[index] = Mathf.Max(0, _remainingPrefabUses[index] - 1);
    }

    void SpawnOne(GameObject prefab)
    {
        Vector3 hitPoint;
        Vector3 hitNormal;
        Transform anchor = null;

        if (placementMode == SpawnPlacementMode.Anchors)
        {
            if (!TryPickFreeAnchor(out anchor))
                return; // 没有可用锚点（占用满或全已退役）；调用方会停止本批
            hitPoint = anchor.position;
            hitNormal = anchor.up;
        }
        else
        {
            if (!TrySampleSurface(out hitPoint, out hitNormal))
            {
                Debug.LogWarning($"[TrashHeap] {name} 采样失败（可能 surfaceCollider 太小或斜率限制太严）。", this);
                return;
            }
        }

        // 在表面法线基础上做随机 tilt + yaw，得到物品最终的 up 朝向与旋转。
        Vector3 up = ComputeTiltedUp(hitNormal, maxTiltFromSurfaceNormal);
        float yaw = randomYaw ? UnityEngine.Random.Range(0f, 360f) : 0f;
        Quaternion rotation = Quaternion.AngleAxis(yaw, up) * Quaternion.FromToRotation(Vector3.up, up);

        // 始终先在世界根下实例化，保留预制体的原始 lossyScale。
        // 这样可以避免 TrashHeap 自身 transform 的非 1 scale / 旋转通过 Rigidbody 抓取-松开过程
        // 在 Unity 内部 transform 分解时被"修正"成与父节点同样的缩放。
        GameObject instance = Instantiate(prefab, hitPoint, rotation);

        // 按 embedRatio 沿物品自身 up 轴下沉：0=底部贴表面；1=顶部贴表面。
        ApplyEmbedOffset(instance, hitPoint, up);

        if (itemsRoot != null)
        {
            // 用户显式指定了组织节点。worldPositionStays:keepPrefabScale 用来决定是否反向补偿 localScale。
            instance.transform.SetParent(itemsRoot, worldPositionStays: keepPrefabScale);
        }

        ItemSpawner.EnsureItemInformation(instance, prefab);
        ItemSpawner.EnsurePhysics(instance);

        var rb = instance.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        }

        var embedded = instance.GetComponent<EmbeddedTrashItem>();
        if (embedded == null) embedded = instance.AddComponent<EmbeddedTrashItem>();
        embedded.AttachToHeap(this);

        _activeItems.Add(instance);
        _totalSpawnedCount++;

        if (anchor != null)
        {
            _occupiedAnchors.Add(anchor);
            _instanceToAnchor[instance] = anchor;
        }
    }

    /// <summary>从 spawnAnchors 中挑一个未被占用、（按需）未退役的锚点。</summary>
    bool TryPickFreeAnchor(out Transform anchor)
    {
        anchor = null;
        if (spawnAnchors == null || spawnAnchors.Length == 0) return false;

        // 复用一个临时列表会稍微好一点；这里项目不缺这点 GC，先简单实现。
        List<Transform> free = new List<Transform>(spawnAnchors.Length);
        for (int i = 0; i < spawnAnchors.Length; i++)
        {
            Transform a = spawnAnchors[i];
            if (a == null) continue;
            if (_occupiedAnchors.Contains(a)) continue;
            if (!reuseAnchorsAfterDestroy && _retiredAnchors.Contains(a)) continue;
            free.Add(a);
        }
        if (free.Count == 0) return false;

        anchor = free[UnityEngine.Random.Range(0, free.Count)];
        return true;
    }

    bool TrySampleSurface(out Vector3 hitPoint, out Vector3 hitNormal)
    {
        hitPoint = default;
        hitNormal = Vector3.up;

        Bounds b = surfaceCollider.bounds;
        Vector3 c = b.center;
        Vector3 ext = b.extents;
        float rayLength = ext.y * 2f + raycastStartLift * 2f + 1f;

        for (int attempt = 0; attempt < maxSampleAttempts; attempt++)
        {
            float dx = UnityEngine.Random.Range(-1f, 1f) * ext.x * horizontalSampleRatio;
            float dz = UnityEngine.Random.Range(-1f, 1f) * ext.z * horizontalSampleRatio;
            Vector3 origin = new Vector3(c.x + dx, b.max.y + raycastStartLift, c.z + dz);

            // 仅命中本堆的碰撞体，避免射到地板或其它物体上
            if (!surfaceCollider.Raycast(new Ray(origin, Vector3.down), out RaycastHit hit, rayLength))
                continue;

            float angle = Vector3.Angle(hit.normal, Vector3.up);
            if (angle > maxSlopeAngle) continue;

            hitPoint = hit.point;
            hitNormal = hit.normal;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 在表面法线基础上随机偏转一个角度，得到物品的 up 方向。
    /// maxTiltDeg = 0 时严格贴法线；越大越杂乱。
    /// </summary>
    static Vector3 ComputeTiltedUp(Vector3 surfaceNormal, float maxTiltDeg)
    {
        if (maxTiltDeg <= 0f || surfaceNormal.sqrMagnitude < 1e-6f)
            return surfaceNormal.sqrMagnitude < 1e-6f ? Vector3.up : surfaceNormal.normalized;

        Vector3 n = surfaceNormal.normalized;

        // 在 n 周围构造一组正交基
        Vector3 reference = Mathf.Abs(Vector3.Dot(n, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right;
        Vector3 tangent = Vector3.Cross(n, reference);
        if (tangent.sqrMagnitude < 1e-6f) tangent = Vector3.Cross(n, Vector3.forward);
        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(n, tangent);

        // 在 [0, maxTiltDeg] 锥角内、随机方向上摆出一个新的 up
        float tiltRad = UnityEngine.Random.Range(0f, maxTiltDeg) * Mathf.Deg2Rad;
        float spinRad = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        Vector3 inPlane = Mathf.Cos(spinRad) * tangent + Mathf.Sin(spinRad) * bitangent;
        return (Mathf.Cos(tiltRad) * n + Mathf.Sin(tiltRad) * inPlane).normalized;
    }

    /// <summary>
    /// 实例化后按 embedRatio 沿物品自身 up 轴下沉。原点位于 hitPoint 时：
    /// ratio=0 → 包围盒底部贴 hitPoint；ratio=1 → 包围盒顶部贴 hitPoint。
    /// 自动兼容 pivot 不在中心 / 不在底部的预制体。
    /// </summary>
    void ApplyEmbedOffset(GameObject instance, Vector3 hitPoint, Vector3 up)
    {
        if (embedRatio <= 0f) return;
        if (!TryGetWorldRendererBounds(instance, out Bounds wb)) return;

        Vector3 ext = wb.extents;
        float halfExtentAlongUp =
            ext.x * Mathf.Abs(up.x) + ext.y * Mathf.Abs(up.y) + ext.z * Mathf.Abs(up.z);
        float fullExtentAlongUp = halfExtentAlongUp * 2f;
        if (fullExtentAlongUp <= 0f) return;

        // 当前 pivot 在 hitPoint，pivot→bounds.center 在 up 方向上的偏移。
        float pivotToCenterAlongUp = Vector3.Dot(wb.center - instance.transform.position, up);

        // 目标：bounds.bottomAlongUp = hitPointAlongUp - ratio * fullExtentAlongUp
        // 推得 pivot 沿 up 需要平移 shift = halfExtent - ratio * fullExtent - pivotToCenter
        float shift = halfExtentAlongUp - embedRatio * fullExtentAlongUp - pivotToCenterAlongUp;
        instance.transform.position += up * shift;
    }

    static bool TryGetWorldRendererBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
        bool any = false;
        for (int i = 0; i < rs.Length; i++)
        {
            Renderer r = rs[i];
            if (r == null) continue;
            if (r is ParticleSystemRenderer) continue;

            if (!any)
            {
                bounds = r.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }
        return any;
    }

    Collider ResolveSurfaceCollider()
    {
        var colliders = GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null && !colliders[i].isTrigger)
                return colliders[i];
        }
        return null;
    }

    internal void NotifyItemPromoted(GameObject item)
    {
        // 物品已被玩家抓取并晋升为自由物理。仍保留在 _activeItems 中以便统计；销毁时再移除。
    }

    internal void NotifyItemDestroyed(GameObject item)
    {
        if (item == null) return;
        _activeItems.Remove(item);

        if (_instanceToAnchor.TryGetValue(item, out Transform anchor))
        {
            _instanceToAnchor.Remove(item);
            _occupiedAnchors.Remove(anchor);
            if (!reuseAnchorsAfterDestroy)
                _retiredAnchors.Add(anchor);
        }

        if (!refillToInitialOnItemDestroyed) return;
        if (IsTotalQuotaExhausted()) return;

        int target = GetEffectiveTargetActiveCount();
        if (_activeItems.Count >= target) return;

        int delta = target - _activeItems.Count;
        Spawn(delta);   // Spawn 内部已按 maxTotalSpawned 截断
    }

    void OnDrawGizmosSelected()
    {
        if (placementMode == SpawnPlacementMode.RandomSurface && surfaceCollider != null)
        {
            Bounds b = surfaceCollider.bounds;
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.25f);
            Vector3 size = new Vector3(b.size.x * horizontalSampleRatio, 0.02f, b.size.z * horizontalSampleRatio);
            Gizmos.DrawCube(new Vector3(b.center.x, b.max.y + 0.01f, b.center.z), size);
        }

        if (placementMode == SpawnPlacementMode.Anchors && spawnAnchors != null)
        {
            for (int i = 0; i < spawnAnchors.Length; i++)
            {
                Transform a = spawnAnchors[i];
                if (a == null) continue;

                bool retired = _retiredAnchors != null && _retiredAnchors.Contains(a);
                bool occupied = _occupiedAnchors != null && _occupiedAnchors.Contains(a);

                Gizmos.color = retired ? new Color(0.6f, 0.6f, 0.6f, 0.7f)
                              : occupied ? new Color(1f, 0.5f, 0.2f, 0.9f)
                              : new Color(1f, 0.95f, 0.2f, 0.9f);

                Gizmos.DrawSphere(a.position, 0.05f);
                Gizmos.DrawLine(a.position, a.position + a.up * 0.25f);
            }
        }
    }
}

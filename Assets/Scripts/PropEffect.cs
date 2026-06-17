using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 道具效果统一组件：Inspector 顶部 <see cref="propType"/> 切换道具类型，
/// 切换后仅显示对应类型的设置（见 PropEffectEditor）。
///
/// 各类型行为：
/// - <see cref="PropType.Coin"/>: 玩家拾取时一次性触发（粒子 / 音效 / 加分 / 销毁）。
/// - <see cref="PropType.Magnet"/>: 玩家持握期间持续吸附附近的基础金属垃圾（Metal + Basic），
///   吸附后与磁石合并为单一刚体；松手后仍跟随磁石；投入分拣通道时一次性结算总价。
/// - <see cref="PropType.Lighter"/>: 玩家持握期间将范围内指定分类的物品（默认 OrganicMatter）点燃并延迟销毁。
///
/// 使用：物品 prefab 上挂 <see cref="ItemInformation"/>（category = Prop）和本组件，按需配置即可。
/// </summary>
[DisallowMultipleComponent]
public class PropEffect : MonoBehaviour
{
    public enum PropType
    {
        Coin,
        Magnet,
        Lighter,
    }

    [Tooltip("道具类型；切换后下方仅显示对应类型的设置。")]
    public PropType propType = PropType.Coin;

    [Tooltip("仅 Coin 生效：触发一次后注销监听。")]
    public bool triggerOnce = true;

    public CoinConfig coin = new CoinConfig();
    public MagnetConfig magnet = new MagnetConfig();
    public LighterConfig lighter = new LighterConfig();

    // ------------------------------------------------------------------
    // 序列化子配置
    // ------------------------------------------------------------------

    [System.Serializable]
    public class CoinConfig
    {
        [Header("拾取奖励")]
        [Tooltip("被拾取后给玩家增加的信用点数；允许为负。")]
        public int creditReward = 100;

        [Tooltip("是否在屏幕底部弹出 +N credits 字幕。")]
        public bool showSubtitle = true;

        [Min(0.1f)] public float subtitleDuration = 1.5f;
        public Color subtitleColor = new Color(1f, 0.85f, 0.2f, 1f);

        [Header("视觉特效")]
        [Tooltip("拾取瞬间在物体位置生成的粒子/特效 prefab；留空则不生成。")]
        public GameObject pickupVfxPrefab;

        [Tooltip("VFX 自动销毁时间（秒）；<= 0 表示不自动销毁。")]
        public float vfxLifetime = 2f;

        [Header("音效")]
        [Tooltip("额外的拾取音效；不填则只用 CreditManager.AddCredits 里默认的金币音效。")]
        public AudioClip pickupSfx;

        [Range(0f, 1f)] public float pickupSfxVolume = 1f;

        [Header("销毁")]
        [Tooltip("拾取后是否销毁本 GameObject。")]
        public bool destroyOnPickup = true;
    }

    [System.Serializable]
    public class MagnetConfig
    {
        [Header("吸附目标")]
        [Tooltip("会被吸附的垃圾分类（须同时满足 complexity = Basic，可直接投入对应通道的单体物）。")]
        public ItemInformation.ItemCategory targetCategory = ItemInformation.ItemCategory.Metal;

        [Tooltip("吸附检测的 LayerMask。")]
        public LayerMask attractMask = ~0;

        [Header("吸附参数")]
        [Min(0.05f)]
        [Tooltip("以磁石中心为球心的吸附半径（米）。")]
        public float attractRadius = 1.6f;

        [Min(1)]
        [Tooltip("可同时吸附的垃圾数量上限。")]
        public int maxAttachedItems = 5;

        [Min(0f)]
        [Tooltip("吸附后磁石表面与物品表面之间的额外间隙（米）；多件物品会沿吸附方向自动错开。")]
        public float attachOffset = 0.02f;

        [Tooltip("两次吸附检测的间隔（秒），用于节流性能。")]
        [Min(0.02f)] public float attractInterval = 0.08f;

        [Header("音效")]
        public AudioClip attachSfx;
        [Range(0f, 1f)] public float attachSfxVolume = 0.8f;

        [Header("投入通道")]
        [Tooltip("被投入分拣通道时，把所有附着物的 creditsOnCorrectThrow 加总作为奖励。\n" +
                 "错误分类的物品按 AisleDetection.wrongAislePenalty 计入。")]
        public bool sumCreditsOnAisleHit = true;
    }

    [System.Serializable]
    public class LighterConfig
    {
        [Header("点燃目标")]
        public ItemInformation.ItemCategory targetCategory = ItemInformation.ItemCategory.OrganicMatter;
        public LayerMask burnMask = ~0;

        [Min(0.05f)]
        [Tooltip("以打火机中心为球心的引燃半径（米）。")]
        public float ignitionRadius = 0.8f;

        [Min(0f)]
        [Tooltip("点燃后多少秒销毁目标物体。")]
        public float burnDelay = 1.5f;

        [Tooltip("引燃 / 销毁时在目标位置生成的特效；留空则不生成。")]
        public GameObject burnVfxPrefab;

        [Tooltip("特效自动销毁时间（秒）；<= 0 表示不自动销毁。")]
        public float burnVfxLifetime = 2f;

        [Tooltip("引燃时播放的音效；留空则静默。")]
        public AudioClip burnSfx;
        [Range(0f, 1f)] public float burnSfxVolume = 0.8f;
    }

    // ------------------------------------------------------------------
    // 共用 / 类型相关运行时状态
    // ------------------------------------------------------------------

    CharacterInteraction _interaction;

    // Coin
    bool _coinTriggered;

    // Magnet
    bool _magnetHeld;                                 // 仅持握时才主动吸附
    float _nextAttractCheckTime;
    readonly List<GameObject> _attachedItems = new List<GameObject>();
    readonly HashSet<int> _attachedIds = new HashSet<int>();

    // Lighter
    bool _lighterHeld;
    readonly HashSet<int> _burnScheduled = new HashSet<int>();

    // 全局标记：被某个磁石认领的物品 InstanceID。AisleDetection / 其它磁石用它来跳过这些物品。
    static readonly HashSet<int> s_MagnetClaimedItems = new HashSet<int>();

    public static bool IsClaimedByMagnet(GameObject go) =>
        go != null && s_MagnetClaimedItems.Contains(go.GetInstanceID());

    // ------------------------------------------------------------------
    // 生命周期
    // ------------------------------------------------------------------

    void OnEnable()
    {
        TryBindInteraction();
    }

    void OnDisable()
    {
        if (_interaction != null)
        {
            _interaction.Grabbed -= OnGrabbed;
            _interaction.Released -= OnReleased;
            _interaction.Thrown -= OnReleased;
            _interaction = null;
        }

        // 安全清理：磁石被销毁时把附着物释放为 Dynamic，避免它们随父物体一并消失。
        ReleaseAttachedItems(destroyThem: false);
        _magnetHeld = false;
        _lighterHeld = false;
    }

    void TryBindInteraction()
    {
        if (_interaction != null) return;

#if UNITY_2023_1_OR_NEWER
        _interaction = Object.FindAnyObjectByType<CharacterInteraction>();
#else
        _interaction = Object.FindObjectOfType<CharacterInteraction>();
#endif
        if (_interaction == null) return;

        _interaction.Grabbed += OnGrabbed;
        _interaction.Released += OnReleased;
        _interaction.Thrown += OnReleased;
    }

    void OnGrabbed(GameObject grabbed)
    {
        if (grabbed != gameObject) return;

        switch (propType)
        {
            case PropType.Coin:
                HandleCoinGrabbed();
                break;
            case PropType.Magnet:
                _magnetHeld = true;
                _nextAttractCheckTime = 0f; // 立即检测一次
                break;
            case PropType.Lighter:
                _lighterHeld = true;
                break;
        }
    }

    void OnReleased()
    {
        // Released / Thrown 不带参数：松手时只把当前持握状态置 false。
        // 磁石松手后保留附着物，不主动吸附新物品；打火机松手则停止点燃新目标。
        _magnetHeld = false;
        _lighterHeld = false;
    }

    void FixedUpdate()
    {
        if (propType == PropType.Magnet && _magnetHeld)
            TickMagnetAttraction();
    }

    void Update()
    {
        if (propType == PropType.Lighter && _lighterHeld)
            TickLighterBurn();
    }

    // ------------------------------------------------------------------
    // COIN
    // ------------------------------------------------------------------

    void HandleCoinGrabbed()
    {
        if (triggerOnce && _coinTriggered) return;
        _coinTriggered = true;

        if (_interaction != null)
            _interaction.ForceReleaseIfHolding(gameObject);

        Vector3 pos = transform.position;
        SpawnCoinVfx(pos);
        PlayCoinSfx(pos);
        AwardCoinCredits();

        if (coin.destroyOnPickup)
            Destroy(gameObject);
    }

    void SpawnCoinVfx(Vector3 pos)
    {
        if (coin.pickupVfxPrefab == null) return;
        GameObject vfx = Instantiate(coin.pickupVfxPrefab, pos, Quaternion.identity);
        if (coin.vfxLifetime > 0f)
            Destroy(vfx, coin.vfxLifetime);
    }

    void PlayCoinSfx(Vector3 pos)
    {
        if (coin.pickupSfx == null) return;
        AudioSource.PlayClipAtPoint(coin.pickupSfx, pos, Mathf.Clamp01(coin.pickupSfxVolume));
    }

    void AwardCoinCredits()
    {
        if (coin.creditReward == 0) return;
        if (CreditManager.Instance == null) return;

        CreditManager.Instance.AddCredits(coin.creditReward);

        if (coin.showSubtitle)
        {
            string text = coin.creditReward >= 0
                ? $"+{coin.creditReward} credits"
                : $"{coin.creditReward} credits";
            CreditManager.Instance.ShowSubtitle(text, coin.subtitleDuration, coin.subtitleColor);
        }
    }

    // ------------------------------------------------------------------
    // MAGNET
    // ------------------------------------------------------------------

    void TickMagnetAttraction()
    {
        if (_attachedItems.Count >= magnet.maxAttachedItems) return;
        if (Time.time < _nextAttractCheckTime) return;
        _nextAttractCheckTime = Time.time + magnet.attractInterval;

        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            magnet.attractRadius,
            magnet.attractMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            if (_attachedItems.Count >= magnet.maxAttachedItems) break;
            Collider c = hits[i];
            if (c == null) continue;

            ItemInformation info = c.GetComponentInParent<ItemInformation>();
            if (!IsMagnetAttractable(info)) continue;

            Rigidbody rb = info.GetComponentInParent<Rigidbody>();
            if (rb == null) continue;
            if (rb.gameObject == gameObject) continue;

            int id = rb.gameObject.GetInstanceID();
            if (_attachedIds.Contains(id)) continue;
            if (s_MagnetClaimedItems.Contains(id)) continue;
            if (rb.transform.IsChildOf(transform)) continue;

            AttachItem(rb);
        }
    }

    /// <summary>
    /// 仅吸附可直接投入目标通道的基础单体物（如 Basic_metal），排除复合物 / 危险品 / 道具等。
    /// </summary>
    bool IsMagnetAttractable(ItemInformation info)
    {
        if (info == null) return false;
        if (info.gameObject == gameObject) return false;
        if (info.category != magnet.targetCategory) return false;
        if (info.complexity != ItemInformation.ItemComplexity.Basic) return false;
        if (info.GetComponentInParent<InspectableItem>() != null) return false;
        return true;
    }

    void AttachItem(Rigidbody rb)
    {
        if (rb == null) return;

        GameObject itemRoot = rb.gameObject;
        Transform itemTransform = rb.transform;

        Vector3 dir = rb.position - transform.position;
        if (dir.sqrMagnitude < 1e-6f) dir = Random.onUnitSphere;
        dir.Normalize();

        float magnetExtent = GetColliderExtentAlong(transform, dir, includeChildren: false);
        float snapDistance = ComputeSnapDistanceAlongDir(dir, itemTransform, magnetExtent);
        Vector3 snapPos = transform.position + dir * snapDistance;

        itemTransform.SetParent(transform, worldPositionStays: true);
        itemTransform.position = snapPos;

        // 移除子刚体，碰撞体并入磁石刚体，抓取任意部件时整体跟随磁石移动。
        Destroy(rb);

        int id = itemRoot.GetInstanceID();
        _attachedItems.Add(itemRoot);
        _attachedIds.Add(id);
        s_MagnetClaimedItems.Add(id);

        if (magnet.attachSfx != null)
            AudioSource.PlayClipAtPoint(magnet.attachSfx, transform.position, Mathf.Clamp01(magnet.attachSfxVolume));
    }

    /// <summary>
    /// 由 <see cref="AisleDetection"/> 在 Prop 类（磁石）进入触发区时调用。
    /// 返回 true 表示已自行处理（含销毁附着物 + 自销毁 + 加分），AisleDetection 应跳过默认销毁；
    /// 返回 false 表示本组件不接管，AisleDetection 继续按 Prop 默认流程销毁磁石。
    /// </summary>
    public bool HandleMagnetAisleThrow(AisleDetection aisle)
    {
        if (propType != PropType.Magnet) return false;
        if (aisle == null) return false;
        if (!magnet.sumCreditsOnAisleHit) return false;
        if (_attachedItems.Count == 0) return false;

        int totalCredits = 0;
        int matchCount = 0;
        int mismatchCount = 0;

        for (int i = 0; i < _attachedItems.Count; i++)
        {
            GameObject item = _attachedItems[i];
            if (item == null) continue;

            ItemInformation info = item.GetComponentInParent<ItemInformation>();
            int id = item.GetInstanceID();
            s_MagnetClaimedItems.Remove(id);

            if (info != null)
            {
                if (info.category == aisle.aisleCategory)
                {
                    totalCredits += Mathf.Max(0, info.creditsOnCorrectThrow);
                    matchCount++;
                }
                else if (aisle.wrongAislePenalty != 0)
                {
                    totalCredits += aisle.wrongAislePenalty;
                    mismatchCount++;
                }
            }

            Destroy(item);
        }
        _attachedItems.Clear();
        _attachedIds.Clear();

        if (matchCount > 0 && SfxManager.Instance != null)
            SfxManager.Instance.PlayCorrectThrow();
        else if (mismatchCount > 0 && SfxManager.Instance != null)
            SfxManager.Instance.PlayWrongThrow();

        if (CreditManager.Instance != null && totalCredits != 0)
        {
            CreditManager.Instance.AddCredits(totalCredits);
            string subtitle = totalCredits >= 0
                ? $"+{totalCredits} credits ({matchCount}件)"
                : $"{totalCredits} credits ({mismatchCount}件错分类)";
            Color color = totalCredits >= 0
                ? new Color(0.4f, 1f, 0.4f, 1f)
                : Color.red;
            CreditManager.Instance.ShowSubtitle(subtitle, 2f, color);
        }

        Destroy(gameObject);
        return true;
    }

    /// <summary>
    /// 沿 <paramref name="worldDir"/> 从 <paramref name="origin"/> 到碰撞体外沿的最大投影距离（米）。
    /// 磁石只统计自身 Collider，避免已附着物撑大吸附半径。
    /// </summary>
    static float GetColliderExtentAlong(Transform origin, Vector3 worldDir, bool includeChildren)
    {
        if (origin == null) return 0f;

        Collider[] colliders = includeChildren
            ? origin.GetComponentsInChildren<Collider>()
            : origin.GetComponents<Collider>();

        worldDir.Normalize();
        Vector3 originPos = origin.position;
        float maxProj = 0f;
        bool found = false;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider col = colliders[i];
            if (col == null || col.isTrigger) continue;

            Bounds b = col.bounds;
            Vector3 c = b.center;
            Vector3 e = b.extents;

            for (int bx = 0; bx < 2; bx++)
            {
                for (int by = 0; by < 2; by++)
                {
                    for (int bz = 0; bz < 2; bz++)
                    {
                        Vector3 corner = new Vector3(
                            bx == 0 ? c.x - e.x : c.x + e.x,
                            by == 0 ? c.y - e.y : c.y + e.y,
                            bz == 0 ? c.z - e.z : c.z + e.z);
                        float proj = Vector3.Dot(corner - originPos, worldDir);
                        if (proj > maxProj) maxProj = proj;
                        found = true;
                    }
                }
            }
        }

        return found ? maxProj : 0f;
    }

    /// <summary>
    /// 沿吸附方向从磁石中心到待附着物刚体中心的距离。
    /// </summary>
    float ComputeSnapDistanceAlongDir(Vector3 dir, Transform itemTransform, float magnetExtent)
    {
        float dist = magnetExtent + magnet.attachOffset;

        for (int i = 0; i < _attachedItems.Count; i++)
        {
            GameObject attached = _attachedItems[i];
            if (attached == null) continue;

            Vector3 attachedDir = attached.transform.position - transform.position;
            if (attachedDir.sqrMagnitude < 1e-6f) continue;
            attachedDir.Normalize();
            if (Vector3.Dot(attachedDir, dir) < 0.55f) continue;

            dist += GetColliderExtentAlong(attached.transform, dir, includeChildren: true);
            dist += GetColliderExtentAlong(attached.transform, -dir, includeChildren: true);
        }

        dist += GetColliderExtentAlong(itemTransform, -dir, includeChildren: true);
        return dist;
    }

    void ReleaseAttachedItems(bool destroyThem)
    {
        for (int i = 0; i < _attachedItems.Count; i++)
        {
            GameObject item = _attachedItems[i];
            if (item == null) continue;

            s_MagnetClaimedItems.Remove(item.GetInstanceID());

            if (destroyThem)
            {
                Destroy(item);
                continue;
            }

            item.transform.SetParent(null, true);
            Rigidbody restoredRb = item.GetComponent<Rigidbody>();
            if (restoredRb == null)
                restoredRb = item.AddComponent<Rigidbody>();
            restoredRb.isKinematic = false;
            restoredRb.useGravity = true;
            restoredRb.detectCollisions = true;
            restoredRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        _attachedItems.Clear();
        _attachedIds.Clear();
    }

    // ------------------------------------------------------------------
    // LIGHTER
    // ------------------------------------------------------------------

    void TickLighterBurn()
    {
        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            lighter.ignitionRadius,
            lighter.burnMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i];
            if (c == null) continue;

            ItemInformation info = c.GetComponentInParent<ItemInformation>();
            if (info == null) continue;
            if (info.category != lighter.targetCategory) continue;
            if (info.gameObject == gameObject) continue;

            int id = info.gameObject.GetInstanceID();
            if (!_burnScheduled.Add(id)) continue;

            StartCoroutine(BurnAfterDelay(info.gameObject));
        }
    }

    IEnumerator BurnAfterDelay(GameObject target)
    {
        if (lighter.burnDelay > 0f)
            yield return new WaitForSeconds(lighter.burnDelay);

        if (target == null) yield break;

        if (lighter.burnVfxPrefab != null)
        {
            GameObject vfx = Instantiate(lighter.burnVfxPrefab, target.transform.position, Quaternion.identity);
            if (lighter.burnVfxLifetime > 0f)
                Destroy(vfx, lighter.burnVfxLifetime);
        }

        if (lighter.burnSfx != null)
            AudioSource.PlayClipAtPoint(lighter.burnSfx, target.transform.position, Mathf.Clamp01(lighter.burnSfxVolume));

        Destroy(target);
    }

    // ------------------------------------------------------------------
    // Gizmo：在 Scene 视图里可视化磁石 / 打火机的作用范围
    // ------------------------------------------------------------------

    void OnDrawGizmosSelected()
    {
        switch (propType)
        {
            case PropType.Magnet:
                Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.35f);
                Gizmos.DrawWireSphere(transform.position, magnet.attractRadius);
                break;
            case PropType.Lighter:
                Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.35f);
                Gizmos.DrawWireSphere(transform.position, lighter.ignitionRadius);
                break;
        }
    }
}

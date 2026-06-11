using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 「死亡平面」：场上 Y 低于此平面的物品会被自动销毁，避免掉落到地板下方后变成无法回收的废物。
///
/// 用法：
/// 1. 场景里新建空物体（例如命名 "KillPlane"），把它拖到地板下方约 2~5m 的位置；
/// 2. 挂上本组件；killY 默认跟随自身 transform.position.y，调整空物体高度即可调整死亡平面；
/// 3. 默认会扫描场上所有挂有 <see cref="ItemInformation"/> 的物品（含切割/分离碎片、垃圾堆产物等），
///    以及 Knife / Screwdriver 工具；玩家手中的物品会先被强制放开再销毁。
/// </summary>
[DisallowMultipleComponent]
public class WorldKillPlane : MonoBehaviour
{
    [Header("死亡平面")]
    [Tooltip("若开启，killY 自动跟随本物体的世界 Y 坐标（推荐，方便在 Scene 视图里直接拖动定位）。")]
    public bool followTransformY = true;

    [Tooltip("世界 Y 阈值；物品 Y 低于此值即销毁。followTransformY 开启时此字段每帧被覆盖。")]
    public float killY = -10f;

    [Header("扫描设置")]
    [Tooltip("两次扫描之间的间隔（秒）。0 = 每帧扫描；通常 0.25~1s 足够。")]
    [Min(0f)] public float checkInterval = 0.5f;

    [Tooltip("是否一并清理 Knife / Screwdriver 这类没有 ItemInformation 的工具。")]
    public bool includeTools = true;

    [Tooltip("是否在控制台打印销毁日志，便于调试。")]
    public bool debugLog = false;

    [Header("可视化")]
    [Tooltip("Scene 视图中预览平面的水平尺寸（米）。仅 Gizmo 显示，不影响逻辑。")]
    public Vector2 gizmoSize = new Vector2(40f, 40f);

    public Color gizmoColor = new Color(1f, 0.25f, 0.25f, 0.35f);

    float _nextCheckTime;

    void Update()
    {
        if (followTransformY)
            killY = transform.position.y;

        if (Time.time < _nextCheckTime) return;
        _nextCheckTime = Time.time + Mathf.Max(0f, checkInterval);

        DestroyItemsBelowPlane();
    }

    void DestroyItemsBelowPlane()
    {
        var roots = new HashSet<GameObject>();
        CollectItemRoots(roots);
        if (includeTools)
        {
            CollectToolRoots<Knife>(roots);
            CollectToolRoots<Screwdriver>(roots);
        }

        if (roots.Count == 0) return;

        CharacterInteraction character = FindObjectOfType<CharacterInteraction>();

        foreach (GameObject root in roots)
        {
            if (root == null) continue;
            if (root.transform.position.y > killY) continue;

            character?.ForceReleaseIfHolding(root);

            if (debugLog)
                Debug.Log($"[WorldKillPlane] 销毁掉出地图的物品 '{root.name}' (y={root.transform.position.y:F2} < killY={killY:F2})", this);

            Destroy(root);
        }
    }

    static void CollectItemRoots(HashSet<GameObject> roots)
    {
        ItemInformation[] items = FindObjectsOfType<ItemInformation>();
        for (int i = 0; i < items.Length; i++)
        {
            ItemInformation info = items[i];
            if (info == null) continue;
            GameObject root = ResolveItemRoot(info);
            if (root != null)
                roots.Add(root);
        }
    }

    static void CollectToolRoots<T>(HashSet<GameObject> roots) where T : Component
    {
        T[] tools = FindObjectsOfType<T>();
        for (int i = 0; i < tools.Length; i++)
        {
            if (tools[i] == null) continue;
            Rigidbody rb = tools[i].GetComponentInParent<Rigidbody>();
            GameObject root = rb != null ? rb.gameObject : tools[i].gameObject;
            if (root != null)
                roots.Add(root);
        }
    }

    /// <summary>与 LevelManager.GetGameplayItemRoot 保持一致：优先取 InspectableItem / Rigidbody 所在节点。</summary>
    static GameObject ResolveItemRoot(ItemInformation info)
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

    void OnDrawGizmos()
    {
        float y = followTransformY ? transform.position.y : killY;
        Vector3 center = new Vector3(transform.position.x, y, transform.position.z);
        Vector3 size = new Vector3(Mathf.Max(0.1f, gizmoSize.x), 0.02f, Mathf.Max(0.1f, gizmoSize.y));

        Gizmos.color = gizmoColor;
        Gizmos.DrawCube(center, size);

        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
        Gizmos.DrawWireCube(center, size);
    }
}

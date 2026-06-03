using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 标记一个可被审视（Inspect）的物品。手持本物品时按下 inspectKey 即可
/// 进入审视界面（详见 InspectionView）。
///
/// 审视界面支持多种交互模式（<see cref="InspectionInteraction"/>）：
///   - DragDetach：左键按住任一 detachableParts 拖拽，到阈值后分离单个子件并退出审视。
///   - KnifeCut：屏幕右侧出现一把切割刀，左键按住切割刀拖到中心物品身上，
///     即把外壳与所有 detachableParts 一并分离，结束审视，所有部件自然掉落。
/// </summary>
[DisallowMultipleComponent]
public class InspectableItem : MonoBehaviour
{
    public enum InspectionInteraction
    {
        DragDetach,
        KnifeCut,
    }

    [Tooltip("玩家手持本物品时按下该按键进入审视界面。")]
    public KeyCode inspectKey = KeyCode.E;

    [Tooltip("审视界面中的交互方式。DragDetach=左键拖拽分离单件；KnifeCut=左键拖切割刀分离外壳与所有子件。")]
    public InspectionInteraction interactionMode = InspectionInteraction.DragDetach;

    [Tooltip("可被分离的子物体。DragDetach 模式下命中其本身或其后代 Collider 即视为命中该可分离件；KnifeCut 模式下一旦切割触发，全部一并分离。")]
    public List<Transform> detachableParts = new List<Transform>();

    [Header("DragDetach 模式")]
    [Tooltip("拖拽位移达到屏幕高度的该比例时立即触发分离。0.18 = 拖动屏幕高度的 18%。")]
    [Range(0.05f, 1f)]
    public float detachScreenRatio = 0.18f;

    [Tooltip("拖拽鼠标到 3D 世界位移的灵敏度（米/像素）。")]
    public float dragWorldSensitivity = 0.005f;

    [Tooltip("分离瞬间给被分离件沿拖拽方向施加的初速（米/秒）。")]
    public float detachVelocity = 1.2f;

    [Header("审视显示")]
    [Tooltip("在审视界面中物品的固定欧拉角（相对于审视相机的本地坐标系，审视相机 +Z 朝物品）。例如 (0, 180, 0) 让模型 -Z 朝相机；(0, 0, 0) 让模型 +Z 朝相机。")]
    public Vector3 inspectionDisplayEulers = new Vector3(0f, 180f, 0f);

    [Header("KnifeCut 模式")]
    [Tooltip("屏幕上显示的切割刀图标。留空则使用一个纯色矩形占位。")]
    public Sprite knifeSprite;

    [Tooltip("切割刀的初始锚点（屏幕比例：(0.5,0.5)=正中，(1,0.5)=最右侧）。")]
    public Vector2 knifeIdleAnchor = new Vector2(0.85f, 0.5f);

    [Tooltip("切割刀 UI 的显示尺寸（像素，基于 1920×1080 参考分辨率）。")]
    public Vector2 knifeUISize = new Vector2(220f, 220f);

    [Tooltip("切割刀图标的“刀尖”所在的像素归一化坐标（0,0=左下，1,1=右上）。" +
             "拖拽时鼠标位置即切割刀的此点，用于命中检测。")]
    public Vector2 knifeTipPivot = new Vector2(0.15f, 0.85f);

    [Tooltip("切割刀图标的额外旋转角度（度，正值=逆时针）。")]
    public float knifeUIRotation = 0f;

    [Tooltip("松开左键且未命中物品时，切割刀回到初始锚点的平滑时间（秒）。")]
    public float knifeReturnSmoothTime = 0.12f;

    [Tooltip("切割完成后，各子件沿远离外壳中心的方向获得的瞬时冲量（牛·秒）。")]
    public float knifeCutSeparateImpulse = 0.5f;

    [Tooltip("切割完成后，各部件附加的初始向下速度（米/秒）。0 即纯靠重力。")]
    public float knifeCutInitialDropSpeed = 0.4f;

    [Tooltip("切割完成后，各子件相对外壳掉落位置的水平铺开半径（米）。")]
    public float knifeCutDropSpread = 0.18f;

    [Header("玩家手撕前不分离")]
    [Tooltip("启用后，物品创建时会让父物体的 Collider 与所有 detachableParts 的 Collider 相互忽略碰撞，避免两者刚体相互推挤造成抖动/分离。手撕分离时再恢复。")]
    public bool ignoreSelfCollisionsWhileAttached = true;

    [Header("分离后的物品信息")]
    [Tooltip("外壳（本 GameObject）分离后应用的 ItemInformation 数据。\n" +
             "KnifeCut 模式下整体分离时一定生效；DragDetach 模式下外壳不会脱离玩家手部，仍保留原 ItemInformation。")]
    public ItemPartInfoOverride shellInfo;

    [Tooltip("各 detachableParts 分离后应用的 ItemInformation 数据。\n" +
             "优先按 targetPart 引用匹配；未指定 targetPart 的条目，按数组顺序匹配 detachableParts。\n" +
             "未匹配上的子件保留原有 ItemInformation。")]
    public ItemPartInfoOverride[] partInfos;

    /// <summary>
    /// 根据某个 detachable part 找到对应的 ItemPartInfoOverride 数据。找不到返回 null。
    /// </summary>
    public ItemPartInfoOverride ResolvePartInfo(Transform part)
    {
        if (partInfos == null || partInfos.Length == 0 || part == null) return null;

        // 优先按 targetPart 引用匹配
        for (int i = 0; i < partInfos.Length; i++)
        {
            ItemPartInfoOverride info = partInfos[i];
            if (info != null && info.targetPart == part)
                return info;
        }

        // 回退到按 detachableParts 中的下标匹配（跳过已绑定到其它 targetPart 的条目）
        int detachIndex = detachableParts.IndexOf(part);
        if (detachIndex < 0) return null;

        for (int i = 0, used = 0; i < partInfos.Length; i++)
        {
            ItemPartInfoOverride info = partInfos[i];
            if (info == null) continue;
            if (info.targetPart != null) continue;
            if (used == detachIndex) return info;
            used++;
        }
        return null;
    }

    public bool TryResolveDetachable(Transform hit, out Transform partRoot)
    {
        partRoot = null;
        if (hit == null) return false;
        for (int i = 0; i < detachableParts.Count; i++)
        {
            Transform part = detachableParts[i];
            if (part == null) continue;
            if (hit == part || hit.IsChildOf(part))
            {
                partRoot = part;
                return true;
            }
        }
        return false;
    }

    void Awake()
    {
        if (ignoreSelfCollisionsWhileAttached)
            ApplyCollisionIgnore(true);
    }

    /// <summary>
    /// 让某个可分离件与父物体上其余 Collider 之间的碰撞恢复/取消忽略。
    /// 在 InspectionView 分离时由其调用。
    /// </summary>
    public void RestoreCollisionFor(Transform part)
    {
        if (part == null) return;
        SetIgnoreForPart(part, false);
    }

    void ApplyCollisionIgnore(bool ignore)
    {
        for (int i = 0; i < detachableParts.Count; i++)
        {
            Transform part = detachableParts[i];
            if (part == null) continue;
            SetIgnoreForPart(part, ignore);
        }
    }

    void SetIgnoreForPart(Transform part, bool ignore)
    {
        if (part == null) return;
        Collider[] partCols = part.GetComponentsInChildren<Collider>(true);
        if (partCols == null || partCols.Length == 0) return;

        Collider[] allCols = GetComponentsInChildren<Collider>(true);
        for (int a = 0; a < allCols.Length; a++)
        {
            Collider rootCol = allCols[a];
            if (rootCol == null) continue;
            if (System.Array.IndexOf(partCols, rootCol) >= 0) continue;
            for (int b = 0; b < partCols.Length; b++)
            {
                if (partCols[b] == null) continue;
                Physics.IgnoreCollision(rootCol, partCols[b], ignore);
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 标记一个可被审视（Inspect）的物品。手持本物品时按下 inspectKey 即可
/// 进入审视界面（详见 InspectionView）。
///
/// 所有模式共用「审视显示」配置（欧拉角、距离）与顶部操作提示字幕；Inspector 中的「审视界面示意」
/// 可实时预览运行时审视姿态。审视界面支持三种交互模式（<see cref="InspectionInteraction"/>）：
///   - DragDetach：左键按住物品任意位置拖拽，位移达到 detachScreenRatio 阈值
///     即触发整体分离。
///   - KnifeCut：屏幕一侧出现切割刀与物品上的红色虚线切割标记（圆/线）。
///     左键点击拾起刀，长按左键在物品上划过锚点/线条，全部划开后触发整体分离。
///     锚点与线条在 Inspector 的「切割锚点编辑器」中配置。
///   - HammerSmash：屏幕一侧出现一把锤子。第一次左键点击锤子拾起，之后每次
///     左键点击物品算一次敲击；累计敲够 hammerHitsRequired 下后触发整体分离。
///
/// 分离触发时：销毁本物品，按 <see cref="dropEntries"/> 在玩家手部位置实例化
/// 对应数量的 prefab。每个 prefab 自带的 ItemInformation / Rigidbody / Collider /
/// 外观等信息会被沿用，不需要在此重复配置。
/// </summary>
[DisallowMultipleComponent]
public class InspectableItem : MonoBehaviour
{
    public enum InspectionInteraction
    {
        DragDetach,
        KnifeCut,
        HammerSmash,
    }

    [Tooltip("玩家手持本物品时按下该按键进入审视界面。")]
    public KeyCode inspectKey = KeyCode.E;

    [Tooltip("审视界面中的交互方式。\n" +
             "DragDetach=左键按住物品任意位置拖拽，到阈值后整体分离；\n" +
             "KnifeCut=拾起切割刀后长按左键划过所有切割锚点/线条后整体分离；\n" +
             "HammerSmash=左键点击拾起锤子，对物品累计敲击 hammerHitsRequired 下后整体分离。")]
    public InspectionInteraction interactionMode = InspectionInteraction.DragDetach;

    [Header("DragDetach 模式")]
    [Tooltip("拖拽位移达到屏幕高度的该比例时立即触发分离。0.18 = 拖动屏幕高度的 18%。")]
    [Range(0.05f, 1f)]
    public float detachScreenRatio = 0.18f;

    [Tooltip("拖拽过程中物品在审视相机里跟随鼠标的视觉位移灵敏度（米/像素）。仅做反馈，不影响分离判定。")]
    public float dragWorldSensitivity = 0.005f;

    [Header("审视操作提示")]
    [Tooltip("审视界面顶部居中显示的操作说明（DragDetach）。")]
    public string dragDetachInstruction = "左键按住仿生皮肤后撕扯";

    [Tooltip("审视界面顶部居中显示的操作说明（KnifeCut）。")]
    public string knifeCutInstruction = "左键拾取切割刀，长按左键划过所有红色切割标记";

    [Tooltip("审视界面顶部居中显示的操作说明（HammerSmash）。{0} = 所需敲击次数。")]
    public string hammerSmashInstruction = "左键拾取锤子，对物品敲击 {0} 次";

    [Header("审视显示")]
    [Tooltip("在审视界面中物品的固定欧拉角（相对于审视相机的本地坐标系，审视相机 +Z 朝物品）。" +
             "例如 (0, 180, 0) 让模型 -Z 朝相机；(0, 0, 0) 让模型 +Z 朝相机。")]
    public Vector3 inspectionDisplayEulers = new Vector3(0f, 180f, 0f);

    [Tooltip("审视相机与物品的距离（米）。≤0 时自动使用玩家持物距离或 InspectionView 的全局默认值。")]
    [Min(0f)]
    public float inspectionDisplayDistance = 0f;

    /// <summary>
    /// 解析审视相机距离：优先使用本物品配置，否则用 <paramref name="fallback"/>。
    /// </summary>
    public float ResolveInspectionDisplayDistance(float fallback)
    {
        if (inspectionDisplayDistance > 0f)
            return Mathf.Max(0.4f, inspectionDisplayDistance);
        return Mathf.Max(0.4f, fallback);
    }

    [Header("KnifeCut 模式")]
    [Tooltip("屏幕上显示的切割刀图标。留空则使用 InspectionView.defaultKnifeSprite 或内置占位刀图。")]
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

    [Tooltip("尚未拾起切割刀时（或刚进入审视时），切割刀回到 / 留在初始锚点的平滑时间（秒）。\n" +
             "切割刀一旦拾起就持续跟随鼠标直到分离或退出审视，本字段只在未拾起阶段生效。")]
    public float knifeReturnSmoothTime = 0.12f;

    [Tooltip("圆形切割锚点。在 Inspector「审视界面示意」预览中配置（KnifeCut 模式）。")]
    public List<InspectableCutAnchor> cutAnchors = new List<InspectableCutAnchor>();

    [Tooltip("线形切割锚点。在 Inspector「审视界面示意」预览中配置（KnifeCut 模式）。")]
    public List<InspectableCutLine> cutLines = new List<InspectableCutLine>();

    [Tooltip("切割标记颜色。")]
    public Color cutMarkerColor = new Color(1f, 0.15f, 0.15f, 1f);

    [Tooltip("虚线段长度（米）。")]
    [Min(0.002f)]
    public float cutMarkerDashLength = 0.02f;

    [Tooltip("虚线间隔长度（米）。")]
    [Min(0.002f)]
    public float cutMarkerGapLength = 0.015f;

    [Header("HammerSmash 模式")]
    [Tooltip("屏幕上显示的锤子图标。留空则使用一个纯色矩形占位。")]
    public Sprite hammerSprite;

    [Tooltip("锤子的初始锚点（屏幕比例：(0.5,0.5)=正中，(1,0.5)=最右侧，(0,0.5)=最左侧）。")]
    public Vector2 hammerIdleAnchor = new Vector2(0.85f, 0.5f);

    [Tooltip("锤子 UI 的显示尺寸（像素，基于 1920×1080 参考分辨率）。")]
    public Vector2 hammerUISize = new Vector2(240f, 240f);

    [Tooltip("锤子图标的“锤头”所在的像素归一化坐标（0,0=左下，1,1=右上）。\n" +
             "拾起后鼠标位置即锤子的此点，用于命中检测。")]
    public Vector2 hammerHeadPivot = new Vector2(0.2f, 0.85f);

    [Tooltip("锤子图标的额外旋转角度（度，正值=逆时针）。")]
    public float hammerUIRotation = 0f;

    [Tooltip("尚未拾起锤子时（或刚进入审视时），锤子回到 / 留在初始锚点的平滑时间（秒）。\n" +
             "锤子一旦拾起就持续跟随鼠标直到分离或退出审视，本字段只在未拾起阶段生效。")]
    public float hammerReturnSmoothTime = 0.12f;

    [Tooltip("累计敲击多少下后才触发整体分离。1 = 一击即开（等价于 KnifeCut 命中即开）。")]
    [Min(1)]
    public int hammerHitsRequired = 3;

    [Header("分离后生成 - 必出")]
    [Tooltip("分离触发后销毁本物品，并按此列表在玩家手部位置实例化 prefab（必定生成）。\n" +
             "每条可设置 prefab + 数量。prefab 自带的 ItemInformation / Rigidbody /\n" +
             "Collider / 外观等信息会被沿用，无需在此重复配置。")]
    public List<DetachSpawnEntry> dropEntries = new List<DetachSpawnEntry>();

    [Header("分离后生成 - 可能出")]
    [Tooltip("分离时每条独立按 spawnChance 摇一次：命中则该条所有 count 一起出，\n" +
             "未命中则一个都不出。用于配置低概率掉落的彩蛋/惊喜物品。")]
    public List<OptionalDetachSpawnEntry> optionalDropEntries = new List<OptionalDetachSpawnEntry>();

    [Tooltip("生成的物品的初始向下速度（米/秒），用于让它们立即开始下落、自然堆叠。0 即纯靠重力。")]
    public float dropInitialSpeed = 0.4f;

    /// <summary>
    /// KnifeCut 模式是否配置了至少一个有效切割锚点或线条。
    /// </summary>
    public bool HasAnyCutTarget
    {
        get
        {
            if (cutAnchors != null)
            {
                for (int i = 0; i < cutAnchors.Count; i++)
                {
                    if (cutAnchors[i] != null) return true;
                }
            }
            if (cutLines != null)
            {
                for (int i = 0; i < cutLines.Count; i++)
                {
                    if (cutLines[i] != null) return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// 本物品是否还有任何"分离产物"可生成（必出 + 可能出 任一列表里有有效条目）。
    /// 两个列表全空时进入审视没有意义，InspectionView 会跳过启动。
    /// KnifeCut 模式额外要求 <see cref="HasAnyCutTarget"/>。
    /// </summary>
    public bool HasAnyDropEntry
    {
        get
        {
            if (dropEntries != null)
            {
                for (int i = 0; i < dropEntries.Count; i++)
                {
                    DetachSpawnEntry e = dropEntries[i];
                    if (e != null && e.prefab != null && e.count > 0)
                        return true;
                }
            }
            if (optionalDropEntries != null)
            {
                for (int i = 0; i < optionalDropEntries.Count; i++)
                {
                    OptionalDetachSpawnEntry e = optionalDropEntries[i];
                    if (e != null && e.prefab != null && e.count > 0 && e.spawnChance > 0f)
                        return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// 在 <paramref name="anchor"/> 处实例化所有 dropEntries（必出）+
    /// 按 spawnChance 摇过的 optionalDropEntries（可能出），并按 dropInitialSpeed 给一个初速度。
    /// </summary>
    public void SpawnDropEntries(Vector3 anchor)
    {
        DetachSpawnUtility.SpawnEntries(dropEntries, anchor, dropInitialSpeed);
        DetachSpawnUtility.SpawnOptionalEntries(optionalDropEntries, anchor, dropInitialSpeed);
    }

    /// <summary>按当前交互模式返回审视界面顶部操作提示文案。</summary>
    public string GetInspectionInstructionText()
    {
        switch (interactionMode)
        {
            case InspectionInteraction.KnifeCut:
                return knifeCutInstruction ?? string.Empty;
            case InspectionInteraction.HammerSmash:
                if (string.IsNullOrEmpty(hammerSmashInstruction))
                    return string.Empty;
                return hammerSmashInstruction.Replace(
                    "{0}", Mathf.Max(1, hammerHitsRequired).ToString());
            default:
                return dragDetachInstruction ?? string.Empty;
        }
    }

    /// <summary>
    /// 是否允许进入审视界面（有分离产物，且 KnifeCut 时已配置切割目标）。
    /// </summary>
    public bool CanEnterInspection
    {
        get
        {
            if (!HasAnyDropEntry) return false;
            if (interactionMode == InspectionInteraction.KnifeCut && !HasAnyCutTarget)
                return false;
            return true;
        }
    }
}

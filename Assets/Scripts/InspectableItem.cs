using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 标记一个可被审视（Inspect）的物品。手持本物品时按下 inspectKey 即可
/// 进入审视界面（详见 InspectionView）。审视界面中右键按住任一
/// detachableParts 中的子物体拖拽，可将其与父物体分离。
/// </summary>
[DisallowMultipleComponent]
public class InspectableItem : MonoBehaviour
{
    [Tooltip("玩家手持本物品时按下该按键进入审视界面。")]
    public KeyCode inspectKey = KeyCode.E;

    [Tooltip("可以被右键拖拽分离的子物体。命中其本身或其后代上的 Collider 即视为命中该可分离件。")]
    public List<Transform> detachableParts = new List<Transform>();

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

    [Header("玩家手撕前不分离")]
    [Tooltip("启用后，物品创建时会让父物体的 Collider 与所有 detachableParts 的 Collider 相互忽略碰撞，避免两者刚体相互推挤造成抖动/分离。手撕分离时再恢复。")]
    public bool ignoreSelfCollisionsWhileAttached = true;

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

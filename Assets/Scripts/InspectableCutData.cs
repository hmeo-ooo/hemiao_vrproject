using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// KnifeCut 模式下，物品表面上的一个圆形切割锚点（本地坐标）。
/// 审视界面会在该位置绘制红色虚线圆，玩家用刀尖划过即视为切开。
/// </summary>
[System.Serializable]
public class InspectableCutAnchor
{
    [Tooltip("锚点中心，相对于物品根节点的本地坐标。")]
    public Vector3 localPosition;

    [Tooltip("锚点所在表面的本地法线，用于确定虚线圆所在平面。")]
    public Vector3 localNormal = Vector3.up;

    [Tooltip("虚线圆半径 & 刀尖命中半径（米）。")]
    [Min(0.005f)]
    public float radius = 0.05f;
}

/// <summary>
/// KnifeCut 模式下，物品表面的一条切割线（本地坐标）。
/// 审视界面会绘制红色虚线，玩家用刀尖划过即视为切开。
/// </summary>
[System.Serializable]
public class InspectableCutLine
{
    [Tooltip("线段起点，相对于物品根节点的本地坐标。")]
    public Vector3 localStart;

    [Tooltip("线段终点，相对于物品根节点的本地坐标。")]
    public Vector3 localEnd;

    [Tooltip("刀尖划过判定的线宽半径（米）。")]
    [Min(0.005f)]
    public float hitRadius = 0.04f;
}

/// <summary>
/// 切割锚点/线条的世界坐标换算与划过命中检测。
/// </summary>
public static class InspectableCutUtility
{
    public static Vector3 AnchorWorldPosition(Transform itemRoot, InspectableCutAnchor anchor)
    {
        if (itemRoot == null || anchor == null) return Vector3.zero;
        return itemRoot.TransformPoint(anchor.localPosition);
    }

    public static Vector3 AnchorWorldNormal(Transform itemRoot, InspectableCutAnchor anchor)
    {
        if (itemRoot == null || anchor == null) return Vector3.up;
        return itemRoot.TransformDirection(anchor.localNormal).normalized;
    }

    public static void LineWorldEndpoints(Transform itemRoot, InspectableCutLine line, out Vector3 a, out Vector3 b)
    {
        a = b = Vector3.zero;
        if (itemRoot == null || line == null) return;
        a = itemRoot.TransformPoint(line.localStart);
        b = itemRoot.TransformPoint(line.localEnd);
    }

    /// <summary>
    /// 判断刀尖从 <paramref name="prev"/> 移动到 <paramref name="curr"/> 的划过是否切开锚点。
    /// </summary>
    public static bool IsAnchorCutBySwipe(
        Transform itemRoot, InspectableCutAnchor anchor, Vector3 prev, Vector3 curr)
    {
        if (itemRoot == null || anchor == null) return false;

        Vector3 center = AnchorWorldPosition(itemRoot, anchor);
        float r = Mathf.Max(0.005f, anchor.radius);

        if ((curr - center).sqrMagnitude <= r * r) return true;
        if (prev.sqrMagnitude > 1e-8f && SegmentPointDistanceSqr(prev, curr, center) <= r * r)
            return true;

        return false;
    }

    /// <summary>
    /// 判断刀尖划过是否切开线段。
    /// </summary>
    public static bool IsLineCutBySwipe(
        Transform itemRoot, InspectableCutLine line, Vector3 prev, Vector3 curr)
    {
        if (itemRoot == null || line == null) return false;

        LineWorldEndpoints(itemRoot, line, out Vector3 a, out Vector3 b);
        float r = Mathf.Max(0.005f, line.hitRadius);

        if (PointSegmentDistance(a, b, curr) <= r) return true;
        if (prev.sqrMagnitude > 1e-8f && SegmentSegmentDistance(prev, curr, a, b) <= r)
            return true;

        return false;
    }

    public static int CountRemaining(
        IList<InspectableCutAnchor> anchors, bool[] anchorDone,
        IList<InspectableCutLine> lines, bool[] lineDone)
    {
        int n = 0;
        if (anchors != null)
        {
            for (int i = 0; i < anchors.Count; i++)
            {
                if (anchors[i] == null) continue;
                if (anchorDone == null || i >= anchorDone.Length || !anchorDone[i]) n++;
            }
        }
        if (lines != null)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] == null) continue;
                if (lineDone == null || i >= lineDone.Length || !lineDone[i]) n++;
            }
        }
        return n;
    }

    static float SegmentPointDistanceSqr(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float lenSqr = ab.sqrMagnitude;
        if (lenSqr < 1e-8f) return (p - a).sqrMagnitude;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / lenSqr);
        Vector3 closest = a + ab * t;
        return (p - closest).sqrMagnitude;
    }

    static float PointSegmentDistance(Vector3 a, Vector3 b, Vector3 p)
    {
        return Mathf.Sqrt(SegmentPointDistanceSqr(a, b, p));
    }

    static float SegmentSegmentDistance(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4)
    {
        Vector3 u = p2 - p1;
        Vector3 v = p4 - p3;
        Vector3 w = p1 - p3;
        float a = Vector3.Dot(u, u);
        float b = Vector3.Dot(u, v);
        float c = Vector3.Dot(v, v);
        float d = Vector3.Dot(u, w);
        float e = Vector3.Dot(v, w);
        float denom = a * c - b * b;
        float sc, tc;

        if (denom < 1e-8f)
        {
            sc = 0f;
            tc = c > 1e-8f ? Mathf.Clamp01(e / c) : 0f;
        }
        else
        {
            sc = Mathf.Clamp01((b * e - c * d) / denom);
            tc = Mathf.Clamp01((a * e - b * d) / denom);
        }

        Vector3 cp1 = p1 + sc * u;
        Vector3 cp2 = p3 + tc * v;
        return Vector3.Distance(cp1, cp2);
    }
}

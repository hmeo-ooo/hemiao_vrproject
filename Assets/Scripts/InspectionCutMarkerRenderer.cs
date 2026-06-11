using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 在审视舱中为 KnifeCut 锚点/线条生成红色虚线标记（运行时 LineRenderer）。
/// </summary>
public class InspectionCutMarkerRenderer : MonoBehaviour
{
    static Material sLineMaterial;

    readonly List<GameObject> anchorMarkerRoots = new List<GameObject>();
    readonly List<GameObject> lineMarkerRoots = new List<GameObject>();

    public static InspectionCutMarkerRenderer Build(
        Transform itemRoot,
        InspectableItem item,
        bool[] anchorDone,
        bool[] lineDone)
    {
        if (itemRoot == null || item == null) return null;

        var rootGo = new GameObject("_InspectionCutMarkers");
        rootGo.transform.SetParent(itemRoot, false);
        rootGo.transform.localPosition = Vector3.zero;
        rootGo.transform.localRotation = Quaternion.identity;
        rootGo.transform.localScale = Vector3.one;

        var renderer = rootGo.AddComponent<InspectionCutMarkerRenderer>();
        renderer.Rebuild(itemRoot, item, anchorDone, lineDone);
        return renderer;
    }

    public void Rebuild(
        Transform itemRoot,
        InspectableItem item,
        bool[] anchorDone,
        bool[] lineDone)
    {
        ClearChildren();

        Color color = item.cutMarkerColor;
        float dash = Mathf.Max(0.002f, item.cutMarkerDashLength);
        float gap = Mathf.Max(0.002f, item.cutMarkerGapLength);

        if (item.cutAnchors != null)
        {
            for (int i = 0; i < item.cutAnchors.Count; i++)
            {
                InspectableCutAnchor anchor = item.cutAnchors[i];
                if (anchor == null) continue;

                bool done = anchorDone != null && i < anchorDone.Length && anchorDone[i];
                GameObject markerRoot = new GameObject($"CutAnchor_{i:00}");
                markerRoot.transform.SetParent(transform, false);
                anchorMarkerRoots.Add(markerRoot);

                if (!done)
                {
                    Vector3 center = itemRoot.TransformPoint(anchor.localPosition);
                    Vector3 normal = itemRoot.TransformDirection(anchor.localNormal).normalized;
                    CreateDashedCircle(markerRoot.transform, center, normal, anchor.radius, color, dash, gap);
                }
                else
                {
                    markerRoot.SetActive(false);
                }
            }
        }

        if (item.cutLines != null)
        {
            for (int i = 0; i < item.cutLines.Count; i++)
            {
                InspectableCutLine line = item.cutLines[i];
                if (line == null) continue;

                bool done = lineDone != null && i < lineDone.Length && lineDone[i];
                GameObject markerRoot = new GameObject($"CutLine_{i:00}");
                markerRoot.transform.SetParent(transform, false);
                lineMarkerRoots.Add(markerRoot);

                if (!done)
                {
                    Vector3 a = itemRoot.TransformPoint(line.localStart);
                    Vector3 b = itemRoot.TransformPoint(line.localEnd);
                    CreateDashedLine(markerRoot.transform, a, b, color, dash, gap);
                }
                else
                {
                    markerRoot.SetActive(false);
                }
            }
        }
    }

    public void SetAnchorDone(int index, bool done)
    {
        if (index < 0 || index >= anchorMarkerRoots.Count) return;
        GameObject go = anchorMarkerRoots[index];
        if (go != null) go.SetActive(!done);
    }

    public void SetLineDone(int index, bool done)
    {
        if (index < 0 || index >= lineMarkerRoots.Count) return;
        GameObject go = lineMarkerRoots[index];
        if (go != null) go.SetActive(!done);
    }

    void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);
        anchorMarkerRoots.Clear();
        lineMarkerRoots.Clear();
    }

    static void CreateDashedCircle(
        Transform parent, Vector3 center, Vector3 normal, float radius,
        Color color, float dashLength, float gapLength)
    {
        Vector3 n = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
        Vector3 tangent = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 bitangent = Vector3.Cross(n, tangent);

        float circumference = Mathf.PI * 2f * radius;
        float pattern = dashLength + gapLength;
        int dashCount = Mathf.Max(4, Mathf.FloorToInt(circumference / pattern));
        float angDash = (dashLength / circumference) * Mathf.PI * 2f;

        for (int d = 0; d < dashCount; d++)
        {
            float ang0 = (d / (float)dashCount) * Mathf.PI * 2f;
            float ang1 = ang0 + angDash;
            Vector3 p0 = center + (Mathf.Cos(ang0) * tangent + Mathf.Sin(ang0) * bitangent) * radius;
            Vector3 p1 = center + (Mathf.Cos(ang1) * tangent + Mathf.Sin(ang1) * bitangent) * radius;
            CreateDashSegment(parent, p0, p1, color);
        }
    }

    static void CreateDashedLine(
        Transform parent, Vector3 a, Vector3 b,
        Color color, float dashLength, float gapLength)
    {
        Vector3 dir = b - a;
        float len = dir.magnitude;
        if (len < 1e-5f) return;

        dir /= len;
        float pattern = dashLength + gapLength;
        float traveled = 0f;
        bool drawing = true;

        while (traveled < len)
        {
            float chunk = drawing ? dashLength : gapLength;
            float next = Mathf.Min(traveled + chunk, len);
            if (drawing)
                CreateDashSegment(parent, a + dir * traveled, a + dir * next, color);
            traveled = next;
            drawing = !drawing;
        }
    }

    static void CreateDashSegment(Transform parent, Vector3 a, Vector3 b, Color color)
    {
        if ((a - b).sqrMagnitude < 1e-8f) return;

        var go = new GameObject("Dash");
        go.transform.SetParent(parent, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
        lr.startWidth = lr.endWidth = 0.008f;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.material = GetLineMaterial();
        lr.startColor = lr.endColor = color;
    }

    static Material GetLineMaterial()
    {
        if (sLineMaterial != null) return sLineMaterial;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) return null;

        sLineMaterial = new Material(shader);
        return sLineMaterial;
    }
}

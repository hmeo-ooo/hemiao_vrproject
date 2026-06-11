using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 给 <see cref="TrashHeap"/> 提供锚点编辑器：
/// 在 Inspector 里按"进入锚点编辑模式"，然后在 Scene 视图里直接在垃圾堆表面点击就会落锚点。
/// 锚点会作为 TrashHeap 的子物体生成，position = 命中点，up = 命中表面法线。
///
/// 操作约定：
///   - 鼠标左键点击垃圾堆表面 → 添加新锚点
///   - Shift + 左键点击已有锚点 → 删除该锚点
///   - Esc / 再次按按钮 → 退出编辑模式
/// </summary>
[CustomEditor(typeof(TrashHeap))]
public class TrashHeapEditor : Editor
{
    const string EditorPrefsKey = "Hemiao.TrashHeapEditor.AnchorEditMode";
    const float AnchorPickRadius = 0.35f;
    const string AnchorParentName = "Anchors";

    bool _editMode;

    void OnEnable()
    {
        _editMode = EditorPrefs.GetBool(EditorPrefsKey, false);
        SceneView.duringSceneGui += OnSceneGUI;
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("锚点编辑器", EditorStyles.boldLabel);

        TrashHeap heap = (TrashHeap)target;

        bool isAnchorMode = heap.placementMode == TrashHeap.SpawnPlacementMode.Anchors;
        if (!isAnchorMode)
        {
            EditorGUILayout.HelpBox(
                "当前 Placement Mode = RandomSurface。锚点列表只有在 Placement Mode = Anchors 时才会被使用。" +
                "可继续在编辑器里摆放锚点，但记得切换到 Anchors 才会生效。",
                MessageType.Info);
        }

        Color prev = GUI.backgroundColor;
        GUI.backgroundColor = _editMode ? new Color(1f, 0.55f, 0.3f) : Color.white;
        string buttonLabel = _editMode ? "退出锚点编辑模式" : "进入锚点编辑模式";
        if (GUILayout.Button(buttonLabel, GUILayout.Height(28)))
        {
            SetEditMode(!_editMode);
        }
        GUI.backgroundColor = prev;

        if (_editMode)
        {
            EditorGUILayout.HelpBox(
                "Scene 视图操作：\n" +
                "  • 左键点击垃圾堆表面 → 添加锚点（自动用表面法线作为 up）\n" +
                "  • Shift + 左键点击已有锚点 → 删除该锚点\n" +
                "  • Esc / 再次点击上方按钮 → 退出编辑模式\n" +
                "\n所有操作都支持 Ctrl/Cmd + Z 撤销。",
                MessageType.Info);
        }

        using (new EditorGUI.DisabledScope(heap.spawnAnchors == null || heap.spawnAnchors.Length == 0))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("整理锚点（剔空 + 重命名）"))
                CleanupAnchorArray(heap);
            if (GUILayout.Button("清空所有锚点"))
            {
                if (EditorUtility.DisplayDialog(
                        "清空锚点",
                        $"将删除 {CountValidAnchors(heap)} 个锚点 GameObject 并清空数组，确认？",
                        "确定", "取消"))
                {
                    ClearAllAnchors(heap);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        if (heap.spawnAnchors != null && heap.spawnAnchors.Length > 0)
            EditorGUILayout.LabelField("当前锚点数量", $"{CountValidAnchors(heap)} / {heap.spawnAnchors.Length}");
    }

    void OnSceneGUI(SceneView sv)
    {
        if (!_editMode) return;
        TrashHeap heap = target as TrashHeap;
        if (heap == null) return;

        // 1. 取一个 control id 并 AddDefaultControl，让 SceneView 默认的"点空场景取消选中 / 选中场景物体"被我们截胡。
        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(controlId);

        Event e = Event.current;

        // 2. Esc 退出
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            SetEditMode(false);
            e.Use();
            return;
        }

        // 3. 左键 → 添加 / 删除
        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

            if (e.shift)
            {
                if (TryPickNearestAnchor(heap, ray, out int idx))
                {
                    RemoveAnchorAt(heap, idx);
                    e.Use();
                    sv.Repaint();
                    return;
                }
            }
            else
            {
                if (TryRaycastHeapSurface(heap, ray, out Vector3 point, out Vector3 normal))
                {
                    AddAnchorAt(heap, point, normal);
                    e.Use();
                    sv.Repaint();
                    return;
                }
            }
        }

        // 4. 画锚点 + 当前射线指示
        DrawAnchorHandles(heap);
        DrawHoverIndicator(heap, e);
    }

    // ---------- 锚点增删 ----------

    void AddAnchorAt(TrashHeap heap, Vector3 worldPosition, Vector3 worldNormal)
    {
        Undo.SetCurrentGroupName("Add TrashHeap Anchor");
        int group = Undo.GetCurrentGroup();

        Transform anchorsRoot = EnsureAnchorsRoot(heap);

        var go = new GameObject("Anchor");
        Undo.RegisterCreatedObjectUndo(go, "Add TrashHeap Anchor");

        go.transform.SetParent(anchorsRoot, worldPositionStays: true);
        go.transform.position = worldPosition;
        Vector3 up = worldNormal.sqrMagnitude > 1e-6f ? worldNormal.normalized : Vector3.up;
        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, up);

        Undo.RecordObject(heap, "Add TrashHeap Anchor");
        var list = new List<Transform>(heap.spawnAnchors ?? new Transform[0]) { go.transform };
        heap.spawnAnchors = list.ToArray();
        EditorUtility.SetDirty(heap);

        RenameAnchorsSequentially(heap);

        Undo.CollapseUndoOperations(group);
    }

    void RemoveAnchorAt(TrashHeap heap, int index)
    {
        if (heap.spawnAnchors == null || index < 0 || index >= heap.spawnAnchors.Length) return;

        Undo.SetCurrentGroupName("Remove TrashHeap Anchor");
        int group = Undo.GetCurrentGroup();

        Transform anchor = heap.spawnAnchors[index];

        Undo.RecordObject(heap, "Remove TrashHeap Anchor");
        var list = new List<Transform>(heap.spawnAnchors);
        list.RemoveAt(index);
        heap.spawnAnchors = list.ToArray();
        EditorUtility.SetDirty(heap);

        if (anchor != null)
            Undo.DestroyObjectImmediate(anchor.gameObject);

        RenameAnchorsSequentially(heap);

        Undo.CollapseUndoOperations(group);
    }

    void ClearAllAnchors(TrashHeap heap)
    {
        Undo.SetCurrentGroupName("Clear TrashHeap Anchors");
        int group = Undo.GetCurrentGroup();

        Undo.RecordObject(heap, "Clear TrashHeap Anchors");
        if (heap.spawnAnchors != null)
        {
            for (int i = heap.spawnAnchors.Length - 1; i >= 0; i--)
            {
                Transform a = heap.spawnAnchors[i];
                if (a != null) Undo.DestroyObjectImmediate(a.gameObject);
            }
        }
        heap.spawnAnchors = new Transform[0];
        EditorUtility.SetDirty(heap);

        Undo.CollapseUndoOperations(group);
    }

    void CleanupAnchorArray(TrashHeap heap)
    {
        if (heap.spawnAnchors == null) return;

        Undo.RecordObject(heap, "Cleanup TrashHeap Anchors");
        var list = new List<Transform>(heap.spawnAnchors.Length);
        for (int i = 0; i < heap.spawnAnchors.Length; i++)
        {
            Transform a = heap.spawnAnchors[i];
            if (a != null) list.Add(a);
        }
        heap.spawnAnchors = list.ToArray();
        EditorUtility.SetDirty(heap);

        RenameAnchorsSequentially(heap);
    }

    void RenameAnchorsSequentially(TrashHeap heap)
    {
        if (heap.spawnAnchors == null) return;
        int idx = 1;
        for (int i = 0; i < heap.spawnAnchors.Length; i++)
        {
            Transform a = heap.spawnAnchors[i];
            if (a == null) continue;
            string n = $"Anchor_{idx:00}";
            if (a.gameObject.name != n)
            {
                Undo.RecordObject(a.gameObject, "Rename TrashHeap Anchor");
                a.gameObject.name = n;
            }
            idx++;
        }
    }

    static Transform EnsureAnchorsRoot(TrashHeap heap)
    {
        // 把所有锚点统一挂到 heap 下名为 Anchors 的子节点，便于 Hierarchy 整洁。
        Transform existing = heap.transform.Find(AnchorParentName);
        if (existing != null) return existing;

        var go = new GameObject(AnchorParentName);
        Undo.RegisterCreatedObjectUndo(go, "Create TrashHeap Anchors Root");
        go.transform.SetParent(heap.transform, worldPositionStays: false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    // ---------- 射线工具 ----------

    static bool TryRaycastHeapSurface(TrashHeap heap, Ray ray, out Vector3 point, out Vector3 normal)
    {
        point = default;
        normal = Vector3.up;

        if (heap.surfaceCollider != null && !heap.surfaceCollider.isTrigger)
        {
            if (heap.surfaceCollider.Raycast(ray, out RaycastHit hit, 10000f))
            {
                point = hit.point;
                normal = hit.normal;
                return true;
            }
            return false;
        }

        // 没指定 surfaceCollider：在 heap 自身及子节点上的所有非 trigger collider 里找最近命中。
        Collider[] colliders = heap.GetComponentsInChildren<Collider>();
        float bestDistance = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider c = colliders[i];
            if (c == null || c.isTrigger) continue;
            if (!c.Raycast(ray, out RaycastHit hit, 10000f)) continue;
            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                point = hit.point;
                normal = hit.normal;
                found = true;
            }
        }
        return found;
    }

    static bool TryPickNearestAnchor(TrashHeap heap, Ray ray, out int index)
    {
        index = -1;
        if (heap.spawnAnchors == null) return false;

        // 把锚点位置投射到射线上，取最近且距离 < AnchorPickRadius 的那个。
        float bestDist = AnchorPickRadius;
        for (int i = 0; i < heap.spawnAnchors.Length; i++)
        {
            Transform a = heap.spawnAnchors[i];
            if (a == null) continue;

            Vector3 p = a.position;
            Vector3 toPoint = p - ray.origin;
            float t = Vector3.Dot(toPoint, ray.direction);
            if (t < 0f) continue;
            Vector3 closest = ray.origin + ray.direction * t;
            float d = Vector3.Distance(closest, p);
            if (d < bestDist)
            {
                bestDist = d;
                index = i;
            }
        }
        return index >= 0;
    }

    // ---------- Scene 可视化 ----------

    void DrawAnchorHandles(TrashHeap heap)
    {
        if (heap.spawnAnchors == null) return;

        for (int i = 0; i < heap.spawnAnchors.Length; i++)
        {
            Transform a = heap.spawnAnchors[i];
            if (a == null) continue;

            Vector3 p = a.position;
            float size = HandleUtility.GetHandleSize(p) * 0.08f;

            Handles.color = new Color(1f, 0.95f, 0.2f, 0.95f);
            Handles.SphereHandleCap(0, p, Quaternion.identity, size, EventType.Repaint);

            Handles.color = new Color(0.3f, 1f, 1f, 0.9f);
            Handles.DrawLine(p, p + a.up * size * 4f);

            Handles.Label(p + a.up * size * 5f, $"#{i + 1}");
        }
    }

    void DrawHoverIndicator(TrashHeap heap, Event e)
    {
        if (e.type != EventType.MouseMove && e.type != EventType.Repaint) return;

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (e.shift)
        {
            if (TryPickNearestAnchor(heap, ray, out int idx))
            {
                Transform a = heap.spawnAnchors[idx];
                if (a != null)
                {
                    Handles.color = new Color(1f, 0.3f, 0.3f, 1f);
                    float s = HandleUtility.GetHandleSize(a.position) * 0.12f;
                    Handles.DrawWireDisc(a.position, a.up, s);
                    Handles.DrawWireDisc(a.position, a.up, s * 0.6f);
                }
            }
        }
        else
        {
            if (TryRaycastHeapSurface(heap, ray, out Vector3 hp, out Vector3 hn))
            {
                Handles.color = new Color(0.4f, 1f, 0.4f, 1f);
                float s = HandleUtility.GetHandleSize(hp) * 0.1f;
                Handles.DrawWireDisc(hp, hn, s);
                Handles.DrawLine(hp, hp + hn * s * 4f);
            }
        }

        if (e.type == EventType.MouseMove)
            HandleUtility.Repaint();
    }

    // ---------- 杂项 ----------

    void SetEditMode(bool value)
    {
        if (_editMode == value) return;
        _editMode = value;
        EditorPrefs.SetBool(EditorPrefsKey, value);

        if (_editMode)
        {
            // 进入编辑模式时强制选中堆，避免点击场景丢失选中。
            Selection.activeObject = target;
        }
        SceneView.RepaintAll();
        Repaint();
    }

    static int CountValidAnchors(TrashHeap heap)
    {
        if (heap.spawnAnchors == null) return 0;
        int n = 0;
        for (int i = 0; i < heap.spawnAnchors.Length; i++)
            if (heap.spawnAnchors[i] != null) n++;
        return n;
    }
}

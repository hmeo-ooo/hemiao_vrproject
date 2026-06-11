using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// InspectableItem 自定义 Inspector：
/// 所有交互模式均提供 2D 审视界面示意，用于调整欧拉角与距离；
/// KnifeCut 模式下额外支持在预览中编辑切割锚点/线条。
/// </summary>
[CustomEditor(typeof(InspectableItem))]
public class InspectableItemEditor : Editor
{
    const float PreviewPickRadiusPx = 14f;

    enum CutEditTool
    {
        CircleAnchor,
        Line,
    }

    CutEditTool _tool = CutEditTool.CircleAnchor;
    bool _hasPendingLineStart;
    Vector3 _pendingLineStartLocal;
    int _draggingKnifePivot;

    InspectableItemKnifeCutPreview _preview;

    SerializedProperty _interactionMode;
    SerializedProperty _inspectionDisplayEulers;
    SerializedProperty _inspectionDisplayDistance;
    SerializedProperty _knifeSprite;
    SerializedProperty _knifeTipPivot;
    SerializedProperty _cutAnchors;
    SerializedProperty _cutLines;

    void OnEnable()
    {
        _interactionMode = serializedObject.FindProperty("interactionMode");
        _inspectionDisplayEulers = serializedObject.FindProperty("inspectionDisplayEulers");
        _inspectionDisplayDistance = serializedObject.FindProperty("inspectionDisplayDistance");
        _knifeSprite = serializedObject.FindProperty("knifeSprite");
        _knifeTipPivot = serializedObject.FindProperty("knifeTipPivot");
        _cutAnchors = serializedObject.FindProperty("cutAnchors");
        _cutLines = serializedObject.FindProperty("cutLines");
        _preview = new InspectableItemKnifeCutPreview();
    }

    void OnDisable()
    {
        _preview?.Dispose();
        _preview = null;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        InspectableItem item = (InspectableItem)target;

        EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("inspectKey"));
        EditorGUILayout.PropertyField(_interactionMode);

        DrawInspectionDisplayPanel(item);

        if (item.interactionMode == InspectableItem.InspectionInteraction.KnifeCut)
            DrawKnifeCutEditorPanel(item);

        if (item.interactionMode == InspectableItem.InspectionInteraction.KnifeCut)
        {
            DrawPropertiesExcluding(serializedObject,
                "m_Script", "inspectKey", "interactionMode",
                "inspectionDisplayEulers", "inspectionDisplayDistance",
                "knifeSprite", "knifeIdleAnchor", "knifeUISize", "knifeTipPivot",
                "knifeUIRotation", "knifeReturnSmoothTime",
                "cutAnchors", "cutLines");
        }
        else
        {
            DrawPropertiesExcluding(serializedObject,
                "m_Script", "inspectKey", "interactionMode",
                "inspectionDisplayEulers", "inspectionDisplayDistance");
        }

        serializedObject.ApplyModifiedProperties();
    }

    void DrawInspectionDisplayPanel(InspectableItem item)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("审视界面示意", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "下方预览与运行时审视界面一致（红框区域）。\n" +
            "调整「审视欧拉角」「审视距离」可实时查看物品在审视中的姿态与远近。",
            MessageType.Info);

        EditorGUILayout.PropertyField(_inspectionDisplayEulers, new GUIContent("审视欧拉角"));
        EditorGUILayout.PropertyField(_inspectionDisplayDistance, new GUIContent("审视距离"));

        DrawInspectionPreview2D(item);
    }

    void DrawKnifeCutEditorPanel(InspectableItem item)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("KnifeCut 切割锚点编辑器", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "在上方审视预览中编辑切割目标：\n" +
            "• 左键点击预览画面 → 添加圆形锚点；Line 工具需点击两次画线段\n" +
            "• Shift + 左键 → 删除最近的锚点/线条",
            MessageType.Info);

        EditorGUILayout.PropertyField(_knifeSprite, new GUIContent("切割刀图标"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("knifeIdleAnchor"), new GUIContent("切割刀初始位置"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("knifeUISize"), new GUIContent("切割刀尺寸"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("knifeUIRotation"), new GUIContent("切割刀旋转"));

        EditorGUILayout.Space(4);
        _tool = (CutEditTool)EditorGUILayout.EnumPopup("编辑工具", _tool);
        if (_tool == CutEditTool.Line && _hasPendingLineStart)
            EditorGUILayout.HelpBox("已设置线段起点，请在上方预览中再点击终点。", MessageType.Warning);

        int anchorCount = item.cutAnchors != null ? item.cutAnchors.Count : 0;
        int lineCount = item.cutLines != null ? item.cutLines.Count : 0;
        EditorGUILayout.LabelField("当前切割目标", $"圆形锚点 {anchorCount}，线条 {lineCount}");

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("清空所有切割锚点"))
        {
            if (EditorUtility.DisplayDialog("清空", "删除所有圆形锚点？", "确定", "取消"))
                ClearList(item.cutAnchors, _cutAnchors);
        }
        if (GUILayout.Button("清空所有切割线条"))
        {
            if (EditorUtility.DisplayDialog("清空", "删除所有切割线条？", "确定", "取消"))
                ClearList(item.cutLines, _cutLines);
        }
        EditorGUILayout.EndHorizontal();

        DrawKnifeTipEditor(item);
        EditorGUILayout.Space(4);
    }

    void DrawInspectionPreview2D(InspectableItem item)
    {
        float width = Mathf.Max(260f, EditorGUIUtility.currentViewWidth - 36f);
        float height = Mathf.Clamp(width * 0.72f, 220f, 420f);
        Rect rect = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(true));

        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        HandleUtility.AddDefaultControl(controlId);

        _preview.Sync(item);

        if (Event.current.type == EventType.Repaint)
        {
            Texture tex = _preview.Render(rect);
            if (tex != null)
                GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, false);

            DrawPreviewBorder(rect);

            if (item.interactionMode == InspectableItem.InspectionInteraction.KnifeCut)
            {
                DrawCutMarkersOverlay(item, rect);
                DrawPendingLineOverlay(item, rect);
                DrawKnifeOverlay(item, rect);
            }
            else if (item.interactionMode == InspectableItem.InspectionInteraction.HammerSmash)
            {
                DrawHammerOverlay(item, rect);
            }
        }

        if (item.interactionMode == InspectableItem.InspectionInteraction.KnifeCut)
            HandlePreviewInput(item, rect);
    }

    static void DrawPreviewBorder(Rect rect)
    {
        Color border = new Color(1f, 0.35f, 0.35f, 0.85f);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), border);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), border);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), border);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), border);
    }

    void DrawCutMarkersOverlay(InspectableItem item, Rect previewRect)
    {
        if (_preview?.PreviewInstance == null) return;

        Transform root = _preview.PreviewInstance.transform;
        Color color = item.cutMarkerColor;

        if (item.cutAnchors != null)
        {
            for (int i = 0; i < item.cutAnchors.Count; i++)
            {
                InspectableCutAnchor anchor = item.cutAnchors[i];
                if (anchor == null) continue;

                Vector3 center = root.TransformPoint(anchor.localPosition);
                Vector3 normal = root.TransformDirection(anchor.localNormal).normalized;
                DrawScreenDottedCircle(previewRect, center, normal, anchor.radius, color);
                Vector2 labelPos = _preview.WorldToGui(previewRect, center);
                if (labelPos.x > -5000f)
                    GUI.Label(new Rect(labelPos.x + 4f, labelPos.y - 14f, 40f, 18f), $"●{i + 1}", EditorStyles.whiteMiniLabel);
            }
        }

        if (item.cutLines != null)
        {
            for (int i = 0; i < item.cutLines.Count; i++)
            {
                InspectableCutLine line = item.cutLines[i];
                if (line == null) continue;

                Vector3 a = root.TransformPoint(line.localStart);
                Vector3 b = root.TransformPoint(line.localEnd);
                DrawScreenDottedLine(previewRect, a, b, color);
                Vector2 mid = _preview.WorldToGui(previewRect, Vector3.Lerp(a, b, 0.5f));
                if (mid.x > -5000f)
                    GUI.Label(new Rect(mid.x + 4f, mid.y - 8f, 40f, 18f), $"─{i + 1}", EditorStyles.whiteMiniLabel);
            }
        }
    }

    void DrawPendingLineOverlay(InspectableItem item, Rect previewRect)
    {
        if (!_hasPendingLineStart || _preview?.PreviewInstance == null) return;

        Vector3 start = _preview.PreviewInstance.transform.TransformPoint(_pendingLineStartLocal);
        Vector2 startGui = _preview.WorldToGui(previewRect, start);
        if (startGui.x < -5000f) return;

        Handles.BeginGUI();
        Handles.color = Color.yellow;
        Handles.DrawSolidDisc(startGui, Vector3.forward, 5f);
        Handles.EndGUI();

        if (previewRect.Contains(Event.current.mousePosition)
            && _preview.Raycast(previewRect, Event.current.mousePosition, out Vector3 hover, out _))
        {
            DrawScreenDottedLine(previewRect, start, hover, Color.yellow);
        }
    }

    void DrawScreenDottedCircle(Rect previewRect, Vector3 center, Vector3 normal, float radius, Color color)
    {
        Vector3 n = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
        Vector3 tangent = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 bitangent = Vector3.Cross(n, tangent);

        int segments = Mathf.Max(20, Mathf.CeilToInt(radius * 120f));
        Vector2[] pts = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float ang = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 w = center + (Mathf.Cos(ang) * tangent + Mathf.Sin(ang) * bitangent) * radius;
            pts[i] = _preview.WorldToGui(previewRect, w);
        }

        Handles.BeginGUI();
        Handles.color = color;
        for (int i = 0; i < segments; i++)
        {
            if (i % 2 != 0) continue;
            int next = (i + 1) % segments;
            if (pts[i].x < -5000f || pts[next].x < -5000f) continue;
            Handles.DrawLine(new Vector3(pts[i].x, pts[i].y, 0f), new Vector3(pts[next].x, pts[next].y, 0f));
        }
        Handles.EndGUI();
    }

    void DrawScreenDottedLine(Rect previewRect, Vector3 a, Vector3 b, Color color)
    {
        Vector2 ga = _preview.WorldToGui(previewRect, a);
        Vector2 gb = _preview.WorldToGui(previewRect, b);
        if (ga.x < -5000f || gb.x < -5000f) return;

        const int dashCount = 12;
        Handles.BeginGUI();
        Handles.color = color;
        for (int i = 0; i < dashCount; i += 2)
        {
            float t0 = i / (float)dashCount;
            float t1 = (i + 1) / (float)dashCount;
            Vector2 p0 = Vector2.Lerp(ga, gb, t0);
            Vector2 p1 = Vector2.Lerp(ga, gb, t1);
            Handles.DrawLine(new Vector3(p0.x, p0.y, 0f), new Vector3(p1.x, p1.y, 0f));
        }
        Handles.EndGUI();
    }

    void HandlePreviewInput(InspectableItem item, Rect previewRect)
    {
        Event e = Event.current;
        if (!previewRect.Contains(e.mousePosition)) return;

        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            if (e.shift)
            {
                if (TryDeleteNearestCutTarget(item, previewRect, e.mousePosition))
                {
                    e.Use();
                    Repaint();
                }
                return;
            }

            if (_preview.Raycast(previewRect, e.mousePosition, out Vector3 point, out Vector3 normal))
            {
                if (_tool == CutEditTool.CircleAnchor)
                    AddCircleAnchor(item, point, normal);
                else
                    HandleLineClick(item, point);

                e.Use();
                Repaint();
            }
        }
    }

    void DrawKnifeOverlay(InspectableItem item, Rect previewRect)
    {
        Sprite sprite = InspectionUiSprites.ResolveKnifeSprite(item.knifeSprite, null);
        DrawToolOverlay(
            previewRect,
            sprite,
            item.knifeIdleAnchor,
            item.knifeUISize,
            item.knifeTipPivot,
            item.knifeUIRotation);
    }

    void DrawHammerOverlay(InspectableItem item, Rect previewRect)
    {
        Sprite sprite = InspectionUiSprites.ResolveHammerSprite(item.hammerSprite, null);
        DrawToolOverlay(
            previewRect,
            sprite,
            item.hammerIdleAnchor,
            item.hammerUISize,
            item.hammerHeadPivot,
            item.hammerUIRotation);
    }

    static void DrawToolOverlay(
        Rect previewRect,
        Sprite sprite,
        Vector2 idleAnchor,
        Vector2 uiSize,
        Vector2 pivot01,
        float rotation)
    {
        if (sprite == null || sprite.texture == null) return;

        const float refW = 1920f;
        const float refH = 1080f;
        float drawW = uiSize.x * (previewRect.width / refW);
        float drawH = uiSize.y * (previewRect.height / refH);

        float centerX = Mathf.Lerp(previewRect.xMin, previewRect.xMax, idleAnchor.x);
        float centerY = Mathf.Lerp(previewRect.yMax, previewRect.yMin, idleAnchor.y);

        Matrix4x4 prev = GUI.matrix;
        GUIUtility.RotateAroundPivot(rotation, new Vector2(centerX, centerY));
        Rect imgRect = new Rect(
            centerX - pivot01.x * drawW,
            centerY - (1f - pivot01.y) * drawH,
            drawW,
            drawH);
        DrawSprite(imgRect, sprite);
        GUI.matrix = prev;
    }

    static void DrawSprite(Rect rect, Sprite sprite)
    {
        if (sprite == null || sprite.texture == null) return;

        Rect tex = sprite.textureRect;
        Rect uv = new Rect(
            tex.x / sprite.texture.width,
            tex.y / sprite.texture.height,
            tex.width / sprite.texture.width,
            tex.height / sprite.texture.height);
        GUI.DrawTextureWithTexCoords(rect, sprite.texture, uv, true);
    }

    void DrawKnifeTipEditor(InspectableItem item)
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("刀尖位置", EditorStyles.boldLabel);

        Sprite sprite = InspectionUiSprites.ResolveKnifeSprite(item.knifeSprite, null);
        if (sprite == null || sprite.texture == null)
        {
            EditorGUILayout.PropertyField(_knifeTipPivot, new GUIContent("刀尖 Pivot（归一化）"));
            EditorGUILayout.HelpBox("未指定 knifeSprite 时将使用内置占位刀图；也可拖入自定义 Sprite。", MessageType.Info);
            return;
        }

        float previewW = 180f;
        float aspect = sprite.rect.height / Mathf.Max(1f, sprite.rect.width);
        float previewH = previewW * aspect;
        Rect block = GUILayoutUtility.GetRect(previewW, previewH + 20f);
        Rect imgRect = new Rect(block.x, block.y, previewW, previewH);

        DrawSprite(imgRect, sprite);

        Vector2 pivot = item.knifeTipPivot;
        Vector2 handleScreen = new Vector2(
            imgRect.x + pivot.x * imgRect.width,
            imgRect.y + (1f - pivot.y) * imgRect.height);

        const float handleSize = 10f;
        Rect handleRect = new Rect(
            handleScreen.x - handleSize * 0.5f,
            handleScreen.y - handleSize * 0.5f,
            handleSize, handleSize);
        EditorGUI.DrawRect(handleRect, Color.red);

        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        switch (Event.current.type)
        {
            case EventType.MouseDown:
                if (Event.current.button == 0 && handleRect.Contains(Event.current.mousePosition))
                {
                    _draggingKnifePivot = controlId;
                    GUIUtility.hotControl = controlId;
                    Event.current.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == _draggingKnifePivot)
                {
                    Vector2 local = Event.current.mousePosition - imgRect.position;
                    Vector2 newPivot = new Vector2(
                        Mathf.Clamp01(local.x / imgRect.width),
                        Mathf.Clamp01(1f - local.y / imgRect.height));
                    Undo.RecordObject(item, "Move Knife Tip Pivot");
                    item.knifeTipPivot = newPivot;
                    EditorUtility.SetDirty(item);
                    Repaint();
                    Event.current.Use();
                }
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == _draggingKnifePivot)
                {
                    GUIUtility.hotControl = 0;
                    _draggingKnifePivot = 0;
                    Event.current.Use();
                }
                break;
        }

        EditorGUILayout.LabelField("Pivot", $"{item.knifeTipPivot.x:F2}, {item.knifeTipPivot.y:F2}");
    }

    void AddCircleAnchor(InspectableItem item, Vector3 worldPoint, Vector3 worldNormal)
    {
        Undo.RecordObject(item, "Add Cut Anchor");
        if (item.cutAnchors == null)
            item.cutAnchors = new List<InspectableCutAnchor>();

        item.cutAnchors.Add(new InspectableCutAnchor
        {
            localPosition = _preview.WorldToSourceLocal(worldPoint),
            localNormal = _preview.WorldToSourceLocalDirection(worldNormal),
            radius = 0.05f,
        });
        EditorUtility.SetDirty(item);
        serializedObject.Update();
    }

    void HandleLineClick(InspectableItem item, Vector3 worldPoint)
    {
        if (!_hasPendingLineStart)
        {
            _pendingLineStartLocal = _preview.WorldToSourceLocal(worldPoint);
            _hasPendingLineStart = true;
            return;
        }

        Undo.RecordObject(item, "Add Cut Line");
        if (item.cutLines == null)
            item.cutLines = new List<InspectableCutLine>();

        item.cutLines.Add(new InspectableCutLine
        {
            localStart = _pendingLineStartLocal,
            localEnd = _preview.WorldToSourceLocal(worldPoint),
            hitRadius = 0.04f,
        });
        _hasPendingLineStart = false;
        EditorUtility.SetDirty(item);
        serializedObject.Update();
    }

    bool TryDeleteNearestCutTarget(InspectableItem item, Rect previewRect, Vector2 guiMouse)
    {
        if (_preview?.PreviewInstance == null) return false;

        Transform root = _preview.PreviewInstance.transform;
        float bestDist = PreviewPickRadiusPx;
        bool found = false;
        bool isAnchor = true;
        int bestIndex = -1;

        if (item.cutAnchors != null)
        {
            for (int i = 0; i < item.cutAnchors.Count; i++)
            {
                InspectableCutAnchor anchor = item.cutAnchors[i];
                if (anchor == null) continue;
                Vector3 w = root.TransformPoint(anchor.localPosition);
                Vector2 gui = _preview.WorldToGui(previewRect, w);
                if (gui.x < -5000f) continue;
                float d = Vector2.Distance(gui, guiMouse);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestIndex = i;
                    isAnchor = true;
                    found = true;
                }
            }
        }

        if (item.cutLines != null)
        {
            for (int i = 0; i < item.cutLines.Count; i++)
            {
                InspectableCutLine line = item.cutLines[i];
                if (line == null) continue;
                Vector2 ga = _preview.WorldToGui(previewRect, root.TransformPoint(line.localStart));
                Vector2 gb = _preview.WorldToGui(previewRect, root.TransformPoint(line.localEnd));
                if (ga.x < -5000f || gb.x < -5000f) continue;
                float d = DistancePointToSegment(guiMouse, ga, gb);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestIndex = i;
                    isAnchor = false;
                    found = true;
                }
            }
        }

        if (!found) return false;

        Undo.RecordObject(item, "Remove Cut Target");
        if (isAnchor)
            item.cutAnchors.RemoveAt(bestIndex);
        else
            item.cutLines.RemoveAt(bestIndex);

        EditorUtility.SetDirty(item);
        serializedObject.Update();
        return true;
    }

    static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lenSqr = ab.sqrMagnitude;
        if (lenSqr < 1e-6f) return Vector2.Distance(p, a);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSqr);
        return Vector2.Distance(p, a + ab * t);
    }

    void ClearList<T>(List<T> list, SerializedProperty prop)
    {
        InspectableItem item = (InspectableItem)target;
        Undo.RecordObject(item, "Clear Cut Targets");
        list?.Clear();
        _hasPendingLineStart = false;
        serializedObject.Update();
        prop.ClearArray();
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(item);
        Repaint();
    }
}

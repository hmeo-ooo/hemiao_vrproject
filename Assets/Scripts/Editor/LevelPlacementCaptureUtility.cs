#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 将场景物体的 Transform 写入 LevelAislePlacement / LevelPropPlacement 的 local 字段。
/// 坐标相对 LevelManager 的 aislesRoot / propsRoot；找不到时写入世界坐标。
/// </summary>
static class LevelPlacementCaptureUtility
{
    public enum PlacementRootKind
    {
        Aisles,
        Props,
    }

    /// <summary>绘制“从场景拖入 + 读取选中物体”控件，返回占用高度。</summary>
    public static float DrawSceneCaptureField(
        Rect rect,
        SerializedProperty placementProperty,
        PlacementRootKind rootKind,
        bool includeScale)
    {
        float line = EditorGUIUtility.singleLineHeight;
        float gap = EditorGUIUtility.standardVerticalSpacing;
        float y = rect.y;

        Rect objectRow = new Rect(rect.x, y, rect.width, line);
        Rect fieldRect = EditorGUI.PrefixLabel(objectRow, new GUIContent("从场景拖入"));
        EditorGUI.BeginChangeCheck();
        Transform sceneTransform = (Transform)EditorGUI.ObjectField(
            fieldRect,
            null,
            typeof(Transform),
            true);
        if (EditorGUI.EndChangeCheck() && sceneTransform != null)
        {
            CaptureTransform(placementProperty, sceneTransform, rootKind, includeScale);
            MarkParentDirty(placementProperty);
        }
        y += line + gap;

        Rect buttonRect = new Rect(rect.x, y, rect.width, line);
        if (GUI.Button(buttonRect, "读取当前选中物体坐标"))
        {
            Transform selected = Selection.activeTransform;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("未选中物体", "请先在 Hierarchy 中选中一个场景物体。", "确定");
            }
            else
            {
                CaptureTransform(placementProperty, selected, rootKind, includeScale);
                MarkParentDirty(placementProperty);
            }
        }

        return line + gap + line + gap;
    }

    public static void CaptureTransform(
        SerializedProperty placementProperty,
        Transform sceneTransform,
        PlacementRootKind rootKind,
        bool includeScale)
    {
        if (placementProperty == null || sceneTransform == null) return;

        Transform root = FindPlacementRoot(rootKind, sceneTransform);
        SerializedProperty localPos = placementProperty.FindPropertyRelative("localPosition");
        SerializedProperty localEuler = placementProperty.FindPropertyRelative("localEulerAngles");

        if (root != null)
        {
            if (localPos != null)
                localPos.vector3Value = root.InverseTransformPoint(sceneTransform.position);
            if (localEuler != null)
            {
                Quaternion localRot = Quaternion.Inverse(root.rotation) * sceneTransform.rotation;
                localEuler.vector3Value = localRot.eulerAngles;
            }

            if (includeScale)
            {
                SerializedProperty localScale = placementProperty.FindPropertyRelative("localScale");
                if (localScale != null)
                    localScale.vector3Value = ComputeLocalScale(root, sceneTransform);
            }
        }
        else
        {
            if (localPos != null)
                localPos.vector3Value = sceneTransform.position;
            if (localEuler != null)
                localEuler.vector3Value = sceneTransform.eulerAngles;
            if (includeScale)
            {
                SerializedProperty localScale = placementProperty.FindPropertyRelative("localScale");
                if (localScale != null)
                    localScale.vector3Value = sceneTransform.lossyScale;
            }

            Debug.LogWarning(
                "[LevelDefinition] 未找到可用 LevelManager（或 aislesRoot/propsRoot）。" +
                "已写入世界坐标。请打开含 LevelManager 的场景后再拖入；" +
                "若 LevelManager 的 aislesRoot/propsRoot 未指定，运行时会回退到自身 Transform，Editor 中也会同样处理。");
        }

        placementProperty.serializedObject.ApplyModifiedProperties();
    }

    static Transform FindPlacementRoot(PlacementRootKind rootKind, Transform contextTransform)
    {
        LevelManager manager = FindLevelManagerInEditor(contextTransform);
        if (manager == null) return null;

        Transform root = rootKind == PlacementRootKind.Aisles ? manager.aislesRoot : manager.propsRoot;
        // 与 LevelManager.Awake 一致：未指定时回退到 LevelManager 自身
        if (root == null)
            root = manager.transform;
        return root;
    }

    /// <summary>
    /// Editor 中 Object.FindObjectOfType 在仅选中 ScriptableObject 时常返回 null；
    /// 改用 Resources.FindObjectsOfTypeAll 并过滤出已加载场景里的实例。
    /// 优先选取与 contextTransform 同场景的 LevelManager。
    /// </summary>
    static LevelManager FindLevelManagerInEditor(Transform contextTransform)
    {
        LevelManager[] all = Resources.FindObjectsOfTypeAll<LevelManager>();
        LevelManager sameScene = null;
        LevelManager anyLoaded = null;

        for (int i = 0; i < all.Length; i++)
        {
            LevelManager manager = all[i];
            if (manager == null) continue;

            GameObject go = manager.gameObject;
            if (EditorUtility.IsPersistent(go)) continue;
            if ((go.hideFlags & HideFlags.HideInHierarchy) != 0) continue;

            Scene scene = go.scene;
            if (!scene.IsValid() || !scene.isLoaded) continue;

            if (contextTransform != null && contextTransform.gameObject.scene == scene)
            {
                sameScene = manager;
                break;
            }

            if (anyLoaded == null)
                anyLoaded = manager;
        }

        return sameScene != null ? sameScene : anyLoaded;
    }

    static Vector3 ComputeLocalScale(Transform root, Transform sceneTransform)
    {
        Vector3 rootScale = root.lossyScale;
        Vector3 lossy = sceneTransform.lossyScale;
        return new Vector3(
            Mathf.Approximately(rootScale.x, 0f) ? lossy.x : lossy.x / rootScale.x,
            Mathf.Approximately(rootScale.y, 0f) ? lossy.y : lossy.y / rootScale.y,
            Mathf.Approximately(rootScale.z, 0f) ? lossy.z : lossy.z / rootScale.z);
    }

    static void MarkParentDirty(SerializedProperty placementProperty)
    {
        placementProperty.serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(placementProperty.serializedObject.targetObject);
    }
}
#endif

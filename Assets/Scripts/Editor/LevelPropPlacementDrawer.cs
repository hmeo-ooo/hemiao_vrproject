#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(LevelPropPlacement))]
public class LevelPropPlacementDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        float line = EditorGUIUtility.singleLineHeight;
        float gap = EditorGUIUtility.standardVerticalSpacing;
        float y = position.y;

        property.isExpanded = EditorGUI.Foldout(
            new Rect(position.x, y, position.width, line),
            property.isExpanded,
            label,
            true);
        y += line + gap;

        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;

            float captureHeight = LevelPlacementCaptureUtility.DrawSceneCaptureField(
                new Rect(position.x, y, position.width, line * 2f + gap * 2f),
                property,
                LevelPlacementCaptureUtility.PlacementRootKind.Props,
                includeScale: false);
            y += captureHeight;

            y = DrawChild(property, y, position.x, position.width, "prefab");
            y = DrawChild(property, y, position.x, position.width, "localPosition");
            y = DrawChild(property, y, position.x, position.width, "localEulerAngles");

            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!property.isExpanded)
            return EditorGUIUtility.singleLineHeight;

        float line = EditorGUIUtility.singleLineHeight;
        float gap = EditorGUIUtility.standardVerticalSpacing;
        float h = line + gap;
        h += line + gap + line + gap;
        h += GetChildHeight(property, "prefab");
        h += GetChildHeight(property, "localPosition");
        h += GetChildHeight(property, "localEulerAngles");
        return h;
    }

    static float DrawChild(SerializedProperty parent, float y, float x, float width, string name)
    {
        SerializedProperty child = parent.FindPropertyRelative(name);
        if (child == null) return y;

        float h = EditorGUI.GetPropertyHeight(child, true);
        EditorGUI.PropertyField(new Rect(x, y, width, h), child, true);
        return y + h + EditorGUIUtility.standardVerticalSpacing;
    }

    static float GetChildHeight(SerializedProperty parent, string name)
    {
        SerializedProperty child = parent.FindPropertyRelative(name);
        if (child == null) return 0f;
        return EditorGUI.GetPropertyHeight(child, true) + EditorGUIUtility.standardVerticalSpacing;
    }
}
#endif

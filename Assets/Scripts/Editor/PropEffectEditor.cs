using UnityEditor;
using UnityEngine;

/// <summary>
/// PropEffect 的自定义 Inspector：根据 propType 只显示对应类型的子配置，
/// 避免把所有道具的字段堆在一起。
/// </summary>
[CustomEditor(typeof(PropEffect))]
public class PropEffectEditor : Editor
{
    SerializedProperty _propType;
    SerializedProperty _triggerOnce;
    SerializedProperty _coin;
    SerializedProperty _magnet;
    SerializedProperty _lighter;

    void OnEnable()
    {
        _propType = serializedObject.FindProperty("propType");
        _triggerOnce = serializedObject.FindProperty("triggerOnce");
        _coin = serializedObject.FindProperty("coin");
        _magnet = serializedObject.FindProperty("magnet");
        _lighter = serializedObject.FindProperty("lighter");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(_propType);
        EditorGUILayout.Space(4f);

        PropEffect.PropType type = (PropEffect.PropType)_propType.enumValueIndex;

        switch (type)
        {
            case PropEffect.PropType.Coin:
                EditorGUILayout.PropertyField(_triggerOnce);
                EditorGUILayout.Space(2f);
                DrawSectionHeader("Coin 道具设置");
                EditorGUILayout.PropertyField(_coin, includeChildren: true);
                break;

            case PropEffect.PropType.Magnet:
                DrawSectionHeader("Magnet 道具设置");
                EditorGUILayout.HelpBox(
                    "玩家持握磁石时持续吸附范围内的指定分类垃圾（默认 Metal），\n" +
                    "上限达到后停止吸附；松手后已附着物随磁石移动不分离；\n" +
                    "磁石被投入分拣通道时一次性结算所有附着物的总价。",
                    MessageType.Info);
                EditorGUILayout.PropertyField(_magnet, includeChildren: true);
                break;

            case PropEffect.PropType.Lighter:
                DrawSectionHeader("Lighter 道具设置");
                EditorGUILayout.HelpBox(
                    "玩家持握打火机时持续点燃范围内的指定分类垃圾（默认 OrganicMatter），\n" +
                    "点燃后经 burnDelay 秒销毁目标。",
                    MessageType.Info);
                EditorGUILayout.PropertyField(_lighter, includeChildren: true);
                break;
        }

        serializedObject.ApplyModifiedProperties();
    }

    static void DrawSectionHeader(string title)
    {
        EditorGUILayout.Space(4f);
        Rect r = GUILayoutUtility.GetRect(1f, 1f);
        EditorGUI.DrawRect(r, new Color(0.4f, 0.4f, 0.4f, 0.5f));
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }
}

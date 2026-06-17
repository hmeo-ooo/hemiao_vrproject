using UnityEngine;

/// <summary>
/// 运行时 UI 工具：HideAndDontSave 标记与销毁，避免 Play 模式 Inspector 报错。
/// </summary>
static class RuntimeUiUtility
{
    public static void MarkPlayModeOnly(GameObject root)
    {
        if (root == null) return;
        root.hideFlags = HideFlags.HideAndDontSave;
        Transform t = root.transform;
        for (int i = 0; i < t.childCount; i++)
            MarkPlayModeOnly(t.GetChild(i).gameObject);
    }

    public static void DestroyCanvas(ref Canvas canvas)
    {
        if (canvas == null) return;

        GameObject go = canvas.gameObject;
        canvas = null;
        if (go == null) return;

        if (Application.isPlaying)
            Object.Destroy(go);
        else
            Object.DestroyImmediate(go);
    }
}

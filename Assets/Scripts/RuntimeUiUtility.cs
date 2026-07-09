using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 运行时 UI 工具：创建后的 Canvas 在 Hierarchy 中可见、可选、可编辑；
/// 退出 Play 时由 DestroyCanvas / 父物体销毁清理，不写入场景。
/// </summary>
public static class RuntimeUiUtility
{
    public static void ConfigureOverlayCanvasScaler(CanvasScaler scaler)
    {
        if (scaler == null) return;

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = GameDisplaySettings.DesignReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }

    /// <summary>
    /// 确保运行时 UI 无 hideFlags，在 Hierarchy 中正常显示并可编辑。
    /// </summary>
    public static void MarkPlayModeOnly(GameObject root)
    {
        if (root == null) return;
        root.hideFlags = HideFlags.None;
        Transform t = root.transform;
        for (int i = 0; i < t.childCount; i++)
            MarkPlayModeOnly(t.GetChild(i).gameObject);
    }

    /// <summary>
    /// Overlay Canvas 挂到普通 Transform 下时，校正 scale / Rect，避免继承 HUD 的 scale=0。
    /// </summary>
    public static void NormalizeOverlayCanvas(Canvas canvas, Transform preferredParent = null)
    {
        if (canvas == null) return;

        Transform t = canvas.transform;
        if (preferredParent != null && t.parent != preferredParent)
            t.SetParent(preferredParent, false);

        t.localScale = Vector3.one;
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;

        var rt = t as RectTransform;
        if (rt != null)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
        }

        if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        canvas.enabled = true;
        if (!canvas.gameObject.activeSelf)
            canvas.gameObject.SetActive(true);
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

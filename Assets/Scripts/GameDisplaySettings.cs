using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 全局显示配置：以 1920×1080 为设计基准，4K 等高分辨率屏下统一缩放 UI 与 OnGUI。
/// </summary>
public static class GameDisplaySettings
{
    public static readonly Vector2 DesignReferenceResolution = new Vector2(1920f, 1080f);
    public const float DesignWidth = 1920f;
    public const float DesignHeight = 1080f;

    /// <summary>宽或高达到此阈值视为高分辨率（含 4K UHD）。</summary>
    public const int HighResolutionThreshold = 2560;

    static float _uiScaleFactor = 1f;
    static int _lastScreenWidth;
    static int _lastScreenHeight;

    /// <summary>
    /// 相对设计分辨率的 UI 缩放系数（与 CanvasScaler MatchWidthOrHeight = 0.5 一致）。
    /// </summary>
    public static float UiScaleFactor
    {
        get
        {
            RefreshUiScaleFactorIfNeeded();
            return _uiScaleFactor;
        }
    }

    public static bool IsHighResolution =>
        Screen.width >= HighResolutionThreshold || Screen.height >= HighResolutionThreshold;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Initialize()
    {
        RefreshUiScaleFactorIfNeeded();
        ApplyToAllOverlayCanvasScalers();
    }

    public static void ApplyToAllOverlayCanvasScalers()
    {
        var scalers = Object.FindObjectsOfType<CanvasScaler>(true);
        for (int i = 0; i < scalers.Length; i++)
        {
            var scaler = scalers[i];
            if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
                continue;

            RuntimeUiUtility.ConfigureOverlayCanvasScaler(scaler);
        }
    }

    public static void RefreshUiScaleFactorIfNeeded()
    {
        if (Screen.width == _lastScreenWidth && Screen.height == _lastScreenHeight)
            return;

        _lastScreenWidth = Screen.width;
        _lastScreenHeight = Screen.height;

        float scaleW = Screen.width / DesignWidth;
        float scaleH = Screen.height / DesignHeight;
        _uiScaleFactor = Mathf.Sqrt(scaleW * scaleH);
    }

    /// <summary>将基于 1920×1080 设计的像素值换算为当前屏幕像素。</summary>
    public static float ScaleDesignPixels(float designPixels) => designPixels * UiScaleFactor;

    public static int ScaleDesignPixelsInt(float designPixels) =>
        Mathf.Max(1, Mathf.RoundToInt(ScaleDesignPixels(designPixels)));
}

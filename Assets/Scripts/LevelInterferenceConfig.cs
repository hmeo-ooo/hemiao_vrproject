using UnityEngine;

/// <summary>
/// 关卡内单条干扰（Interference）的配置。
/// 由 <see cref="LevelDefinition"/> 持有，<see cref="LevelSessionController"/>
/// 在回合开始后按 <see cref="triggerAtSeconds"/> 排程触发，
/// 到达 <see cref="durationSeconds"/> 后自动收起。
/// </summary>
[System.Serializable]
public class LevelInterferenceConfig
{
    public enum InterferenceType
    {
        /// <summary>屏幕叠加“电视雪花”噪声效果。</summary>
        TVStaticOverlay = 0,
    }

    [Tooltip("干扰类型。目前仅有 TVStaticOverlay（电视雪花点）。")]
    public InterferenceType type = InterferenceType.TVStaticOverlay;

    [Tooltip("从本回合开始后多少秒触发该干扰。例如 30 = 进入关卡 30 秒后开始。")]
    [Min(0f)]
    public float triggerAtSeconds = 30f;

    [Tooltip("干扰持续时间（秒）。0 = 一直持续到回合结束。")]
    [Min(0f)]
    public float durationSeconds = 0f;

    [Header("TV Static Overlay — 雪花")]
    [Tooltip("雪花点整体不透明度。0 = 不可见；1 = 完全遮挡画面。")]
    [Range(0f, 1f)]
    public float intensity = 0.45f;

    [Tooltip("雪花点动画帧率（每秒重新生成噪声纹理的次数）。值越高动画越快、性能开销越大。")]
    [Min(1)]
    public int noiseFps = 24;

    [Tooltip("噪声纹理的边长（像素）。常用 128 ~ 256；越大颗粒越细，越小颗粒越粗。")]
    [Range(32, 1024)]
    public int noiseTextureSize = 256;

    [Tooltip("雪花点的色调。Alpha 会再与 intensity 相乘。")]
    public Color tint = Color.white;

    [Header("TV Static Overlay — 中央图案 / 取消")]
    [Tooltip("屏幕中央跳动图案使用的精灵。留空时画一个默认的白色矩形。")]
    public Sprite centerPatternSprite;

    [Tooltip("中央图案的像素尺寸（参考 1920×1080）。")]
    public Vector2 centerPatternSize = new Vector2(220f, 220f);

    [Tooltip("中央图案放大跳动的最大缩放倍数（1=不跳动）。")]
    [Min(1f)]
    public float centerPatternPulseScale = 1.25f;

    [Tooltip("中央图案跳动频率（Hz，每秒来回次数）。")]
    [Min(0f)]
    public float centerPatternPulseFrequencyHz = 2f;

    [Tooltip("中央图案静止时的颜色。")]
    public Color centerPatternColor = Color.white;

    [Tooltip("玩家用来取消干扰的按键。")]
    public KeyCode cancelKey = KeyCode.E;

    [Tooltip("连续点击多少次 cancelKey 才能提前结束干扰。")]
    [Min(1)]
    public int pressesToCancel = 10;

    [Tooltip("每次按下 cancelKey 时中央图案闪烁的颜色。")]
    public Color flashColor = Color.red;

    [Tooltip("单次按键闪烁持续时间（秒）。")]
    [Min(0f)]
    public float flashDuration = 0.18f;
}

using System;
using UnityEngine;

/// <summary>
/// 关卡中一个静态道具的生成方式。坐标可在 Inspector 中从场景拖入物体自动写入。
/// </summary>
[Serializable]
public class LevelPropPlacement
{
    [Tooltip("要生成的预制体。")]
    public GameObject prefab;

    [Tooltip("相对 LevelManager.propsRoot 的本地坐标。在 Inspector 中可从场景拖入物体自动写入。")]
    public Vector3 localPosition;

    [Tooltip("相对 LevelManager.propsRoot 的本地欧拉角。在 Inspector 中可从场景拖入物体自动写入。")]
    public Vector3 localEulerAngles;
}

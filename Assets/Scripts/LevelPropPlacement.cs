using System;
using UnityEngine;

/// <summary>
/// 关卡中一个静态道具的生成方式：优先使用 spawnPoint，否则用 positionEuler。
/// </summary>
[Serializable]
public class LevelPropPlacement
{
    [Tooltip("要生成的预制体。")]
    public GameObject prefab;

    [Tooltip("可选：场景里预先摆好的空物体，决定位置与旋转。")]
    public Transform spawnPoint;

    [Tooltip("未指定 spawnPoint 时，相对 propsRoot 的本地坐标。")]
    public Vector3 localPosition;

    [Tooltip("未指定 spawnPoint 时，相对 propsRoot 的本地欧拉角。")]
    public Vector3 localEulerAngles;
}

using System;
using UnityEngine;

/// <summary>
/// 关卡中一条分拣通道的配置：接受哪类物品、初始位置与旋转。
/// 由 <see cref="LevelDefinition.aisles"/> 持有，
/// <see cref="LevelManager"/> 在 LoadLevel 时据此生成或摆放通道。
/// </summary>
[Serializable]
public class LevelAislePlacement
{
    [Tooltip("本通道接受的物品类别（与 ItemInformation.category / AisleDetection.aisleCategory 对应）。")]
    public ItemInformation.ItemCategory category = ItemInformation.ItemCategory.Metal;

    [Tooltip("可选：仅用于在 Inspector 中标注，例如「左侧金属通道」。")]
    public string label;

    [Tooltip("可选：要使用的通道预制体（需含 AisleDetection + Collider）。留空则使用 LevelManager.defaultAislePrefab。")]
    public GameObject prefab;

    [Tooltip("相对 LevelManager.aislesRoot 的本地坐标。在 Inspector 中可从场景拖入物体自动写入。")]
    public Vector3 localPosition;

    [Tooltip("相对 LevelManager.aislesRoot 的本地欧拉角。在 Inspector 中可从场景拖入物体自动写入。")]
    public Vector3 localEulerAngles;

    [Tooltip("相对 LevelManager.aislesRoot 的本地缩放。在 Inspector 中可从场景拖入物体自动写入。")]
    public Vector3 localScale = Vector3.one;
}

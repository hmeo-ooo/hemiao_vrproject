using UnityEngine;

/// <summary>
/// 在 Cuttable / InspectableItem 等"可拆解物体"分离出某个子件时，应用到该子件上的
/// ItemInformation 数据。任一字段留空 / 不勾 override，则保留分离前已有的值。
/// 若子件原本没有 ItemInformation，会在第一次有内容写入时自动 AddComponent。
/// </summary>
[System.Serializable]
public class ItemPartInfoOverride
{
    [Tooltip("可选：明确指定要写入的子件。\n" +
             "InspectableItem 中按引用匹配 detachableParts；\n" +
             "Cuttable 中留空表示按数组顺序匹配。")]
    public Transform targetPart;

    [Tooltip("仅 Inspector 备注，不参与逻辑。")]
    public string note;

    [Tooltip("显示名称。留空时保留原名（或 GameObject 名）。")]
    public string displayName;

    [Tooltip("准星 UI 显示的介绍文本。留空保留原文本。")]
    [TextArea(2, 6)]
    public string description;

    [Tooltip("是否覆盖分类（决定投入哪条分拣通道）。")]
    public bool overrideCategory = true;

    public ItemInformation.ItemCategory category = ItemInformation.ItemCategory.Metal;

    [Tooltip("是否覆盖复杂度。")]
    public bool overrideComplexity = false;

    public ItemInformation.ItemComplexity complexity = ItemInformation.ItemComplexity.Basic;

    [Tooltip("是否覆盖正确投入奖励。")]
    public bool overrideCredits = false;

    [Min(0)] public int creditsOnCorrectThrow = 10;

    [Tooltip("恶搞价值标签。留空时保留原标签。")]
    public string prankValueLabel;

    [Tooltip("是否覆盖描边颜色。默认不覆盖，与 ItemSpawner 生成物一样按 category 着色。")]
    public bool overrideOutlineColor = false;

    public Color outlineColor = Color.white;

    public bool HasAnyContent =>
        !string.IsNullOrWhiteSpace(displayName)
        || !string.IsNullOrWhiteSpace(description)
        || overrideCategory
        || overrideComplexity
        || overrideCredits
        || !string.IsNullOrWhiteSpace(prankValueLabel)
        || overrideOutlineColor;

    public void ApplyTo(Transform part)
    {
        if (part == null || !HasAnyContent) return;

        ItemInformation ii = part.GetComponent<ItemInformation>();
        if (ii == null)
            ii = part.gameObject.AddComponent<ItemInformation>();

        if (!string.IsNullOrWhiteSpace(displayName))
            ii.itemDisplayName = displayName;

        if (!string.IsNullOrWhiteSpace(description))
            ii.itemDescription = description;

        if (overrideCategory) ii.category = category;
        if (overrideComplexity) ii.complexity = complexity;
        if (overrideCredits) ii.creditsOnCorrectThrow = creditsOnCorrectThrow;

        if (!string.IsNullOrWhiteSpace(prankValueLabel))
            ii.prankValueLabel = prankValueLabel;

        if (overrideOutlineColor)
        {
            ii.overrideOutlineColor = true;
            ii.outlineColor = outlineColor;
        }
    }
}

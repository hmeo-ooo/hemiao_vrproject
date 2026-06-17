using UnityEngine;

public class ItemInformation : MonoBehaviour
{
    public enum ItemCategory
    {
        Metal,
        OrganicMatter,
        CoreEnergy,
        DangerousGoods,
        /// <summary>
        /// 道具类。投入任意分拣通道仅销毁、无奖惩；每个具体道具的实际效果由挂在物品上的
        /// <see cref="PropEffect"/> 组件（按 propType 选择 Coin / Magnet / Lighter）实现，
        /// 通常在被玩家抓取或持握期间触发。
        /// </summary>
        Prop,
    }

    /// <summary>
    /// 物品复杂度，用于关卡按概率抽取掉落物。
    /// Basic：基础单体物（金属/有机/核心能源单件等）。
    /// Composite：复合物（如可拆解/可切割的多部件物）。
    /// Dangerous：高危品（危险品，错误处理风险更高）。
    /// </summary>
    public enum ItemComplexity
    {
        Basic,
        Composite,
        Dangerous
    }

    [Tooltip("\u7269\u54C1\u5206\u7C7B\uFF0C\u7528\u4E8E\u5206\u62E3\u901A\u9053\u5224\u65AD")]
    public ItemCategory category = ItemCategory.Metal;

    [Tooltip("\u7269\u54C1\u590D\u6742\u5EA6\u3002\u5173\u5361\u53EF\u6309\u6BD4\u4F8B\u62BD\u53D6\u6BCF\u79CD\u590D\u6742\u5EA6\u3002")]
    public ItemComplexity complexity = ItemComplexity.Basic;

    [Header("\u5206\u62E3\u5956\u52B1")]
    [Tooltip("\u6295\u5165\u5339\u914D\u5206\u7C7B\u7684\u901A\u9053\u65F6\u589E\u52A0\u7684\u4FE1\u7528\u70B9\u3002")]
    public int creditsOnCorrectThrow = 10;

    [Header("\u5C55\u793A\u4FE1\u606F")]
    [Tooltip("\u51C6\u661F\u5BF9\u51C6\u65F6 UI \u663E\u793A\u7684\u540D\u79F0")]
    public string itemDisplayName;

    [Tooltip("\u51C6\u661F\u5BF9\u51C6\u65F6 UI \u663E\u793A\u7684\u4ECB\u7ECD")]
    [TextArea(2, 6)]
    public string itemDescription;

    public string ResolvedDisplayName =>
        string.IsNullOrWhiteSpace(itemDisplayName) ? gameObject.name : itemDisplayName;

    void Awake()
    {
        ApplyDefaultDisplayIfEmpty();
    }

    void OnValidate()
    {
        ApplyDefaultDisplayIfEmpty();
    }

    void ApplyDefaultDisplayIfEmpty()
    {
        if (string.IsNullOrWhiteSpace(itemDisplayName))
            itemDisplayName = gameObject.name;

        if (string.IsNullOrWhiteSpace(itemDescription))
        {
            switch (category)
            {
                case ItemCategory.Metal:
                    itemDescription = "\u91D1\u5C5E\u7C7B\u56DE\u6536\u7269\uFF0C\u6295\u5165\u91D1\u5C5E\u901A\u9053\u3002";
                    break;
                case ItemCategory.OrganicMatter:
                    itemDescription = "\u6709\u673A\u7C7B\u56DE\u6536\u7269\uFF0C\u6295\u5165\u6709\u673A\u901A\u9053\u3002";
                    break;
                case ItemCategory.CoreEnergy:
                    itemDescription = "\u6838\u5FC3\u80FD\u6E90\uFF0C\u6295\u5165\u6838\u5FC3\u80FD\u6E90\u901A\u9053\u3002";
                    break;
                case ItemCategory.DangerousGoods:
                    itemDescription = "\u5371\u9669\u54C1\uFF0C\u6295\u5165\u5371\u9669\u901A\u9053\u3002";
                    break;
                case ItemCategory.Prop:
                    itemDescription = "道具：拾取后触发自身效果，投入任意通道仅销毁、无奖惩。";
                    break;
            }
        }
    }

}

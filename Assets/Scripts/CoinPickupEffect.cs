using UnityEngine;

/// <summary>
/// 金币（Coin）类道具：被玩家拾取后播放粒子爆炸特效与可选音效，
/// 给玩家增加 <see cref="creditReward"/> 信用点，然后销毁自身。
///
/// 用法：把本组件挂到金币 prefab 上，并把 <see cref="ItemInformation.category"/> 设为
/// <see cref="ItemInformation.ItemCategory.Prop"/>，即可在场景中通过 <see cref="TrashHeap"/>
/// 或其他生成器生成、抓取触发。
/// </summary>
[DisallowMultipleComponent]
public class CoinPickupEffect : PropPickupEffect
{
    [Header("拾取奖励")]
    [Tooltip("被拾取后给玩家增加的信用点数。允许为负。")]
    public int creditReward = 100;

    [Tooltip("是否在屏幕底部弹出 +N credits 字幕。")]
    public bool showSubtitle = true;

    [Tooltip("字幕显示时长（秒）。")]
    [Min(0.1f)] public float subtitleDuration = 1.5f;

    [Tooltip("字幕颜色（金黄）。")]
    public Color subtitleColor = new Color(1f, 0.85f, 0.2f, 1f);

    [Header("视觉特效")]
    [Tooltip("拾取瞬间在物体位置生成的粒子/特效 prefab；留空则不生成。")]
    public GameObject pickupVfxPrefab;

    [Tooltip("VFX 自动销毁时间（秒）。<= 0 表示不自动销毁。")]
    public float vfxLifetime = 2f;

    [Header("音效")]
    [Tooltip("额外的拾取音效；不填则只用 CreditManager.AddCredits 里默认的金币音效。")]
    public AudioClip pickupSfx;

    [Tooltip("额外拾取音效的音量。")]
    [Range(0f, 1f)] public float pickupSfxVolume = 1f;

    [Header("销毁")]
    [Tooltip("拾取后是否销毁本 GameObject。")]
    public bool destroyOnPickup = true;

    protected override void OnTriggered()
    {
        Vector3 pos = transform.position;

        SpawnVfx(pos);
        PlayExtraSfx(pos);
        AwardCredits();

        if (destroyOnPickup)
            Destroy(gameObject);
    }

    void SpawnVfx(Vector3 pos)
    {
        if (pickupVfxPrefab == null) return;

        GameObject vfx = Instantiate(pickupVfxPrefab, pos, Quaternion.identity);
        if (vfxLifetime > 0f)
            Destroy(vfx, vfxLifetime);
    }

    void PlayExtraSfx(Vector3 pos)
    {
        if (pickupSfx == null) return;
        AudioSource.PlayClipAtPoint(pickupSfx, pos, Mathf.Clamp01(pickupSfxVolume));
    }

    void AwardCredits()
    {
        if (creditReward == 0) return;
        if (CreditManager.Instance == null) return;

        CreditManager.Instance.AddCredits(creditReward);

        if (showSubtitle)
        {
            string text = creditReward >= 0
                ? $"+{creditReward} credits"
                : $"{creditReward} credits";
            CreditManager.Instance.ShowSubtitle(text, subtitleDuration, subtitleColor);
        }
    }
}

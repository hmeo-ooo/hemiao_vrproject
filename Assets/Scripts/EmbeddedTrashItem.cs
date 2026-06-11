using UnityEngine;

/// <summary>
/// 由 <see cref="TrashHeap"/> 自动挂在每个生成的垃圾上。
///
/// 作用：垃圾在生成时是 Kinematic（嵌在堆里不会掉），但 <see cref="CharacterInteraction"/>
/// 在抓取时会缓存"抓前的物理状态"并在松手时还原；如果不干预，玩家松手后垃圾就又被
/// 还原成 Kinematic 卡在空中。
///
/// 本组件订阅 <see cref="CharacterInteraction.Grabbed"/>，当玩家抓到本物体时：
/// 1. 调用 <see cref="CharacterInteraction.PromoteHeldItemPermanently"/> 改写"抓前缓存"，
///    让松手后物品永久变为受重力影响的自由物理体；
/// 2. 通知 <see cref="TrashHeap"/> 播放拔出音效 / 粒子效果。
///
/// 组件本身不会主动 Destroy（避免误触发 NotifyItemDestroyed）。物品被销毁时随 GameObject 一同释放。
/// </summary>
[DisallowMultipleComponent]
public class EmbeddedTrashItem : MonoBehaviour
{
    TrashHeap _heap;
    CharacterInteraction _interaction;
    bool _promoted;

    internal void AttachToHeap(TrashHeap heap)
    {
        _heap = heap;
    }

    void OnEnable()
    {
        if (_interaction == null)
            _interaction = FindCharacterInteraction();

        if (_interaction != null)
            _interaction.Grabbed += OnGrabbed;
    }

    void OnDisable()
    {
        if (_interaction != null)
            _interaction.Grabbed -= OnGrabbed;
    }

    void OnDestroy()
    {
        // 注意：仅在 GameObject 真正被销毁时通知堆。本组件不会 Destroy(this)。
        if (_heap != null)
            _heap.NotifyItemDestroyed(gameObject);
    }

    void OnGrabbed(GameObject grabbed)
    {
        if (_promoted) return;
        if (grabbed != gameObject) return;

        if (_interaction != null)
            _interaction.PromoteHeldItemPermanently();

        _promoted = true;
        if (_heap != null)
        {
            _heap.PlayPullOutEffects(transform.position);
            _heap.NotifyItemPromoted(gameObject);
        }
    }

    static CharacterInteraction FindCharacterInteraction()
    {
#if UNITY_2022_2_OR_NEWER
        return Object.FindAnyObjectByType<CharacterInteraction>();
#else
        return Object.FindObjectOfType<CharacterInteraction>();
#endif
    }
}

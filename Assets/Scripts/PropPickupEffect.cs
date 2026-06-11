using UnityEngine;

/// <summary>
/// 道具类（<see cref="ItemInformation.ItemCategory.Prop"/>）拾取效果基类。
///
/// 工作流程：
/// 1. <see cref="OnEnable"/> 时自动订阅场景中 <see cref="CharacterInteraction.Grabbed"/> 事件；
/// 2. 当玩家抓到挂载本组件的 GameObject 时，强制释放玩家握持，调用 <see cref="OnTriggered"/>；
/// 3. <see cref="OnTriggered"/> 由具体子类实现（播放粒子、加分、生成新物品等）。
///
/// 使用：
/// - 物品 prefab 上挂 <see cref="ItemInformation"/> 并设 <c>category = Prop</c>；
/// - 再挂任意一个 PropPickupEffect 子类（例如 <see cref="CoinPickupEffect"/>）；
/// - 多个子类可以叠加：彼此独立触发。
///
/// 注意：基类不会主动销毁本物体——是否销毁、何时销毁全部交由子类的 <see cref="OnTriggered"/> 决定。
/// </summary>
public abstract class PropPickupEffect : MonoBehaviour
{
    [Tooltip("勾选后，触发一次后此组件会自动注销监听，不会再次触发。")]
    public bool triggerOnce = true;

    bool _triggered;
    CharacterInteraction _interaction;

    protected bool HasTriggered => _triggered;
    protected CharacterInteraction Interaction => _interaction;

    protected virtual void OnEnable()
    {
        TryBindInteraction();
    }

    protected virtual void OnDisable()
    {
        if (_interaction != null)
        {
            _interaction.Grabbed -= HandleGrabbed;
            _interaction = null;
        }
    }

    void TryBindInteraction()
    {
        if (_interaction != null) return;

#if UNITY_2023_1_OR_NEWER
        _interaction = Object.FindAnyObjectByType<CharacterInteraction>();
#else
        _interaction = Object.FindObjectOfType<CharacterInteraction>();
#endif
        if (_interaction != null)
            _interaction.Grabbed += HandleGrabbed;
    }

    void HandleGrabbed(GameObject grabbedObject)
    {
        if (grabbedObject != gameObject) return;
        if (triggerOnce && _triggered) return;

        _triggered = true;

        if (_interaction != null)
            _interaction.ForceReleaseIfHolding(gameObject);

        OnTriggered();
    }

    /// <summary>玩家拾取本道具时的具体效果，由子类实现。</summary>
    protected abstract void OnTriggered();
}

using UnityEngine;
using UnityEngine.Events;

public class TransitionInterfaceController : MonoBehaviour
{
    [Tooltip("运行时要显示的 ListPanel（开始可保持 inactive）")]
    public GameObject listPanel;

    [Tooltip("指向 IntroCompleteNotifier（检测 Intro 完成））")]
    public IntroCompleteNotifier introNotifier;

    [Tooltip("是否在 Start 时隐藏 ListPanel")]
    public bool hideListAtStart = true;

    [Tooltip("ListPanel 显示时是否自动关闭 IntroPanel")]
    public bool deactivateIntroOnShow = true;

    [Tooltip("要被隐藏的 IntroPanel 根对象（如果为空则不隐藏）")]
    public GameObject introPanel;

    void Start()
    {
        if (listPanel != null && hideListAtStart)
            listPanel.SetActive(false);

        if (introNotifier != null)
            introNotifier.onComplete.AddListener(ShowListPanel);
        else
            Debug.LogWarning("TransitionInterfaceController: introNotifier 未设置。");
    }

    void OnDestroy()
    {
        if (introNotifier != null)
            introNotifier.onComplete.RemoveListener(ShowListPanel);
    }

    public void ShowListPanel()
    {
        if (listPanel != null)
            listPanel.SetActive(true);

        if (deactivateIntroOnShow && introPanel != null)
            introPanel.SetActive(false);
    }
}
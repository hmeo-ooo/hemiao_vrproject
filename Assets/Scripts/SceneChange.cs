using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneChange : MonoBehaviour
{
    // 在 UI Button 的 OnClick 中绑定此方法：点击后加载下一个在 Build Settings 中配置的场景
    public void OnStartButtonPressed()
    {
        int nextIndex = SceneManager.GetActiveScene().buildIndex + 1;
        if (nextIndex < SceneManager.sceneCountInBuildSettings)
        {
            SceneManager.LoadScene(nextIndex);
        }
        else
        {
            Debug.LogWarning($"[StartGame] 无可用的下一个场景（当前索引 {SceneManager.GetActiveScene().buildIndex}）。请在 Build Settings 中添加场景。");
        }
    }
}

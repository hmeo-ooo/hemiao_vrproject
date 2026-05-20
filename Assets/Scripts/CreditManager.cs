using System;
using UnityEngine;

public class CreditManager : MonoBehaviour
{
    public static CreditManager Instance { get; private set; }

    [Tooltip("\u5F53\u524D\u79EF\u5206")]
    public int credits;

    /// <summary>\u79EF\u5206\u53D8\u5316\u65F6\u89E6\u53D1\uFF0C\u53C2\u6570\u4E3A\u5F53\u524D\u603B\u989D\u3002</summary>
    public event Action<int> OnCreditsChanged;

    string subtitle;
    float subtitleEndTime;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        NotifyCreditsChanged();
    }

    public void AddCredits(int amount)
    {
        if (amount == 0) return;
        credits += amount;
        NotifyCreditsChanged();
    }

    void NotifyCreditsChanged()
    {
        OnCreditsChanged?.Invoke(credits);
    }

    public void ShowSubtitle(string text, float duration = 2f)
    {
        subtitle = text;
        subtitleEndTime = Time.time + Mathf.Max(0.1f, duration);
    }

    void OnGUI()
    {
        if (!string.IsNullOrEmpty(subtitle) && Time.time < subtitleEndTime)
        {
            GUIStyle subStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.LowerCenter,
                fontSize = 22,
                normal = { textColor = Color.red }
            };
            float h = 40f;
            GUI.Label(new Rect(0, Screen.height - h - 10f, Screen.width, h), subtitle, subStyle);
        }
    }
}

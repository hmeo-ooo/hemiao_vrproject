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
    Color subtitleColor = Color.red;

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

    /// <summary>消耗信用点；余额不足时返回 false。</summary>
    public bool TrySpendCredits(int amount)
    {
        if (amount <= 0) return true;
        if (credits < amount) return false;
        credits -= amount;
        NotifyCreditsChanged();
        return true;
    }

    void NotifyCreditsChanged()
    {
        OnCreditsChanged?.Invoke(credits);
    }

    public void ShowSubtitle(string text, float duration = 2f)
    {
        ShowSubtitle(text, duration, Color.red);
    }

    public void ShowSubtitle(string text, float duration, Color color)
    {
        subtitle = text;
        subtitleColor = color;
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
                normal = { textColor = subtitleColor }
            };
            float h = 40f;
            GUI.Label(new Rect(0, Screen.height - h - 10f, Screen.width, h), subtitle, subStyle);
        }
    }
}

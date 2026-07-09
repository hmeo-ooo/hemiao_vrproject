using System;
using UnityEngine;

public class CreditManager : MonoBehaviour
{
    public static CreditManager Instance { get; private set; }

    [Tooltip("\u5F53\u524D\u79EF\u5206")]
    public int credits;

    /// <summary>积分变化时触发，参数为当前总额。</summary>
    public event Action<int> OnCreditsChanged;

    /// <summary>连续正确投掷连击数变化时触发。</summary>
    public event Action<int> OnThrowComboChanged;

    [Header("连击")]
    [Tooltip("每达到该连击数，正确投掷奖励增加 comboBonusPercentPerTier%。")]
    [SerializeField] int comboTierSize = 5;

    [Tooltip("每个连击档位增加的奖励百分比。")]
    [SerializeField] int comboBonusPercentPerTier = 10;

    int throwComboCount;

    /// <summary>当前连续正确投掷连击数。</summary>
    public int ThrowComboCount => throwComboCount;

    public struct CorrectThrowAward
    {
        public int baseCredits;
        public int awardedCredits;
        public int combo;
        public int bonusPercent;
    }

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

    public void AddCredits(int amount, bool playSfx = true)
    {
        if (amount == 0) return;
        credits += amount;
        if (playSfx && amount > 0 && SfxManager.Instance != null)
            SfxManager.Instance.PlayCoin();
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

    /// <summary>连续正确投掷：递增连击并按档位加成后发放信用点。</summary>
    public CorrectThrowAward AwardCorrectThrowCredits(int baseCredits, bool playSfx = false)
    {
        throwComboCount = Mathf.Max(0, throwComboCount + 1);
        int bonusPercent = GetThrowComboBonusPercent(throwComboCount);
        float multiplier = 1f + bonusPercent / 100f;
        int awarded = baseCredits == 0
            ? 0
            : Mathf.Max(0, Mathf.RoundToInt(baseCredits * multiplier));

        if (awarded != 0)
            AddCredits(awarded, playSfx);

        OnThrowComboChanged?.Invoke(throwComboCount);

        return new CorrectThrowAward
        {
            baseCredits = baseCredits,
            awardedCredits = awarded,
            combo = throwComboCount,
            bonusPercent = bonusPercent
        };
    }

    public void ResetThrowCombo()
    {
        if (throwComboCount == 0) return;
        throwComboCount = 0;
        OnThrowComboChanged?.Invoke(0);
    }

    public int GetThrowComboBonusPercent(int combo)
    {
        if (comboTierSize <= 0 || comboBonusPercentPerTier <= 0 || combo <= 0)
            return 0;
        return combo / comboTierSize * comboBonusPercentPerTier;
    }

    public static string FormatCorrectThrowSubtitle(CorrectThrowAward award)
    {
        int delta = award.awardedCredits;
        string text = delta >= 0 ? $"+{delta} credits" : $"{delta} credits";
        if (award.combo <= 1) return text;

        if (award.bonusPercent > 0)
            return $"{text}  Combo x{award.combo} (+{award.bonusPercent}%)";

        return $"{text}  Combo x{award.combo}";
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
            float h = GameDisplaySettings.ScaleDesignPixels(40f);
            float bottomPad = GameDisplaySettings.ScaleDesignPixels(10f);
            GUIStyle subStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.LowerCenter,
                fontSize = GameDisplaySettings.ScaleDesignPixelsInt(22),
                normal = { textColor = subtitleColor }
            };
            GUI.Label(new Rect(0, Screen.height - h - bottomPad, Screen.width, h), subtitle, subStyle);
        }
    }
}

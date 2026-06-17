using System;
using UnityEngine;

/// <summary>
/// 债务：与 CreditManager 配合，可用信用点归还。
/// </summary>
public class DebtManager : MonoBehaviour
{
    public static DebtManager Instance { get; private set; }

    [Tooltip("开局债务。")]
    public int initialDebt = 100_000;

    [Tooltip("当前剩余债务；运行时会从 initialDebt 初始化（若仍为 0 且 initialDebt > 0）。")]
    public int currentDebt;

    public event Action<int> OnDebtChanged;

    public int CurrentDebt => currentDebt;
    public bool IsDebtCleared => currentDebt <= 0;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (currentDebt <= 0 && initialDebt > 0)
            currentDebt = initialDebt;
    }

    void Start()
    {
        NotifyDebtChanged();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void SetDebt(int amount)
    {
        currentDebt = Mathf.Max(0, amount);
        NotifyDebtChanged();
    }

    /// <summary>
    /// 用当前全部可用信用点偿还债务（最多还清剩余债务）。
    /// </summary>
    public bool TryRepayFromCredits(out int amountPaid)
    {
        amountPaid = 0;
        if (currentDebt <= 0) return false;
        if (CreditManager.Instance == null) return false;

        int available = CreditManager.Instance.credits;
        if (available <= 0) return false;

        amountPaid = Mathf.Min(available, currentDebt);
        if (!CreditManager.Instance.TrySpendCredits(amountPaid))
            return false;

        currentDebt -= amountPaid;
        NotifyDebtChanged();
        return true;
    }

    void NotifyDebtChanged()
    {
        OnDebtChanged?.Invoke(currentDebt);
    }
}

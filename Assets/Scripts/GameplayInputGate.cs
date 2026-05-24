/// <summary>
/// 关卡准备 UI 显示时暂停玩家移动与交互。
/// </summary>
public static class GameplayInputGate
{
    public static bool IsBlocked { get; private set; }

    public static void SetBlocked(bool blocked)
    {
        IsBlocked = blocked;
    }
}

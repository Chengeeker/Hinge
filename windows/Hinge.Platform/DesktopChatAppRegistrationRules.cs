namespace Hinge.Platform;

public static class DesktopChatAppRegistrationRules
{
    public static bool IsWeixinEntry(string? displayName, string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return false;

        return string.Equals(displayName, "微信", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(displayName, "WeChat", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(displayName, "Weixin", StringComparison.OrdinalIgnoreCase);
    }
}

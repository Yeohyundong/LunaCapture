using Microsoft.Win32;

namespace LunaCapture.Services;

/// <summary>"Windows 시작 시 자동 실행" — 현재 사용자 Run 레지스트리 키에 등록/해제한다 (관리자 권한 불필요).</summary>
internal static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "LunaCapture";

    static string Command(string exe) => $"\"{exe}\"";

    /// <summary>지금 실행 중인 이 exe가 자동 실행으로 등록돼 있는지.</summary>
    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string v && string.Equals(v, Command(Environment.ProcessPath ?? ""), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>어떤 위치든 LunaCapture 자동 실행이 등록돼 있는지.</summary>
    public static bool IsRegistered
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    /// <param name="exePath">등록할 exe (기본: 지금 실행 중인 exe)</param>
    public static void Set(bool enabled, string? exePath = null)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command(exePath ?? Environment.ProcessPath ?? ""));
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

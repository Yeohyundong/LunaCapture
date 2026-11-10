using System.Diagnostics;
using System.Globalization;
using LunaCapture.Interop;

namespace LunaCapture.Capture;

internal static unsafe class CaptureFileName
{
    public static string? AppName(nint window)
    {
        if (window == 0) return null;
        char* className = stackalloc char[256];
        int length = User32.GetClassNameW(window, className, 256);
        string windowClass = new(className, 0, length);
        if (windowClass is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return null;

        if (windowClass == "ApplicationFrameWindow")
        {
            nint appWindow = User32.FindWindowExW(window, 0, "Windows.UI.Core.CoreWindow", null);
            if (appWindow == 0) return null;
            window = appWindow;
        }

        uint pid = 0;
        User32.GetWindowThreadProcessId(window, &pid);
        if (pid == 0) return null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            string name = process.ProcessName;
            if (name.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return "File Explorer";
            try
            {
                var version = process.MainModule?.FileVersionInfo;
                if (!string.IsNullOrWhiteSpace(version?.FileDescription)) return version.FileDescription;
                if (!string.IsNullOrWhiteSpace(version?.ProductName)) return version.ProductName;
            }
            catch (System.ComponentModel.Win32Exception) { }
            return name;
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    public static string Create(string? appName, DateTime time, string extension)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string prefix = appName == null ? "" : string.Concat(appName.Select(c => c == '.' || invalid.Contains(c) ? '_' : c));
        if (prefix.Length > 80)
            prefix = prefix[..(char.IsHighSurrogate(prefix[79]) ? 79 : 80)];
        prefix = prefix.Trim(' ', '.', '_');
        if (prefix.Length == 0) prefix = "Luna";
        return prefix + "_" + time.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "." + extension;
    }
}

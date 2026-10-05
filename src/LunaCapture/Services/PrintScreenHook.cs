using System.Diagnostics;
using System.Runtime.InteropServices;
using LunaCapture.Interop;

namespace LunaCapture.Services;

/// <summary>
/// 다른 프로그램이 PrtSc를 차지하고 있을 때, 사용자가 허락하면 LunaCapture가 먼저 받는 장치("우선 사용").
/// 저수준 키보드 훅으로 "PrtSc 단독 입력"만 확인하고 나머지 키는 보지도 저장하지도 않고 그대로 통과시킨다.
/// Alt/Ctrl/Shift/Win 과 함께 누른 PrtSc(윈도우 기본 기능)는 건드리지 않는다.
/// </summary>
internal static unsafe partial class PrintScreenHook
{
    const int WH_KEYBOARD_LL = 13;
    const uint VK_SNAPSHOT = 0x2C;
    const uint WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public nuint dwExtraInfo;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetWindowsHookExW(int id, delegate* unmanaged<int, nint, nint, nint> proc, nint module, uint thread);
    [LibraryImport("user32.dll")] private static partial int UnhookWindowsHookEx(nint hook);
    [LibraryImport("user32.dll")] private static partial nint CallNextHookEx(nint hook, int code, nint w, nint l);
    [LibraryImport("user32.dll")] private static partial short GetAsyncKeyState(int vk);

    static nint s_hook, s_target;
    static uint s_message;
    static bool s_down, s_fired;

    public static bool IsActive => s_hook != 0;

    /// <summary>PrtSc가 눌리면 target 창에 message를 보낸다.</summary>
    public static bool Install(nint target, uint message)
    {
        if (s_hook != 0) return true;
        s_target = target;
        s_message = message;
        s_hook = SetWindowsHookExW(WH_KEYBOARD_LL, &Proc, Kernel32.GetModuleHandleW(0), 0);
        if (s_hook == 0) Log.Info($"키보드 훅 설치 실패: {Marshal.GetLastPInvokeError()}");
        return s_hook != 0;
    }

    public static void Uninstall()
    {
        if (s_hook == 0) return;
        UnhookWindowsHookEx(s_hook);
        s_hook = 0;
    }

    // Ctrl·Alt·Win과 함께 누른 PrtSc는 윈도우 기본 기능이라 건드리지 않는다. Shift+PrtSc는 LunaCapture 캡처 기록.
    static bool ModifierHeld() =>
        GetAsyncKeyState(0x11) < 0 || GetAsyncKeyState(0x12) < 0 ||  // Ctrl, Alt
        GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0;     // Win

    static nint Which() => GetAsyncKeyState(0x10) < 0 ? 1 : 0; // 1 = Shift (기록 열기)

    /// <summary>모든 키 입력마다 불리므로 최대한 짧게: PrtSc가 아니면 바로 통과.</summary>
    [UnmanagedCallersOnly]
    static nint Proc(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && ((KBDLLHOOKSTRUCT*)lParam)->vkCode == VK_SNAPSHOT && !ModifierHeld())
        {
            uint msg = (uint)wParam;
            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                if (!s_down) { s_down = true; s_fired = true; User32.PostMessageW(s_target, s_message, Which(), 0); }
                return 1; // 삼킴 → 다른 프로그램은 못 받음
            }
            if (msg is WM_KEYUP or WM_SYSKEYUP)
            {
                // 어떤 환경에서는 PrtSc가 '뗄 때'만 들어온다.
                if (!s_fired) User32.PostMessageW(s_target, s_message, Which(), 0);
                s_down = false;
                s_fired = false;
                return 1;
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    /// <summary>PrtSc를 차지하고 있을 법한 프로그램 이름 (알려진 캡처 프로그램 중 실행 중인 것).</summary>
    public static string? GuessOwner()
    {
        (string process, string name)[] known =
        [
            ("picpick", "PicPick"), ("ShareX", "ShareX"), ("Greenshot", "Greenshot"), ("Lightshot", "Lightshot"),
            ("Snipaste", "Snipaste"), ("Snagit32", "Snagit"), ("SnagitCapture", "Snagit"), ("FSCapture", "FastStone Capture"),
            ("Gyazo", "Gyazo"), ("ALCapture", "알캡처"), ("HoneyCam", "꿀캠"), ("Bandicam", "반디캠"), ("oCam", "오캠"),
        ];
        foreach (var (process, name) in known)
        {
            var found = Process.GetProcessesByName(process);
            bool running = found.Length > 0;
            foreach (var p in found) p.Dispose();
            if (running) return name;
        }
        return null;
    }
}

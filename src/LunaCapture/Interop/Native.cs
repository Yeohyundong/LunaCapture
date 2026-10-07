using System.Runtime.InteropServices;

namespace LunaCapture.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct POINT { public int X, Y; }

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left, Top, Right, Bottom;
    public RECT(int l, int t, int r, int b) { Left = l; Top = t; Right = r; Bottom = b; }
    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WNDCLASSEXW
{
    public uint cbSize;
    public uint style;
    public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public nint hInstance;
    public nint hIcon;
    public nint hCursor;
    public nint hbrBackground;
    public char* lpszMenuName;
    public char* lpszClassName;
    public nint hIconSm;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public nint hwnd;
    public uint message;
    public nint wParam;
    public nint lParam;
    public uint time;
    public POINT pt;
    public uint lPrivate;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PAINTSTRUCT
{
    public nint hdc;
    public int fErase;
    public RECT rcPaint;
    public int fRestore;
    public int fIncUpdate;
    public fixed byte rgbReserved[32];
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    public uint biSize;
    public int biWidth;
    public int biHeight;
    public ushort biPlanes;
    public ushort biBitCount;
    public uint biCompression;
    public uint biSizeImage;
    public int biXPelsPerMeter;
    public int biYPelsPerMeter;
    public uint biClrUsed;
    public uint biClrImportant;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct NOTIFYICONDATAW
{
    public uint cbSize;
    public nint hWnd;
    public uint uID;
    public uint uFlags;
    public uint uCallbackMessage;
    public nint hIcon;
    public fixed char szTip[128];
    public uint dwState;
    public uint dwStateMask;
    public fixed char szInfo[256];
    public uint uVersion;
    public fixed char szInfoTitle[64];
    public uint dwInfoFlags;
    public Guid guidItem;
    public nint hBalloonIcon;
}

internal static class Win
{
    // Window messages
    public const uint WM_NULL = 0x0000, WM_CREATE = 0x0001, WM_DESTROY = 0x0002, WM_PAINT = 0x000F,
        WM_CLOSE = 0x0010, WM_ERASEBKGND = 0x0014, WM_SETCURSOR = 0x0020, WM_CONTEXTMENU = 0x007B,
        WM_NCDESTROY = 0x0082, WM_KEYDOWN = 0x0100, WM_COMMAND = 0x0111, WM_MOUSEMOVE = 0x0200,
        WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205,
        WM_HOTKEY = 0x0312, WM_APP = 0x8000;

    public const uint WM_SIZE = 0x0005, WM_SETFOCUS = 0x0007, WM_GETMINMAXINFO = 0x0024, WM_CHAR = 0x0102,
        WM_TIMER = 0x0113, WM_MOUSELEAVE = 0x02A3, WM_DPICHANGED = 0x02E0,
        WM_IME_STARTCOMPOSITION = 0x010D, WM_IME_ENDCOMPOSITION = 0x010E, WM_IME_COMPOSITION = 0x010F,
        WM_IME_SETCONTEXT = 0x0281;
    public const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    public const int HTCLIENT = 1;
    public const int IDC_IBEAM = 32513, IDC_HAND = 32649;
    public const int VK_BACK = 0x08, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_DELETE = 0x2E;
    public const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint MF_CHECKED = 0x0008, MF_UNCHECKED = 0x0000;
    public const int HALFTONE = 4;
    public const uint WM_NCHITTEST = 0x0084;
    public const int HTCAPTION = 2;
    public const uint WS_EX_LAYERED = 0x00080000, WS_EX_NOACTIVATE = 0x08000000;
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    public const uint LWA_ALPHA = 0x2;
    public const int RGN_DIFF = 4;
    public const uint CURSOR_SHOWING = 1;
    public const uint DI_NORMAL = 3, DI_DEFAULTSIZE = 8;
    public const int SM_CXCURSOR = 13;
    public const uint CF_HDROP = 15;
    public const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    public static bool IsKeyDown(int vk) => User32.GetKeyState(vk) < 0;

    // Window styles
    public const uint WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000;
    public const uint WS_EX_TOPMOST = 0x00000008, WS_EX_TRANSPARENT = 0x00000020, WS_EX_TOOLWINDOW = 0x00000080;
    public const int GWL_EXSTYLE = -20;
    public const uint GW_HWNDNEXT = 2;

    public const int SW_SHOW = 5;
    public const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
    public const int IDC_ARROW = 32512, IDC_CROSS = 32515, IDI_APPLICATION = 32512;

    public const int VK_RETURN = 0x0D, VK_ESCAPE = 0x1B;
    public const uint MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_NOREPEAT = 0x4000;

    public const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;

    // Tray
    public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    public const uint NIF_MESSAGE = 0x01, NIF_ICON = 0x02, NIF_TIP = 0x04, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    public const uint NIIF_INFO = 0x01, NIIF_WARNING = 0x02;
    public const uint NOTIFYICON_VERSION_4 = 4;
    public const uint NIN_SELECT = 0x0400, NIN_KEYSELECT = 0x0401, NIN_BALLOONUSERCLICK = 0x0405;

    // Menu
    public const uint MF_STRING = 0x0000, MF_SEPARATOR = 0x0800;
    public const uint TPM_RIGHTBUTTON = 0x0002, TPM_NONOTIFY = 0x0080, TPM_RETURNCMD = 0x0100;

    public const uint CF_DIB = 8;
    public const uint GMEM_MOVEABLE = 0x0002;

    public const int TRANSPARENT = 1;
    public const uint DT_CENTER = 0x01, DT_VCENTER = 0x04, DT_SINGLELINE = 0x20, DT_NOCLIP = 0x100, DT_CALCRECT = 0x400;

    public static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    public static int LoWord(nint v) => (short)((long)v & 0xFFFF);
    public static int HiWord(nint v) => (short)(((long)v >> 16) & 0xFFFF);
    public static uint Rgb(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));
}

internal static unsafe partial class User32
{
    const string Dll = "user32.dll";

    [LibraryImport(Dll)] public static partial ushort RegisterClassExW(WNDCLASSEXW* wc);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [LibraryImport(Dll)] public static partial nint DefWindowProcW(nint hwnd, uint msg, nint w, nint l);
    [LibraryImport(Dll)] public static partial int GetMessageW(MSG* msg, nint hwnd, uint min, uint max);
    [LibraryImport(Dll)] public static partial int TranslateMessage(MSG* msg);
    [LibraryImport(Dll)] public static partial nint DispatchMessageW(MSG* msg);
    [LibraryImport(Dll)] public static partial void PostQuitMessage(int code);
    [LibraryImport(Dll)] public static partial int PostMessageW(nint hwnd, uint msg, nint w, nint l);
    [LibraryImport(Dll)] public static partial int DestroyWindow(nint hwnd);
    [LibraryImport(Dll)] public static partial int ShowWindow(nint hwnd, int cmd);
    [LibraryImport(Dll)] public static partial int SetForegroundWindow(nint hwnd);
    [LibraryImport(Dll)] public static partial nint SetFocus(nint hwnd);
    [LibraryImport(Dll)] public static partial int RegisterHotKey(nint hwnd, int id, uint mods, uint vk);
    [LibraryImport(Dll)] public static partial int UnregisterHotKey(nint hwnd, int id);
    [LibraryImport(Dll)] public static partial nint LoadCursorW(nint inst, nint name);
    [LibraryImport(Dll)] public static partial nint LoadIconW(nint inst, nint name);
    [LibraryImport(Dll)] public static partial nint SetCursor(nint cursor);
    [LibraryImport(Dll)] public static partial int GetSystemMetrics(int index);
    [LibraryImport(Dll)] public static partial nint BeginPaint(nint hwnd, PAINTSTRUCT* ps);
    [LibraryImport(Dll)] public static partial int EndPaint(nint hwnd, PAINTSTRUCT* ps);
    [LibraryImport(Dll)] public static partial int InvalidateRect(nint hwnd, RECT* rect, int erase);
    [LibraryImport(Dll)] public static partial int GetCursorPos(POINT* pt);
    [LibraryImport(Dll)] public static partial nint SetCapture(nint hwnd);
    [LibraryImport(Dll)] public static partial int ReleaseCapture();
    [LibraryImport(Dll)] public static partial nint CreatePopupMenu();
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int AppendMenuW(nint menu, uint flags, nuint id, string? text);
    [LibraryImport(Dll)] public static partial int TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);
    [LibraryImport(Dll)] public static partial int DestroyMenu(nint menu);
    [LibraryImport(Dll)] public static partial nint GetDC(nint hwnd);
    [LibraryImport(Dll)] public static partial int ReleaseDC(nint hwnd, nint hdc);
    [LibraryImport(Dll)] public static partial int FillRect(nint hdc, RECT* rect, nint brush);
    [LibraryImport(Dll)] public static partial int FrameRect(nint hdc, RECT* rect, nint brush);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int DrawTextW(nint hdc, string text, int len, RECT* rect, uint format);
    [LibraryImport(Dll)] public static partial int OpenClipboard(nint hwnd);
    [LibraryImport(Dll)] public static partial int EmptyClipboard();
    [LibraryImport(Dll)] public static partial nint SetClipboardData(uint format, nint mem);
    [LibraryImport(Dll)] public static partial int CloseClipboard();
    [LibraryImport(Dll)] public static partial int IsClipboardFormatAvailable(uint format);
    [LibraryImport(Dll)] public static partial nint GetClipboardData(uint format);
    [LibraryImport(Dll)] public static partial int SetProcessDpiAwarenessContext(nint ctx);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessageW(string name);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MessageBoxW(nint hwnd, string text, string caption, uint type);
    [LibraryImport(Dll)] public static partial uint GetDpiForWindow(nint hwnd);
    [LibraryImport(Dll)] public static partial nint GetTopWindow(nint hwnd);
    [LibraryImport(Dll)] public static partial nint GetWindow(nint hwnd, uint cmd);
    [LibraryImport(Dll)] public static partial int IsWindowVisible(nint hwnd);
    [LibraryImport(Dll)] public static partial int IsIconic(nint hwnd);
    [LibraryImport(Dll)] public static partial int GetWindowRect(nint hwnd, RECT* rect);
    [LibraryImport(Dll)] public static partial nint GetWindowLongPtrW(nint hwnd, int index);
    [LibraryImport(Dll)] public static partial int GetClientRect(nint hwnd, RECT* rect);
    [LibraryImport(Dll)] public static partial int ScreenToClient(nint hwnd, POINT* pt);
    [LibraryImport(Dll)] public static partial uint GetDoubleClickTime();
    [LibraryImport(Dll)] public static partial int ClientToScreen(nint hwnd, POINT* pt);
    [LibraryImport(Dll)] public static partial nint GetForegroundWindow();
    [LibraryImport(Dll)] public static partial uint GetWindowThreadProcessId(nint hwnd, uint* processId);
    [LibraryImport(Dll)] public static partial int AttachThreadInput(uint attach, uint attachTo, int doAttach);
    [LibraryImport(Dll)] public static partial int BringWindowToTop(nint hwnd);

    /// <summary>
    /// 창을 맨 앞으로 + 키보드 포커스까지 확실히 가져온다.
    /// 윈도우는 "직전에 입력을 정식으로 받은 프로그램"만 앞으로 나올 수 있게 막는데,
    /// PrtSc를 키보드 훅으로 받은 경우엔 이 권한이 없어서 화면은 떠도 키 입력(Tab, Esc)이 원래 창으로 간다.
    /// 지금 앞에 있는 창의 입력 스레드에 잠시 붙어서 포커스를 넘겨받는다.
    /// </summary>
    public static void ForceForeground(nint hwnd)
    {
        nint fg = GetForegroundWindow();
        uint fgThread = fg != 0 ? GetWindowThreadProcessId(fg, null) : 0;
        uint myThread = Kernel32.GetCurrentThreadId();
        bool attached = fgThread != 0 && fgThread != myThread && AttachThreadInput(myThread, fgThread, 1) != 0;
        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            SetFocus(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(myThread, fgThread, 0);
        }
    }
    [LibraryImport(Dll)] public static partial int SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
    [LibraryImport(Dll)] public static partial nuint SetTimer(nint hwnd, nuint id, uint ms, nint proc);
    [LibraryImport(Dll)] public static partial int KillTimer(nint hwnd, nuint id);
    [LibraryImport(Dll)] public static partial short GetKeyState(int vk);
    [LibraryImport(Dll)] public static partial nint MonitorFromPoint(POINT pt, uint flags);
    [LibraryImport(Dll)] public static partial int GetMonitorInfoW(nint monitor, MONITORINFO* info);
    [LibraryImport(Dll)] public static partial int AdjustWindowRectExForDpi(RECT* rect, uint style, int menu, uint exStyle, uint dpi);
    [LibraryImport(Dll)] public static partial uint CheckMenuItem(nint menu, uint id, uint check);
    [LibraryImport(Dll)] public static partial int SetWindowDisplayAffinity(nint hwnd, uint affinity);
    [LibraryImport(Dll)] public static partial int TrackMouseEvent(TRACKMOUSEEVENT* tme);
    [LibraryImport(Dll)] public static partial int SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    [LibraryImport(Dll)] public static partial int SetWindowRgn(nint hwnd, nint rgn, int redraw);
    [LibraryImport(Dll)] public static partial int GetCursorInfo(CURSORINFO* info);
    [LibraryImport(Dll)] public static partial int GetIconInfo(nint icon, ICONINFO* info);
    [LibraryImport(Dll)] public static partial int DrawIconEx(nint hdc, int x, int y, nint icon, int cx, int cy, uint step, nint brush, uint flags);
}

[StructLayout(LayoutKind.Sequential)]
internal struct TRACKMOUSEEVENT
{
    public uint cbSize;
    public uint dwFlags;
    public nint hwndTrack;
    public uint dwHoverTime;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CURSORINFO
{
    public uint cbSize;
    public uint flags;
    public nint hCursor;
    public POINT ptScreenPos;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ICONINFO
{
    public int fIcon;
    public int xHotspot;
    public int yHotspot;
    public nint hbmMask;
    public nint hbmColor;
}

internal static partial class Winmm
{
    [LibraryImport("winmm.dll")] public static partial uint timeBeginPeriod(uint ms);
    [LibraryImport("winmm.dll")] public static partial uint timeEndPeriod(uint ms);
}

[StructLayout(LayoutKind.Sequential)]
internal struct MONITORINFO
{
    public uint cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct OPENFILENAMEW
{
    public uint lStructSize;
    public nint hwndOwner;
    public nint hInstance;
    public char* lpstrFilter;
    public char* lpstrCustomFilter;
    public uint nMaxCustFilter;
    public uint nFilterIndex;
    public char* lpstrFile;
    public uint nMaxFile;
    public char* lpstrFileTitle;
    public uint nMaxFileTitle;
    public char* lpstrInitialDir;
    public char* lpstrTitle;
    public uint Flags;
    public ushort nFileOffset;
    public ushort nFileExtension;
    public char* lpstrDefExt;
    public nint lCustData;
    public nint lpfnHook;
    public char* lpTemplateName;
    public nint pvReserved;
    public uint dwReserved;
    public uint FlagsEx;
}

internal static unsafe partial class Comdlg32
{
    public const uint OFN_OVERWRITEPROMPT = 0x2, OFN_NOCHANGEDIR = 0x8, OFN_PATHMUSTEXIST = 0x800;

    [LibraryImport("comdlg32.dll")] public static partial int GetSaveFileNameW(OPENFILENAMEW* ofn);
}

internal static unsafe partial class Imm32
{
    public const uint GCS_COMPSTR = 0x0008, GCS_RESULTSTR = 0x0800;
    public const uint ISC_SHOWUICOMPOSITIONWINDOW = 0x80000000;

    [LibraryImport("imm32.dll")] public static partial nint ImmGetContext(nint hwnd);
    [LibraryImport("imm32.dll")] public static partial int ImmReleaseContext(nint hwnd, nint himc);
    [LibraryImport("imm32.dll")] public static partial nint ImmAssociateContext(nint hwnd, nint himc);
    [LibraryImport("imm32.dll")] public static partial int ImmGetCompositionStringW(nint himc, uint index, void* buf, uint len);
    [LibraryImport("imm32.dll")] public static partial int ImmNotifyIME(nint himc, uint action, uint index, uint value);

    public const uint NI_COMPOSITIONSTR = 0x15, CPS_CANCEL = 0x4;

    public static string GetString(nint himc, uint index)
    {
        int bytes = ImmGetCompositionStringW(himc, index, null, 0);
        if (bytes <= 0) return "";
        var buf = new char[bytes / 2];
        fixed (char* p = buf) ImmGetCompositionStringW(himc, index, p, (uint)bytes);
        return new string(buf);
    }
}

internal static unsafe partial class Dwmapi
{
    public const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;

    public const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmGetWindowAttribute(nint hwnd, uint attr, void* value, uint size);
    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, uint attr, void* value, uint size);
}

internal static unsafe partial class Gdi32
{
    const string Dll = "gdi32.dll";

    [LibraryImport(Dll)] public static partial nint CreateCompatibleDC(nint hdc);
    [LibraryImport(Dll)] public static partial nint CreateDIBSection(nint hdc, BITMAPINFOHEADER* bmi, uint usage, void** bits, nint section, uint offset);
    [LibraryImport(Dll)] public static partial nint SelectObject(nint hdc, nint obj);
    [LibraryImport(Dll)] public static partial int DeleteObject(nint obj);
    [LibraryImport(Dll)] public static partial int DeleteDC(nint hdc);
    [LibraryImport(Dll)] public static partial int BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);
    [LibraryImport(Dll)] public static partial nint CreateSolidBrush(uint color);
    [LibraryImport(Dll)] public static partial int SetBkMode(nint hdc, int mode);
    [LibraryImport(Dll)] public static partial uint SetTextColor(nint hdc, uint color);
    [LibraryImport(Dll)] public static partial int GdiFlush();
    [LibraryImport(Dll)] public static partial int StretchBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, int sw, int sh, uint rop);
    [LibraryImport(Dll)] public static partial int SetStretchBltMode(nint hdc, int mode);
    [LibraryImport(Dll)] public static partial int SetBrushOrgEx(nint hdc, int x, int y, POINT* old);
    [LibraryImport(Dll)] public static partial nint CreateRectRgn(int l, int t, int r, int b);
    [LibraryImport(Dll)] public static partial int CombineRgn(nint dst, nint a, nint b, int mode);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFontW(int h, int w, int esc, int orient, int weight, uint italic, uint underline,
        uint strike, uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);
}

internal static unsafe partial class Kernel32
{
    const string Dll = "kernel32.dll";

    [LibraryImport(Dll)] public static partial nint GetModuleHandleW(nint name);
    [LibraryImport(Dll)] public static partial nint GlobalAlloc(uint flags, nuint bytes);
    [LibraryImport(Dll)] public static partial void* GlobalLock(nint mem);
    [LibraryImport(Dll)] public static partial int GlobalUnlock(nint mem);
    [LibraryImport(Dll)] public static partial nint GlobalFree(nint mem);
    [LibraryImport(Dll)] public static partial nuint GlobalSize(nint mem);
    [LibraryImport(Dll)] public static partial nint GetCurrentProcess();
    [LibraryImport(Dll)] public static partial uint GetCurrentThreadId();
    [LibraryImport(Dll)] public static partial int SetProcessWorkingSetSize(nint proc, nint min, nint max);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial nint CreateMutexW(nint attrs, int initialOwner, string name);
}

internal static unsafe partial class Shell32
{
    const string Dll = "shell32.dll";

    [LibraryImport(Dll)] public static partial int Shell_NotifyIconW(uint msg, NOTIFYICONDATAW* data);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint ShellExecuteW(nint hwnd, string? op, string file, string? args, string? dir, int show);
}

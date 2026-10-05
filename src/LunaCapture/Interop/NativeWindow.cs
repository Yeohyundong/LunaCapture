using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LunaCapture.Interop;

/// <summary>
/// 모든 Win32 창의 공통 바탕. 하나의 창 클래스를 등록하고 메시지를 각 인스턴스로 라우팅한다.
/// </summary>
internal abstract unsafe class NativeWindow
{
    const string ClassName = "LunaCaptureWnd";

    static readonly Dictionary<nint, NativeWindow> s_windows = new();
    static NativeWindow? s_creating;
    static bool s_registered;

    /// <summary>모든 창의 제목 표시줄·작업 표시줄 아이콘 (창을 만들기 전에 설정).</summary>
    public static nint DefaultIcon, DefaultSmallIcon;

    public nint Handle { get; private set; }

    protected void CreateHandle(uint exStyle, uint style, int x, int y, int w, int h, string title = "")
    {
        EnsureClass();
        s_creating = this;
        try
        {
            nint hwnd = User32.CreateWindowExW(exStyle, ClassName, title, style, x, y, w, h, 0, 0,
                Kernel32.GetModuleHandleW(0), 0);
            if (hwnd == 0) throw new InvalidOperationException("CreateWindowEx failed");
            Handle = hwnd;
        }
        finally { s_creating = null; }
    }

    public void Destroy()
    {
        if (Handle != 0) User32.DestroyWindow(Handle);
    }

    protected virtual nint WndProc(uint msg, nint wParam, nint lParam)
        => User32.DefWindowProcW(Handle, msg, wParam, lParam);

    protected virtual void OnDestroyed() { }

    static void EnsureClass()
    {
        if (s_registered) return;
        fixed (char* name = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &StaticWndProc,
                hInstance = Kernel32.GetModuleHandleW(0),
                hCursor = User32.LoadCursorW(0, Win.IDC_ARROW),
                hIcon = DefaultIcon,
                hIconSm = DefaultSmallIcon,
                lpszClassName = name,
            };
            if (User32.RegisterClassExW(&wc) == 0) throw new InvalidOperationException("RegisterClassEx failed");
        }
        s_registered = true;
    }

    [UnmanagedCallersOnly]
    static nint StaticWndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (!s_windows.TryGetValue(hwnd, out var win))
        {
            if (s_creating == null) return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
            win = s_creating;
            win.Handle = hwnd;
            s_windows[hwnd] = win;
        }

        try
        {
            nint result = win.WndProc(msg, wParam, lParam);
            if (msg == Win.WM_NCDESTROY)
            {
                s_windows.Remove(hwnd);
                win.Handle = 0;
                win.OnDestroyed();
            }
            return result;
        }
        catch (Exception ex)
        {
            // 네이티브 콜백 밖으로 예외가 새면 프로세스가 죽으므로 여기서 막는다.
            Log.Error(ex);
            return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }
}

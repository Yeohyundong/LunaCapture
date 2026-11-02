using LunaCapture.Graphics;
using LunaCapture.Interop;

namespace LunaCapture.Editor;

/// <summary>Freeze the virtual desktop and sample the exact pixel selected by the user.</summary>
internal sealed unsafe class ScreenColorPicker : NativeWindow
{
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmFlush();
    readonly GdiSurface _screen;
    readonly Action<uint?> _done;
    bool _completed;
    public ScreenColorPicker(Action<uint?> done)
    {
        _done = done;
        int x = User32.GetSystemMetrics(Win.SM_XVIRTUALSCREEN), y = User32.GetSystemMetrics(Win.SM_YVIRTUALSCREEN);
        int w = User32.GetSystemMetrics(Win.SM_CXVIRTUALSCREEN), h = User32.GetSystemMetrics(Win.SM_CYVIRTUALSCREEN);
        DwmFlush(); // Wait until the hidden palette windows disappear from the desktop.
        _screen = GdiSurface.CaptureScreen(x, y, w, h);
        CreateHandle(Win.WS_EX_TOPMOST | Win.WS_EX_TOOLWINDOW, Win.WS_POPUP, x, y, w, h, "색상 스포이드 · 클릭해서 선택 · Esc 취소");
        User32.ShowWindow(Handle, Win.SW_SHOW);
        User32.ForceForeground(Handle);
    }
    void Finish(uint? value)
    {
        if (_completed) return;
        _completed = true;
        Destroy(); _done(value);
    }
    protected override nint WndProc(uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case Win.WM_ERASEBKGND: return 1;
            case Win.WM_PAINT:
                PAINTSTRUCT ps;
                nint dc = User32.BeginPaint(Handle, &ps);
                Gdi32.BitBlt(dc, 0, 0, _screen.Width, _screen.Height, _screen.Dc, 0, 0, Win.SRCCOPY);
                User32.EndPaint(Handle, &ps); return 0;
            case Win.WM_SETCURSOR:
                User32.SetCursor(User32.LoadCursorW(0, Win.IDC_CROSS)); return 1;
            case Win.WM_LBUTTONDOWN:
                int x = Math.Clamp(Win.LoWord(lParam) & 65535, 0, _screen.Width - 1), y = Math.Clamp(Win.HiWord(lParam) & 65535, 0, _screen.Height - 1);
                Finish(((uint*)(_screen.Bits + y * _screen.Stride))[x] | 0xFF000000); return 0;
            case Win.WM_RBUTTONDOWN: Finish(null); return 0;
            case Win.WM_KEYDOWN:
                if (wParam == Win.VK_ESCAPE) Finish(null);
                return 0;
        }
        return base.WndProc(msg, wParam, lParam);
    }
    protected override void OnDestroyed() { _screen.Dispose(); base.OnDestroyed(); }
}

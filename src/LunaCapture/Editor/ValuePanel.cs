using System.Runtime.InteropServices;
using LunaCapture.Interop;
using LunaCapture.Recording;

namespace LunaCapture.Editor;

internal abstract unsafe class ValuePanel : PanelWindow
{
    readonly nint _owner;
    protected nint Owner => _owner;
    readonly Action _closed;
    protected readonly List<nint> Fields = [];

    [DllImport("user32.dll")] static extern int EnableWindow(nint hwnd, int enable);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(nint hwnd, char* text, int count);
    [DllImport("user32.dll")] static extern int GetWindowTextLengthW(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTextW(nint hwnd, string text);
    [DllImport("user32.dll")] static extern nint SetFocus(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    protected ValuePanel(nint owner, int width, int height, Action closed)
    {
        _owner = owner; _closed = closed;
        RECT bounds;
        User32.GetWindowRect(owner, &bounds);
        CreatePanel(bounds.Left + 30, bounds.Top + 60, false);
        SetWindowLongPtrW(Handle, -8 /* GWLP_HWNDPARENT */, owner);
        SetWindowLongPtrW(Handle, -20 /* GWL_EXSTYLE */, 8 /* WS_EX_TOPMOST */);
        var mi = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        User32.GetMonitorInfoW(User32.MonitorFromPoint(new POINT { X = bounds.Left + bounds.Width / 2, Y = bounds.Top + bounds.Height / 2 }, Win.MONITOR_DEFAULTTONEAREST), &mi);
        SetBounds(Math.Clamp(bounds.Left + 30, mi.rcWork.Left, Math.Max(mi.rcWork.Left, mi.rcWork.Right - Px(width))),
            Math.Clamp(bounds.Top + 60, mi.rcWork.Top, Math.Max(mi.rcWork.Top, mi.rcWork.Bottom - Px(height))), Px(width), Px(height));
        EnableWindow(owner, 0);
    }

    protected void Show()
    {
        User32.ShowWindow(Handle, Win.SW_SHOW);
        User32.ForceForeground(Handle);
        if (Fields.Count > 0) SetFocus(Fields[0]);
    }

    protected nint Edit(string text, int x, int y, int width, int id)
    {
        nint field = User32.CreateWindowExW(0x200, "EDIT", text, 0x50010080, Px(x), Px(y), Px(width), Px(28), Handle, id, Kernel32.GetModuleHandleW(0), 0);
        Fields.Add(field);
        return field;
    }

    protected string Read(nint field)
    {
        int capacity = Math.Clamp(GetWindowTextLengthW(field) + 1, 1, 10001);
        char[] text = new char[capacity];
        fixed (char* buffer = text)
        {
            int length = GetWindowTextW(field, buffer, capacity);
            return new string(buffer, 0, length);
        }
    }
    protected void Write(nint field, string value) => SetWindowTextW(field, value);
    protected void Label(Gfx g, string text, float x, float y, float size = 13, uint color = TextMain)
    {
        using var font = new GdiFont(UiFonts.Family, size * S, bold: false);
        g.Text(text, font, color, x * S, y * S);
    }
    protected void Button(Gfx g, string id, string text, int x, int y, int width, Action click)
    {
        var rect = new RECT(Px(x), Px(y), Px(x + width), Px(y + 30));
        g.FillRoundRect(IsHover(id) ? ItemHover : ItemBg, rect.Left, rect.Top, rect.Width, rect.Height, Px(5));
        Label(g, text, x + 10, y + 6);
        AddItem(id, rect, click);
    }
    protected override nint WndProc(uint msg, nint wParam, nint lParam)
    {
        if (msg == 0x10 || (msg == Win.WM_KEYDOWN && wParam == Win.VK_ESCAPE)) { Destroy(); return 0; }
        return base.WndProc(msg, wParam, lParam);
    }
    protected override void OnDestroyed()
    {
        EnableWindow(_owner, 1);
        User32.ForceForeground(_owner);
        _closed();
        base.OnDestroyed();
    }
}

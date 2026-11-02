using LunaCapture.Interop;
using LunaCapture.Services;

namespace LunaCapture.Editor;

internal sealed class DetailedColorWindow : ValuePanel
{
    readonly Action<uint> _apply;
    uint _color;
    int _tab, _lastField;
    bool _syncing, _drag;
    int _slider;
    double _h, _sat, _value;
    byte _alpha;
    readonly nint[] _inputs = new nint[5];
    string _error = "";
    public DetailedColorWindow(nint owner, uint color, Action<uint> apply, Action closed) : base(owner, 430, 610, closed)
    {
        _apply = apply; SetColor(color);
        _syncing = true;
        _inputs[0] = Edit("", 20, 402, 130, 10);
        for (int i = 1; i < 5; i++) _inputs[i] = Edit("", 160 + (i - 1) * 63, 402, 56, 10 + i);
        _syncing = false;
        _tab = 1; SyncInputs(); Show();
    }

    void SetColor(uint color)
    {
        _color = color; _alpha = (byte)(color >> 24);
        double r = ((color >> 16) & 255) / 255.0, g = ((color >> 8) & 255) / 255.0, b = (color & 255) / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        _value = max; _sat = max == 0 ? 0 : d / max;
        if (d > 0) _h = ((max == r ? (g - b) / d : max == g ? (b - r) / d + 2 : (r - g) / d + 4) * 60 + 360) % 360;
    }

    internal static uint Hsv(double h, double s, double v, byte alpha = 255)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = h switch { < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x), < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x) };
        return (uint)alpha << 24 | (uint)Math.Round((r + m) * 255) << 16 | (uint)Math.Round((g + m) * 255) << 8 | (uint)Math.Round((b + m) * 255);
    }

    void SyncInputs()
    {
        _syncing = true;
        Write(_inputs[0], $"#{_color & 0xFFFFFF:X6}{_color >> 24:X2}");
        for (int i = 1; i <= 3; i++) Write(_inputs[i], ((_color >> (24 - i * 8)) & 255).ToString());
        Write(_inputs[4], Math.Round((_color >> 24) * 100.0 / 255, 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        _syncing = false; _lastField = 0;
    }

    void ShowFields(bool show) { foreach (var field in _inputs) User32.ShowWindow(field, show ? Win.SW_SHOW : 0); }
    void Tab(int tab) { _tab = tab; _error = ""; ShowFields(tab == 1); if (tab == 1) SyncInputs(); Invalidate(); }

    bool ReadInputs()
    {
        if (_lastField == 0) return true;
        if (_lastField == 10)
        {
            if (TryParseHex(Read(_inputs[0]), out uint value))
            {
                SetColor(value);
                SyncInputs(); return true;
            }
        }
        else if (byte.TryParse(Read(_inputs[1]), out byte r) && byte.TryParse(Read(_inputs[2]), out byte g) && byte.TryParse(Read(_inputs[3]), out byte b)
            && double.TryParse(Read(_inputs[4]), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double a) && double.IsFinite(a) && a >= 0 && a <= 100)
        {
            SetColor((uint)Math.Round(a * 255 / 100) << 24 | (uint)r << 16 | (uint)g << 8 | b); SyncInputs(); return true;
        }
        _error = "HEX: #RRGGBB 또는 #RRGGBBAA · RGB: 0~255 · A: 0~100";
        Invalidate(); return false;
    }

    internal static bool TryParseHex(string text, out uint color)
    {
        string hex = text.Trim().TrimStart('#');
        color = 0;
        if ((hex.Length != 6 && hex.Length != 8) || !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint value)) return false;
        color = hex.Length == 6 ? 0xFF000000 | value : (value >> 8) | (value << 24);
        return true;
    }

    void Choose(uint color) { _apply(color); Destroy(); }
    void SaveCustom()
    {
        if (!ReadInputs()) return;
        var colors = AppSettings.Current.CustomColors;
        colors.Remove(_color); colors.Insert(0, _color);
        if (colors.Count > 24) colors.RemoveRange(24, colors.Count - 24);
        AppSettings.Save(); Choose(_color);
    }

    void Swatch(Gfx g, string id, uint color, int x, int y, int size = 25)
    {
        float radius = size / 2f;
        for (int yy = 0; yy < size; yy += 2) for (int xx = 0; xx < size; xx += 2)
            if (MathF.Pow(xx + 1 - radius, 2) + MathF.Pow(yy + 1 - radius, 2) < (radius - 1) * (radius - 1))
                g.FillRect(((xx / 6 + yy / 6) & 1) == 0 ? 0xFFF0F0F0 : 0xFFB8B8B8, Px(x + xx), Px(y + yy), Px(2), Px(2));
        g.FillEllipse(color, Px(x), Px(y), Px(size), Px(size));
        g.Ellipse(color == _color ? 0xFFFFFFFF : 0xFF686872, color == _color ? Px(2) : 1, Px(x), Px(y), Px(size), Px(size));
        AddItem(id, new RECT(Px(x), Px(y), Px(x + size), Px(y + size)), () => Choose(color));
    }
    void Checker(Gfx g, int x, int y, int w, int h)
    {
        for (int yy = 0; yy < h; yy += 6) for (int xx = 0; xx < w; xx += 6)
            g.FillRect(((xx / 6 + yy / 6) & 1) == 0 ? 0xFFF0F0F0 : 0xFFB8B8B8, Px(x + xx), Px(y + yy), Px(Math.Min(6, w - xx)), Px(Math.Min(6, h - yy)));
    }

    NativeWindow? _picker;
    protected override void Render(Gfx g)
    {
        Label(g, "맞춤 색상", 20, 18, 18);
        Button(g, "close", "×", 370, 10, 40, Destroy);
        Button(g, "eyedropper", "스포이드", 20, 55, 115, () =>
        {
            if (_picker != null) return;
            User32.ShowWindow(Handle, 0);
            User32.ShowWindow(Owner, 0);
            _picker = new ScreenColorPicker(value =>
            {
                _picker = null;
                if (value.HasValue) { SetColor(value.Value); SyncInputs(); }
                User32.ShowWindow(Owner, Win.SW_SHOW);
                User32.ShowWindow(Handle, Win.SW_SHOW);
                User32.ForceForeground(Handle);
                Invalidate();
            });
        });
        Label(g, "화면을 클릭해 추출 · Esc 취소", 150, 63, 12);
        DrawDetail(g);
    }
    protected override void OnDestroyed()
    {
        _picker?.Destroy();
        base.OnDestroyed();
    }

    void DrawDetail(Gfx g)
    {
        Label(g, "맞춤 색상 · 채도 / 명도", 20, 102);
        for (int y = 0; y < 160; y += 4) for (int x = 0; x < 390; x += 4)
            g.FillRect(Hsv(_h, x / 389.0, 1 - y / 159.0), Px(20 + x), Px(132 + y), Px(Math.Min(4, 390 - x)), Px(4));
        g.Ellipse(0xFFFFFFFF, Px(2), Px(20 + (float)_sat * 390 - 5), Px(132 + (1 - (float)_value) * 160 - 5), Px(10), Px(10));
        Label(g, "색조", 20, 305);
        Label(g, "투명도", 20, 340);
        for (int x = 0; x < 300; x += 3) g.FillRect(Hsv(x * 359.0 / 299, 1, 1), Px(100 + x), Px(307), Px(3), Px(17));
        Checker(g, 100, 343, 300, 17);
        for (int x = 0; x < 300; x += 3) g.FillRect((_color & 0xFFFFFF) | (uint)(x * 255 / 299) << 24, Px(100 + x), Px(343), Px(3), Px(17));
        g.Ellipse(0xFFFFFFFF, Px(2), Px(100 + (float)_h / 360 * 300 - 5), Px(307 - 2), Px(10), Px(21));
        g.Ellipse(0xFFFFFFFF, Px(2), Px(100 + _alpha / 255f * 300 - 5), Px(341), Px(10), Px(21));
        Label(g, "HEX (#RRGGBBAA)", 20, 378, 11);
        for (int i = 0; i < 4; i++) Label(g, new[] { "R", "G", "B", "A %" }[i], 165 + i * 63, 378);
        Button(g, "preview", "값 적용", 20, 443, 100, () => { if (ReadInputs()) Invalidate(); });
        Checker(g, 145, 444, 80, 30); g.FillRect(_color, Px(145), Px(444), Px(80), Px(30));
        Label(g, _error, 20, 487, 11, 0xFFFF9999);
        Label(g, "확인하면 맞춤 색상에 저장됩니다 (최근 24개)", 20, 516, 12);
        Button(g, "cancel", "취소", 220, 552, 85, Destroy);
        Button(g, "save", "확인", 320, 552, 90, SaveCustom);
    }

    void Slide(int x, int y)
    {
        double dx = x / S, dy = y / S;
        if (_slider == 1) { _sat = Math.Clamp((dx - 20) / 390, 0, 1); _value = 1 - Math.Clamp((dy - 132) / 160, 0, 1); }
        if (_slider == 2) _h = Math.Clamp((dx - 100) / 300, 0, 0.99999) * 360;
        if (_slider == 3) _alpha = (byte)Math.Round(Math.Clamp((dx - 100) / 300, 0, 1) * 255);
        _color = Hsv(_h, _sat, _value, _alpha); _error = ""; SyncInputs(); Invalidate();
    }

    protected override nint WndProc(uint msg, nint wParam, nint lParam)
    {
        if (msg == Win.WM_COMMAND && !_syncing && Win.HiWord(wParam) == 0x300) _lastField = Win.LoWord(wParam);
        if (_tab == 1 && msg == Win.WM_LBUTTONDOWN)
        {
            int x = Win.LoWord(lParam), y = Win.HiWord(lParam);
            _slider = x >= Px(20) && x <= Px(410) && y >= Px(132) && y <= Px(292) ? 1
                : x >= Px(100) && x <= Px(400) && y >= Px(300) && y <= Px(330) ? 2
                : x >= Px(100) && x <= Px(400) && y >= Px(337) && y <= Px(366) ? 3 : 0;
            if (_slider != 0) { _drag = true; User32.SetCapture(Handle); Slide(x, y); return 0; }
        }
        if (msg == Win.WM_MOUSEMOVE && _drag) { Slide(Win.LoWord(lParam), Win.HiWord(lParam)); return 0; }
        if (msg == Win.WM_LBUTTONUP && _drag) { _drag = false; User32.ReleaseCapture(); return 0; }
        // PanelWindow treats non-button space as caption; sliders must receive client mouse events.
        if (_tab == 1 && msg == Win.WM_NCHITTEST)
        {
            unsafe
            {
                var p = new POINT { X = Win.LoWord(lParam), Y = Win.HiWord(lParam) }; User32.ScreenToClient(Handle, &p);
                if (p.X >= Px(20) && p.X <= Px(410) && p.Y >= Px(132) && p.Y <= Px(366)) return Win.HTCLIENT;
            }
        }
        return base.WndProc(msg, wParam, lParam);
    }
}

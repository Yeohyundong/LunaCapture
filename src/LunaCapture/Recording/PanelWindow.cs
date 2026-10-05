using LunaCapture.Graphics;
using LunaCapture.Interop;

namespace LunaCapture.Recording;

/// <summary>
/// 녹화 설정 창 / 녹화 바의 공통 바탕: 테두리 없는 둥근 창, 녹화에 찍히지 않음,
/// 버튼 영역(HitItem) 클릭 처리, 빈 곳을 잡고 끌면 창 이동.
/// </summary>
internal abstract unsafe class PanelWindow : NativeWindow
{
    protected sealed class HitItem
    {
        public RECT Rect;
        public string Id = "";
        public Action? OnClick;
        public bool Enabled = true;
    }

    protected const uint PanelBg = 0xFF232329, PanelBorder = 0xFF3A3A44, TextMain = 0xFFECECF1, TextSub = 0xFF9A9AA6;
    protected const uint Accent = 0xFF8B7CF6, AccentHover = 0xFF9D90FF, ItemBg = 0xFF2F2F37, ItemHover = 0xFF3B3B45;
    protected const uint RecRed = 0xFFE5484D;

    protected readonly List<HitItem> Items = new();
    protected string? HoverId;
    protected float S = 1f;
    protected int Width, Height;

    GdiSurface? _back;
    string? _pressedId;
    bool _tracking;

    protected void CreatePanel(int x, int y, bool noActivate)
    {
        uint ex = Win.WS_EX_TOPMOST | Win.WS_EX_TOOLWINDOW | (noActivate ? Win.WS_EX_NOACTIVATE : 0);
        CreateHandle(ex, Win.WS_POPUP, x, y, 10, 10, "LunaCapture Panel");
        ExcludeFromCapture(Handle);
        int round = 2; // DWMWCP_ROUND
        Dwmapi.DwmSetWindowAttribute(Handle, Win.DWMWA_WINDOW_CORNER_PREFERENCE, &round, sizeof(int));
        S = User32.GetDpiForWindow(Handle) / 96f;
    }

    /// <summary>녹화에 찍히지 않게 한다. (개발 중 스크린샷 확인용으로 LUNA_DEBUG_SHOW_PANELS=1 이면 끔)</summary>
    public static void ExcludeFromCapture(nint hwnd)
    {
        if (Environment.GetEnvironmentVariable("LUNA_DEBUG_SHOW_PANELS") == "1") return;
        User32.SetWindowDisplayAffinity(hwnd, Win.WDA_EXCLUDEFROMCAPTURE);
    }

    protected void SetBounds(int x, int y, int w, int h)
    {
        Width = w; Height = h;
        User32.SetWindowPos(Handle, 0, x, y, w, h, Win.SWP_NOZORDER | Win.SWP_NOACTIVATE);
    }

    protected int Px(float v) => (int)MathF.Round(v * S);

    /// <summary>하위 클래스가 그리면서 Items에 버튼 영역을 등록한다.</summary>
    protected abstract void Render(Gfx g);

    protected HitItem AddItem(string id, RECT r, Action onClick, bool enabled = true)
    {
        var item = new HitItem { Id = id, Rect = r, OnClick = onClick, Enabled = enabled };
        Items.Add(item);
        return item;
    }

    protected bool IsHover(string id) => HoverId == id;

    protected void Invalidate()
    {
        if (Handle != 0) User32.InvalidateRect(Handle, null, 0);
    }

    protected override nint WndProc(uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case Win.WM_ERASEBKGND:
                return 1;

            case Win.WM_PAINT:
                Paint();
                return 0;

            case Win.WM_NCHITTEST:
            {
                var pt = new POINT { X = Win.LoWord(lParam), Y = Win.HiWord(lParam) };
                User32.ScreenToClient(Handle, &pt);
                return Hit(pt.X, pt.Y) != null ? Win.HTCLIENT : Win.HTCAPTION;
            }

            case Win.WM_SETCURSOR:
                User32.SetCursor(User32.LoadCursorW(0, Win.LoWord(lParam) == Win.HTCLIENT ? Win.IDC_HAND : Win.IDC_ARROW));
                if (Win.LoWord(lParam) != Win.HTCLIENT && HoverId != null) { HoverId = null; Invalidate(); }
                return 1;

            case Win.WM_MOUSEMOVE:
            {
                if (!_tracking)
                {
                    var tme = new TRACKMOUSEEVENT { cbSize = (uint)sizeof(TRACKMOUSEEVENT), dwFlags = 2 /* TME_LEAVE */, hwndTrack = Handle };
                    User32.TrackMouseEvent(&tme);
                    _tracking = true;
                }
                string? id = Hit(Win.LoWord(lParam), Win.HiWord(lParam))?.Id;
                if (id != HoverId) { HoverId = id; Invalidate(); }
                return 0;
            }

            case Win.WM_MOUSELEAVE:
                _tracking = false;
                if (HoverId != null) { HoverId = null; Invalidate(); }
                return 0;

            case Win.WM_LBUTTONDOWN:
                _pressedId = Hit(Win.LoWord(lParam), Win.HiWord(lParam))?.Id;
                return 0;

            case Win.WM_LBUTTONUP:
            {
                var item = Hit(Win.LoWord(lParam), Win.HiWord(lParam));
                if (item != null && item.Id == _pressedId && item.Enabled) item.OnClick?.Invoke();
                _pressedId = null;
                Invalidate();
                return 0;
            }

            case Win.WM_DESTROY:
                _back?.Dispose();
                _back = null;
                return 0;
        }
        return base.WndProc(msg, wParam, lParam);
    }

    HitItem? Hit(int x, int y)
    {
        foreach (var it in Items)
            if (x >= it.Rect.Left && x < it.Rect.Right && y >= it.Rect.Top && y < it.Rect.Bottom) return it;
        return null;
    }

    void Paint()
    {
        PAINTSTRUCT ps;
        nint hdc = User32.BeginPaint(Handle, &ps);
        if (Width > 0 && Height > 0)
        {
            if (_back == null || _back.Width != Width || _back.Height != Height)
            {
                _back?.Dispose();
                _back = new GdiSurface(Width, Height);
            }
            Items.Clear();
            using (var g = new Gfx(_back.Dc))
            {
                g.FillRect(PanelBg, 0, 0, Width, Height);
                Render(g);
                g.Rect(PanelBorder, 1, 0.5f, 0.5f, Width - 1, Height - 1);
            }
            Gdi32.BitBlt(hdc, 0, 0, Width, Height, _back.Dc, 0, 0, Win.SRCCOPY);
        }
        User32.EndPaint(Handle, &ps);
    }

    // ── 공통 위젯 ──

    protected void Button(Gfx g, string id, RECT r, string text, GdiFont font, Action onClick, bool primary = false, bool enabled = true,
        IconKind icon = IconKind.None)
    {
        AddItem(id, r, onClick, enabled);
        uint bg = primary ? (IsHover(id) ? AccentHover : Accent) : (IsHover(id) && enabled ? ItemHover : ItemBg);
        g.FillRoundRect(bg, r.Left, r.Top, r.Width, r.Height, Px(6));
        uint fg = enabled ? (primary ? 0xFFFFFFFF : TextMain) : TextSub;

        if (icon == IconKind.None)
        {
            g.CenterText(text, font, fg, r.Left, r.Top, r.Width, r.Height);
            return;
        }
        // 아이콘 (+ 글자): 둘을 한 덩어리로 가운데 정렬
        float iconSize = Px(12), gap = text.Length > 0 ? Px(7) : 0;
        float tw = text.Length > 0 ? g.MeasureWidth(text, font) : 0;
        float x = r.Left + (r.Width - iconSize - gap - tw) / 2;
        float cy = r.Top + r.Height / 2f;
        Icons.Draw(g, icon, x + iconSize / 2, cy, iconSize, fg);
        if (text.Length > 0) g.Text(text, font, fg, x + iconSize + gap, cy - g.LineHeight(font) / 2);
    }

    /// <summary>여러 선택지 중 하나를 고르는 세그먼트 버튼.</summary>
    protected void Segmented(Gfx g, string id, int x, int y, int h, string[] labels, int selected, GdiFont font, Action<int> onSelect, int itemWidth)
    {
        int gap = Px(4);
        for (int i = 0; i < labels.Length; i++)
        {
            var r = new RECT(x + i * (itemWidth + gap), y, x + i * (itemWidth + gap) + itemWidth, y + h);
            int idx = i;
            string itemId = id + i;
            AddItem(itemId, r, () => onSelect(idx));
            uint bg = i == selected ? Accent : IsHover(itemId) ? ItemHover : ItemBg;
            g.FillRoundRect(bg, r.Left, r.Top, r.Width, r.Height, Px(6));
            g.CenterText(labels[i], font, i == selected ? 0xFFFFFFFF : TextMain, r.Left, r.Top, r.Width, r.Height);
        }
    }

    /// <summary>[−] 값 [+] 형태의 스테퍼.</summary>
    protected void Stepper(Gfx g, string id, int x, int y, int h, string value, GdiFont font, Action onMinus, Action onPlus,
        bool canMinus, bool canPlus, int valueWidth)
    {
        var minus = new RECT(x, y, x + h, y + h);
        var plus = new RECT(x + h + valueWidth, y, x + h * 2 + valueWidth, y + h);
        Button(g, id + "-", minus, "−", font, onMinus, enabled: canMinus);
        Button(g, id + "+", plus, "+", font, onPlus, enabled: canPlus);
        g.CenterText(value, font, TextMain, minus.Right, y, valueWidth, h);
    }
}

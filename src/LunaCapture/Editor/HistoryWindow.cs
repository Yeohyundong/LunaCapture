using LunaCapture.Graphics;
using LunaCapture.Interop;
using LunaCapture.Services;

namespace LunaCapture.Editor;

/// <summary>
/// 캡처 기록 창: 최근 캡처·녹화를 미리보기 격자로 보여 준다.
/// 미리보기는 창을 열 때만 디스크에서 읽고, 창을 닫으면 메모리를 바로 돌려준다.
/// 더블클릭/Enter = 사진은 다시 편집, GIF·동영상은 열기 · Ctrl+C 복사 · Del 기록에서 삭제 · 오른쪽 클릭 메뉴
/// </summary>
internal sealed unsafe class HistoryWindow : NativeWindow
{
    const uint Bg = 0xFF1C1C20, CellBg = 0xFF26262C, CellHover = 0xFF2F2F37, Accent = 0xFF8B7CF6;
    const uint TextMain = 0xFFECECF1, TextSub = 0xFF9A9AA6;
    const nuint StatusTimer = 1;
    const uint WM_MOUSEWHEEL = 0x020A;

    readonly Action<HistoryEntry> _onEdit, _onCopy;
    readonly Action _onClosed;

    List<HistoryEntry> _entries = new();
    readonly Dictionary<string, GdiSurface> _thumbs = new();

    float _s = 1f;
    int _cw, _ch;
    int _scroll, _contentH;
    int _selected = -1, _hover = -1;
    long _lastClickTick;
    int _lastClickIndex = -1;
    string _status = "";
    GdiSurface? _back;
    GdiFont? _title, _body, _small, _badge;

    public HistoryWindow(Action<HistoryEntry> onEdit, Action<HistoryEntry> onCopy, Action onClosed)
    {
        _onEdit = onEdit;
        _onCopy = onCopy;
        _onClosed = onClosed;

        POINT cursor;
        User32.GetCursorPos(&cursor);
        var mi = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        User32.GetMonitorInfoW(User32.MonitorFromPoint(cursor, Win.MONITOR_DEFAULTTONEAREST), &mi);
        var work = mi.rcWork;

        CreateHandle(0, Win.WS_OVERLAPPEDWINDOW, work.Left, work.Top, 200, 200, "LunaCapture 캡처 기록");
        int dark = 1;
        Dwmapi.DwmSetWindowAttribute(Handle, Dwmapi.DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, sizeof(int));
        _s = User32.GetDpiForWindow(Handle) / 96f;
        _title = new GdiFont(UiFonts.Family, 17 * _s, true);
        _body = new GdiFont(UiFonts.Family, 13 * _s, false);
        _small = new GdiFont(UiFonts.Family, 12 * _s, false);
        _badge = new GdiFont(UiFonts.Family, 11 * _s, true);

        Reload();

        int w = Math.Min(work.Width - Px(80), CellW * 4 + Px(40)), h = Math.Min(work.Height - Px(80), Px(760));
        var r = new RECT(0, 0, w, h);
        User32.AdjustWindowRectExForDpi(&r, Win.WS_OVERLAPPEDWINDOW, 0, 0, User32.GetDpiForWindow(Handle));
        User32.SetWindowPos(Handle, 0, work.Left + (work.Width - r.Width) / 2, work.Top + (work.Height - r.Height) / 2,
            r.Width, r.Height, Win.SWP_NOZORDER | Win.SWP_NOACTIVATE);
        User32.ShowWindow(Handle, Win.SW_SHOW);
        User32.ForceForeground(Handle);
    }

    int Px(float v) => (int)MathF.Round(v * _s);
    int HeaderH => Px(64);
    int CellW => Px(HistoryStore.ThumbSide + 24);
    int CellH => Px(150 + 70);
    int Gap => Px(12);
    int Columns => Math.Max(1, (_cw - Px(20) * 2 + Gap) / (CellW + Gap));

    /// <summary>기록 목록과 미리보기를 다시 읽는다 (편집 후 갱신용).</summary>
    public void Reload()
    {
        foreach (var t in _thumbs.Values) t.Dispose();
        _thumbs.Clear();
        _entries = HistoryStore.LoadAll();
        foreach (var e in _entries)
        {
            var img = GdiPlus.LoadImage(HistoryStore.Thumb(e.Id));
            if (img == null) continue;
            var surf = new GdiSurface(img.Width, img.Height);
            fixed (byte* p = img.Bgra) Buffer.MemoryCopy(p, surf.Bits, img.Bgra.Length, img.Bgra.Length);
            _thumbs[e.Id] = surf;
        }
        if (_selected >= _entries.Count) _selected = _entries.Count - 1;
        Layout();
    }

    public void BringToFront() => User32.ForceForeground(Handle);

    // ───────────────────────── 메시지 ─────────────────────────

    protected override nint WndProc(uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case Win.WM_ERASEBKGND: return 1;
            case Win.WM_PAINT: Paint(); return 0;
            case Win.WM_SIZE:
                _cw = Win.LoWord(lParam) & 0xFFFF;
                _ch = Win.HiWord(lParam) & 0xFFFF;
                Layout();
                return 0;
            case WM_MOUSEWHEEL:
                ScrollBy(-Win.HiWord(wParam) * Px(120) / 120);
                return 0;
            case Win.WM_MOUSEMOVE:
            {
                int h = HitCell(Win.LoWord(lParam), Win.HiWord(lParam));
                if (h != _hover) { _hover = h; Invalidate(); }
                return 0;
            }
            case Win.WM_LBUTTONDOWN:
                OnClick(Win.LoWord(lParam), Win.HiWord(lParam));
                return 0;
            case Win.WM_RBUTTONUP:
            {
                int i = HitCell(Win.LoWord(lParam), Win.HiWord(lParam));
                if (i >= 0) { _selected = i; Invalidate(); ShowMenu(Win.LoWord(lParam), Win.HiWord(lParam)); }
                return 0;
            }
            case Win.WM_KEYDOWN:
                OnKey((int)wParam);
                return 0;
            case Win.WM_TIMER:
                if (wParam == (nint)StatusTimer) { User32.KillTimer(Handle, StatusTimer); _status = ""; Invalidate(); }
                return 0;
            case Win.WM_DESTROY:
                User32.KillTimer(Handle, StatusTimer);
                foreach (var t in _thumbs.Values) t.Dispose();
                _thumbs.Clear();
                _back?.Dispose(); _back = null;
                _title?.Dispose(); _body?.Dispose(); _small?.Dispose(); _badge?.Dispose();
                _onClosed();
                return 0;
        }
        return base.WndProc(msg, wParam, lParam);
    }

    void OnClick(int x, int y)
    {
        int i = HitCell(x, y);
        if (i < 0) return;
        long now = Environment.TickCount64;
        bool dbl = i == _lastClickIndex && now - _lastClickTick <= User32.GetDoubleClickTime();
        _lastClickTick = now;
        _lastClickIndex = i;
        _selected = i;
        Invalidate();
        if (dbl) OpenSelected();
    }

    void OnKey(int vk)
    {
        bool ctrl = Win.IsKeyDown(Win.VK_CONTROL);
        if (ctrl && vk == 'C') { CopySelected(); return; }
        int cols = Columns;
        switch (vk)
        {
            case Win.VK_ESCAPE: Destroy(); return;
            case Win.VK_RETURN: OpenSelected(); return;
            case Win.VK_DELETE: DeleteSelected(); return;
            case 0x25: Select(_selected - 1); return;      // ←
            case 0x27: Select(_selected + 1); return;      // →
            case 0x26: Select(_selected - cols); return;   // ↑
            case 0x28: Select(_selected + cols); return;   // ↓
        }
    }

    void Select(int i)
    {
        if (_entries.Count == 0) return;
        _selected = Math.Clamp(i < 0 && _selected < 0 ? 0 : i, 0, _entries.Count - 1);
        EnsureVisible(_selected);
        Invalidate();
    }

    HistoryEntry? Selected => _selected >= 0 && _selected < _entries.Count ? _entries[_selected] : null;

    /// <summary>사진은 편집기로, GIF·동영상은 기본 프로그램으로 연다.</summary>
    void OpenSelected()
    {
        if (Selected is not { } e) return;
        if (e.Kind == "image") _onEdit(e);
        else if (e.FilePath != null && File.Exists(e.FilePath))
            Shell32.ShellExecuteW(0, "open", e.FilePath, null, null, Win.SW_SHOW);
        else ShowStatus("파일이 없어요 (지워졌거나 옮겨졌어요)");
    }

    void CopySelected()
    {
        if (Selected is not { } e) return;
        _onCopy(e);
        ShowStatus(e.Kind == "image" ? "이미지를 클립보드에 복사했어요" : "파일을 클립보드에 복사했어요");
    }

    void DeleteSelected()
    {
        if (Selected is not { } e) return;
        HistoryStore.Delete(e.Id);
        int keep = _selected;
        Reload();
        _selected = Math.Min(keep, _entries.Count - 1);
        ShowStatus("기록에서 지웠어요 (저장된 파일은 그대로예요)");
    }

    void ShowMenu(int x, int y)
    {
        if (Selected is not { } e) return;
        bool image = e.Kind == "image";
        bool exists = e.FilePath != null && File.Exists(e.FilePath);
        nint menu = User32.CreatePopupMenu();
        User32.AppendMenuW(menu, Win.MF_STRING, 1, image ? "다시 편집하기\tEnter" : "열기\tEnter");
        User32.AppendMenuW(menu, Win.MF_STRING, 2, "복사\tCtrl+C");
        User32.AppendMenuW(menu, Win.MF_STRING | (exists ? 0u : 0x1u), 3, "파일 위치 열기");
        User32.AppendMenuW(menu, Win.MF_SEPARATOR, 0, null);
        User32.AppendMenuW(menu, Win.MF_STRING, 4, "기록에서 삭제\tDel");
        POINT pt = new() { X = x, Y = y };
        User32.ClientToScreen(Handle, &pt);
        int cmd = User32.TrackPopupMenu(menu, Win.TPM_RIGHTBUTTON | Win.TPM_RETURNCMD | Win.TPM_NONOTIFY, pt.X, pt.Y, 0, Handle, 0);
        User32.DestroyMenu(menu);
        switch (cmd)
        {
            case 1: OpenSelected(); break;
            case 2: CopySelected(); break;
            case 3: Shell32.ShellExecuteW(0, "open", "explorer.exe", $"/select,\"{e.FilePath}\"", null, Win.SW_SHOW); break;
            case 4: DeleteSelected(); break;
        }
    }

    void ShowStatus(string text)
    {
        _status = text;
        User32.SetTimer(Handle, StatusTimer, 2500, 0);
        Invalidate();
    }

    // ───────────────────────── 배치 / 스크롤 ─────────────────────────

    void Layout()
    {
        if (_cw <= 0 || _ch <= 0) return;
        if (_back == null || _back.Width != _cw || _back.Height != _ch)
        {
            _back?.Dispose();
            _back = new GdiSurface(_cw, _ch);
        }
        int rows = (_entries.Count + Columns - 1) / Columns;
        _contentH = rows * (CellH + Gap) + Px(20);
        ScrollBy(0);
    }

    void ScrollBy(int dy)
    {
        int max = Math.Max(0, _contentH - (_ch - HeaderH));
        _scroll = Math.Clamp(_scroll + dy, 0, max);
        Invalidate();
    }

    RECT CellRect(int i)
    {
        int cols = Columns;
        int gridW = cols * CellW + (cols - 1) * Gap;
        int x0 = (_cw - gridW) / 2;
        int col = i % cols, row = i / cols;
        int x = x0 + col * (CellW + Gap), y = HeaderH + Px(8) + row * (CellH + Gap) - _scroll;
        return new RECT(x, y, x + CellW, y + CellH);
    }

    void EnsureVisible(int i)
    {
        var r = CellRect(i);
        if (r.Top < HeaderH) ScrollBy(r.Top - HeaderH - Px(8));
        else if (r.Bottom > _ch) ScrollBy(r.Bottom - _ch + Px(8));
    }

    int HitCell(int x, int y)
    {
        if (y < HeaderH) return -1;
        for (int i = 0; i < _entries.Count; i++)
        {
            var r = CellRect(i);
            if (x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom) return i;
        }
        return -1;
    }

    // ───────────────────────── 그리기 ─────────────────────────

    void Invalidate()
    {
        if (Handle != 0) User32.InvalidateRect(Handle, null, 0);
    }

    static string When(DateTime t)
    {
        var today = DateTime.Today;
        if (t.Date == today) return $"오늘 {t:HH:mm}";
        if (t.Date == today.AddDays(-1)) return $"어제 {t:HH:mm}";
        return $"{t.Month}월 {t.Day}일 {t:HH:mm}";
    }

    void Paint()
    {
        PAINTSTRUCT ps;
        nint hdc = User32.BeginPaint(Handle, &ps);
        if (_back == null) { User32.EndPaint(Handle, &ps); return; }
        nint dc = _back.Dc;

        using (var g = new Gfx(dc))
        {
            g.FillRect(Bg, 0, 0, _cw, _ch);
            if (_entries.Count == 0)
                g.CenterText("아직 기록이 없어요 · PrtSc로 캡처해 보세요", _body!, TextSub, 0, HeaderH, _cw, _ch - HeaderH);

            // 셀 배경·글자 (미리보기 그림은 아래에서 GDI로)
            for (int i = 0; i < _entries.Count; i++)
            {
                var r = CellRect(i);
                if (r.Bottom < HeaderH || r.Top > _ch) continue;
                var e = _entries[i];
                g.FillRoundRect(i == _hover ? CellHover : CellBg, r.Left, r.Top, r.Width, r.Height, Px(10));
                if (i == _selected) g.RoundRect(Accent, Px(2), r.Left + 1, r.Top + 1, r.Width - 2, r.Height - 2, Px(10));

                float ty = r.Top + Px(12) + Px(150) + Px(10);
                g.Text(When(e.Created), _body!, TextMain, r.Left + Px(12), ty);
                string info = $"{e.Width}×{e.Height}" + (e.Kind == "image" && e.Items.Count > 0 ? $" · 편집 {e.Items.Count}개" : "");
                g.Text(info, _small!, TextSub, r.Left + Px(12), ty + Px(22));

                if (e.Kind != "image")
                {
                    string badge = e.Kind == "gif" ? "GIF" : "MP4";
                    float bw = g.MeasureWidth(badge, _badge!) + Px(12), bh = Px(20);
                    float bx = r.Right - Px(12) - bw;
                    g.FillRoundRect(e.Kind == "gif" ? 0xFFE5484D : 0xFF0090FF, bx, ty, bw, bh, Px(5));
                    g.CenterText(badge, _badge!, 0xFFFFFFFF, bx, ty, bw, bh);
                }
            }
        }

        // 미리보기 그림 (원래 크기 그대로, 칸 가운데)
        for (int i = 0; i < _entries.Count; i++)
        {
            var r = CellRect(i);
            if (r.Bottom < HeaderH || r.Top > _ch) continue;
            int areaX = r.Left + Px(12), areaY = r.Top + Px(12), areaW = r.Width - Px(24), areaH = Px(150);
            if (_thumbs.TryGetValue(_entries[i].Id, out var t))
            {
                float k = MathF.Min(1f, MathF.Min(areaW / (float)t.Width, areaH / (float)t.Height));
                int w = (int)(t.Width * k), h = (int)(t.Height * k);
                int x = areaX + (areaW - w) / 2, y = areaY + (areaH - h) / 2;
                if (k >= 0.999f) Gdi32.BitBlt(dc, x, y, w, h, t.Dc, 0, 0, Win.SRCCOPY);
                else
                {
                    Gdi32.SetStretchBltMode(dc, Win.HALFTONE);
                    Gdi32.SetBrushOrgEx(dc, 0, 0, null);
                    Gdi32.StretchBlt(dc, x, y, w, h, t.Dc, 0, 0, t.Width, t.Height, Win.SRCCOPY);
                }
            }
        }

        using (var g = new Gfx(dc))
        {
            // 머리글은 스크롤과 상관없이 맨 위에
            g.FillRect(Bg, 0, 0, _cw, HeaderH);
            g.FillRect(0xFF2C2C34, 0, HeaderH - 1, _cw, 1);
            g.Text("캡처 기록", _title!, TextMain, Px(20), (HeaderH - g.LineHeight(_title!)) / 2);
            float tw = g.MeasureWidth("캡처 기록", _title!);
            int max = AppSettings.Current.HistoryMax;
            g.Text($"{_entries.Count} / {max}", _body!, TextSub, Px(20) + tw + Px(10), (HeaderH - g.LineHeight(_body!)) / 2 + Px(1));
            string hint = _status.Length > 0 ? _status : "더블클릭: 편집·열기  ·  Ctrl+C: 복사  ·  Del: 기록에서 삭제  ·  오른쪽 클릭: 메뉴";
            float hw = g.MeasureWidth(hint, _small!);
            g.Text(hint, _small!, _status.Length > 0 ? 0xFF4CC38A : TextSub, _cw - Px(20) - hw, (HeaderH - g.LineHeight(_small!)) / 2);

            // 스크롤 표시
            int view = _ch - HeaderH;
            if (_contentH > view)
            {
                float barH = MathF.Max(Px(30), view * view / (float)_contentH);
                float barY = HeaderH + (view - barH) * _scroll / (float)(_contentH - view);
                g.FillRoundRect(0x55FFFFFF, _cw - Px(8), barY, Px(4), barH, Px(2));
            }
        }

        Gdi32.BitBlt(hdc, 0, 0, _cw, _ch, dc, 0, 0, Win.SRCCOPY);
        User32.EndPaint(Handle, &ps);
    }
}

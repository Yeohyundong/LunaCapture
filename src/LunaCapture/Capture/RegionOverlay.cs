using LunaCapture.Graphics;
using LunaCapture.Interop;

namespace LunaCapture.Capture;

internal enum CaptureMode { Image, Record }

/// <summary>오버레이 결과: 고른 모드, 화면 좌표 영역, (이미지 모드면) 잘라낸 이미지.</summary>
internal sealed record OverlayResult(CaptureMode Mode, RECT ScreenRect, PixelImage? Image);

/// <summary>
/// 화면을 정지시킨 뒤 어둡게 덮고, 드래그(영역) 또는 클릭(창)으로 캡처 범위를 고르게 하는 전체화면 오버레이.
/// 위쪽의 모드 버튼(사진 · 녹화)은 Tab / 숫자키 / 클릭으로 바꾼다. 녹화 형식(GIF/동영상)은 영역을 고른 뒤 설정 창에서 정한다.
/// 결과는 콜백으로 전달되고, 큰 버퍼는 즉시 해제된다.
/// </summary>
internal sealed unsafe class RegionOverlay : NativeWindow
{
    const int DragThreshold = 4;
    static readonly uint AccentColor = Win.Rgb(0x8B, 0x7C, 0xF6);
    static readonly uint RecordColor = Win.Rgb(0xE5, 0x48, 0x4D);
    static readonly string[] ModeLabels = ["사진", "녹화"];
    const int ModeCount = 2;

    readonly Action<OverlayResult?> _onDone;
    CaptureMode _mode;
    readonly int _originX, _originY;
    readonly List<RECT> _windowRects;
    readonly RECT _homeMonitor; // 모드 버튼을 띄울 모니터 (열 때 커서가 있던 곳)
    readonly RECT[] _modeRects = new RECT[ModeCount];

    GdiSurface? _frozen, _dim, _back;
    nint _accentBrush, _recordBrush, _labelBrush, _guideBrush, _font, _crossCursor;
    GdiFont? _modeFont, _hintFont, _loupeFont;
    float _scale = 1f;

    bool _mouseDown, _dragging;
    POINT _start, _cursor;
    RECT? _selection;
    bool _finished;

    RegionOverlay(CaptureMode mode, Action<OverlayResult?> onDone)
    {
        _mode = mode;
        _onDone = onDone;
        _originX = User32.GetSystemMetrics(Win.SM_XVIRTUALSCREEN);
        _originY = User32.GetSystemMetrics(Win.SM_YVIRTUALSCREEN);
        int w = User32.GetSystemMetrics(Win.SM_CXVIRTUALSCREEN);
        int h = User32.GetSystemMetrics(Win.SM_CYVIRTUALSCREEN);

        // 오버레이를 띄우기 전에 화면과 창 목록을 먼저 얼려둔다.
        _windowRects = CollectWindowRects(w, h);
        _frozen = GdiSurface.CaptureScreen(_originX, _originY, w, h);
        _dim = MakeDimmed(_frozen);
        _back = new GdiSurface(w, h);

        _accentBrush = Gdi32.CreateSolidBrush(AccentColor);
        _recordBrush = Gdi32.CreateSolidBrush(RecordColor);
        _labelBrush = Gdi32.CreateSolidBrush(Win.Rgb(0x1E, 0x1E, 0x24));
        _guideBrush = Gdi32.CreateSolidBrush(Win.Rgb(0xB0, 0xA8, 0xFF));
        _crossCursor = User32.LoadCursorW(0, Win.IDC_CROSS);

        CreateHandle(Win.WS_EX_TOPMOST | Win.WS_EX_TOOLWINDOW, Win.WS_POPUP, _originX, _originY, w, h, "LunaCapture Overlay");
        _scale = User32.GetDpiForWindow(Handle) / 96f;
        _font = Gdi32.CreateFontW(-(int)(14 * _scale), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, UiFonts.BoldFamily);
        _modeFont = new GdiFont(UiFonts.Family, 14 * _scale, bold: true);
        _hintFont = new GdiFont(UiFonts.Family, 13 * _scale, bold: false);
        _loupeFont = new GdiFont(UiFonts.Family, 12 * _scale, bold: false);

        POINT pt;
        User32.GetCursorPos(&pt);
        _cursor = ToLocal(pt);
        _selection = HitWindow(_cursor);
        _homeMonitor = MonitorAt(_cursor);
        LayoutModeButtons();

        User32.ShowWindow(Handle, Win.SW_SHOW);
        User32.ForceForeground(Handle); // PrtSc를 키보드 훅으로 받았어도 Tab·Esc가 이 창으로 오게
    }

    public static void Start(CaptureMode mode, Action<OverlayResult?> onDone) => _ = new RegionOverlay(mode, onDone);

    bool IsRecordingMode => _mode == CaptureMode.Record;

    void SetMode(CaptureMode mode)
    {
        _mode = mode;
        Invalidate();
    }

    /// <summary>모드 버튼들을 기준 모니터 위쪽 가운데에 배치.</summary>
    void LayoutModeButtons()
    {
        int bw = (int)(84 * _scale), bh = (int)(34 * _scale), gap = (int)(4 * _scale);
        int total = bw * ModeCount + gap * (ModeCount - 1);
        int x = (_homeMonitor.Left + _homeMonitor.Right - total) / 2;
        int y = _homeMonitor.Top + (int)(28 * _scale);
        for (int i = 0; i < ModeCount; i++)
            _modeRects[i] = new RECT(x + i * (bw + gap), y, x + i * (bw + gap) + bw, y + bh);
    }

    int HitModeButton(POINT p)
    {
        for (int i = 0; i < ModeCount; i++)
        {
            var r = _modeRects[i];
            if (p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom) return i;
        }
        return -1;
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

            case Win.WM_SETCURSOR:
                User32.SetCursor(_crossCursor);
                return 1;

            case Win.WM_MOUSEMOVE:
                _cursor = new POINT { X = Win.LoWord(lParam), Y = Win.HiWord(lParam) };
                if (_mouseDown)
                {
                    if (!_dragging && (Math.Abs(_cursor.X - _start.X) > DragThreshold || Math.Abs(_cursor.Y - _start.Y) > DragThreshold))
                        _dragging = true;
                    if (_dragging) _selection = Normalize(_start, _cursor);
                }
                else
                {
                    _selection = HitWindow(_cursor);
                }
                Invalidate();
                return 0;

            case Win.WM_LBUTTONDOWN:
            {
                var p = new POINT { X = Win.LoWord(lParam), Y = Win.HiWord(lParam) };
                int modeHit = HitModeButton(p);
                if (modeHit >= 0) { SetMode((CaptureMode)modeHit); return 0; }
                _mouseDown = true;
                _dragging = false;
                _start = p;
                User32.SetCapture(Handle);
                return 0;
            }

            case Win.WM_LBUTTONUP:
                if (!_mouseDown) return 0;
                _mouseDown = false;
                User32.ReleaseCapture();
                // 드래그했으면 그 영역, 클릭만 했으면 커서 아래 창.
                if (_selection is { } sel && sel.Width > 1 && sel.Height > 1) Finish(sel);
                return 0;

            case Win.WM_RBUTTONUP:
                Finish(null);
                return 0;

            case Win.WM_KEYDOWN:
                switch ((int)wParam)
                {
                    case Win.VK_ESCAPE: Finish(null); break;
                    case Win.VK_RETURN: Finish(IsRecordingMode ? MonitorAt(_cursor) : new RECT(0, 0, _frozen!.Width, _frozen.Height)); break;
                    case 0x09: // Tab: 사진 ↔ 녹화
                        SetMode(IsRecordingMode ? CaptureMode.Image : CaptureMode.Record);
                        break;
                    case '1': SetMode(CaptureMode.Image); break;
                    case '2': SetMode(CaptureMode.Record); break;
                }
                return 0;

            case Win.WM_DESTROY:
                ReleaseResources();
                return 0;
        }
        return base.WndProc(msg, wParam, lParam);
    }

    void Finish(RECT? rect)
    {
        if (_finished) return;
        _finished = true;

        RECT? clipped = null;
        if (rect is { } r && _frozen != null)
        {
            r = Clip(r, _frozen.Width, _frozen.Height);
            if (r.Width > 0 && r.Height > 0) clipped = r;
        }

        OverlayResult? result = null;
        if (clipped is { } c)
        {
            var screen = new RECT(c.Left + _originX, c.Top + _originY, c.Right + _originX, c.Bottom + _originY);
            result = new OverlayResult(_mode, screen, _mode == CaptureMode.Image ? _frozen!.Crop(c) : null);
        }
        Destroy();
        _onDone(result);
    }

    /// <summary>지점이 속한 모니터 전체 (오버레이 좌표). 녹화의 "Enter"는 이 모니터만 — 여러 모니터를 한 영상으로 묶는 건 의미가 없어서.</summary>
    RECT MonitorAt(POINT local)
    {
        var mi = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        User32.GetMonitorInfoW(User32.MonitorFromPoint(new POINT { X = local.X + _originX, Y = local.Y + _originY },
            Win.MONITOR_DEFAULTTONEAREST), &mi);
        var m = mi.rcMonitor;
        return new RECT(m.Left - _originX, m.Top - _originY, m.Right - _originX, m.Bottom - _originY);
    }

    void Paint()
    {
        PAINTSTRUCT ps;
        nint hdc = User32.BeginPaint(Handle, &ps);
        if (_back == null || _dim == null || _frozen == null) { User32.EndPaint(Handle, &ps); return; }

        nint dc = _back.Dc;
        int w = _back.Width, h = _back.Height;
        Gdi32.BitBlt(dc, 0, 0, w, h, _dim.Dc, 0, 0, Win.SRCCOPY);

        nint frameBrush = IsRecordingMode ? _recordBrush : _accentBrush;
        if (_selection is { } s)
        {
            Gdi32.BitBlt(dc, s.Left, s.Top, s.Width, s.Height, _frozen.Dc, s.Left, s.Top, Win.SRCCOPY);
            var outer = new RECT(s.Left - 1, s.Top - 1, s.Right + 1, s.Bottom + 1);
            User32.FrameRect(dc, &outer, frameBrush);
            outer = new RECT(s.Left - 2, s.Top - 2, s.Right + 2, s.Bottom + 2);
            User32.FrameRect(dc, &outer, frameBrush);
        }

        if (!_dragging)
        {
            // 정밀하게 맞출 수 있도록 커서 위치에 가이드 라인.
            var hLine = new RECT(0, _cursor.Y, w, _cursor.Y + 1);
            var vLine = new RECT(_cursor.X, 0, _cursor.X + 1, h);
            User32.FillRect(dc, &hLine, _guideBrush);
            User32.FillRect(dc, &vLine, _guideBrush);
        }

        Gdi32.SelectObject(dc, _font);
        Gdi32.SetBkMode(dc, Win.TRANSPARENT);
        Gdi32.SetTextColor(dc, Win.Rgb(0xFF, 0xFF, 0xFF));

        if (_selection is { } sel)
        {
            DrawLabel(dc, $"{sel.Width} × {sel.Height}", sel.Left, sel.Top - (int)(30 * _scale), sel.Top + 4);
        }
        DrawModeButtons(dc);
        DrawLoupe(dc);

        var pr = ps.rcPaint;
        Gdi32.BitBlt(hdc, pr.Left, pr.Top, pr.Width, pr.Height, dc, pr.Left, pr.Top, Win.SRCCOPY);
        User32.EndPaint(Handle, &ps);
    }

    /// <summary>위쪽 가운데의 [사진 | 녹화] 모드 버튼. 녹화 모드는 빨간색으로 강조.</summary>
    void DrawModeButtons(nint dc)
    {
        using var g = new Gfx(dc);
        float pad = 4 * _scale;
        var first = _modeRects[0];
        var last = _modeRects[ModeCount - 1];
        g.FillRoundRect(0xF01E1E24, first.Left - pad, first.Top - pad, last.Right - first.Left + pad * 2, first.Height + pad * 2, 10 * _scale);

        for (int i = 0; i < ModeCount; i++)
        {
            var r = _modeRects[i];
            bool selected = (int)_mode == i;
            if (selected)
                g.FillRoundRect(i == 0 ? 0xFF8B7CF6 : 0xFFE5484D, r.Left, r.Top, r.Width, r.Height, 7 * _scale);
            g.CenterText(ModeLabels[i], _modeFont!, selected ? 0xFFFFFFFF : 0xFFB4B4BE, r.Left, r.Top, r.Width, r.Height);
        }

        // 조작 안내 (예전엔 커서를 따라다녔지만 확대경과 겹쳐서 여기로 옮김)
        string hint = IsRecordingMode
            ? "드래그: 영역  ·  클릭: 창  ·  Enter: 이 모니터  ·  Tab: 사진/녹화  ·  Esc: 취소"
            : "드래그: 영역  ·  클릭: 창  ·  Enter: 전체 화면  ·  Tab: 사진/녹화  ·  Esc: 취소";
        float hw = g.MeasureWidth(hint, _hintFont!), hh = g.LineHeight(_hintFont!);
        float hx = (first.Left + last.Right - hw) / 2, hy = last.Bottom + 14 * _scale;
        g.FillRoundRect(0xD01E1E24, hx - 10 * _scale, hy - 5 * _scale, hw + 20 * _scale, hh + 10 * _scale, 8 * _scale);
        g.Text(hint, _hintFont!, 0xFFE6E6EC, hx, hy);
    }

    /// <summary>
    /// 확대경: 커서 주변 15×15 픽셀을 크게 보여 주어 경계선을 1픽셀 단위로 맞출 수 있게 한다.
    /// 가운데 칸 = 지금 커서가 가리키는 픽셀. 아래에 화면 좌표와 그 픽셀의 색을 표시.
    /// </summary>
    void DrawLoupe(nint dc)
    {
        const int Cells = 15, Half = Cells / 2;
        int cell = Math.Max(6, (int)(8 * _scale));
        int view = Cells * cell;
        int pad = (int)(5 * _scale), footer = (int)(24 * _scale), off = (int)(28 * _scale);
        int boxW = view + pad * 2, boxH = view + pad * 2 + footer;

        // 커서 오른쪽 아래, 화면 밖으로 나가면 반대쪽으로
        int bx = _cursor.X + off, by = _cursor.Y + off;
        if (bx + boxW > _back!.Width) bx = _cursor.X - off - boxW;
        if (by + boxH > _back.Height) by = _cursor.Y - off - boxH;
        bx = Math.Max(0, bx); by = Math.Max(0, by);
        int vx = bx + pad, vy = by + pad;

        using (var g = new Gfx(dc))
        {
            g.FillRoundRect(0xF2141418, bx, by, boxW, boxH, 9 * _scale);
            g.FillRect(0xFF000000, vx, vy, view, view);
        }

        // 확대된 픽셀 (가장자리에서는 화면 밖 부분을 빼고 그림). 부드럽게 하지 않고 픽셀 그대로.
        int sx = _cursor.X - Half, sy = _cursor.Y - Half;
        int sx0 = Math.Max(0, sx), sy0 = Math.Max(0, sy);
        int sx1 = Math.Min(_frozen!.Width, sx + Cells), sy1 = Math.Min(_frozen.Height, sy + Cells);
        if (sx1 > sx0 && sy1 > sy0)
        {
            Gdi32.SetStretchBltMode(dc, 3 /* COLORONCOLOR: 최근접 */);
            Gdi32.StretchBlt(dc, vx + (sx0 - sx) * cell, vy + (sy0 - sy) * cell, (sx1 - sx0) * cell, (sy1 - sy0) * cell,
                _frozen.Dc, sx0, sy0, sx1 - sx0, sy1 - sy0, Win.SRCCOPY);
        }

        using (var g = new Gfx(dc))
        {
            uint guide = IsRecordingMode ? 0xB0E5484D : 0xB08B7CF6;
            float c0 = Half * cell;
            float mid = cell / 2f;
            // 가운데 칸을 지나는 가로·세로 안내선 (가운데 칸은 비워 둠)
            g.FillRect(guide, vx, vy + c0 + mid - 0.5f, c0, 1);
            g.FillRect(guide, vx + c0 + cell, vy + c0 + mid - 0.5f, view - c0 - cell, 1);
            g.FillRect(guide, vx + c0 + mid - 0.5f, vy, 1, c0);
            g.FillRect(guide, vx + c0 + mid - 0.5f, vy + c0 + cell, 1, view - c0 - cell);
            // 가운데 칸 테두리 (밝은 화면·어두운 화면 모두에서 보이게 흰색 + 검정)
            g.Rect(0xFF000000, 1, vx + c0 - 1, vy + c0 - 1, cell + 2, cell + 2);
            g.Rect(0xFFFFFFFF, 1, vx + c0, vy + c0, cell, cell);
            g.Rect(0xFF3A3A44, 1, vx - 0.5f, vy - 0.5f, view + 1, view + 1);

            // 좌표와 색
            uint px = 0;
            if (_cursor.X >= 0 && _cursor.Y >= 0 && _cursor.X < _frozen.Width && _cursor.Y < _frozen.Height)
                px = ((uint*)_frozen.Bits)[_cursor.Y * _frozen.Width + _cursor.X] & 0xFFFFFF;
            float ty = vy + view + (footer - g.LineHeight(_loupeFont!)) / 2 + 1;
            g.TextTabular($"{_cursor.X + _originX}, {_cursor.Y + _originY}", _loupeFont!, 0xFFE6E6EC, vx + 2, ty);
            string hex = $"#{px:X6}";
            float hexW = g.MeasureTabular(hex, _loupeFont!);
            float sw = 10 * _scale;
            g.TextTabular(hex, _loupeFont!, 0xFFB4B4BE, vx + view - hexW - 2, ty);
            float swY = vy + view + (footer - sw) / 2 + 1;
            g.FillRoundRect(0xFF000000 | px, vx + view - hexW - sw - 8 * _scale, swY, sw, sw, 2 * _scale);
            g.RoundRect(0x66FFFFFF, 1, vx + view - hexW - sw - 8 * _scale, swY, sw, sw, 2 * _scale);
        }
    }

    /// <summary>둥근 느낌의 어두운 배지에 텍스트를 그린다. 화면 밖으로 나가면 안쪽으로 밀어 넣는다.</summary>
    void DrawLabel(nint dc, string text, int x, int y, int? fallbackY)
    {
        var measure = new RECT(0, 0, 0, 0);
        User32.DrawTextW(dc, text, text.Length, &measure, Win.DT_SINGLELINE | Win.DT_CALCRECT);
        int padX = (int)(8 * _scale), padY = (int)(4 * _scale);
        int bw = measure.Width + padX * 2, bh = measure.Height + padY * 2;

        if (y < 0 && fallbackY is { } fy) y = fy;
        x = Math.Clamp(x, 0, Math.Max(0, _back!.Width - bw));
        y = Math.Clamp(y, 0, Math.Max(0, _back.Height - bh));

        var box = new RECT(x, y, x + bw, y + bh);
        User32.FillRect(dc, &box, _labelBrush);
        User32.DrawTextW(dc, text, text.Length, &box, Win.DT_SINGLELINE | Win.DT_CENTER | Win.DT_VCENTER | Win.DT_NOCLIP);
    }

    void Invalidate() => User32.InvalidateRect(Handle, null, 0);

    RECT? HitWindow(POINT p)
    {
        foreach (var r in _windowRects)
            if (p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom) return r;
        return null;
    }

    POINT ToLocal(POINT screen) => new() { X = screen.X - _originX, Y = screen.Y - _originY };

    static RECT Normalize(POINT a, POINT b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    static RECT Clip(RECT r, int w, int h) =>
        new(Math.Max(0, r.Left), Math.Max(0, r.Top), Math.Min(w, r.Right), Math.Min(h, r.Bottom));

    /// <summary>보이는 최상위 창들의 실제 경계(그림자 제외)를 z-order 순서로 모은다.</summary>
    List<RECT> CollectWindowRects(int w, int h)
    {
        var list = new List<RECT>();
        for (nint hwnd = User32.GetTopWindow(0); hwnd != 0; hwnd = User32.GetWindow(hwnd, Win.GW_HWNDNEXT))
        {
            if (User32.IsWindowVisible(hwnd) == 0 || User32.IsIconic(hwnd) != 0) continue;
            if (((ulong)User32.GetWindowLongPtrW(hwnd, Win.GWL_EXSTYLE) & Win.WS_EX_TRANSPARENT) != 0) continue;

            int cloaked = 0;
            Dwmapi.DwmGetWindowAttribute(hwnd, Dwmapi.DWMWA_CLOAKED, &cloaked, sizeof(int));
            if (cloaked != 0) continue;

            RECT r;
            if (Dwmapi.DwmGetWindowAttribute(hwnd, Dwmapi.DWMWA_EXTENDED_FRAME_BOUNDS, &r, (uint)sizeof(RECT)) != 0)
                User32.GetWindowRect(hwnd, &r);

            r = Clip(new RECT(r.Left - _originX, r.Top - _originY, r.Right - _originX, r.Bottom - _originY), w, h);
            if (r.Width > 8 && r.Height > 8) list.Add(r);
        }
        return list;
    }

    static GdiSurface MakeDimmed(GdiSurface src)
    {
        var dim = new GdiSurface(src.Width, src.Height);
        uint* s = (uint*)src.Bits;
        uint* d = (uint*)dim.Bits;
        long n = (long)src.Width * src.Height;
        for (long i = 0; i < n; i++)
        {
            // 각 채널을 약 45%로 (비트 연산으로 빠르게: x/2 - x/16)
            uint p = s[i];
            uint half = (p >> 1) & 0x7F7F7F;
            uint sixteenth = (p >> 4) & 0x0F0F0F;
            d[i] = half - sixteenth;
        }
        return dim;
    }

    void ReleaseResources()
    {
        _frozen?.Dispose(); _frozen = null;
        _dim?.Dispose(); _dim = null;
        _back?.Dispose(); _back = null;
        if (_accentBrush != 0) Gdi32.DeleteObject(_accentBrush);
        if (_recordBrush != 0) Gdi32.DeleteObject(_recordBrush);
        _modeFont?.Dispose(); _modeFont = null;
        _hintFont?.Dispose(); _hintFont = null;
        _loupeFont?.Dispose(); _loupeFont = null;
        if (_labelBrush != 0) Gdi32.DeleteObject(_labelBrush);
        if (_guideBrush != 0) Gdi32.DeleteObject(_guideBrush);
        if (_font != 0) Gdi32.DeleteObject(_font);
        _accentBrush = _recordBrush = _labelBrush = _guideBrush = _font = 0;
    }
}

using LunaCapture.Graphics;
using LunaCapture.Interop;
using LunaCapture.Services;

namespace LunaCapture.Editor;

/// <summary>
/// 영역 선택 도구: 영역을 잡고 복사 · 저장 · 오려내기 · 자르기 · 강조. 그리고 Ctrl+V 붙여넣기.
/// 영역은 주석이 아니라 잠깐 쓰는 표시라서 되돌리기 기록에 남지 않는다 (영역으로 한 동작은 남는다).
/// </summary>
internal sealed unsafe partial class EditorWindow
{
    RectF? _marquee;        // 이미지 좌표, 정수 픽셀, 이미지 안으로 잘림
    PointF _mqAnchor;       // 영역을 그릴 때 고정된 모서리
    readonly List<RECT> _barRects = new();
    int _barHover = -1;
    GdiFont? _barFont;

    static readonly (string Label, string Tip)[] BarActions =
    [
        ("복사", "선택 영역만 클립보드에 복사 (Ctrl+C)"),
        ("저장", "선택 영역만 파일로 저장 (Ctrl+S)"),
        ("오려내기", "오려내서 옮기기 (Ctrl+X) · 빈 자리는 주변 색으로 채워요"),
        ("자르기", "선택 영역만 남기고 나머지는 잘라내기"),
        ("강조", "선택 영역만 밝게, 나머지는 어둡게"),
    ];

    void RunBarAction(int i)
    {
        switch (i)
        {
            case 0: CopyRegion(); break;
            case 1: SaveRegion(); break;
            case 2: CutRegion(); break;
            case 3: CropToRegion(); break;
            case 4: SpotlightRegion(); break;
        }
        _barHover = -1;
        Invalidate();
    }

    // ───────────────────────── 마우스 ─────────────────────────

    /// <summary>영역 선택 도구로 이미지 위를 눌렀을 때.</summary>
    void OnSelectToolDown(int x, int y, PointF p)
    {
        if (_marquee is { } m)
        {
            int mh = HitMarqueeHandle(x, y);
            if (mh >= 0)
            {
                _mqAnchor = Corners(m)[3 - mh]; // 반대쪽 모서리를 고정하고 다시 그리듯 조절
                BeginDrag(DragMode.Marquee, p);
                return;
            }
            if (Geo.Inside(m, p))
            {
                BeginDrag(DragMode.MarqueeMove, p);
                return;
            }
        }

        // 오려낸·붙여넣은 그림과 강조 테두리는 이 도구로도 잡아서 옮긴다
        if ((_selected != null && HitHandle(x, y) >= 0) || (HitItem(p) is { } hit && SelectToolPicks(hit)))
        {
            _marquee = null;
            TrySelectAt(x, y, p);
            return;
        }

        _selected = null;
        _marquee = null;
        _mqAnchor = Snap(p);
        BeginDrag(DragMode.Marquee, p);
        Invalidate();
    }

    static bool SelectToolPicks(Annotation a) => a is ImageAnnotation or SpotlightAnnotation or NumberAnnotation;

    void DragMarquee(PointF p)
    {
        if (_mode == DragMode.Marquee)
        {
            _marquee = ClampToImage(Geo.FromPoints(_mqAnchor, Snap(p)));
        }
        else if (_marquee is { } m)
        {
            float dx = MathF.Round(p.X - _dragLast.X), dy = MathF.Round(p.Y - _dragLast.Y);
            if (dx == 0 && dy == 0) return;
            MoveMarquee(dx, dy);
            _dragLast = new PointF(_dragLast.X + dx, _dragLast.Y + dy);
        }
        Invalidate();
    }

    /// <summary>영역을 옮긴다 (이미지 밖으로 나가지 않게).</summary>
    void MoveMarquee(float dx, float dy)
    {
        if (_marquee is not { } m) return;
        dx = Math.Clamp(dx, -m.X, _base.Width - (m.X + m.Width));
        dy = Math.Clamp(dy, -m.Y, _base.Height - (m.Y + m.Height));
        _marquee = new RectF(m.X + dx, m.Y + dy, m.Width, m.Height);
        Invalidate();
    }

    static PointF Snap(PointF p) => new(MathF.Round(p.X), MathF.Round(p.Y));

    RectF ClampToImage(RectF r)
    {
        float x0 = Math.Clamp(r.X, 0, _base.Width), y0 = Math.Clamp(r.Y, 0, _base.Height);
        float x1 = Math.Clamp(r.X + r.Width, 0, _base.Width), y1 = Math.Clamp(r.Y + r.Height, 0, _base.Height);
        return new RectF(x0, y0, x1 - x0, y1 - y0);
    }

    int HitMarqueeHandle(int x, int y)
    {
        if (_marquee is not { } m) return -1;
        var c = Corners(m);
        float reach = 7 * _scale;
        for (int i = 0; i < c.Length; i++)
        {
            float sx = _imgX + c[i].X * _zoom, sy = _imgY + c[i].Y * _zoom;
            if (MathF.Abs(x - sx) <= reach && MathF.Abs(y - sy) <= reach) return i;
        }
        return -1;
    }

    int HitBar(int x, int y)
    {
        for (int i = 0; i < _barRects.Count; i++)
        {
            var r = _barRects[i];
            if (x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom) return i;
        }
        return -1;
    }

    void SelectAll()
    {
        SetTool(Tool.Select);
        _marquee = new RectF(0, 0, _base.Width, _base.Height);
        ShowStatus("전체를 선택했어요 · 영역 모서리를 끌어 크기를 바꿀 수 있어요");
    }

    void ShowRegionMenu(int x, int y)
    {
        nint menu = User32.CreatePopupMenu();
        User32.AppendMenuW(menu, Win.MF_STRING, 1, "복사\tCtrl+C");
        User32.AppendMenuW(menu, Win.MF_STRING, 2, "저장…\tCtrl+S");
        User32.AppendMenuW(menu, Win.MF_STRING, 3, "오려내기\tCtrl+X");
        User32.AppendMenuW(menu, Win.MF_SEPARATOR, 0, null);
        User32.AppendMenuW(menu, Win.MF_STRING, 4, "자르기 (이 영역만 남기기)");
        User32.AppendMenuW(menu, Win.MF_STRING, 5, "강조 (나머지 어둡게)");
        POINT pt = new() { X = x, Y = y };
        User32.ClientToScreen(Handle, &pt);
        int cmd = User32.TrackPopupMenu(menu, Win.TPM_RIGHTBUTTON | Win.TPM_RETURNCMD | Win.TPM_NONOTIFY, pt.X, pt.Y, 0, Handle, 0);
        User32.DestroyMenu(menu);
        if (cmd > 0) RunBarAction(cmd - 1);
    }

    /// <summary>빈 곳을 오른쪽 클릭: 붙여넣기 · 전체 선택.</summary>
    void ShowCanvasMenu(int x, int y)
    {
        bool hasImage = User32.IsClipboardFormatAvailable(Win.CF_DIB) != 0 || User32.IsClipboardFormatAvailable(17 /* CF_DIBV5 */) != 0;
        nint menu = User32.CreatePopupMenu();
        User32.AppendMenuW(menu, Win.MF_STRING | (hasImage ? 0u : 0x1u /* MF_GRAYED */), 1, "붙여넣기\tCtrl+V");
        User32.AppendMenuW(menu, Win.MF_STRING, 2, "전체 선택\tCtrl+A");
        POINT pt = new() { X = x, Y = y };
        User32.ClientToScreen(Handle, &pt);
        int cmd = User32.TrackPopupMenu(menu, Win.TPM_RIGHTBUTTON | Win.TPM_RETURNCMD | Win.TPM_NONOTIFY, pt.X, pt.Y, 0, Handle, 0);
        User32.DestroyMenu(menu);
        if (cmd == 1) Paste();
        else if (cmd == 2) SelectAll();
    }

    // ───────────────────────── 동작 ─────────────────────────

    RECT? RegionRect()
    {
        if (_marquee is not { } m) return null;
        var r = new RECT((int)m.X, (int)m.Y, (int)(m.X + m.Width), (int)(m.Y + m.Height));
        return r.Width > 0 && r.Height > 0 ? r : null;
    }

    /// <summary>지금 보이는 그대로(원본 + 주석)에서 영역만 잘라 온다.</summary>
    PixelImage RegionImage(RECT r)
    {
        CommitText();
        Gdi32.GdiFlush();
        return _committed!.Crop(r);
    }

    void CopyRegion()
    {
        if (RegionRect() is not { } r) return;
        bool ok = ClipboardService.SetImage(Handle, RegionImage(r));
        ShowStatus(ok ? $"선택 영역({r.Width}×{r.Height})을 복사했어요" : "클립보드 복사에 실패했어요");
    }

    void SaveRegion()
    {
        if (RegionRect() is not { } r) return;
        SaveImageAs(RegionImage(r));
    }

    /// <summary>
    /// 오려내기: 영역을 그림 조각으로 떼어 내고, 빈 자리는 주변에서 가장 많은 색으로 메운다.
    /// 조각은 바로 선택된 상태라 끌어서 옮기면 된다. (메운 칸 + 조각 = 주석 두 개 → 되돌리기 한 번에 원래대로)
    /// </summary>
    void CutRegion()
    {
        if (RegionRect() is not { } r) return;
        var piece = RegionImage(r);
        uint fill = SurroundingColor(r);
        PushUndo();
        PointF a = new(r.Left, r.Top), b = new(r.Right, r.Bottom);
        _items.Add(new PatchAnnotation { Color = fill, A = a, B = b });
        var img = new ImageAnnotation { Image = piece, A = a, B = b };
        _items.Add(img);
        _marquee = null;
        _selected = img;
        RebuildCommitted();
        ShowStatus("오려냈어요 · 끌어서 옮기고, 모서리로 크기를 바꿀 수 있어요");
    }

    /// <summary>자르기: 영역만 남긴다. 그려 둔 주석도 같이 옮겨져서 계속 고칠 수 있다.</summary>
    void CropToRegion()
    {
        if (RegionRect() is not { } r) return;
        if (r.Width == _base.Width && r.Height == _base.Height) { _marquee = null; Invalidate(); return; }
        CommitText();
        PushUndo();
        var cropped = Composer.Crop(_base, r);
        foreach (var a in _items) a.Offset(-r.Left, -r.Top);
        _marquee = null;
        _selected = null;
        SetBase(cropped);
        ShowStatus($"{r.Width}×{r.Height}로 잘랐어요 · Ctrl+Z로 되돌릴 수 있어요");
    }

    void SpotlightRegion()
    {
        if (RegionRect() is not { } r) return;
        PushUndo();
        var s = new SpotlightAnnotation { A = new(r.Left, r.Top), B = new(r.Right, r.Bottom) };
        _items.Add(s);
        _marquee = null;
        _selected = s;
        RebuildCommitted();
        ShowStatus("강조했어요 · 여러 곳을 강조할 수 있어요 · 테두리를 잡아 옮겨요");
    }

    /// <summary>클립보드의 그림을 붙인다: 영역이 있으면 그 자리, 없으면 가운데. 너무 크면 맞게 줄인다.</summary>
    void Paste()
    {
        var img = ClipboardService.GetImage(Handle);
        if (img == null) { ShowStatus("클립보드에 그림이 없어요"); return; }
        CommitText();
        float k = MathF.Min(1f, MathF.Min(_base.Width * 0.9f / img.Width, _base.Height * 0.9f / img.Height));
        float w = MathF.Round(img.Width * k), h = MathF.Round(img.Height * k);
        float x = MathF.Round((_base.Width - w) / 2), y = MathF.Round((_base.Height - h) / 2);
        if (_marquee is { } m) { x = m.X; y = m.Y; }
        PushUndo();
        var a = new ImageAnnotation { Image = img, A = new(x, y), B = new(x + w, y + h) };
        _items.Add(a);
        _selected = a;
        _marquee = null;
        RebuildCommitted();
        ShowStatus(k < 1 ? "붙여넣었어요 (크기에 맞게 줄였어요) · 끌어서 옮기세요" : "붙여넣었어요 · 끌어서 옮기고, 모서리로 크기를 바꿔요");
    }

    /// <summary>영역 바로 바깥 테두리 픽셀에서 가장 많은 색 (비슷한 색끼리 묶어서 평균).</summary>
    uint SurroundingColor(RECT r)
    {
        Gdi32.GdiFlush();
        var s = _committed!;
        var buckets = new Dictionary<uint, (long b, long g, long r, int n)>();
        void Sample(int x, int y)
        {
            if (x < 0 || y < 0 || x >= s.Width || y >= s.Height) return;
            uint c = *(uint*)(s.Bits + y * s.Stride + x * 4);
            uint key = c & 0x00F0F0F0;
            buckets.TryGetValue(key, out var v);
            buckets[key] = (v.b + (c & 0xFF), v.g + ((c >> 8) & 0xFF), v.r + ((c >> 16) & 0xFF), v.n + 1);
        }
        // 이미지 가장자리에 붙은 영역이면 바깥이 없으니 안쪽 첫 줄을 쓴다
        int left = r.Left > 0 ? r.Left - 1 : r.Left, right = r.Right < s.Width ? r.Right : r.Right - 1;
        int top = r.Top > 0 ? r.Top - 1 : r.Top, bottom = r.Bottom < s.Height ? r.Bottom : r.Bottom - 1;
        for (int x = left; x <= right; x++) { Sample(x, top); Sample(x, bottom); }
        for (int y = top + 1; y < bottom; y++) { Sample(left, y); Sample(right, y); }
        if (buckets.Count == 0) return 0xFFFFFFFF;
        var best = buckets.Values.MaxBy(v => v.n);
        return 0xFF000000 | (uint)(best.r / best.n) << 16 | (uint)(best.g / best.n) << 8 | (uint)(best.b / best.n);
    }

    // ───────────────────────── 그리기 ─────────────────────────

    /// <summary>영역 바깥을 어둡게 + 흰 테두리 + 크기 표시 + 아래에 동작 버튼 줄.</summary>
    void DrawMarquee(Gfx g)
    {
        _barRects.Clear();
        if (_marquee is not { } m || _barFont == null) return;
        float x = _imgX + m.X * _zoom, y = _imgY + m.Y * _zoom, w = m.Width * _zoom, h = m.Height * _zoom;
        float right = _imgX + _dispW, bottom = _imgY + _dispH;
        const uint Shade = 0x88000000;
        g.FillRect(Shade, _imgX, _imgY, _dispW, MathF.Max(0, y - _imgY));
        g.FillRect(Shade, _imgX, y + h, _dispW, MathF.Max(0, bottom - (y + h)));
        g.FillRect(Shade, _imgX, y, MathF.Max(0, x - _imgX), h);
        g.FillRect(Shade, x + w, y, MathF.Max(0, right - (x + w)), h);
        g.Rect(0x99000000, 3 * _scale, x, y, w, h);
        g.Rect(0xFFFFFFFF, 1.2f * _scale, x, y, w, h);

        float hs = 8 * _scale;
        foreach (var c in Corners(m))
        {
            float sx = _imgX + c.X * _zoom, sy = _imgY + c.Y * _zoom;
            g.FillRoundRect(0xFFFFFFFF, sx - hs / 2, sy - hs / 2, hs, hs, 2 * _scale);
            g.RoundRect(Toolbar.Accent, 1.5f * _scale, sx - hs / 2, sy - hs / 2, hs, hs, 2 * _scale);
        }

        // 크기 표시 (영역 왼쪽 위, 자리가 없으면 안쪽)
        string size = $"{(int)m.Width} × {(int)m.Height}";
        float lh = g.LineHeight(_barFont), pad = 6 * _scale;
        float lw = g.MeasureWidth(size, _barFont) + pad * 2, lbh = lh + pad * 0.6f;
        float ly = y - lbh - 6 * _scale;
        if (ly < _toolbar.Height + 2) ly = y + 6 * _scale;
        float lx = x + (ly > y ? 6 * _scale : 0);
        g.FillRoundRect(0xE0202024, lx, ly, lw, lbh, 5 * _scale);
        g.CenterText(size, _barFont, 0xFFECECF1, lx, ly, lw, lbh);

        // 영역을 그리는 중에는 버튼 줄을 숨긴다
        if (_dragging && _mode is DragMode.Marquee or DragMode.MarqueeMove) return;

        float bh = 32 * _scale, bpad = 12 * _scale, gap = 2 * _scale, inner = 4 * _scale;
        Span<float> widths = stackalloc float[BarActions.Length];
        float total = inner * 2 - gap;
        for (int i = 0; i < BarActions.Length; i++)
        {
            widths[i] = g.MeasureWidth(BarActions[i].Label, _barFont) + bpad * 2;
            total += widths[i] + gap;
        }
        float barH = bh + inner * 2;
        int canvasW = _clientW - PanelWidth;
        float bx = Math.Clamp(x, 4 * _scale, MathF.Max(4 * _scale, canvasW - total - 4 * _scale));
        float by = y + h + 8 * _scale;
        if (by + barH > _clientH - 4 * _scale) by = y - barH - 8 * _scale;               // 아래 자리가 없으면 위
        if (by < _toolbar.Height + 4 * _scale) by = y + h - barH - 8 * _scale;           // 위도 없으면 영역 안쪽 아래
        by = MathF.Max(by, _toolbar.Height + 4 * _scale);

        g.FillRoundRect(0x40000000, bx, by + 2 * _scale, total, barH, 10 * _scale);
        g.FillRoundRect(0xF62A2A30, bx, by, total, barH, 10 * _scale);
        float cx = bx + inner;
        for (int i = 0; i < BarActions.Length; i++)
        {
            var rect = new RECT((int)cx, (int)(by + inner), (int)(cx + widths[i]), (int)(by + inner + bh));
            _barRects.Add(rect);
            if (i == _barHover) g.FillRoundRect(0xFF3D3A5C, rect.Left, rect.Top, rect.Width, rect.Height, 7 * _scale);
            g.CenterText(BarActions[i].Label, _barFont, i == _barHover ? 0xFFFFFFFF : 0xFFE6E6EC, rect.Left, rect.Top, rect.Width, rect.Height);
            cx += widths[i] + gap;
        }
    }
}

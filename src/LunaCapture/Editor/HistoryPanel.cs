using LunaCapture.Graphics;
using LunaCapture.Interop;
using LunaCapture.Services;

namespace LunaCapture.Editor;

/// <summary>
/// 편집 창 오른쪽의 캡처 기록 목록 (세로 스크롤). 클릭하면 그 캡처로 바꿔서 편집한다.
/// 미리보기는 편집 창이 열려 있는 동안만 메모리에 있고, 창을 닫으면 해제된다.
/// </summary>
internal sealed unsafe class HistoryPanel : IDisposable
{
    const uint Bg = 0xFF202026, Border = 0xFF2E2E36, CellHover = 0xFF2C2C34, Accent = 0xFF8B7CF6;
    const uint TextMain = 0xFFECECF1, TextSub = 0xFF9A9AA6;

    List<HistoryEntry> _entries = new();
    readonly Dictionary<string, GdiSurface> _thumbs = new();
    readonly List<RECT> _cells = new();   // 스크롤 전 좌표
    RECT _area;
    float _s = 1f;
    int _scroll, _contentH, _hover = -1;
    GdiFont? _title, _small, _badge;

    public string? CurrentId;
    public static int BaseWidth => 210;

    public void SetScale(float scale)
    {
        _s = scale;
        _title?.Dispose(); _small?.Dispose(); _badge?.Dispose();
        _title = new GdiFont(UiFonts.Family, 13 * scale, true);
        _small = new GdiFont(UiFonts.Family, 11 * scale, false);
        _badge = new GdiFont(UiFonts.Family, 10 * scale, true);
    }

    int Px(float v) => (int)MathF.Round(v * _s);
    int HeaderH => Px(36);

    public void Load()
    {
        foreach (var t in _thumbs.Values) t.Dispose();
        _thumbs.Clear();
        _entries = HistoryStore.LoadAll();
        foreach (var e in _entries) LoadThumb(e.Id);
        Relayout();
    }

    /// <summary>편집이 저장된 뒤 그 항목의 미리보기만 다시 읽는다.</summary>
    public void RefreshThumb(string id)
    {
        if (_thumbs.Remove(id, out var old)) old.Dispose();
        LoadThumb(id);
        var fresh = HistoryStore.LoadAll();
        _entries = fresh;
        Relayout();
    }

    void LoadThumb(string id)
    {
        var img = GdiPlus.LoadImage(HistoryStore.Thumb(id));
        if (img == null) return;
        var surf = new GdiSurface(img.Width, img.Height);
        fixed (byte* p = img.Bgra) Buffer.MemoryCopy(p, surf.Bits, img.Bgra.Length, img.Bgra.Length);
        _thumbs[id] = surf;
    }

    public void Layout(RECT area)
    {
        _area = area;
        Relayout();
    }

    /// <summary>미리보기 높이는 사진 비율에 맞춘다 (너무 길거나 납작하지 않게 제한).</summary>
    void Relayout()
    {
        _cells.Clear();
        int pad = Px(10), w = Math.Max(1, _area.Width - pad * 2);
        int y = HeaderH + Px(4);
        foreach (var e in _entries)
        {
            float aspect = e.Width > 0 ? e.Height / (float)e.Width : 0.6f;
            // 최소 높이가 최대보다 커지지 않게 (패널 폭이 아직 정해지기 전에도 안전하게)
            float minH = Px(50), maxH = MathF.Max(minH, w * 0.85f);
            int th = (int)Math.Clamp(w * aspect, minH, maxH);
            int h = th + Px(30);
            _cells.Add(new RECT(pad, y, pad + w, y + h));
            y += h + Px(8);
        }
        _contentH = y;
        Scroll(0);
    }

    public bool Contains(int x, int y) => x >= _area.Left && x < _area.Right && y >= _area.Top && y < _area.Bottom;

    public void Scroll(int dy)
    {
        int max = Math.Max(0, _contentH - _area.Height);
        _scroll = Math.Clamp(_scroll + dy, 0, max);
    }

    RECT ScreenCell(int i)
    {
        var c = _cells[i];
        int ox = _area.Left, oy = _area.Top - _scroll;
        return new RECT(c.Left + ox, c.Top + oy, c.Right + ox, c.Bottom + oy);
    }

    public int Hit(int x, int y)
    {
        if (!Contains(x, y) || y < _area.Top + HeaderH) return -1;
        for (int i = 0; i < _cells.Count; i++)
        {
            var r = ScreenCell(i);
            if (x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom) return i;
        }
        return -1;
    }

    public HistoryEntry? EntryAt(int index) => index >= 0 && index < _entries.Count ? _entries[index] : null;

    /// <returns>호버 항목이 바뀌었는지 (다시 그려야 하는지)</returns>
    public bool SetHover(int x, int y)
    {
        int h = Hit(x, y);
        if (h == _hover) return false;
        _hover = h;
        return true;
    }

    /// <summary>지금 편집 중인 항목이 보이도록 스크롤.</summary>
    public void RevealCurrent()
    {
        int i = _entries.FindIndex(e => e.Id == CurrentId);
        if (i < 0) return;
        var c = _cells[i];
        if (c.Top - _scroll < HeaderH) Scroll(c.Top - HeaderH - Px(4) - _scroll);
        else if (c.Bottom - _scroll > _area.Height) Scroll(c.Bottom - _area.Height + Px(8) - _scroll);
    }

    static string When(DateTime t)
    {
        var today = DateTime.Today;
        if (t.Date == today) return $"오늘 {t:HH:mm}";
        if (t.Date == today.AddDays(-1)) return $"어제 {t:HH:mm}";
        return $"{t.Month}/{t.Day} {t:HH:mm}";
    }

    public void Draw(nint dc)
    {
        var a = _area;
        if (a.Width <= 0 || a.Height <= 0) return;

        // 패널 영역 밖으로 그림이 나가지 않게 잘라서 그린다
        nint clip = Gdi32.CreateRectRgn(a.Left, a.Top, a.Right, a.Bottom);
        SelectClipRgn(dc, clip);

        using (var g = new Gfx(dc))
        {
            g.FillRect(Bg, a.Left, a.Top, a.Width, a.Height);
            g.FillRect(Border, a.Left, a.Top, 1, a.Height);
            if (_entries.Count == 0)
                g.CenterText("기록이 없어요", _small!, TextSub, a.Left, a.Top + HeaderH, a.Width, Px(40));

            for (int i = 0; i < _entries.Count; i++)
            {
                var r = ScreenCell(i);
                if (r.Bottom < a.Top + HeaderH || r.Top > a.Bottom) continue;
                var e = _entries[i];
                bool current = e.Id == CurrentId;
                if (i == _hover || current) g.FillRoundRect(CellHover, r.Left - Px(4), r.Top - Px(4), r.Width + Px(8), r.Height + Px(4), Px(8));
                if (current) g.RoundRect(Accent, Px(2), r.Left - Px(4), r.Top - Px(4), r.Width + Px(8), r.Height + Px(4), Px(8));

                float ty = r.Bottom - Px(22);
                g.Text(When(e.Created), _small!, current ? TextMain : TextSub, r.Left, ty);
                if (e.Kind != "image")
                {
                    string badge = e.Kind == "gif" ? "GIF" : "MP4";
                    float bw = g.MeasureWidth(badge, _badge!) + Px(10), bh = Px(16);
                    g.FillRoundRect(e.Kind == "gif" ? 0xFFE5484D : 0xFF0090FF, r.Right - bw, ty, bw, bh, Px(4));
                    g.CenterText(badge, _badge!, 0xFFFFFFFF, r.Right - bw, ty, bw, bh);
                }
                else if (e.Items.Count > 0)
                {
                    string n = $"편집 {e.Items.Count}";
                    g.Text(n, _small!, TextSub, r.Right - g.MeasureWidth(n, _small!), ty);
                }
            }
        }

        // 미리보기 그림
        Gdi32.SetStretchBltMode(dc, Win.HALFTONE);
        Gdi32.SetBrushOrgEx(dc, 0, 0, null);
        for (int i = 0; i < _entries.Count; i++)
        {
            var r = ScreenCell(i);
            if (r.Bottom < a.Top + HeaderH || r.Top > a.Bottom) continue;
            if (!_thumbs.TryGetValue(_entries[i].Id, out var t)) continue;
            int areaH = r.Height - Px(30);
            float k = MathF.Min(r.Width / (float)t.Width, areaH / (float)t.Height);
            int w = (int)(t.Width * k), h = (int)(t.Height * k);
            Gdi32.StretchBlt(dc, r.Left + (r.Width - w) / 2, r.Top + (areaH - h) / 2, w, h, t.Dc, 0, 0, t.Width, t.Height, Win.SRCCOPY);
        }

        using (var g = new Gfx(dc))
        {
            // 머리글 (스크롤해도 위에 고정)
            g.FillRect(Bg, a.Left + 1, a.Top, a.Width - 1, HeaderH);
            g.Text("캡처 기록", _title!, TextMain, a.Left + Px(12), a.Top + (HeaderH - g.LineHeight(_title!)) / 2);
            string count = $"{_entries.Count}";
            g.Text(count, _small!, TextSub, a.Right - Px(12) - g.MeasureWidth(count, _small!), a.Top + (HeaderH - g.LineHeight(_small!)) / 2);
            if (_contentH > a.Height)
            {
                int view = a.Height;
                float barH = MathF.Max(Px(24), view * view / (float)_contentH);
                float barY = a.Top + (view - barH) * _scroll / (float)Math.Max(1, _contentH - view);
                g.FillRoundRect(0x55FFFFFF, a.Right - Px(5), barY, Px(3), barH, Px(1.5f));
            }
        }

        SelectClipRgn(dc, 0);
        Gdi32.DeleteObject(clip);
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    static extern int SelectClipRgn(nint hdc, nint rgn);

    public void Dispose()
    {
        foreach (var t in _thumbs.Values) t.Dispose();
        _thumbs.Clear();
        _title?.Dispose(); _small?.Dispose(); _badge?.Dispose();
        _title = _small = _badge = null;
    }
}

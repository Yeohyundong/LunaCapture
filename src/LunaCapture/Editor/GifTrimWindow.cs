using LunaCapture.Gif;
using LunaCapture.Graphics;
using LunaCapture.Interop;

namespace LunaCapture.Editor;

/// <summary>
/// 완성된 GIF의 앞뒤를 잘라내는 창.
/// - 미리보기 디코딩은 별도 스레드에서 하고, 가장 최근 요청만 처리한다(타임라인을 끌어도 버벅이지 않게).
/// - 중간 상태(체크포인트)를 메모리 예산 안에서 몇 장 저장해 두어 되감기를 빠르게 한다.
/// - 저장할 때는 시작 프레임만 다시 압축하고 나머지는 원본 바이트를 그대로 복사한다(빠르고 화질 손실 없음).
/// </summary>
internal sealed unsafe class GifTrimWindow : NativeWindow
{
    const uint MsgFrameReady = Win.WM_APP + 10;
    const nuint PlayTimer = 1;
    const long CheckpointBudget = 48L * 1024 * 1024;

    const uint Bg = 0xFF1C1C20, PanelBg = 0xFF232329, Track = 0xFF34343E, Accent = 0xFF8B7CF6, AccentHover = 0xFF9D90FF;
    const uint TextMain = 0xFFECECF1, TextSub = 0xFF9A9AA6, ItemBg = 0xFF2F2F37, ItemHover = 0xFF3B3B45;

    readonly string _path;
    readonly Action<bool> _onClosed; // true = 잘라서 저장함
    GifReader? _gif;

    // 디코딩 스레드 소유
    uint[] _canvas;
    readonly Dictionary<int, uint[]> _checkpoints = new();
    readonly int _checkpointEvery;
    Thread? _worker;
    readonly AutoResetEvent _wake = new(false);
    volatile int _requested;
    volatile bool _stopWorker;

    // 화면 표시용 (UI 스레드 ↔ 디코딩 스레드 공유)
    readonly object _displayLock = new();
    GdiSurface? _display;
    volatile int _displayedIndex = -1;

    // UI 상태
    int _start, _end, _playhead;
    bool _playing;
    enum Drag { None, Start, End, Playhead }
    Drag _drag;
    string? _hover;
    readonly List<(string id, RECT r, Action act)> _buttons = new();

    float _s = 1f;
    int _cw, _ch;
    RECT _preview, _track;
    GdiSurface? _back;
    GdiFont? _body, _small, _mono;
    string _status = "";

    public GifTrimWindow(string path, Action<bool> onClosed)
    {
        _path = path;
        _onClosed = onClosed;
        _gif = new GifReader(path);
        _canvas = new uint[_gif.Width * _gif.Height];
        _display = new GdiSurface(_gif.Width, _gif.Height);

        long frameBytes = (long)_gif.Width * _gif.Height * 4;
        int maxCheckpoints = (int)Math.Max(1, CheckpointBudget / frameBytes);
        _checkpointEvery = Math.Max(4, (_gif.Frames.Count + maxCheckpoints - 1) / maxCheckpoints);

        _start = 0;
        _end = _gif.Frames.Count - 1;

        POINT cursor;
        User32.GetCursorPos(&cursor);
        var mi = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        User32.GetMonitorInfoW(User32.MonitorFromPoint(cursor, Win.MONITOR_DEFAULTTONEAREST), &mi);
        var work = mi.rcWork;

        const uint style = 0x00C00000 | 0x00080000 | 0x00020000; // CAPTION | SYSMENU | MINIMIZEBOX
        CreateHandle(0, style, work.Left, work.Top, 200, 200,
            $"LunaCapture GIF 자르기 — {_gif.Width}×{_gif.Height} · {FormatTime(_gif.TotalCs)} · {FormatSize(_gif.FileLength)}");
        int dark = 1;
        Dwmapi.DwmSetWindowAttribute(Handle, Dwmapi.DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, sizeof(int));
        _s = User32.GetDpiForWindow(Handle) / 96f;
        _body = new GdiFont(UiFonts.Family, 14 * _s, false);
        _small = new GdiFont(UiFonts.Family, 13 * _s, false);
        _mono = new GdiFont(UiFonts.Family, 14 * _s, false); // 시간 표시: 숫자 폭 고정

        // 미리보기 크기: 화면의 70% 안, 원본보다 크게는 안 함
        int maxW = (int)(work.Width * 0.7), maxH = (int)(work.Height * 0.7) - Px(120);
        float zoom = MathF.Min(1f, MathF.Min(maxW / (float)_gif.Width, maxH / (float)_gif.Height));
        int pw = Math.Max(Px(560), (int)(_gif.Width * zoom) + Px(32));
        int ph = (int)(_gif.Height * zoom) + Px(32) + Px(112);
        var r = new RECT(0, 0, pw, ph);
        User32.AdjustWindowRectExForDpi(&r, style, 0, 0, User32.GetDpiForWindow(Handle));
        User32.SetWindowPos(Handle, 0, work.Left + (work.Width - r.Width) / 2, work.Top + (work.Height - r.Height) / 2,
            r.Width, r.Height, Win.SWP_NOZORDER | Win.SWP_NOACTIVATE);

        _worker = new Thread(WorkerLoop) { Name = "GIF preview", IsBackground = true };
        _worker.Start();
        Request(0);

        User32.ShowWindow(Handle, Win.SW_SHOW);
        User32.ForceForeground(Handle);
    }

    int Px(float v) => (int)MathF.Round(v * _s);
    int FrameCount => _gif!.Frames.Count;

    // ───────────────────────── 디코딩 스레드 ─────────────────────────

    void Request(int index)
    {
        _requested = Math.Clamp(index, 0, FrameCount - 1);
        _wake.Set();
    }

    void WorkerLoop()
    {
        var gif = _gif!;
        fixed (uint* canvas = _canvas)
        {
            gif.Reset(canvas);
            while (!_stopWorker)
            {
                _wake.WaitOne();
                if (_stopWorker) break;
                try
                {
                    SeekTo(gif, canvas);
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                }
            }
        }
    }

    /// <summary>요청된 프레임까지 디코딩해서 표시용 서피스에 복사. 도중에 요청이 바뀌면 바로 따라간다.</summary>
    void SeekTo(GifReader gif, uint* canvas)
    {
        while (!_stopWorker)
        {
            int target = _requested;
            int cur = gif.DecodedIndex;

            // 뒤로 가야 하거나, 가까운 체크포인트가 더 앞서 있으면 거기서 시작
            int best = -1;
            foreach (var k in _checkpoints.Keys)
                if (k <= target && k > best) best = k;
            if (target < cur || best > cur)
            {
                if (best >= 0)
                {
                    fixed (uint* cp = _checkpoints[best]) Buffer.MemoryCopy(cp, canvas, _canvas.Length * 4L, _canvas.Length * 4L);
                    gif.RestoreState(best);
                }
                else
                {
                    gif.Reset(canvas);
                }
            }

            while (gif.DecodedIndex < target && _requested == target && !_stopWorker)
            {
                gif.DecodeNext(canvas);
                int idx = gif.DecodedIndex;
                if (idx % _checkpointEvery == 0 && !_checkpoints.ContainsKey(idx))
                    _checkpoints[idx] = (uint[])_canvas.Clone();
            }

            if (_requested != target) continue; // 그새 다른 곳을 요청함

            lock (_displayLock)
            {
                if (_display != null)
                    Buffer.MemoryCopy(canvas, _display.Bits, _canvas.Length * 4L, _canvas.Length * 4L);
            }
            _displayedIndex = target;
            User32.PostMessageW(Handle, MsgFrameReady, target, 0);
            return;
        }
    }

    // ───────────────────────── 메시지 ─────────────────────────

    protected override nint WndProc(uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case Win.WM_ERASEBKGND:
                return 1;

            case Win.WM_PAINT:
                Paint();
                return 0;

            case Win.WM_SIZE:
                _cw = Win.LoWord(lParam) & 0xFFFF;
                _ch = Win.HiWord(lParam) & 0xFFFF;
                Layout();
                return 0;

            case MsgFrameReady:
                Invalidate();
                if (_playing) ScheduleNextFrame((int)wParam);
                return 0;

            case Win.WM_TIMER:
                if (wParam == (nint)PlayTimer)
                {
                    User32.KillTimer(Handle, PlayTimer);
                    if (_playing)
                    {
                        _playhead = _playhead >= _end ? _start : _playhead + 1;
                        Request(_playhead);
                    }
                }
                return 0;

            case Win.WM_SETCURSOR:
                if (Win.LoWord(lParam) == Win.HTCLIENT)
                {
                    POINT p;
                    User32.GetCursorPos(&p);
                    User32.ScreenToClient(Handle, &p);
                    bool hand = HitButton(p.X, p.Y) != null || HitHandle(p.X, p.Y) != Drag.None;
                    User32.SetCursor(User32.LoadCursorW(0, hand ? Win.IDC_HAND : Win.IDC_ARROW));
                    return 1;
                }
                break;

            case Win.WM_MOUSEMOVE:
                OnMouseMove(Win.LoWord(lParam), Win.HiWord(lParam));
                return 0;

            case Win.WM_LBUTTONDOWN:
                OnMouseDown(Win.LoWord(lParam), Win.HiWord(lParam));
                return 0;

            case Win.WM_LBUTTONUP:
                if (_drag != Drag.None) { _drag = Drag.None; User32.ReleaseCapture(); Invalidate(); }
                else if (HitButton(Win.LoWord(lParam), Win.HiWord(lParam)) is { } b) b.act();
                return 0;

            case Win.WM_KEYDOWN:
                OnKey((int)wParam);
                return 0;

            case Win.WM_CLOSE:
                Close(saved: false);
                return 0;

            case Win.WM_DESTROY:
                Shutdown();
                return 0;
        }
        return base.WndProc(msg, wParam, lParam);
    }

    void ScheduleNextFrame(int shown)
    {
        int delay = _gif!.Frames[shown].DelayCs;
        User32.SetTimer(Handle, PlayTimer, (uint)Math.Max(20, (delay < 2 ? 10 : delay) * 10), 0);
    }

    void OnKey(int vk)
    {
        switch (vk)
        {
            case 0x20: TogglePlay(); break;                                   // Space
            case 0x25: SetPlayhead(_playhead - 1); break;                     // ←
            case 0x27: SetPlayhead(_playhead + 1); break;                     // →
            case 0x24: SetPlayhead(_start); break;                            // Home
            case 0x23: SetPlayhead(_end); break;                              // End
            case 0xDB: _start = Math.Min(_playhead, _end); Invalidate(); break; // [
            case 0xDD: _end = Math.Max(_playhead, _start); Invalidate(); break; // ]
            case Win.VK_RETURN: Save(); break;
            case Win.VK_ESCAPE: Close(saved: false); break;
        }
    }

    void OnMouseDown(int x, int y)
    {
        if (HitButton(x, y) != null) return;
        var h = HitHandle(x, y);
        if (h != Drag.None)
        {
            _drag = h;
            User32.SetCapture(Handle);
            return;
        }
        if (y >= _track.Top - Px(10) && y <= _track.Bottom + Px(10))
        {
            _drag = Drag.Playhead;
            User32.SetCapture(Handle);
            OnMouseMove(x, y);
        }
    }

    void OnMouseMove(int x, int y)
    {
        string? hover = HitButton(x, y)?.id;
        if (hover != _hover) { _hover = hover; Invalidate(); }

        if (_drag == Drag.None) return;
        int f = FrameAtX(x);
        switch (_drag)
        {
            case Drag.Start:
                _start = Math.Min(f, _end);
                SetPlayhead(_start);
                break;
            case Drag.End:
                _end = Math.Max(f, _start);
                SetPlayhead(_end);
                break;
            case Drag.Playhead:
                SetPlayhead(f);
                break;
        }
    }

    void SetPlayhead(int frame)
    {
        _playhead = Math.Clamp(frame, 0, FrameCount - 1);
        Request(_playhead);
        Invalidate();
    }

    void TogglePlay()
    {
        _playing = !_playing;
        if (_playing)
        {
            if (_playhead < _start || _playhead >= _end) _playhead = _start;
            Request(_playhead);
        }
        else
        {
            User32.KillTimer(Handle, PlayTimer);
        }
        Invalidate();
    }

    // ───────────────────────── 타임라인 좌표 ─────────────────────────

    float XAtCs(int cs) => _track.Left + _track.Width * (cs / (float)Math.Max(1, _gif!.TotalCs));

    float XOfFrameStart(int f) => XAtCs(_gif!.Frames[f].StartCs);

    float XOfFrameEnd(int f)
    {
        var fr = _gif!.Frames[f];
        return XAtCs(fr.StartCs + (fr.DelayCs < 2 ? 10 : fr.DelayCs));
    }

    int FrameAtX(int x)
    {
        int cs = (int)((x - _track.Left) / (float)Math.Max(1, _track.Width) * _gif!.TotalCs);
        var frames = _gif.Frames;
        int lo = 0, hi = frames.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (frames[mid].StartCs <= cs) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    Drag HitHandle(int x, int y)
    {
        if (y < _track.Top - Px(14) || y > _track.Bottom + Px(14)) return Drag.None;
        float sx = XOfFrameStart(_start), ex = XOfFrameEnd(_end);
        if (Math.Abs(x - sx) <= Px(9)) return Drag.Start;
        if (Math.Abs(x - ex) <= Px(9)) return Drag.End;
        return Drag.None;
    }

    (string id, RECT r, Action act)? HitButton(int x, int y)
    {
        foreach (var b in _buttons)
            if (x >= b.r.Left && x < b.r.Right && y >= b.r.Top && y < b.r.Bottom) return b;
        return null;
    }

    // ───────────────────────── 그리기 ─────────────────────────

    void Layout()
    {
        if (_cw <= 0 || _ch <= 0) return;
        if (_back == null || _back.Width != _cw || _back.Height != _ch)
        {
            _back?.Dispose();
            _back = new GdiSurface(_cw, _ch);
        }
        int panelH = Px(112), pad = Px(16);
        int availW = _cw - pad * 2, availH = _ch - panelH - pad * 2;
        float zoom = MathF.Min(1f, MathF.Min(availW / (float)_gif!.Width, availH / (float)_gif.Height));
        int pw = Math.Max(1, (int)(_gif.Width * zoom)), ph = Math.Max(1, (int)(_gif.Height * zoom));
        int px = (_cw - pw) / 2, py = pad + (availH - ph) / 2;
        _preview = new RECT(px, py, px + pw, py + ph);

        int ty = _ch - panelH + Px(22);
        _track = new RECT(pad + Px(8), ty, _cw - pad - Px(8), ty + Px(8));
        Invalidate();
    }

    void Paint()
    {
        PAINTSTRUCT ps;
        nint hdc = User32.BeginPaint(Handle, &ps);
        if (_back == null || _gif == null) { User32.EndPaint(Handle, &ps); return; }

        nint dc = _back.Dc;
        int panelTop = _ch - Px(112);
        using (var g = new Gfx(dc))
        {
            g.FillRect(Bg, 0, 0, _cw, _ch);
            g.FillRect(PanelBg, 0, panelTop, _cw, _ch - panelTop);
            g.FillRect(0xFF3A3A44, _preview.Left - 1, _preview.Top - 1, _preview.Width + 2, _preview.Height + 2);
        }

        lock (_displayLock)
        {
            if (_display != null)
            {
                Gdi32.SetStretchBltMode(dc, Win.HALFTONE);
                Gdi32.SetBrushOrgEx(dc, 0, 0, null);
                Gdi32.StretchBlt(dc, _preview.Left, _preview.Top, _preview.Width, _preview.Height,
                    _display.Dc, 0, 0, _display.Width, _display.Height, Win.SRCCOPY);
            }
        }

        using (var g = new Gfx(dc))
        {
            DrawTimeline(g);
            DrawControls(g, panelTop);
        }

        Gdi32.BitBlt(hdc, 0, 0, _cw, _ch, dc, 0, 0, Win.SRCCOPY);
        User32.EndPaint(Handle, &ps);
    }

    void DrawTimeline(Gfx g)
    {
        var t = _track;
        g.FillRoundRect(Track, t.Left, t.Top, t.Width, t.Height, t.Height / 2f);

        float sx = XOfFrameStart(_start), ex = XOfFrameEnd(_end);
        g.FillRect(Accent, sx, t.Top, Math.Max(2, ex - sx), t.Height);

        // 잘려 나갈 부분 표시
        g.FillRect(0x66000000, t.Left, t.Top, sx - t.Left, t.Height);
        g.FillRect(0x66000000, ex, t.Top, t.Right - ex, t.Height);

        // 손잡이
        float hw = Px(6), hh = Px(22);
        foreach (var (x, id) in new[] { (sx, "start"), (ex, "end") })
        {
            g.FillRoundRect(0xFFFFFFFF, x - hw / 2, t.Top + t.Height / 2f - hh / 2, hw, hh, hw / 2);
        }

        // 재생 위치
        float px = XOfFrameStart(_playhead);
        g.FillRect(0xFFFFC53D, px - 1, t.Top - Px(7), 2, t.Height + Px(14));
    }

    void DrawControls(Gfx g, int panelTop)
    {
        _buttons.Clear();
        int pad = Px(16), bh = Px(32), y = _ch - pad - bh;

        var play = new RECT(pad, y, pad + bh, y + bh);
        Button(g, "play", play, "", TogglePlay, icon: _playing ? IconKind.Pause : IconKind.Play);

        var gif = _gif!;
        int selCs = gif.Frames[_end].StartCs + Delay(_end) - gif.Frames[_start].StartCs;
        string info = $"{FormatTime(gif.Frames[_playhead].StartCs)}   ·   선택 {FormatTime(gif.Frames[_start].StartCs)} – {FormatTime(gif.Frames[_end].StartCs + Delay(_end))}  ({FormatTime(selCs)}, 약 {FormatSize(EstimateSize())})";
        g.TextTabular(info, _mono!, TextMain, play.Right + Px(12), y + (bh - g.LineHeight(_mono!)) / 2);

        var save = new RECT(_cw - pad - Px(110), y, _cw - pad, y + bh);
        var cancel = new RECT(save.Left - Px(8) - Px(80), y, save.Left - Px(8), y + bh);
        Button(g, "cancel", cancel, "닫기", () => Close(saved: false));
        bool trimmed = _start > 0 || _end < FrameCount - 1;
        Button(g, "save", save, "잘라서 저장", Save, primary: true, enabled: trimmed);

        string hint = _status.Length > 0 ? _status : "손잡이를 끌어 범위 선택 · Space 재생 · ←→ 한 장씩 · [ ] 현재 위치를 시작/끝으로";
        g.Text(hint, _small!, _status.Length > 0 ? 0xFF4CC38A : TextSub, pad, panelTop + Px(42));
    }

    int Delay(int f) => _gif!.Frames[f].DelayCs < 2 ? 10 : _gif.Frames[f].DelayCs;

    long EstimateSize()
    {
        var gif = _gif!;
        // 시작 프레임은 전체 화면으로 다시 압축(대략 첫 프레임 크기), 나머지는 원본 그대로
        long first = gif.Frames[0].End - gif.Frames[0].Start;
        long rest = _end > _start ? gif.Frames[_end].End - gif.Frames[_start + 1].Start : 0;
        return gif.HeaderEnd + first + rest + 1;
    }

    void Button(Gfx g, string id, RECT r, string text, Action act, bool primary = false, bool enabled = true, IconKind icon = IconKind.None)
    {
        if (enabled) _buttons.Add((id, r, act));
        uint bg = primary ? (enabled ? (_hover == id ? AccentHover : Accent) : ItemBg) : (_hover == id ? ItemHover : ItemBg);
        g.FillRoundRect(bg, r.Left, r.Top, r.Width, r.Height, Px(6));
        uint fg = enabled ? (primary ? 0xFFFFFFFF : TextMain) : TextSub;
        if (icon != IconKind.None) Icons.Draw(g, icon, r.Left + r.Width / 2f, r.Top + r.Height / 2f, Px(12), fg);
        else g.CenterText(text, _body!, fg, r.Left, r.Top, r.Width, r.Height);
    }

    void Invalidate()
    {
        if (Handle != 0) User32.InvalidateRect(Handle, null, 0);
    }

    static string FormatTime(int cs) => $"{cs / 6000:00}:{cs / 100 % 60:00}.{cs / 10 % 10}";

    static string FormatSize(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1048576.0:0.0}MB" : $"{bytes / 1024}KB";

    // ───────────────────────── 저장 ─────────────────────────

    void Save()
    {
        if (_start == 0 && _end == FrameCount - 1) return;
        _playing = false;
        StopWorker();

        string temp = _path + ".trim.tmp";
        try
        {
            var gif = _gif!;
            fixed (uint* canvas = _canvas)
            {
                // 시작 프레임까지 합성 (체크포인트 활용)
                int best = -1;
                foreach (var k in _checkpoints.Keys) if (k <= _start && k > best) best = k;
                if (gif.DecodedIndex > _start || best > gif.DecodedIndex)
                {
                    if (best >= 0)
                    {
                        fixed (uint* cp = _checkpoints[best]) Buffer.MemoryCopy(cp, canvas, _canvas.Length * 4L, _canvas.Length * 4L);
                        gif.RestoreState(best);
                    }
                    else gif.Reset(canvas);
                }
                while (gif.DecodedIndex < _start) gif.DecodeNext(canvas);

                using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                {
                    fs.Write(gif.Bytes(0, gif.HeaderEnd));

                    // 첫 프레임: 합성된 화면 전체를 새로 압축 (투명 없음)
                    var enc = new FrameEncoder();
                    enc.Encode(canvas, null, gif.Width, gif.Height);
                    int delay = gif.Frames[_start].DelayCs;
                    fs.Write([0x21, 0xF9, 0x04, 1 << 2, (byte)delay, (byte)(delay >> 8), 0, 0]);
                    fs.Write(enc.Output.GetBuffer(), 0, (int)enc.Output.Length);

                    // 나머지: 원본 바이트 그대로
                    if (_end > _start)
                        fs.Write(gif.Bytes(gif.Frames[_start + 1].Start, gif.Frames[_end].End));
                    fs.WriteByte(0x3B);
                }
            }

            gif.Dispose();
            _gif = null;
            File.Move(temp, _path, overwrite: true);
            Close(saved: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            try { File.Delete(temp); } catch { }
            _status = "저장 실패: " + ex.Message;
            if (_gif != null) StartWorkerAgain();
            Invalidate();
        }
    }

    void StartWorkerAgain()
    {
        _stopWorker = false;
        _worker = new Thread(WorkerLoop) { Name = "GIF preview", IsBackground = true };
        _worker.Start();
        Request(_playhead);
    }

    bool _closed;

    void Close(bool saved)
    {
        if (_closed) return;
        _closed = true;
        Destroy();
        _onClosed(saved);
    }

    void StopWorker()
    {
        _stopWorker = true;
        _wake.Set();
        _worker?.Join();
        _worker = null;
    }

    void Shutdown()
    {
        User32.KillTimer(Handle, PlayTimer);
        StopWorker();
        _gif?.Dispose();
        _gif = null;
        lock (_displayLock)
        {
            _display?.Dispose();
            _display = null;
        }
        _back?.Dispose();
        _back = null;
        _checkpoints.Clear();
        _canvas = [];
        _body?.Dispose(); _small?.Dispose(); _mono?.Dispose();
    }
}

using LunaCapture.Interop;

namespace LunaCapture.Recording;

/// <summary>
/// 녹화 중 떠 있는 작은 바: 경과 시간 · 용량 게이지(목표 대비) · 남은 시간 · 일시정지/정지/취소.
/// 녹화 영상에는 찍히지 않는다. 포커스를 뺏지 않아 녹화 중인 프로그램을 계속 조작할 수 있다.
/// </summary>
internal sealed class RecordingBar : PanelWindow
{
    const nuint TimerId = 1;

    readonly ScreenRecorder _rec;
    readonly Action _onStop, _onCancel;
    readonly GdiFont _body, _mono, _small;
    bool _done;
    int _tick;

    public RecordingBar(RECT region, ScreenRecorder recorder, Action onStop, Action onCancel)
    {
        _rec = recorder;
        _onStop = onStop;
        _onCancel = onCancel;

        CreatePanel(region.Left, region.Top, noActivate: true);
        _body = new GdiFont(UiFonts.Family, 14 * S, false);
        _mono = new GdiFont(UiFonts.Family, 15 * S, true); // 시간: 숫자 폭을 고정해서 그림
        _small = new GdiFont(UiFonts.Family, 13 * S, false);

        int w = Px(recorder.LimitBytes > 0 ? 560 : 400), h = Px(48);
        var (x, y) = PanelPlacement.Near(region, w, h, Px(10));
        SetBounds(x, y, w, h);
        User32.ShowWindow(Handle, 4 /* SW_SHOWNOACTIVATE */);
        User32.SetTimer(Handle, TimerId, 100, 0);
    }

    protected override void Render(Gfx g)
    {
        int h = Height, pad = Px(14);
        int x = pad;

        // 빨간 점 (일시정지면 회색, 녹화 중이면 깜빡임)
        float d = Px(10);
        uint dot = _rec.Paused ? TextSub : (_tick / 5 % 2 == 0 ? RecRed : 0xFF7A2A2D);
        g.FillEllipse(dot, x, (h - d) / 2, d, d);
        x += Px(18);

        long ms = _rec.ElapsedMs;
        string time = $"{ms / 60000:00}:{ms / 1000 % 60:00}.{ms / 100 % 10}";
        x += (int)g.TextTabular(time, _mono, TextMain, x, (h - g.LineHeight(_mono)) / 2) + Px(16);

        long bytes = _rec.Bytes;
        if (_rec.LimitBytes > 0)
        {
            // 용량 게이지
            int gw = Px(120), gh = Px(6);
            float ratio = Math.Clamp(bytes / (float)_rec.LimitBytes, 0, 1);
            uint fill = ratio < 0.75f ? Accent : ratio < 0.92f ? 0xFFF5A524 : RecRed;
            g.FillRoundRect(ItemBg, x, (h - gh) / 2f, gw, gh, gh / 2f);
            if (ratio > 0) g.FillRoundRect(fill, x, (h - gh) / 2f, Math.Max(gh, gw * ratio), gh, gh / 2f);
            x += gw + Px(10);

            string size = $"{Mb(bytes)} / {Mb(_rec.LimitBytes)}";
            x += (int)g.TextTabular(size, _small, TextMain, x, (h - g.LineHeight(_small)) / 2) + Px(10);

            // 지금까지의 실제 속도로 남은 시간 계산
            if (ms > 1500 && bytes > 0)
            {
                double perMs = bytes / (double)ms;
                double remain = (_rec.LimitBytes - bytes) / perMs / 1000;
                g.TextTabular($"약 {Math.Max(0, remain):0}초 남음", _small, TextSub, x, (h - g.LineHeight(_small)) / 2);
            }
        }
        else
        {
            g.TextTabular(Mb(bytes), _small, TextMain, x, (h - g.LineHeight(_small)) / 2);
        }

        // 오른쪽 버튼들
        int bh = Px(30), by = (h - bh) / 2;
        int right = Width - Px(9);
        var cancel = new RECT(right - Px(34), by, right, by + bh);
        var stop = new RECT(cancel.Left - Px(6) - Px(72), by, cancel.Left - Px(6), by + bh);
        var pause = new RECT(stop.Left - Px(6) - Px(34), by, stop.Left - Px(6), by + bh);

        Button(g, "pause", pause, "", _small, () => { _rec.TogglePause(); Invalidate(); }, icon: _rec.Paused ? IconKind.Play : IconKind.Pause);
        Button(g, "stop", stop, "정지", _body, Stop, primary: true, icon: IconKind.Stop);
        Button(g, "cancel", cancel, "", _body, Cancel, icon: IconKind.Close);
    }

    static string Mb(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1048576.0:0.0}MB" : $"{bytes / 1024}KB";

    public void Stop()
    {
        if (_done) return;
        _done = true;
        Destroy();
        _onStop();
    }

    void Cancel()
    {
        if (_done) return;
        _done = true;
        Destroy();
        _onCancel();
    }

    protected override nint WndProc(uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case Win.WM_TIMER:
                _tick++;
                if (_rec.HitLimit || _rec.Failed) Stop(); // 목표 용량 도달 → 자동 정지
                else Invalidate();
                return 0;
            case Win.WM_DESTROY:
                User32.KillTimer(Handle, TimerId);
                _body.Dispose(); _mono.Dispose(); _small.Dispose();
                break;
        }
        return base.WndProc(msg, wParam, lParam);
    }
}

/// <summary>녹화 영역 바깥쪽에 그려지는 얇은 테두리. 클릭이 통과하고 녹화에 찍히지 않는다.</summary>
internal sealed unsafe class RegionFrame : NativeWindow
{
    readonly int _t;
    uint _color;
    nint _brush;

    public RegionFrame(RECT region, uint rgb)
    {
        _color = rgb;
        _t = 3;
        var outer = new RECT(region.Left - _t, region.Top - _t, region.Right + _t, region.Bottom + _t);
        CreateHandle(Win.WS_EX_TOPMOST | Win.WS_EX_TOOLWINDOW | Win.WS_EX_LAYERED | Win.WS_EX_TRANSPARENT | Win.WS_EX_NOACTIVATE,
            Win.WS_POPUP, outer.Left, outer.Top, outer.Width, outer.Height, "LunaCapture Frame");
        User32.SetLayeredWindowAttributes(Handle, 0, 255, Win.LWA_ALPHA);
        PanelWindow.ExcludeFromCapture(Handle);

        nint rgnOuter = Gdi32.CreateRectRgn(0, 0, outer.Width, outer.Height);
        nint rgnInner = Gdi32.CreateRectRgn(_t, _t, outer.Width - _t, outer.Height - _t);
        Gdi32.CombineRgn(rgnOuter, rgnOuter, rgnInner, Win.RGN_DIFF);
        Gdi32.DeleteObject(rgnInner);
        User32.SetWindowRgn(Handle, rgnOuter, 1); // 이후 소유권은 시스템이 가짐

        User32.ShowWindow(Handle, 4 /* SW_SHOWNOACTIVATE */);
    }

    public void SetColor(uint rgb)
    {
        _color = rgb;
        if (_brush != 0) { Gdi32.DeleteObject(_brush); _brush = 0; }
        User32.InvalidateRect(Handle, null, 1);
    }

    protected override unsafe nint WndProc(uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case Win.WM_ERASEBKGND:
                return 1;
            case Win.WM_PAINT:
            {
                PAINTSTRUCT ps;
                nint hdc = User32.BeginPaint(Handle, &ps);
                if (_brush == 0) _brush = Gdi32.CreateSolidBrush(_color);
                var r = ps.rcPaint;
                User32.FillRect(hdc, &r, _brush);
                User32.EndPaint(Handle, &ps);
                return 0;
            }
            case Win.WM_DESTROY:
                if (_brush != 0) { Gdi32.DeleteObject(_brush); _brush = 0; }
                break;
        }
        return base.WndProc(msg, wParam, lParam);
    }
}

using LunaCapture.Interop;

namespace LunaCapture.Recording;

internal enum RecordKind { Gif, Video }

/// <summary>
/// 녹화 설정 창 (GIF / 동영상 통합).
/// 맨 위에서 파일 형식을 고르고, 목표 용량 · 예상 길이를 정하면 형식에 맞게 fps와 크기를 추천한다.
/// - GIF: 첫 장을 실제로 압축해 본 크기 + 화면 움직임으로 용량을 추정 (GifEstimator)
/// - 동영상: 목표 용량 ÷ 길이 = 비트레이트 (VideoPlanner)
/// </summary>
internal sealed class RecordSetupWindow : PanelWindow
{
    static readonly string[] FormatLabels = ["GIF", "동영상 MP4"];

    // GIF 선택지
    static readonly long[] GifTargetMb = [0, 1, 2, 3, 5, 8, 10, 15, 20, 25, 30, 50, 100];
    static readonly int[] GifLengthSec = [3, 5, 8, 10, 15, 20, 30, 45, 60, 90, 120];
    static readonly string[] MotionLabels = ["적음", "보통", "많음"];
    static readonly string[] GifScaleLabels = ["100%", "75%", "50%", "33%"];

    // 동영상 선택지
    static readonly long[] VideoTargetMb = [0, 5, 8, 10, 15, 20, 25, 30, 50, 100, 200, 500];
    static readonly int[] VideoLengthSec = [5, 10, 15, 20, 30, 45, 60, 90, 120, 180, 300, 600];
    static readonly string[] QualityLabels = ["높음", "보통", "낮음"];
    static readonly string[] VideoScaleLabels = ["100%", "75%", "50%"];

    // 선택 상태 (설정 파일에서 읽고, 녹화를 시작하면 다시 저장 → 다음 녹화 때도 기억)
    RecordKind _kind;
    int _gTarget, _gLength, _gMotion, _gFps, _gScale;
    int _vTarget, _vLength, _vQuality, _vFps, _vScale;

    readonly GifEstimator _est;
    readonly RECT _rect;
    readonly Action<RecordKind, RecordOptions> _onStart;
    readonly Action _onCancel;
    bool _done;

    readonly GdiFont _title, _body, _small, _big;

    public RecordSetupWindow(GifEstimator estimator, Action<RecordKind, RecordOptions> onStart, Action onCancel)
    {
        _est = estimator;
        _rect = estimator.Rect;
        _onStart = onStart;
        _onCancel = onCancel;

        var cfg = Services.AppSettings.Current;
        _kind = cfg.RecordFormat == 1 ? RecordKind.Video : RecordKind.Gif;
        _gTarget = Services.AppSettings.Clamp(cfg.GifTarget, GifTargetMb.Length);
        _gLength = Services.AppSettings.Clamp(cfg.GifLength, GifLengthSec.Length);
        _gMotion = Services.AppSettings.Clamp(cfg.GifMotion, MotionLabels.Length);
        _gFps = Services.AppSettings.Clamp(cfg.GifFps, GifEstimator.FpsOptions.Length);
        _gScale = Services.AppSettings.Clamp(cfg.GifScale, GifEstimator.Scales.Length);
        _vTarget = Services.AppSettings.Clamp(cfg.VideoTarget, VideoTargetMb.Length);
        _vLength = Services.AppSettings.Clamp(cfg.VideoLength, VideoLengthSec.Length);
        _vQuality = Services.AppSettings.Clamp(cfg.VideoQuality, QualityLabels.Length);
        _vFps = Services.AppSettings.Clamp(cfg.VideoFps, VideoPlanner.FpsOptions.Length);
        _vScale = Services.AppSettings.Clamp(cfg.VideoScale, VideoPlanner.Scales.Length);

        CreatePanel(_rect.Left, _rect.Top, noActivate: false);
        _title = new GdiFont(UiFonts.Family, 16 * S, true);
        _body = new GdiFont(UiFonts.Family, 14 * S, false);
        _small = new GdiFont(UiFonts.Family, 13 * S, false);
        _big = new GdiFont(UiFonts.Family, 17 * S, true);

        ApplyRecommendation();

        int w = Px(470), h = Px(488); // 내용(약 466) + 아래 여백 22
        var (x, y) = PanelPlacement.Near(_rect, w, h, Px(12));
        SetBounds(x, y, w, h);
        User32.ShowWindow(Handle, Win.SW_SHOW);
        User32.ForceForeground(Handle);
    }

    bool IsGif => _kind == RecordKind.Gif;

    long LimitBytes => (IsGif ? GifTargetMb[_gTarget] : VideoTargetMb[_vTarget]) * 1024 * 1024;
    int LengthSeconds => IsGif ? GifLengthSec[_gLength] : VideoLengthSec[_vLength];
    float Scale => IsGif ? GifEstimator.Scales[_gScale] : VideoPlanner.Scales[_vScale];
    int Fps => IsGif ? GifEstimator.FpsOptions[_gFps] : VideoPlanner.FpsOptions[_vFps];
    (int w, int h) OutSize => RecordOptions.ScaledSize(_rect, Scale, even: !IsGif);

    int VideoBitrate => LimitBytes > 0
        ? VideoPlanner.BitrateForTarget(LimitBytes, LengthSeconds)
        : VideoPlanner.BitrateForQuality(OutSize.w, OutSize.h, Fps, _vQuality);

    /// <summary>목표 용량이 정해져 있으면, 그 안에 들어가는 가장 좋은 fps·크기를 고른다.</summary>
    void ApplyRecommendation()
    {
        if (LimitBytes == 0) return;
        if (IsGif)
        {
            var (scale, fps, _) = _est.Recommend(LimitBytes, LengthSeconds, (Motion)_gMotion);
            _gScale = Array.IndexOf(GifEstimator.Scales, scale);
            _gFps = Array.IndexOf(GifEstimator.FpsOptions, fps);
        }
        else
        {
            var (scale, fps) = VideoPlanner.Recommend(_rect, VideoBitrate);
            _vScale = Array.IndexOf(VideoPlanner.Scales, scale);
            _vFps = Array.IndexOf(VideoPlanner.FpsOptions, fps);
        }
    }

    void SetKind(RecordKind kind)
    {
        _kind = kind;
        ApplyRecommendation();
        Invalidate();
    }

    void Change(Action change)
    {
        change();
        ApplyRecommendation();
        Invalidate();
    }

    protected override void Render(Gfx g)
    {
        int pad = Px(20), labelW = Px(112), rowH = Px(30), rowGap = Px(14);
        int x0 = pad, cx = pad + labelW;
        int y = Px(18);

        g.Text("녹화", _title, TextMain, x0, y);
        string dims = $"{_rect.Width}×{_rect.Height}";
        g.Text(dims, _small, TextSub, Width - pad - g.MeasureWidth(dims, _small), y + Px(3));
        y += Px(40);

        // 파일 형식
        Label(g, "파일 형식", x0, y, rowH);
        Segmented(g, "kind", cx, y, rowH, FormatLabels, (int)_kind, _body, i => SetKind((RecordKind)i), Px(100));
        g.Text(IsGif ? "자동 재생 · 메신저용" : "긴 녹화 · 작은 용량", _small, TextSub,
            cx + Px(100) * 2 + Px(4) + Px(10), y + (rowH - g.LineHeight(_small)) / 2);
        y += rowH + rowGap;

        g.FillRect(PanelBorder, pad, y - Px(4), Width - pad * 2, 1);
        y += Px(10);

        // 목표 용량
        long[] targets = IsGif ? GifTargetMb : VideoTargetMb;
        int target = IsGif ? _gTarget : _vTarget;
        Label(g, "목표 용량", x0, y, rowH);
        Stepper(g, "target", cx, y, rowH, targets[target] == 0 ? "제한 없음" : $"{targets[target]} MB", _body,
            () => Change(() => { if (IsGif) _gTarget--; else _vTarget--; }),
            () => Change(() => { if (IsGif) _gTarget++; else _vTarget++; }),
            target > 0, target < targets.Length - 1, Px(96));
        y += rowH + rowGap;

        // 예상 길이
        int[] lengths = IsGif ? GifLengthSec : VideoLengthSec;
        int length = IsGif ? _gLength : _vLength;
        Label(g, "예상 녹화 길이", x0, y, rowH);
        Stepper(g, "length", cx, y, rowH, FormatSec(lengths[length]), _body,
            () => Change(() => { if (IsGif) _gLength--; else _vLength--; }),
            () => Change(() => { if (IsGif) _gLength++; else _vLength++; }),
            length > 0, length < lengths.Length - 1, Px(96));
        y += rowH + rowGap;

        // GIF: 화면 움직임 / 동영상: 화질
        if (IsGif)
        {
            Label(g, "화면 움직임", x0, y, rowH);
            Segmented(g, "motion", cx, y, rowH, MotionLabels, _gMotion, _body, i => Change(() => _gMotion = i), Px(64));
            g.Text(_gMotion switch { 0 => "문서·UI", 1 => "스크롤·이동", _ => "영상·게임" }, _small, TextSub,
                cx + Px(64) * 3 + Px(4) * 2 + Px(10), y + (rowH - g.LineHeight(_small)) / 2);
        }
        else
        {
            Label(g, "화질", x0, y, rowH);
            if (LimitBytes == 0)
                Segmented(g, "quality", cx, y, rowH, QualityLabels, _vQuality, _body, i => { _vQuality = i; Invalidate(); }, Px(64));
            else
                g.Text("목표 용량에 맞춰 자동", _body, TextSub, cx, y + (rowH - g.LineHeight(_body)) / 2);
        }
        y += rowH + rowGap;

        g.FillRect(PanelBorder, pad, y - Px(4), Width - pad * 2, 1);
        y += Px(10);

        // 프레임
        int[] fpsOptions = IsGif ? GifEstimator.FpsOptions : VideoPlanner.FpsOptions;
        Label(g, "프레임 (fps)", x0, y, rowH);
        Segmented(g, "fps", cx, y, rowH, fpsOptions.Select(f => f.ToString()).ToArray(), IsGif ? _gFps : _vFps, _body,
            i => { if (IsGif) _gFps = i; else _vFps = i; Invalidate(); }, IsGif ? Px(50) : Px(58));
        y += rowH + rowGap;

        // 크기
        string[] scaleLabels = IsGif ? GifScaleLabels : VideoScaleLabels;
        Label(g, "크기", x0, y, rowH);
        Segmented(g, "scale", cx, y, rowH, scaleLabels, IsGif ? _gScale : _vScale, _body,
            i => { if (IsGif) _gScale = i; else _vScale = i; Invalidate(); }, Px(58));
        var (ow, oh) = OutSize;
        g.Text($"{ow}×{oh}", _small, TextSub, cx + Px(58) * scaleLabels.Length + Px(4) * (scaleLabels.Length - 1) + Px(10),
            y + (rowH - g.LineHeight(_small)) / 2);
        y += rowH + Px(18);

        if (IsGif) DrawGifSummary(g, pad, y, Width - pad * 2, Px(68));
        else DrawVideoSummary(g, pad, y, Width - pad * 2, Px(68));
        y += Px(68) + Px(16);

        // 버튼
        int bh = Px(36);
        var start = new RECT(Width - pad - Px(130), y, Width - pad, y + bh);
        var cancel = new RECT(start.Left - Px(8) - Px(80), y, start.Left - Px(8), y + bh);
        Button(g, "cancel", cancel, "취소", _body, Cancel);
        Button(g, "start", start, "녹화 시작", _body, Start, primary: true, icon: IconKind.RecordDot);
        g.Text("Tab 형식 전환 · Enter 시작 · Esc 취소", _small, TextSub, pad, y + (bh - g.LineHeight(_small)) / 2);
    }

    void DrawGifSummary(Gfx g, int x, int y, int w, int h)
    {
        g.FillRoundRect(0xFF1B1B20, x, y, w, h, Px(8));
        int ix = x + Px(14);
        double seconds = LengthSeconds;
        var motion = (Motion)_gMotion;
        double est = _est.EstimateBytes(Scale, Fps, seconds, motion);

        string line1 = $"{FormatSec(seconds)} 녹화 시 예상 약 {FormatMb(est)}";
        string line2;
        uint color2;
        if (LimitBytes == 0)
        {
            line2 = "용량 제한 없이 녹화해요";
            color2 = TextSub;
        }
        else
        {
            double max = _est.MaxSeconds(Scale, Fps, LimitBytes, motion);
            bool fits = est <= LimitBytes;
            line2 = fits
                ? $"목표 {GifTargetMb[_gTarget]}MB 안에 들어와요 · 최대 약 {FormatSec(Math.Floor(max))}"
                : $"목표 초과 예상 · 약 {FormatSec(Math.Floor(max))}에서 자동으로 멈춰요";
            color2 = fits ? 0xFF4CC38A : 0xFFF5A524;
        }

        g.Text(line1, _big, TextMain, ix, y + Px(12));
        g.Text(line2, _small, color2, ix, y + Px(40));
    }

    void DrawVideoSummary(Gfx g, int x, int y, int w, int h)
    {
        g.FillRoundRect(0xFF1B1B20, x, y, w, h, Px(8));
        int ix = x + Px(14);
        var (ow, oh) = OutSize;
        int bitrate = VideoBitrate;
        double bpp = VideoPlanner.Bpp(bitrate, ow, oh, Fps);
        string quality = VideoPlanner.QualityLabel(bpp);
        uint qColor = bpp >= 0.045 ? 0xFF4CC38A : bpp >= 0.025 ? 0xFFF5A524 : RecRed;

        string line1, line2;
        if (LimitBytes == 0)
        {
            double sec = LengthSeconds;
            line1 = $"{FormatSec(sec)} 녹화 시 예상 약 {FormatMb(VideoPlanner.EstimateBytes(bitrate, sec))}";
            line2 = $"{bitrate / 1e6:0.0} Mbps · 화질 {quality} · 용량 제한 없음";
        }
        else
        {
            line1 = $"{bitrate / 1e6:0.0} Mbps · 화질 {quality}";
            // 그래픽카드 인코더는 화면 변화가 적으면 비트레이트를 덜 써서, 실제로는 더 오래 녹화되는 경우가 많다.
            line2 = $"목표 {VideoTargetMb[_vTarget]}MB · 최소 {FormatSec(LengthSeconds)} 이상 녹화 가능 · 한도에서 자동 정지";
            if (bpp < 0.025) line2 = "화질이 많이 낮아요 · 크기를 줄이거나 길이를 줄여 보세요";
        }

        g.Text(line1, _big, TextMain, ix, y + Px(12));
        g.Text(line2, _small, LimitBytes == 0 ? TextSub : qColor, ix, y + Px(40));
    }

    void Label(Gfx g, string text, int x, int y, int h) =>
        g.Text(text, _body, TextSub, x, y + (h - g.LineHeight(_body)) / 2);

    static string FormatMb(double bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1024 / 1024:0.#}MB" : $"{bytes / 1024:0}KB";

    static string FormatSec(double s) =>
        s >= 60 ? (s % 60 == 0 ? $"{s / 60:0}분" : $"{Math.Floor(s / 60):0}분 {s % 60:0}초") : $"{s:0}초";

    void Start()
    {
        if (_done) return;
        _done = true;

        var cfg = Services.AppSettings.Current;
        cfg.RecordFormat = IsGif ? 0 : 1;
        (cfg.GifTarget, cfg.GifLength, cfg.GifMotion, cfg.GifFps, cfg.GifScale) = (_gTarget, _gLength, _gMotion, _gFps, _gScale);
        (cfg.VideoTarget, cfg.VideoLength, cfg.VideoQuality, cfg.VideoFps, cfg.VideoScale) = (_vTarget, _vLength, _vQuality, _vFps, _vScale);
        Services.AppSettings.Save();

        var opt = new RecordOptions
        {
            Fps = Fps,
            Scale = Scale,
            LimitBytes = LimitBytes,
            ShowCursor = cfg.ShowCursor,
            EvenSize = !IsGif,
            BitrateBps = IsGif ? 0 : VideoBitrate,
        };
        var kind = _kind;
        Destroy();
        _onStart(kind, opt);
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
            case Win.WM_KEYDOWN:
                if (wParam == Win.VK_RETURN) Start();
                else if (wParam == Win.VK_ESCAPE) Cancel();
                else if (wParam == 0x09) SetKind(IsGif ? RecordKind.Video : RecordKind.Gif); // Tab
                return 0;
            case Win.WM_CLOSE:
                Cancel();
                return 0;
            case Win.WM_DESTROY:
                _title.Dispose(); _body.Dispose(); _small.Dispose(); _big.Dispose();
                break;
        }
        return base.WndProc(msg, wParam, lParam);
    }
}

internal static class PanelPlacement
{
    /// <summary>영역 바로 아래 → 위 → 영역 안쪽 아래 순서로, 모니터 안에 들어가는 위치를 찾는다.</summary>
    public static unsafe (int x, int y) Near(RECT region, int w, int h, int gap)
    {
        var center = new POINT { X = (region.Left + region.Right) / 2, Y = (region.Top + region.Bottom) / 2 };
        var mi = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        User32.GetMonitorInfoW(User32.MonitorFromPoint(center, Win.MONITOR_DEFAULTTONEAREST), &mi);
        var work = mi.rcWork;

        int x = Math.Clamp(center.X - w / 2, work.Left + gap, Math.Max(work.Left + gap, work.Right - w - gap));
        int y;
        if (region.Bottom + gap + h <= work.Bottom) y = region.Bottom + gap;
        else if (region.Top - gap - h >= work.Top) y = region.Top - gap - h;
        else y = Math.Max(work.Top + gap, region.Bottom - gap * 2 - h);
        return (x, y);
    }
}

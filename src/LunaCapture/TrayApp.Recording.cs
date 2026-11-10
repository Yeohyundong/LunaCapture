using LunaCapture.Capture;
using LunaCapture.Editor;
using LunaCapture.Graphics;
using LunaCapture.Interop;
using LunaCapture.Recording;
using LunaCapture.Services;

namespace LunaCapture;

/// <summary>
/// 녹화 흐름 (GIF / 동영상 공통): 영역 선택 → 설정 창(형식 선택 + 목표 용량 추천) → 녹화 → 저장·클립보드.
/// </summary>
internal sealed partial class TrayApp
{
    const uint FrameSetupColor = 0x00F67C8B;  // BGR(0x8B7CF6) 보라
    const uint FrameRecordColor = 0x004D48E5; // BGR(0xE5484D) 빨강

    RegionFrame? _recFrame;
    PanelWindow? _recSetup;
    RecordingBar? _recBar;
    ScreenRecorder? _recorder;
    RECT _recRect;
    string? _recAppName;
    PixelImage? _recThumb; // 기록용 미리보기 (녹화 시작 순간의 영역)
    RecordKind _recKind;

    bool IsRecording => _recorder != null;

    /// <summary>캡처 화면의 녹화 모드에서 영역을 고르면 → 녹화 설정 창 (여기서 GIF/동영상을 고른다).</summary>
    void OpenRecordSetup(RECT r, string? appName)
    {
        if (r.Width < 16 || r.Height < 16)
        {
            _busy = false;
            MemoryTrimmer.Trim();
            return;
        }

        try
        {
            _recRect = r;
            _recAppName = appName;
            var estimator = GifEstimator.Measure(r); // GIF 용량 추정용: 테두리·설정창이 뜨기 전에 화면을 측정
            _recFrame = new RegionFrame(r, FrameSetupColor);
            _recSetup = new RecordSetupWindow(estimator, BeginRecording, CancelRecordSetup);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            Notify("녹화 실패", ex.Message, Win.NIIF_WARNING);
            CleanupRecordUi();
        }
    }

    void CancelRecordSetup()
    {
        _recSetup = null;
        CleanupRecordUi();
        MemoryTrimmer.Trim();
    }

    void BeginRecording(RecordKind kind, RecordOptions options)
    {
        _recSetup = null;
        _recKind = kind;
        try
        {
            string folder = _recKind == RecordKind.Gif ? SaveFolder : VideoFolder;
            Directory.CreateDirectory(folder);
            string ext = _recKind == RecordKind.Gif ? "gif" : "mp4";
            string path = Path.Combine(folder, CaptureFileName.Create(_recAppName, DateTime.Now, ext));

            Func<int, int, IFrameSink> sink = _recKind == RecordKind.Gif
                ? (w, h) => new GifSink(path, w, h, options.LimitBytes)
                : (w, h) => new Mp4Sink(path, w, h, options.Fps, options.BitrateBps, options.LimitBytes);

            try
            {
                using var shot = GdiSurface.CaptureScreen(_recRect.Left, _recRect.Top, _recRect.Width, _recRect.Height);
                _recThumb = Composer.Thumbnail(shot.Crop(new RECT(0, 0, shot.Width, shot.Height)), HistoryStore.ThumbSide);
            }
            catch (Exception ex) { Log.Error(ex); _recThumb = null; }
            _recorder = new ScreenRecorder(_recRect, options, path, sink);
            _recFrame?.SetColor(FrameRecordColor);
            _recorder.Start();
            _recBar = new RecordingBar(_recRect, _recorder, StopRecording, CancelRecording);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            Notify("녹화 실패", ex.Message, Win.NIIF_WARNING);
            try { _recorder?.Cancel(); } catch { }
            _recorder = null;
            CleanupRecordUi();
        }
    }

    void StopRecording()
    {
        _finishingRecording = true;
        try { StopRecordingCore(); } finally { _finishingRecording = false; }
    }

    void StopRecordingCore()
    {
        var rec = _recorder;
        _recorder = null;
        _recBar = null;
        CleanupRecordUi();
        if (rec == null) return;

        string kindName = _recKind == RecordKind.Gif ? "GIF" : "동영상";
        try
        {
            var result = rec.Stop();
            _lastSavedPath = result.Path;
            HistoryStore.AddRecording(_recKind == RecordKind.Gif ? "gif" : "mp4", result.Path, _recThumb, _recRect.Width, _recRect.Height);
            _recThumb = null;
            _history?.Reload();
            bool copied = ClipboardService.SetFile(Handle, result.Path);

            string title = result.HitLimit ? "목표 용량에 도달해 녹화를 멈췄어요" : $"{kindName} 저장 완료";
            string info = $"{FormatSize(result.Bytes)} · {result.DurationMs / 1000.0:0.0}초 · {result.Frames}프레임";
            if (copied) info += "\n클립보드에 파일 복사됨 (Ctrl+V로 붙여넣기)";
            Notify(title, info, Win.NIIF_INFO);
            Log.Info($"{kindName} {result.Path} {result.Bytes}B {result.DurationMs}ms frames={result.Frames} dropped={result.Dropped} limit={result.HitLimit}");

            if (_recKind == RecordKind.Gif && Settings.OpenGifTrim && result.Frames > 1)
                OpenGifTrim(result.Path);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            Notify($"{kindName} 저장 실패", ex.Message, Win.NIIF_WARNING);
        }
        MemoryTrimmer.Trim();
    }

    /// <summary>녹화 직후 앞뒤를 잘라낼 수 있는 창. 저장하면 같은 파일을 덮어쓴다(클립보드는 파일 경로라 그대로 유효).</summary>
    void OpenGifTrim(string path)
    {
        try
        {
            _ = new Editor.GifTrimWindow(path, saved =>
            {
                if (saved)
                {
                    _lastSavedPath = path;
                    Notify("잘라낸 GIF 저장 완료", $"{FormatSize(new FileInfo(path).Length)} · 클립보드의 파일도 갱신됨", Win.NIIF_INFO);
                }
                MemoryTrimmer.Trim();
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            Notify("GIF 자르기 창을 열지 못했어요", ex.Message, Win.NIIF_WARNING);
        }
    }

    void CancelRecording()
    {
        var rec = _recorder;
        _recorder = null;
        _recBar = null;
        CleanupRecordUi();
        try { rec?.Cancel(); } catch (Exception ex) { Log.Error(ex); }
        MemoryTrimmer.Trim();
    }

    void CleanupRecordUi()
    {
        _recSetup?.Destroy();
        _recSetup = null;
        _recBar?.Destroy();
        _recBar = null;
        _recFrame?.Destroy();
        _recFrame = null;
        _busy = false;
    }

    /// <summary>앱 종료 시 녹화 중이면 지금까지 녹화한 것은 저장한다.</summary>
    void ShutdownRecording()
    {
        if (_recorder == null) return;
        try { _recorder.Stop(); } catch (Exception ex) { Log.Error(ex); }
        _recorder = null;
    }

    static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1048576.0:0.0}MB" : $"{bytes / 1024}KB";
}

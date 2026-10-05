using System.Collections.Concurrent;
using System.Diagnostics;
using LunaCapture.Gif;
using LunaCapture.Graphics;
using LunaCapture.Interop;

namespace LunaCapture.Recording;

internal sealed class RecordOptions
{
    public int Fps = 15;
    public float Scale = 1f;
    public long LimitBytes;      // 0 = 제한 없음
    public bool ShowCursor = true;
    public bool EvenSize;        // 동영상(H.264)은 가로·세로가 짝수여야 함
    public int BitrateBps;       // 동영상 전용

    public (int w, int h) OutputSize(RECT r) => ScaledSize(r, Scale, EvenSize);

    public static (int w, int h) ScaledSize(RECT r, float scale, bool even = false)
    {
        int w = Math.Max(2, (int)MathF.Round(r.Width * scale));
        int h = Math.Max(2, (int)MathF.Round(r.Height * scale));
        return even ? (w & ~1, h & ~1) : (w, h);
    }
}

internal sealed record RecordResult(string Path, long Bytes, long DurationMs, int Frames, int Dropped, bool HitLimit);

/// <summary>녹화된 프레임을 받아 파일로 만드는 쪽 (GIF / MP4).</summary>
internal interface IFrameSink : IDisposable
{
    FrameResult AddFrame(GdiSurface frame, long timestampMs);
    void Finish(long endTimestampMs);
    /// <summary>지금 멈추면 나올 파일 크기(추정 포함).</summary>
    long CurrentSize { get; }
    int FrameCount { get; }
}

/// <summary>
/// 화면 영역 녹화기.
/// 캡처 스레드는 정해진 간격으로 화면을 찍고, 인코딩 스레드는 싱크(GIF/MP4)에 넘겨 바로 파일에 쓴다.
/// 둘 사이 버퍼는 3장뿐이라 메모리가 일정하게 유지되고, 인코딩이 밀리면 프레임을 건너뛴다.
/// </summary>
internal sealed unsafe class ScreenRecorder
{
    const int PoolSize = 3;

    readonly RECT _rect;
    readonly RecordOptions _opt;
    readonly string _path;
    readonly Func<int, int, IFrameSink> _createSink;
    readonly int _w, _h;

    readonly BlockingCollection<(GdiSurface surf, long ts)> _queue = new(PoolSize);
    readonly ConcurrentQueue<GdiSurface> _free = new();
    readonly Stopwatch _clock = new();
    readonly Dictionary<nint, (int x, int y)> _hotspots = new();

    Thread? _captureThread, _encodeThread;
    volatile bool _stop;
    volatile bool _paused;
    long _pausedTotalMs, _pauseStartMs;
    long _endTs;

    long _bytes;
    int _frames, _dropped;
    volatile bool _hitLimit;
    volatile Exception? _error;

    /// <param name="createSink">인코딩 스레드에서 (출력 가로, 세로)로 호출된다.</param>
    public ScreenRecorder(RECT screenRect, RecordOptions options, string path, Func<int, int, IFrameSink> createSink)
    {
        _rect = screenRect;
        _opt = options;
        _path = path;
        _createSink = createSink;
        (_w, _h) = options.OutputSize(screenRect);
    }

    public long ElapsedMs => _paused ? _pauseStartMs - Interlocked.Read(ref _pausedTotalMs) : _clock.ElapsedMilliseconds - Interlocked.Read(ref _pausedTotalMs);
    public long Bytes => Interlocked.Read(ref _bytes);
    public long LimitBytes => _opt.LimitBytes;
    public bool Paused => _paused;
    /// <summary>용량 한도에 닿아 스스로 멈췄는지. UI가 이걸 보고 Stop()을 부른다.</summary>
    public bool HitLimit => _hitLimit;
    public bool Failed => _error != null;
    public string Path => _path;

    public void Start()
    {
        for (int i = 0; i < PoolSize; i++) _free.Enqueue(new GdiSurface(_w, _h));
        Winmm.timeBeginPeriod(1);
        _clock.Start();

        _encodeThread = new Thread(EncodeLoop) { Name = "Encode", IsBackground = true, Priority = ThreadPriority.BelowNormal };
        _captureThread = new Thread(CaptureLoop) { Name = "Capture", IsBackground = true, Priority = ThreadPriority.AboveNormal };
        _encodeThread.Start();
        _captureThread.Start();
    }

    public void TogglePause()
    {
        if (_paused)
        {
            Interlocked.Add(ref _pausedTotalMs, _clock.ElapsedMilliseconds - _pauseStartMs);
            _paused = false;
        }
        else
        {
            _pauseStartMs = _clock.ElapsedMilliseconds;
            _paused = true;
        }
    }

    /// <summary>녹화를 끝내고 파일을 마무리한다.</summary>
    public RecordResult Stop()
    {
        if (!_hitLimit) _endTs = ElapsedMs; // 한도 도달 시에는 인코더가 이미 끝 시각을 정해 둠
        _stop = true;
        _captureThread?.Join();
        _encodeThread?.Join();
        Cleanup();
        if (_error != null) throw new IOException("녹화 중 오류: " + _error.Message, _error);
        return new RecordResult(_path, new FileInfo(_path).Length, _endTs, _frames, _dropped, _hitLimit);
    }

    public void Cancel()
    {
        _stop = true;
        _captureThread?.Join();
        _encodeThread?.Join();
        Cleanup();
        try { File.Delete(_path); } catch { }
    }

    void Cleanup()
    {
        Winmm.timeEndPeriod(1);
        while (_free.TryDequeue(out var s)) s.Dispose();
        while (_queue.TryTake(out var item)) item.surf.Dispose();
        _queue.Dispose();
    }

    // ───────────────────────── 캡처 스레드 ─────────────────────────

    void CaptureLoop()
    {
        nint screen = User32.GetDC(0);
        try
        {
            double interval = 1000.0 / _opt.Fps;
            double next = 0;
            while (!_stop)
            {
                long now = ElapsedMs;
                if (now < next)
                {
                    Thread.Sleep((int)Math.Max(1, next - now));
                    continue;
                }
                next += interval;
                if (next < now) next = now + interval; // 많이 밀렸으면 따라잡으려 하지 말고 리셋

                if (_paused) continue;

                if (!_free.TryDequeue(out var surf))
                {
                    _dropped++; // 인코더가 밀림 → 이번 프레임은 건너뜀
                    continue;
                }
                Grab(screen, surf);
                _queue.Add((surf, now));
            }
        }
        catch (Exception ex)
        {
            _error ??= ex;
            Log.Error(ex);
        }
        finally
        {
            User32.ReleaseDC(0, screen);
            _queue.CompleteAdding();
        }
    }

    void Grab(nint screen, GdiSurface surf)
    {
        if (_w == _rect.Width && _h == _rect.Height)
        {
            Gdi32.BitBlt(surf.Dc, 0, 0, _w, _h, screen, _rect.Left, _rect.Top, Win.SRCCOPY);
        }
        else
        {
            Gdi32.SetStretchBltMode(surf.Dc, Win.HALFTONE);
            Gdi32.SetBrushOrgEx(surf.Dc, 0, 0, null);
            Gdi32.StretchBlt(surf.Dc, 0, 0, _w, _h, screen, _rect.Left, _rect.Top, _rect.Width, _rect.Height, Win.SRCCOPY);
        }
        if (_opt.ShowCursor) DrawCursor(surf);
        Gdi32.GdiFlush();
    }

    void DrawCursor(GdiSurface surf)
    {
        var ci = new CURSORINFO { cbSize = (uint)sizeof(CURSORINFO) };
        if (User32.GetCursorInfo(&ci) == 0 || (ci.flags & Win.CURSOR_SHOWING) == 0 || ci.hCursor == 0) return;

        if (!_hotspots.TryGetValue(ci.hCursor, out var hot))
        {
            ICONINFO ii;
            if (User32.GetIconInfo(ci.hCursor, &ii) != 0)
            {
                hot = (ii.xHotspot, ii.yHotspot);
                if (ii.hbmMask != 0) Gdi32.DeleteObject(ii.hbmMask);
                if (ii.hbmColor != 0) Gdi32.DeleteObject(ii.hbmColor);
            }
            _hotspots[ci.hCursor] = hot;
        }

        float sx = _w / (float)_rect.Width, sy = _h / (float)_rect.Height;
        int x = (int)((ci.ptScreenPos.X - _rect.Left - hot.x) * sx);
        int y = (int)((ci.ptScreenPos.Y - _rect.Top - hot.y) * sy);
        if (sx >= 0.999f)
        {
            User32.DrawIconEx(surf.Dc, x, y, ci.hCursor, 0, 0, 0, 0, Win.DI_NORMAL | Win.DI_DEFAULTSIZE);
        }
        else
        {
            int size = Math.Max(8, (int)(User32.GetSystemMetrics(Win.SM_CXCURSOR) * sx));
            User32.DrawIconEx(surf.Dc, x, y, ci.hCursor, size, size, 0, 0, Win.DI_NORMAL);
        }
    }

    // ───────────────────────── 인코딩 스레드 ─────────────────────────

    void EncodeLoop()
    {
        long lastTs = 0;
        IFrameSink? sink = null;
        try
        {
            sink = _createSink(_w, _h);
            foreach (var (surf, ts) in _queue.GetConsumingEnumerable())
            {
                try
                {
                    if (_hitLimit) continue;
                    var r = sink.AddFrame(surf, ts);
                    if (r == FrameResult.LimitReached)
                    {
                        _hitLimit = true;
                        _endTs = ts;
                        _stop = true;
                    }
                    else
                    {
                        lastTs = ts;
                        _frames = sink.FrameCount;
                        Interlocked.Exchange(ref _bytes, sink.CurrentSize);
                    }
                }
                finally
                {
                    _free.Enqueue(surf);
                }
            }

            sink.Finish(Math.Max(_endTs, lastTs));
            _frames = sink.FrameCount;
        }
        catch (Exception ex)
        {
            _error ??= ex;
            _stop = true;
            Log.Error(ex);
            // 캡처 스레드가 막히지 않도록 남은 큐를 비운다.
            foreach (var item in _queue.GetConsumingEnumerable()) _free.Enqueue(item.surf);
        }
        finally
        {
            sink?.Dispose();
        }
    }
}

/// <summary>GIF 파일 싱크: 병렬 스트리밍 GIF 인코더를 감싼다.</summary>
internal sealed unsafe class GifSink : IFrameSink
{
    readonly FileStream _fs;
    readonly ParallelGifEncoder _enc;

    public GifSink(string path, int width, int height, long limitBytes)
    {
        _fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
        _enc = new ParallelGifEncoder(_fs, width, height, limitBytes);
    }

    public long CurrentSize => _enc.CurrentSize;
    public int FrameCount => _enc.FrameCount;

    public FrameResult AddFrame(GdiSurface frame, long timestampMs) => _enc.AddFrame((uint*)frame.Bits, timestampMs);

    public void Finish(long endTimestampMs) => _enc.Finish(endTimestampMs);

    public void Dispose()
    {
        _enc.Dispose();
        _fs.Dispose();
    }
}

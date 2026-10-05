namespace LunaCapture.Gif;

internal enum FrameResult { Added, Unchanged, LimitReached }

/// <summary>성능 측정용 누적 시간(Stopwatch 틱). 개발 중 벤치마크에서만 읽는다.</summary>
internal static class Profile
{
    public static long Diff, Quantize, MedianCut, Lzw;
    public static void Reset() => Diff = Quantize = MedianCut = Lzw = 0;
    public static double Ms(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
}

/// <summary>
/// 프레임 한 장을 GIF 이미지 블록으로 압축한다. "이번 장 + 직전 장"만 있으면 되므로
/// 여러 개를 동시에 돌려 여러 장을 병렬로 압축할 수 있다.
/// - 직전 장과 달라진 사각형만 저장하고, 그 안에서도 안 바뀐 픽셀은 투명 처리.
/// </summary>
internal sealed unsafe class FrameEncoder
{
    readonly Quantizer _quantizer = new();
    readonly LzwEncoder _lzw = new();
    byte[] _indices = [];

    public readonly MemoryStream Output = new();
    public bool Changed { get; private set; }
    public bool Transparent { get; private set; }

    /// <param name="prev">직전 프레임 (첫 장이면 null)</param>
    public void Encode(uint* frame, uint* prev, int w, int h)
    {
        Output.SetLength(0);
        int x = 0, y = 0, rw = w, rh = h;

        if (prev != null)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            bool changed = FindChangedRect(frame, prev, w, h, out x, out y, out rw, out rh);
            Profile.Diff += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            if (!changed) { Changed = false; return; }
        }
        Changed = true;
        Transparent = prev != null;

        int n = rw * rh;
        if (_indices.Length < n) _indices = new byte[n];
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        _quantizer.Process(frame, prev, w, x, y, rw, rh, _indices);
        long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
        WriteImageBlock(x, y, rw, rh, _indices.AsSpan(0, n));
        Profile.Quantize += t2 - t1;
        Profile.Lzw += System.Diagnostics.Stopwatch.GetTimestamp() - t2;
    }

    static bool FindChangedRect(uint* frame, uint* prev, int w, int h, out int rx, out int ry, out int rw, out int rh)
    {
        rx = ry = rw = rh = 0;
        int top = -1, bottom = -1;
        for (int y = 0; y < h && top < 0; y++)
            if (!RowEqual(frame + y * w, prev + y * w, w)) top = y;
        if (top < 0) return false;
        for (int y = h - 1; y >= top; y--)
            if (!RowEqual(frame + y * w, prev + y * w, w)) { bottom = y; break; }

        int left = w, right = -1;
        for (int y = top; y <= bottom; y++)
        {
            uint* a = frame + y * w, b = prev + y * w;
            for (int x = 0; x < left; x++)
                if (((a[x] ^ b[x]) & 0xFFFFFF) != 0) { left = x; break; }
            for (int x = w - 1; x > right; x--)
                if (((a[x] ^ b[x]) & 0xFFFFFF) != 0) { right = x; break; }
        }

        rx = left; ry = top; rw = right - left + 1; rh = bottom - top + 1;
        return true;
    }

    static bool RowEqual(uint* a, uint* b, int n)
    {
        for (int x = 0; x < n; x++)
            if (((a[x] ^ b[x]) & 0xFFFFFF) != 0) return false;
        return true;
    }

    void WriteImageBlock(int x, int y, int w, int h, ReadOnlySpan<byte> indices)
    {
        Span<byte> desc = stackalloc byte[10];
        desc[0] = 0x2C;
        desc[1] = (byte)x; desc[2] = (byte)(x >> 8);
        desc[3] = (byte)y; desc[4] = (byte)(y >> 8);
        desc[5] = (byte)w; desc[6] = (byte)(w >> 8);
        desc[7] = (byte)h; desc[8] = (byte)(h >> 8);
        desc[9] = 0x87; // 로컬 팔레트 사용, 256색
        Output.Write(desc);

        Span<byte> pal = stackalloc byte[256 * 3];
        pal.Clear();
        for (int i = 0; i < _quantizer.PaletteCount; i++)
        {
            uint c = _quantizer.Palette[i];
            pal[i * 3] = (byte)(c >> 16);
            pal[i * 3 + 1] = (byte)(c >> 8);
            pal[i * 3 + 2] = (byte)c;
        }
        Output.Write(pal);

        _lzw.Encode(Output, indices);
    }
}

/// <summary>
/// 압축된 프레임들을 순서대로 파일에 쓴다: 헤더, 프레임별 표시 시간, 용량 한도 검사, 마무리.
/// 프레임 하나는 다음 프레임이 올 때까지 대기시킨다(그래야 표시 시간을 정할 수 있음).
/// </summary>
internal sealed class GifWriter
{
    readonly Stream _out;
    readonly MemoryStream _pending = new();
    bool _pendingTransparent, _hasPending, _started, _finished;
    long _startTs, _pendingTs, _emittedCs;
    long _written;

    public int FrameCount { get; private set; }

    /// <summary>지금까지 쓴 바이트 + 대기 중인 프레임 + 마무리 바이트 = 지금 멈추면 나올 파일 크기.</summary>
    public long CurrentSize => _written + (_hasPending ? 8 + _pending.Length : 0) + 1;

    public GifWriter(Stream output, int width, int height)
    {
        _out = output;
        Span<byte> header = stackalloc byte[13 + 19];
        "GIF89a"u8.CopyTo(header);
        header[6] = (byte)width; header[7] = (byte)(width >> 8);
        header[8] = (byte)height; header[9] = (byte)(height >> 8);
        header[10] = 0; // 전역 팔레트 없음 (프레임마다 로컬 팔레트)
        header[11] = 0; header[12] = 0;
        // NETSCAPE2.0 무한 반복
        ReadOnlySpan<byte> loop = [0x21, 0xFF, 0x0B, (byte)'N', (byte)'E', (byte)'T', (byte)'S', (byte)'C', (byte)'A', (byte)'P', (byte)'E',
            (byte)'2', (byte)'.', (byte)'0', 0x03, 0x01, 0x00, 0x00, 0x00];
        loop.CopyTo(header[13..]);
        Write(header);
    }

    /// <summary>압축된 프레임을 확정한다. 한도를 넘게 되면 쓰지 않고 LimitReached.</summary>
    public FrameResult Commit(FrameEncoder f, long timestampMs, long maxBytes)
    {
        if (_finished) throw new InvalidOperationException("이미 끝난 GIF");
        if (!f.Changed) return FrameResult.Unchanged;

        long len = f.Output.Length;
        if (maxBytes > 0 && _written + (_hasPending ? 8 + _pending.Length : 0) + 8 + len + 1 > maxBytes)
            return FrameResult.LimitReached;

        if (!_started) { _startTs = timestampMs; _started = true; }
        if (_hasPending) FlushPending(timestampMs);

        _pending.SetLength(0);
        _pending.Write(f.Output.GetBuffer(), 0, (int)len);
        _pendingTransparent = f.Transparent;
        _pendingTs = timestampMs;
        _hasPending = true;
        FrameCount++;
        return FrameResult.Added;
    }

    /// <summary>마지막 프레임 표시 시간을 endTimestampMs까지로 정하고 파일을 마무리한다.</summary>
    public void Finish(long endTimestampMs)
    {
        if (_finished) return;
        if (_hasPending) FlushPending(Math.Max(endTimestampMs, _pendingTs + 20));
        _out.WriteByte(0x3B);
        _written++;
        _out.Flush();
        _finished = true;
    }

    void FlushPending(long nextTs)
    {
        // 누적 시간 기준으로 반올림해서 오래 녹화해도 시간이 밀리지 않게.
        long totalCs = (nextTs - _startTs + 5) / 10;
        long delay = Math.Clamp(totalCs - _emittedCs, 2, 65535); // 2 미만은 브라우저가 느리게 재생함
        _emittedCs += delay;

        Span<byte> gce = [0x21, 0xF9, 0x04,
            (byte)((1 << 2) | (_pendingTransparent ? 1 : 0)), // 처분 방식 1(그대로 두기) + 투명 여부
            (byte)delay, (byte)(delay >> 8),
            Quantizer.TransparentIndex, 0x00];
        Write(gce);
        Write(_pending.GetBuffer().AsSpan(0, (int)_pending.Length));
        _pending.SetLength(0);
        _hasPending = false;
    }

    void Write(ReadOnlySpan<byte> data)
    {
        _out.Write(data);
        _written += data.Length;
    }
}

/// <summary>
/// 단순(한 스레드) GIF 인코더. 용량 측정·테스트용.
/// </summary>
internal sealed unsafe class GifEncoder : IDisposable
{
    readonly int _w, _h;
    readonly uint[] _prev;
    readonly FrameEncoder _frame = new();
    readonly GifWriter _writer;
    bool _hasFirst;

    public GifEncoder(Stream output, int width, int height)
    {
        _w = width;
        _h = height;
        _prev = new uint[width * height];
        _writer = new GifWriter(output, width, height);
    }

    public int FrameCount => _writer.FrameCount;
    public long CurrentSize => _writer.CurrentSize;

    public FrameResult AddFrame(uint* frame, long timestampMs, long maxBytes = 0)
    {
        fixed (uint* prev = _prev)
        {
            _frame.Encode(frame, _hasFirst ? prev : null, _w, _h);
            var r = _writer.Commit(_frame, timestampMs, maxBytes);
            if (r == FrameResult.Added)
            {
                Buffer.MemoryCopy(frame, prev, _prev.Length * 4L, _prev.Length * 4L);
                _hasFirst = true;
            }
            return r;
        }
    }

    public void Finish(long endTimestampMs) => _writer.Finish(endTimestampMs);

    public void Dispose() { }
}

/// <summary>
/// 병렬 GIF 인코더. 프레임마다 "이번 장 + 직전 장" 복사본을 들고 여러 작업자가 동시에 압축하고,
/// 파일에는 들어온 순서대로 쓴다. 동시에 처리 중인 장 수를 제한해 메모리를 일정하게 유지한다.
/// </summary>
internal sealed unsafe class ParallelGifEncoder : IDisposable
{
    sealed class Job
    {
        public uint[] Cur = null!;
        public uint[]? Prev;
        public FrameEncoder Encoder = null!;
        public long Ts;
        public Task Task = null!;
    }

    const long MemoryBudget = 64L * 1024 * 1024; // 프레임 복사본에 쓸 최대 메모리

    readonly int _w, _h, _workers;
    readonly long _limit;
    readonly GifWriter _writer;
    readonly Queue<Job> _jobs = new();
    readonly Stack<uint[]> _bufferPool = new();
    readonly Stack<FrameEncoder> _encoderPool = new();
    uint[]? _last;
    bool _limitHit;
    long _limitTs;

    public ParallelGifEncoder(Stream output, int width, int height, long limitBytes)
    {
        _w = width;
        _h = height;
        _limit = limitBytes;
        _writer = new GifWriter(output, width, height);

        long frameBytes = (long)width * height * 4;
        int byCpu = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        int byMemory = (int)Math.Max(1, MemoryBudget / frameBytes - 2);
        _workers = Math.Min(byCpu, byMemory);
    }

    public int Workers => _workers;
    public int FrameCount => _writer.FrameCount;
    public long CurrentSize => _writer.CurrentSize;
    public bool LimitHit => _limitHit;
    public long LimitTimestamp => _limitTs;

    public FrameResult AddFrame(uint* frame, long timestampMs)
    {
        CommitCompleted(waitForOldest: false);
        if (_limitHit) return FrameResult.LimitReached;

        // 동시에 처리 중인 장이 많으면 가장 오래된 것이 끝날 때까지 기다린다.
        while (_jobs.Count >= _workers) CommitCompleted(waitForOldest: true);
        if (_limitHit) return FrameResult.LimitReached;

        var cur = _bufferPool.Count > 0 ? _bufferPool.Pop() : new uint[_w * _h];
        fixed (uint* dst = cur) Buffer.MemoryCopy(frame, dst, cur.Length * 4L, cur.Length * 4L);

        var job = new Job
        {
            Cur = cur,
            Prev = _last,
            Encoder = _encoderPool.Count > 0 ? _encoderPool.Pop() : new FrameEncoder(),
            Ts = timestampMs,
        };
        _last = cur;
        int w = _w, h = _h;
        job.Task = Task.Run(() =>
        {
            fixed (uint* c = job.Cur)
            fixed (uint* p = job.Prev)
                job.Encoder.Encode(c, job.Prev == null ? null : p, w, h);
        });
        _jobs.Enqueue(job);
        return FrameResult.Added;
    }

    /// <summary>앞에서부터 끝난 작업들을 순서대로 파일에 쓴다.</summary>
    void CommitCompleted(bool waitForOldest)
    {
        while (_jobs.Count > 0)
        {
            var job = _jobs.Peek();
            if (!job.Task.IsCompleted)
            {
                if (!waitForOldest) return;
                job.Task.Wait();
            }
            _jobs.Dequeue();
            waitForOldest = false;

            if (!_limitHit)
            {
                var r = _writer.Commit(job.Encoder, job.Ts, _limit);
                if (r == FrameResult.LimitReached)
                {
                    _limitHit = true;
                    _limitTs = job.Ts;
                }
            }

            // 이 장의 "직전 장"은 이제 아무도 안 쓴다 (앞 장은 이미 확정됐으므로).
            if (job.Prev != null) _bufferPool.Push(job.Prev);
            _encoderPool.Push(job.Encoder);
        }
    }

    public void Finish(long endTimestampMs)
    {
        while (_jobs.Count > 0) CommitCompleted(waitForOldest: true);
        _writer.Finish(_limitHit ? _limitTs : endTimestampMs);
        _bufferPool.Clear();
        _encoderPool.Clear();
        _last = null;
    }

    public void Dispose()
    {
        foreach (var j in _jobs) { try { j.Task.Wait(); } catch { } }
        _jobs.Clear();
        _bufferPool.Clear();
        _encoderPool.Clear();
    }
}

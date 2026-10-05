using System.IO.MemoryMappedFiles;

namespace LunaCapture.Gif;

internal sealed class GifFrameInfo
{
    public long Start, End;          // 이 프레임의 원본 바이트 범위 (제어 확장 포함)
    public long LzwOffset;           // LZW 최소 코드 크기 바이트 위치
    public long PaletteOffset;       // 사용할 팔레트 위치 (로컬 또는 전역)
    public int PaletteSize;
    public int X, Y, W, H;
    public bool Interlaced;
    public int DelayCs;              // 1/100초
    public int Disposal;
    public bool Transparent;
    public byte TransparentIndex;
    public int StartCs;              // 이 프레임이 시작되는 시각 (누적)
}

/// <summary>
/// GIF 파일 읽기. 파일 전체를 메모리에 올리지 않고 메모리 맵(필요한 부분만 OS가 읽어 줌)으로 접근한다.
/// 프레임 목록을 파싱하고, 순서대로 디코딩해 한 장의 캔버스(BGRA)에 합성한다.
/// </summary>
internal sealed unsafe class GifReader : IDisposable
{
    MemoryMappedFile? _map;
    MemoryMappedViewAccessor? _view;
    byte* _data;
    readonly long _length;

    public int Width { get; }
    public int Height { get; }
    public long HeaderEnd { get; private set; } // 헤더 + 전역 팔레트 + 앞쪽 확장(반복 설정 등)의 끝
    public List<GifFrameInfo> Frames { get; } = new();
    public int TotalCs { get; private set; }
    public long FileLength => _length;

    // 디코딩 상태
    readonly ushort[] _prefix = new ushort[4096];
    readonly byte[] _suffix = new byte[4096];
    readonly byte[] _stack = new byte[4097];
    byte[] _indices = [];
    int _decoded = -1; // 캔버스에 마지막으로 합성된 프레임
    GifFrameInfo? _pendingDispose;

    public GifReader(string path)
    {
        _length = new FileInfo(path).Length;
        _map = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        _view = _map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref _data);
        _data += _view.PointerOffset;

        if (_length < 13 || _data[0] != 'G' || _data[1] != 'I' || _data[2] != 'F')
            throw new InvalidDataException("GIF 파일이 아닙니다.");
        Width = _data[6] | _data[7] << 8;
        Height = _data[8] | _data[9] << 8;
        Parse();
    }

    public int DecodedIndex => _decoded;

    void Parse()
    {
        byte packed = _data[10];
        long pos = 13;
        long globalPal = -1;
        int globalSize = 0;
        if ((packed & 0x80) != 0)
        {
            globalSize = 1 << ((packed & 7) + 1);
            globalPal = pos;
            pos += globalSize * 3;
        }

        long gceStart = -1;
        int delay = 0, disposal = 0;
        bool transparent = false;
        byte transIndex = 0;
        int timeCs = 0;
        HeaderEnd = -1;

        while (pos < _length)
        {
            byte b = _data[pos];
            if (b == 0x3B) break;

            if (b == 0x21)
            {
                byte label = _data[pos + 1];
                if (label == 0xF9)
                {
                    if (HeaderEnd < 0) HeaderEnd = pos;
                    gceStart = pos;
                    byte p = _data[pos + 3];
                    disposal = (p >> 2) & 7;
                    transparent = (p & 1) != 0;
                    delay = _data[pos + 4] | _data[pos + 5] << 8;
                    transIndex = _data[pos + 6];
                    pos += 8;
                }
                else
                {
                    pos += 2;
                    pos = SkipSubBlocks(pos);
                }
                continue;
            }

            if (b == 0x2C)
            {
                if (HeaderEnd < 0) HeaderEnd = pos;
                var f = new GifFrameInfo
                {
                    Start = gceStart >= 0 ? gceStart : pos,
                    X = _data[pos + 1] | _data[pos + 2] << 8,
                    Y = _data[pos + 3] | _data[pos + 4] << 8,
                    W = _data[pos + 5] | _data[pos + 6] << 8,
                    H = _data[pos + 7] | _data[pos + 8] << 8,
                    DelayCs = delay,
                    Disposal = disposal,
                    Transparent = transparent,
                    TransparentIndex = transIndex,
                    StartCs = timeCs,
                };
                byte ip = _data[pos + 9];
                f.Interlaced = (ip & 0x40) != 0;
                pos += 10;
                if ((ip & 0x80) != 0)
                {
                    f.PaletteSize = 1 << ((ip & 7) + 1);
                    f.PaletteOffset = pos;
                    pos += f.PaletteSize * 3;
                }
                else
                {
                    f.PaletteSize = globalSize;
                    f.PaletteOffset = globalPal;
                }
                f.LzwOffset = pos;
                pos = SkipSubBlocks(pos + 1);
                f.End = pos;
                Frames.Add(f);

                // 표시 시간이 0이면 브라우저처럼 0.1초로 본다.
                timeCs += delay < 2 ? 10 : delay;
                gceStart = -1; delay = 0; disposal = 0; transparent = false;
                continue;
            }

            break; // 알 수 없는 블록 → 여기까지만 읽음
        }

        TotalCs = timeCs;
        if (HeaderEnd < 0) HeaderEnd = pos;
        if (Frames.Count == 0) throw new InvalidDataException("프레임이 없는 GIF입니다.");
    }

    long SkipSubBlocks(long pos)
    {
        while (pos < _length)
        {
            int len = _data[pos];
            pos++;
            if (len == 0) break;
            pos += len;
        }
        return pos;
    }

    public ReadOnlySpan<byte> Bytes(long start, long end) => new(_data + start, (int)(end - start));

    // ───────────────────────── 디코딩 / 합성 ─────────────────────────

    /// <summary>캔버스를 지운 상태(첫 프레임 직전)로 되돌린다.</summary>
    public void Reset(uint* canvas)
    {
        new Span<uint>(canvas, Width * Height).Clear();
        _decoded = -1;
        _pendingDispose = null;
    }

    /// <summary>저장해 둔 캔버스 상태(index 프레임까지 합성됨)에서 이어서 디코딩할 수 있게 상태를 맞춘다.</summary>
    public void RestoreState(int index)
    {
        _decoded = index;
        _pendingDispose = index >= 0 ? Frames[index] : null;
    }

    /// <summary>다음 프레임을 캔버스에 합성한다.</summary>
    public void DecodeNext(uint* canvas)
    {
        int index = _decoded + 1;
        if (index >= Frames.Count) return;
        var f = Frames[index];

        // 직전 프레임의 처분 방식 적용 (2 = 배경으로 지우기)
        if (_pendingDispose is { Disposal: 2 } d)
            ClearRect(canvas, d.X, d.Y, d.W, d.H);

        int n = f.W * f.H;
        if (_indices.Length < n) _indices = new byte[n];
        int got = DecodeLzw(f, _indices, n);

        if (f.PaletteOffset >= 0)
        {
            byte* pal = _data + f.PaletteOffset;
            for (int row = 0; row < f.H; row++)
            {
                int y = f.Y + (f.Interlaced ? InterlacedRow(row, f.H) : row);
                if (y >= Height) continue;
                uint* dst = canvas + y * Width;
                int src = row * f.W;
                for (int col = 0; col < f.W; col++, src++)
                {
                    int x = f.X + col;
                    if (x >= Width || src >= got) continue;
                    byte idx = _indices[src];
                    if (f.Transparent && idx == f.TransparentIndex) continue;
                    if (idx >= f.PaletteSize) continue;
                    byte* c = pal + idx * 3;
                    dst[x] = 0xFF000000u | (uint)c[0] << 16 | (uint)c[1] << 8 | c[2];
                }
            }
        }

        _pendingDispose = f;
        _decoded = index;
    }

    void ClearRect(uint* canvas, int x, int y, int w, int h)
    {
        for (int row = y; row < Math.Min(Height, y + h); row++)
            for (int col = x; col < Math.Min(Width, x + w); col++)
                canvas[row * Width + col] = 0;
    }

    static int InterlacedRow(int row, int h)
    {
        // GIF 인터레이스: 8줄 간격(0) → 8줄(4) → 4줄(2) → 2줄(1)
        int p1 = (h + 7) / 8, p2 = (h + 3) / 8, p3 = (h + 1) / 4;
        if (row < p1) return row * 8;
        row -= p1;
        if (row < p2) return row * 8 + 4;
        row -= p2;
        if (row < p3) return row * 4 + 2;
        row -= p3;
        return row * 2 + 1;
    }

    int DecodeLzw(GifFrameInfo f, byte[] output, int maxPixels)
    {
        long pos = f.LzwOffset;
        int minCode = _data[pos++];
        if (minCode < 2 || minCode > 11) return 0;
        int clear = 1 << minCode, eoi = clear + 1;
        int codeSize = minCode + 1, codeMask = (1 << codeSize) - 1;
        int next = clear + 2;
        for (int i = 0; i < clear; i++) { _prefix[i] = 0; _suffix[i] = (byte)i; }

        int blockLeft = 0;
        int bits = 0, accum = 0;
        int old = -1, first = 0, outPos = 0;

        while (outPos < maxPixels)
        {
            // 코드 하나 읽기
            while (bits < codeSize)
            {
                if (blockLeft == 0)
                {
                    blockLeft = _data[pos++];
                    if (blockLeft == 0) return outPos;
                }
                accum |= _data[pos++] << bits;
                bits += 8;
                blockLeft--;
            }
            int code = accum & codeMask;
            accum >>= codeSize;
            bits -= codeSize;

            if (code == clear)
            {
                codeSize = minCode + 1;
                codeMask = (1 << codeSize) - 1;
                next = clear + 2;
                old = -1;
                continue;
            }
            if (code == eoi) break;

            if (old == -1)
            {
                if (code >= clear) return outPos; // 손상
                output[outPos++] = _suffix[code];
                old = code;
                first = code;
                continue;
            }

            int inCode = code;
            int sp = 0;
            if (code >= next)
            {
                _stack[sp++] = (byte)first;
                code = old;
            }
            while (code >= clear)
            {
                if (sp >= _stack.Length - 1) return outPos; // 손상
                _stack[sp++] = _suffix[code];
                code = _prefix[code];
            }
            first = _suffix[code];
            _stack[sp++] = (byte)first;

            while (sp > 0 && outPos < maxPixels) output[outPos++] = _stack[--sp];

            if (next < 4096)
            {
                _prefix[next] = (ushort)old;
                _suffix[next] = (byte)first;
                next++;
                if (next == (1 << codeSize) && codeSize < 12)
                {
                    codeSize++;
                    codeMask = (1 << codeSize) - 1;
                }
            }
            old = inCode;
        }
        return outPos;
    }

    public void Dispose()
    {
        if (_view != null)
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
            _view.Dispose();
            _view = null;
        }
        _map?.Dispose();
        _map = null;
        _data = null;
    }
}

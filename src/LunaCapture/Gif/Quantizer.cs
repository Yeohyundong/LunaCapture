namespace LunaCapture.Gif;

/// <summary>
/// 한 프레임(의 바뀐 영역)을 최대 255색 팔레트로 줄인다.
/// - 색이 255개 이하(UI/문서 화면에 흔함)면 정확한 색 그대로 → 무손실.
/// - 넘으면 6비트/채널 히스토그램에 미디언 컷 → 디더링 없이(화면 녹화는 디더링이 오히려 용량만 키움).
/// 인덱스 255는 "이전 프레임 그대로(투명)"용으로 비워 둔다.
/// </summary>
internal sealed unsafe class Quantizer
{
    public const byte TransparentIndex = 255;
    const int MaxColors = 255;
    const int HistSize = 1 << 18;

    public readonly uint[] Palette = new uint[256]; // 0x00RRGGBB
    public int PaletteCount;

    // 정확한 색 모드용 작은 해시 테이블
    const int ExactSize = 1024;
    readonly uint[] _exactKeys = new uint[ExactSize];
    readonly byte[] _exactVals = new byte[ExactSize];

    // 히스토그램 모드
    readonly int[] _hist = new int[HistSize];
    readonly byte[] _lut = new byte[HistSize];
    readonly List<int> _occupied = new();
    Bin[] _bins = new Bin[4096];

    struct Bin { public int Key, R, G, B, Count; }
    struct Box { public int Start, End; public long Score; public int Axis, AxisMin, AxisMax; }

    static int Key(uint c) => (int)(((c >> 18) & 0x3F) << 12 | ((c >> 10) & 0x3F) << 6 | ((c >> 2) & 0x3F));

    /// <summary>
    /// frame/prev는 전체 프레임(stride = width). rect 안의 픽셀을 인덱스로 바꿔 indices에 쓴다.
    /// prev가 null이 아니면 바뀌지 않은 픽셀은 TransparentIndex가 된다.
    /// </summary>
    public void Process(uint* frame, uint* prevPtr, int width, int rx, int ry, int rw, int rh, byte[] indices)
    {
        {
            bool exact = CollectColors(frame, prevPtr, width, rx, ry, rw, rh);
            if (!exact)
            {
                long t = System.Diagnostics.Stopwatch.GetTimestamp();
                BuildMedianCut();
                Profile.MedianCut += System.Diagnostics.Stopwatch.GetTimestamp() - t;
            }
            Map(frame, prevPtr, width, rx, ry, rw, rh, indices, exact);
            ResetHistogram();
        }
    }

    bool CollectColors(uint* frame, uint* prev, int width, int rx, int ry, int rw, int rh)
    {
        Array.Clear(_exactKeys);
        PaletteCount = 0;
        bool exact = true;
        uint last = 0xFFFFFFFF;

        for (int y = ry; y < ry + rh; y++)
        {
            uint* row = frame + y * width;
            uint* prow = prev == null ? null : prev + y * width;
            for (int x = rx; x < rx + rw; x++)
            {
                uint c = row[x] & 0xFFFFFF;
                if (prow != null && c == (prow[x] & 0xFFFFFF)) continue;

                int k = Key(c);
                if (_hist[k]++ == 0) _occupied.Add(k);

                if (exact && c != last)
                {
                    last = c;
                    if (!ExactInsert(c)) exact = false;
                }
            }
        }
        if (PaletteCount == 0) { Palette[0] = 0; PaletteCount = 1; }
        return exact;
    }

    bool ExactInsert(uint c)
    {
        uint key = c | 0x01000000;
        int h = (int)((c * 2654435761u) >> 22) & (ExactSize - 1);
        while (true)
        {
            uint k = _exactKeys[h];
            if (k == key) return true;
            if (k == 0)
            {
                if (PaletteCount >= MaxColors) return false;
                _exactKeys[h] = key;
                _exactVals[h] = (byte)PaletteCount;
                Palette[PaletteCount++] = c;
                return true;
            }
            h = (h + 1) & (ExactSize - 1);
        }
    }

    byte ExactLookup(uint c)
    {
        uint key = c | 0x01000000;
        int h = (int)((c * 2654435761u) >> 22) & (ExactSize - 1);
        while (_exactKeys[h] != key) h = (h + 1) & (ExactSize - 1);
        return _exactVals[h];
    }

    void BuildMedianCut()
    {
        int n = _occupied.Count;
        if (_bins.Length < n) _bins = new Bin[Math.Max(n, _bins.Length * 2)];
        for (int i = 0; i < n; i++)
        {
            int k = _occupied[i];
            _bins[i] = new Bin { Key = k, R = k >> 12, G = (k >> 6) & 0x3F, B = k & 0x3F, Count = _hist[k] };
        }

        var boxes = new List<Box>(MaxColors) { MakeBox(0, n) };
        while (boxes.Count < MaxColors)
        {
            int best = -1;
            long bestScore = 0;
            for (int i = 0; i < boxes.Count; i++)
                if (boxes[i].End - boxes[i].Start > 1 && boxes[i].Score > bestScore) { best = i; bestScore = boxes[i].Score; }
            if (best < 0) break;

            var box = boxes[best];
            int split = SplitAtMedian(box);
            boxes[best] = MakeBox(box.Start, split);
            boxes.Add(MakeBox(split, box.End));
        }

        PaletteCount = boxes.Count;
        for (int b = 0; b < boxes.Count; b++)
        {
            long r = 0, g = 0, bl = 0, cnt = 0;
            for (int i = boxes[b].Start; i < boxes[b].End; i++)
            {
                ref var bin = ref _bins[i];
                r += (long)Expand(bin.R) * bin.Count;
                g += (long)Expand(bin.G) * bin.Count;
                bl += (long)Expand(bin.B) * bin.Count;
                cnt += bin.Count;
                _lut[bin.Key] = (byte)b;
            }
            cnt = Math.Max(1, cnt);
            Palette[b] = (uint)((r / cnt) << 16 | (g / cnt) << 8 | (bl / cnt));
        }
    }

    static int Expand(int v6) => (v6 << 2) | (v6 >> 4);

    static int AxisValue(in Bin b, int axis) => axis == 0 ? b.R : axis == 1 ? b.G : b.B;

    /// <summary>
    /// 상자를 가장 긴 축의 (픽셀 수 기준) 중앙값에서 둘로 나눈다.
    /// 전체 정렬 대신 "값별 개수 세기(64칸) → 중앙값 찾기 → 한 번 훑으며 좌우로 가르기"만 해서 O(n).
    /// 반환값: 오른쪽 상자의 시작 위치.
    /// </summary>
    int SplitAtMedian(Box box)
    {
        Span<long> counts = stackalloc long[64];
        counts.Clear();
        long total = 0;
        for (int i = box.Start; i < box.End; i++)
        {
            ref var b = ref _bins[i];
            counts[AxisValue(b, box.Axis)] += b.Count;
            total += b.Count;
        }

        long acc = 0;
        int median = box.AxisMin;
        for (int v = box.AxisMin; v <= box.AxisMax; v++)
        {
            acc += counts[v];
            if (acc * 2 >= total) { median = v; break; }
        }
        // 양쪽 모두 비지 않게: 왼쪽 = 값 ≤ median 이므로 median은 최대값보다 작아야 한다.
        if (median >= box.AxisMax) median = box.AxisMax - 1;

        int lo = box.Start, hi = box.End - 1;
        while (lo <= hi)
        {
            if (AxisValue(_bins[lo], box.Axis) <= median) lo++;
            else
            {
                (_bins[lo], _bins[hi]) = (_bins[hi], _bins[lo]);
                hi--;
            }
        }
        return lo;
    }

    Box MakeBox(int start, int end)
    {
        int rMin = 63, rMax = 0, gMin = 63, gMax = 0, bMin = 63, bMax = 0;
        long count = 0;
        for (int i = start; i < end; i++)
        {
            ref var b = ref _bins[i];
            rMin = Math.Min(rMin, b.R); rMax = Math.Max(rMax, b.R);
            gMin = Math.Min(gMin, b.G); gMax = Math.Max(gMax, b.G);
            bMin = Math.Min(bMin, b.B); bMax = Math.Max(bMax, b.B);
            count += b.Count;
        }
        int rr = rMax - rMin, gr = gMax - gMin, br = bMax - bMin;
        // 사람 눈은 초록에 민감 → 약간 가중치
        int wr = rr * 3, wg = gr * 4, wb = br * 2;
        int axis = wr >= wg && wr >= wb ? 0 : wg >= wb ? 1 : 2;
        int range = Math.Max(wr, Math.Max(wg, wb));
        var (aMin, aMax) = axis switch { 0 => (rMin, rMax), 1 => (gMin, gMax), _ => (bMin, bMax) };
        return new Box
        {
            Start = start, End = end, Axis = axis, AxisMin = aMin, AxisMax = aMax,
            Score = range * (long)Math.Sqrt(count + 1),
        };
    }

    void Map(uint* frame, uint* prev, int width, int rx, int ry, int rw, int rh, byte[] indices, bool exact)
    {
        int o = 0;
        uint lastC = 0xFFFFFFFF;
        byte lastI = 0;
        for (int y = ry; y < ry + rh; y++)
        {
            uint* row = frame + y * width;
            uint* prow = prev == null ? null : prev + y * width;
            for (int x = rx; x < rx + rw; x++, o++)
            {
                uint c = row[x] & 0xFFFFFF;
                if (prow != null && c == (prow[x] & 0xFFFFFF))
                {
                    indices[o] = TransparentIndex;
                    continue;
                }
                if (c != lastC)
                {
                    lastC = c;
                    lastI = exact ? ExactLookup(c) : _lut[Key(c)];
                }
                indices[o] = lastI;
            }
        }
    }

    void ResetHistogram()
    {
        foreach (int k in _occupied) _hist[k] = 0;
        _occupied.Clear();
    }
}

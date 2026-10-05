using LunaCapture.Gif;
using LunaCapture.Graphics;
using LunaCapture.Interop;

namespace LunaCapture.Recording;

internal enum Motion { Low, Medium, High }

/// <summary>
/// "목표 용량" 기능의 두뇌.
/// 1) 녹화 전에 현재 화면을 크기별로 실제 GIF 인코딩해 본다 → 첫 프레임 크기(K)를 안다.
/// 2) 이후 프레임들의 용량은 화면 움직임 정도로 추정: 초당 ≈ K × (a + b × fps).
/// 3) 목표 용량 안에 들어가는 가장 좋은 (크기, fps) 조합을 고른다.
/// 추정은 어디까지나 예상치이고, 실제 녹화 중에는 인코더가 정확한 용량을 보고 한도 직전에 멈춘다.
/// </summary>
internal sealed class GifEstimator
{
    public static readonly float[] Scales = [1f, 0.75f, 0.5f, 0.33f];
    public static readonly int[] FpsOptions = [10, 15, 20, 25, 30];

    // 품질 우선순위: 해상도를 먼저 지키고, 그다음 부드러움.
    static readonly (float scale, int fps)[] QualityOrder =
    [
        (1f, 30), (1f, 25), (1f, 20), (1f, 15), (0.75f, 20), (0.75f, 15), (1f, 10),
        (0.5f, 20), (0.75f, 10), (0.5f, 15), (0.5f, 10), (0.33f, 15), (0.33f, 10),
    ];

    readonly long[] _keyBytes = new long[Scales.Length];
    readonly RECT _rect;

    GifEstimator(RECT rect) => _rect = rect;

    /// <summary>현재 화면의 해당 영역을 각 크기로 한 장씩 인코딩해 본다.</summary>
    public static unsafe GifEstimator Measure(RECT rect)
    {
        var est = new GifEstimator(rect);
        using var full = GdiSurface.CaptureScreen(rect.Left, rect.Top, rect.Width, rect.Height);
        for (int i = 0; i < Scales.Length; i++)
        {
            var (w, h) = RecordOptions.ScaledSize(rect, Scales[i]);
            using var scaled = new GdiSurface(w, h);
            if (i == 0)
            {
                Gdi32.BitBlt(scaled.Dc, 0, 0, w, h, full.Dc, 0, 0, Win.SRCCOPY);
            }
            else
            {
                Gdi32.SetStretchBltMode(scaled.Dc, Win.HALFTONE);
                Gdi32.SetBrushOrgEx(scaled.Dc, 0, 0, null);
                Gdi32.StretchBlt(scaled.Dc, 0, 0, w, h, full.Dc, 0, 0, rect.Width, rect.Height, Win.SRCCOPY);
            }
            Gdi32.GdiFlush();

            var counter = new CountingStream();
            using (var enc = new GifEncoder(counter, w, h))
            {
                enc.AddFrame((uint*)scaled.Bits, 0);
                enc.Finish(100);
            }
            est._keyBytes[i] = counter.Length;
        }
        return est;
    }

    public RECT Rect => _rect;

    static (double a, double b) Rate(Motion m) => m switch
    {
        Motion.Low => (0.5, 0.02),    // 문서·UI 조작: 바뀌는 부분이 적음
        Motion.Medium => (1.4, 0.08), // 스크롤, 창 이동 등
        _ => (2.0, 0.55),             // 영상·게임: 매 프레임 대부분이 바뀜
    };

    public double EstimateBytes(float scale, int fps, double seconds, Motion motion)
    {
        long k = _keyBytes[ScaleIndex(scale)];
        var (a, b) = Rate(motion);
        return k + seconds * k * (a + b * fps);
    }

    /// <summary>이 설정으로 목표 용량까지 녹화할 수 있는 대략적인 시간(초).</summary>
    public double MaxSeconds(float scale, int fps, long limitBytes, Motion motion)
    {
        long k = _keyBytes[ScaleIndex(scale)];
        var (a, b) = Rate(motion);
        double perSec = k * (a + b * fps);
        return Math.Max(0, (limitBytes - k) / perSec);
    }

    /// <summary>목표 용량(여유 10%) 안에 들어가는 가장 좋은 조합. 아무것도 안 맞으면 가장 가벼운 조합.</summary>
    public (float scale, int fps, bool fits) Recommend(long limitBytes, double seconds, Motion motion)
    {
        foreach (var (s, f) in QualityOrder)
            if (EstimateBytes(s, f, seconds, motion) <= limitBytes * 0.9) return (s, f, true);
        var last = QualityOrder[^1];
        return (last.scale, last.fps, false);
    }

    static int ScaleIndex(float scale)
    {
        for (int i = 0; i < Scales.Length; i++)
            if (MathF.Abs(Scales[i] - scale) < 0.01f) return i;
        return 0;
    }

    /// <summary>쓰기만 하고 길이만 세는 스트림.</summary>
    sealed class CountingStream : Stream
    {
        long _len;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _len;
        public override long Position { get => _len; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => _len += count;
        public override void Write(ReadOnlySpan<byte> buffer) => _len += buffer.Length;
        public override void WriteByte(byte value) => _len++;
    }
}

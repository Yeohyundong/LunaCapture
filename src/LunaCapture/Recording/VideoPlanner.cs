using LunaCapture.Interop;

namespace LunaCapture.Recording;

/// <summary>
/// 동영상 용량 계산. 동영상은 GIF와 달리 "비트레이트 × 시간 = 용량"이 거의 그대로 맞는다(고정 비트레이트).
/// 목표 용량 ÷ 길이로 비트레이트를 정하고, 그 비트레이트로 화질이 충분히 나오는 해상도·fps를 고른다.
/// </summary>
internal static class VideoPlanner
{
    public static readonly float[] Scales = [1f, 0.75f, 0.5f];
    public static readonly int[] FpsOptions = [24, 30, 60];
    public static readonly double[] QualityBpp = [0.12, 0.07, 0.035]; // 높음/보통/낮음 (픽셀·프레임당 비트)

    static readonly (float scale, int fps)[] QualityOrder =
        [(1f, 30), (0.75f, 30), (1f, 24), (0.75f, 24), (0.5f, 30), (0.5f, 24)];

    const double MinGoodBpp = 0.05;
    const long ContainerOverhead = 64 * 1024;

    public static int BitrateForTarget(long limitBytes, double seconds) =>
        (int)Math.Clamp((limitBytes * 0.94 - ContainerOverhead) * 8 / seconds, 150_000, 80_000_000);

    public static int BitrateForQuality(int w, int h, int fps, int qualityIndex) =>
        (int)Math.Clamp(w * (double)h * fps * QualityBpp[qualityIndex], 300_000, 80_000_000);

    public static double Bpp(int bitrate, int w, int h, int fps) => bitrate / (w * (double)h * fps);

    public static (float scale, int fps) Recommend(RECT rect, int bitrate)
    {
        foreach (var (s, f) in QualityOrder)
        {
            var (w, h) = RecordOptions.ScaledSize(rect, s, even: true);
            if (Bpp(bitrate, w, h, f) >= MinGoodBpp) return (s, f);
        }
        return QualityOrder[^1];
    }

    public static string QualityLabel(double bpp) =>
        bpp >= 0.08 ? "선명" : bpp >= 0.045 ? "보통" : bpp >= 0.025 ? "낮음" : "매우 낮음";

    public static long EstimateBytes(int bitrate, double seconds) => (long)(bitrate / 8.0 * seconds) + ContainerOverhead;
}

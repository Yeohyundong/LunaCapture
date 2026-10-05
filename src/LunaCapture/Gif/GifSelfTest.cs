namespace LunaCapture.Gif;

/// <summary>개발용: `LunaCapture.exe --gif-selftest out.gif` 로 합성 애니메이션을 인코딩해 본다.</summary>
internal static unsafe class GifSelfTest
{
    /// <summary>
    /// 개발용 벤치마크: `--gif-bench report.txt`
    /// 실제 화면(주 모니터)을 한 장 찍고, 매 프레임 스크롤한 것처럼 밀어 가며 60장을 인코딩해 단계별 시간을 잰다.
    /// </summary>
    public static void Bench(string reportPath)
    {
        int w = Interop.User32.GetSystemMetrics(0), h = Interop.User32.GetSystemMetrics(1);
        using var shot = Graphics.GdiSurface.CaptureScreen(0, 0, w, h);
        var src = new uint[w * h];
        fixed (uint* s = src) Buffer.MemoryCopy(shot.Bits, s, src.Length * 4L, src.Length * 4L);

        var report = new System.Text.StringBuilder();
        foreach (var scale in new[] { 1, 2 })
        {
            int fw = w / scale, fh = h / scale;
            var frame = new uint[fw * fh];
            const int frames = 60;
            Profile.Reset();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long bytes;
            using (var ms = new MemoryStream())
            {
                using var enc = new GifEncoder(ms, fw, fh);
                fixed (uint* f = frame)
                {
                    for (int i = 0; i < frames; i++)
                    {
                        int shift = i * 9; // 프레임마다 9px 스크롤
                        for (int y = 0; y < fh; y++)
                        {
                            int sy = ((y + shift) * scale) % h;
                            for (int x = 0; x < fw; x++) f[y * fw + x] = src[sy * w + x * scale];
                        }
                        enc.AddFrame(f, i * 33);
                    }
                    enc.Finish(frames * 33);
                }
                bytes = ms.Length;
            }
            sw.Stop();
            report.AppendLine($"{fw}x{fh} {frames}프레임 전체 스크롤: 총 {sw.ElapsedMilliseconds}ms ({sw.ElapsedMilliseconds / (double)frames:0.0}ms/프레임), {bytes / 1024}KB");
            report.AppendLine($"  diff {Profile.Ms(Profile.Diff):0}ms · quantize {Profile.Ms(Profile.Quantize):0}ms (그중 median cut {Profile.Ms(Profile.MedianCut):0}ms) · lzw {Profile.Ms(Profile.Lzw):0}ms");

            // 같은 입력을 병렬 인코더로
            sw.Restart();
            int workers;
            using (var ms = new MemoryStream())
            {
                using var enc = new ParallelGifEncoder(ms, fw, fh, 0);
                workers = enc.Workers;
                fixed (uint* f = frame)
                {
                    for (int i = 0; i < frames; i++)
                    {
                        int shift = i * 9;
                        for (int y = 0; y < fh; y++)
                        {
                            int sy = ((y + shift) * scale) % h;
                            for (int x = 0; x < fw; x++) f[y * fw + x] = src[sy * w + x * scale];
                        }
                        enc.AddFrame(f, i * 33);
                    }
                    enc.Finish(frames * 33);
                }
                report.AppendLine($"  병렬({workers}개 작업자): 총 {sw.ElapsedMilliseconds}ms ({sw.ElapsedMilliseconds / (double)frames:0.0}ms/프레임), {ms.Length / 1024}KB (같아야 함)");
            }
        }
        File.WriteAllText(reportPath, report.ToString());
    }

    public static void Run(string path)
    {
        const int w = 320, h = 200;
        var frame = new uint[w * h];
        using var fs = File.Create(path);
        using var enc = new GifEncoder(fs, w, h);

        fixed (uint* f = frame)
        {
            for (int i = 0; i < 40; i++)
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        // 배경: 그라디언트(256색 초과 → 미디언 컷 경로), 움직이는 사각형, 10~19번째는 정지 화면
                        int t = i is >= 10 and < 20 ? 10 : i;
                        uint bg = (uint)((x * 255 / w) << 16 | (y * 255 / h) << 8 | 128);
                        bool box = x >= t * 6 && x < t * 6 + 40 && y >= 80 && y < 120;
                        f[y * w + x] = box ? 0xFFFFFF : bg;
                    }
                enc.AddFrame(f, i * 50);
            }
            enc.Finish(40 * 50);
        }
    }
}

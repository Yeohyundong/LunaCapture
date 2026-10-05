using System.Buffers.Binary;
using System.IO.Compression;

namespace LunaCapture.Graphics;

/// <summary>
/// 의존성 없는 PNG 인코더. 스크린샷은 항상 불투명이므로 RGB 24bit로 저장해 용량을 줄인다.
/// 행마다 5가지 필터 중 가장 압축이 잘 될 것을 고른다(표준 휴리스틱).
/// </summary>
internal static class PngEncoder
{
    static readonly uint[] CrcTable = BuildCrcTable();

    public static void Save(PixelImage img, string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        Write(img, fs);
    }

    /// <param name="keepAlpha">true면 RGBA(투명도 포함, 아이콘용), 아니면 RGB</param>
    public static void Write(PixelImage img, Stream output, bool keepAlpha = false)
    {
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, img.Width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], img.Height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = (byte)(keepAlpha ? 6 : 2);  // color type: RGBA / RGB
        ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk(output, "IHDR"u8, ihdr);

        using (var idat = new MemoryStream())
        {
            using (var z = new ZLibStream(idat, CompressionLevel.Optimal, leaveOpen: true))
                WriteFilteredRows(img, z, keepAlpha ? 4 : 3);
            WriteChunk(output, "IDAT"u8, idat.GetBuffer().AsSpan(0, (int)idat.Length));
        }

        WriteChunk(output, "IEND"u8, []);
    }

    static void WriteFilteredRows(PixelImage img, Stream z, int bpp)
    {
        int rowLen = img.Width * bpp;
        var prev = new byte[rowLen];
        var cur = new byte[rowLen];
        var best = new byte[rowLen + 1];
        var trial = new byte[rowLen + 1];
        var src = img.Bgra;

        for (int y = 0; y < img.Height; y++)
        {
            int o = y * img.Stride;
            for (int x = 0, i = 0; x < img.Width; x++, i += bpp, o += 4)
            {
                cur[i] = src[o + 2];     // R
                cur[i + 1] = src[o + 1]; // G
                cur[i + 2] = src[o];     // B
                if (bpp == 4) cur[i + 3] = src[o + 3]; // A
            }

            long bestScore = long.MaxValue;
            for (byte f = 0; f <= 4; f++)
            {
                long score = ApplyFilter(f, cur, prev, trial, bpp);
                if (score < bestScore)
                {
                    bestScore = score;
                    (best, trial) = (trial, best);
                }
            }
            z.Write(best);
            (prev, cur) = (cur, prev);
        }
    }

    static long ApplyFilter(byte filter, byte[] cur, byte[] prev, byte[] outRow, int bpp)
    {
        outRow[0] = filter;
        long sum = 0;
        for (int i = 0; i < cur.Length; i++)
        {
            int a = i >= bpp ? cur[i - bpp] : 0;
            int b = prev[i];
            int c = i >= bpp ? prev[i - bpp] : 0;
            int v = filter switch
            {
                0 => cur[i],
                1 => cur[i] - a,
                2 => cur[i] - b,
                3 => cur[i] - ((a + b) >> 1),
                _ => cur[i] - Paeth(a, b, c),
            };
            byte bv = (byte)v;
            outRow[i + 1] = bv;
            sum += (sbyte)bv < 0 ? -(sbyte)bv : (sbyte)bv;
        }
        return sum;
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    static void WriteChunk(Stream s, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buf, data.Length);
        s.Write(buf);
        s.Write(type);
        s.Write(data);
        uint crc = Crc(Crc(0xFFFFFFFFu, type), data) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(buf, crc);
        s.Write(buf);
    }

    static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }
}

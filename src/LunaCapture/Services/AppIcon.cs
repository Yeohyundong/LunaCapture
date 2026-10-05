using LunaCapture.Graphics;
using LunaCapture.Interop;

namespace LunaCapture.Services;

/// <summary>
/// 앱 아이콘: 보라색 둥근 사각형 위의 흰 초승달(Luna).
/// 이미지 파일 없이 코드로 그려서, 어떤 크기·DPI에서도 선명하게 만든다.
/// </summary>
internal static unsafe class AppIcon
{
    const int PixelFormat32bppARGB = 0x26200A;

    /// <summary>size×size 아이콘을 BGRA(직선 알파)로 그린다.</summary>
    public static PixelImage Render(int size)
    {
        var img = new PixelImage(size, size);
        fixed (byte* p = img.Bgra)
        {
            nint bmp;
            GdiPlus.GdipCreateBitmapFromScan0(size, size, size * 4, PixelFormat32bppARGB, p, &bmp);
            using (var g = Gfx.FromImage(bmp))
            {
                float s = size;
                float inset = size <= 20 ? 0 : s * 0.03f;
                float box = s - inset * 2;

                // 배경: 보라 그라데이션 둥근 사각형
                var p1 = new PointF(inset, inset);
                var p2 = new PointF(s - inset, s - inset);
                nint grad;
                GdiPlus.GdipCreateLineBrush(&p1, &p2, 0xFFA393FF, 0xFF6650DE, 0, &grad);
                FillRound(g, grad, inset, inset, box, box, box * 0.23f);
                GdiPlus.GdipDeleteBrush(grad);

                // 초승달: 큰 원(바깥)에서 오른쪽 위로 비켜 난 원(안쪽)을 뺀 모양.
                // 영역 빼기(Region)는 안티앨리어싱이 안 돼 가장자리가 계단처럼 보이므로,
                // 두 원의 교점을 계산해 "바깥 호 + 안쪽 호"로 된 윤곽선(Path)을 만들어 매끄럽게 칠한다.
                float R = box * 0.36f, cx = s * 0.47f, cy = s * 0.53f;
                float r = R * 0.84f, ccx = cx + R * 0.50f, ccy = cy - R * 0.38f;
                nint moon = CrescentPath(cx, cy, R, ccx, ccy, r);
                nint white;
                GdiPlus.GdipCreateSolidFill(0xFFFFFFFF, &white);
                GdiPlus.GdipFillPath(g.Handle, white, moon);
                GdiPlus.GdipDeleteBrush(white);
                GdiPlus.GdipDeletePath(moon);
            }
            GdiPlus.GdipDisposeImage(bmp);
        }
        return img;
    }

    /// <summary>원(cx,cy,R)에서 원(ox,oy,r)을 뺀 초승달 윤곽선. 각도는 GDI+ 규칙(시계 방향, y 아래로).</summary>
    static nint CrescentPath(float cx, float cy, float R, float ox, float oy, float r)
    {
        // 두 원의 교점
        float dx = ox - cx, dy = oy - cy, d = MathF.Sqrt(dx * dx + dy * dy);
        float a = (R * R - r * r + d * d) / (2 * d);
        float hgt = MathF.Sqrt(MathF.Max(0, R * R - a * a));
        float bx = cx + a * dx / d, by = cy + a * dy / d;
        var p1 = (x: bx - hgt * dy / d, y: by + hgt * dx / d);
        var p2 = (x: bx + hgt * dy / d, y: by - hgt * dx / d);

        static float Angle(float px, float py, float ccx, float ccy) => MathF.Atan2(py - ccy, px - ccx) * 180 / MathF.PI;
        static float Mod360(float v) => ((v % 360) + 360) % 360;

        // 바깥 원: 안쪽 원 쪽(φ)을 지나지 않는 방향의 호
        float a1 = Angle(p1.x, p1.y, cx, cy), a2 = Angle(p2.x, p2.y, cx, cy);
        float phi = Angle(ox, oy, cx, cy);
        float start = a1, sweep = Mod360(a2 - a1);
        (float x, float y) end = p2;
        if (Mod360(phi - a1) < sweep) { start = a2; sweep = Mod360(a1 - a2); end = p1; }
        var begin = end == p2 ? p1 : p2;

        // 안쪽 원: 끝점에서 시작점으로, 바깥 원 중심 쪽(ψ)을 지나는 호
        float b0 = Angle(end.x, end.y, ox, oy), b1 = Angle(begin.x, begin.y, ox, oy);
        float psi = Angle(cx, cy, ox, oy);
        float sweep2 = Mod360(b1 - b0);
        if (Mod360(psi - b0) >= sweep2) sweep2 -= 360;

        nint path;
        GdiPlus.GdipCreatePath(0, &path);
        GdiPlus.GdipAddPathArc(path, cx - R, cy - R, R * 2, R * 2, start, sweep);
        GdiPlus.GdipAddPathArc(path, ox - r, oy - r, r * 2, r * 2, b0, sweep2);
        GdiPlus.GdipClosePathFigure(path);
        return path;
    }

    static void FillRound(Gfx g, nint brush, float x, float y, float w, float h, float r)
    {
        nint path;
        GdiPlus.GdipCreatePath(0, &path);
        float d = r * 2;
        GdiPlus.GdipAddPathArc(path, x, y, d, d, 180, 90);
        GdiPlus.GdipAddPathArc(path, x + w - d, y, d, d, 270, 90);
        GdiPlus.GdipAddPathArc(path, x + w - d, y + h - d, d, d, 0, 90);
        GdiPlus.GdipAddPathArc(path, x, y + h - d, d, d, 90, 90);
        GdiPlus.GdipClosePathFigure(path);
        GdiPlus.GdipFillPath(g.Handle, brush, path);
        GdiPlus.GdipDeletePath(path);
    }

    /// <summary>트레이·창에 쓸 HICON을 만든다.</summary>
    public static nint Load(int size)
    {
        var img = Render(size);
        var bih = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = size,
            biHeight = -size,
            biPlanes = 1,
            biBitCount = 32,
        };
        void* bits;
        nint color = Gdi32.CreateDIBSection(0, &bih, 0, &bits, 0, 0);
        fixed (byte* src = img.Bgra) Buffer.MemoryCopy(src, bits, img.Bgra.Length, img.Bgra.Length);
        nint mask = CreateBitmap(size, size, 1, 1, null);

        var ii = new ICONINFO { fIcon = 1, hbmColor = color, hbmMask = mask };
        nint icon = CreateIconIndirect(&ii);
        Gdi32.DeleteObject(color);
        Gdi32.DeleteObject(mask);
        return icon != 0 ? icon : User32.LoadIconW(0, Win.IDI_APPLICATION);
    }

    /// <summary>개발용: exe에 넣을 .ico 파일(여러 크기, PNG 압축)을 만든다. `--make-icon app.ico`</summary>
    public static void WriteIco(string path)
    {
        // 관례대로: 작은 크기는 BMP(호환성), 256은 PNG(용량)
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 256];
        var pngs = sizes.Select(s =>
        {
            using var ms = new MemoryStream();
            if (s >= 256) PngEncoder.Write(Render(s), ms, keepAlpha: true);
            else WriteIconDib(Render(s), ms);
            return ms.ToArray();
        }).ToArray();

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((ushort)1); w.Write((ushort)32);
            w.Write(pngs[i].Length);
            w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var png in pngs) w.Write(png);
    }

    /// <summary>ICO 안의 BMP 항목: 헤더(높이 2배) + 32bpp 아래→위 픽셀 + 1bpp 마스크(전부 0, 알파가 대신함).</summary>
    static void WriteIconDib(PixelImage img, Stream s)
    {
        using var w = new BinaryWriter(s, System.Text.Encoding.UTF8, leaveOpen: true);
        int size = img.Width;
        int maskStride = ((size + 31) / 32) * 4;
        w.Write(40); w.Write(size); w.Write(size * 2);
        w.Write((ushort)1); w.Write((ushort)32);
        w.Write(0); w.Write(size * size * 4 + maskStride * size);
        w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (int y = size - 1; y >= 0; y--) w.Write(img.Bgra, y * img.Stride, img.Stride);
        w.Write(new byte[maskStride * size]);
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    static extern nint CreateBitmap(int w, int h, uint planes, uint bpp, void* bits);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern nint CreateIconIndirect(ICONINFO* info);
}

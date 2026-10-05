using LunaCapture.Interop;

namespace LunaCapture.Graphics;

/// <summary>
/// 메모리 DC + 32bpp top-down DIB. 픽셀(BGRA)에 직접 접근할 수 있다.
/// </summary>
internal sealed unsafe class GdiSurface : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public nint Dc { get; private set; }
    public byte* Bits { get; private set; }
    public int Stride => Width * 4;

    nint _bitmap, _oldBitmap;

    public GdiSurface(int width, int height)
    {
        Width = width;
        Height = height;

        nint screenDc = User32.GetDC(0);
        Dc = Gdi32.CreateCompatibleDC(screenDc);
        User32.ReleaseDC(0, screenDc);

        var bih = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = width,
            biHeight = -height, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        void* bits;
        _bitmap = Gdi32.CreateDIBSection(Dc, &bih, 0, &bits, 0, 0);
        if (_bitmap == 0) throw new OutOfMemoryException($"CreateDIBSection {width}x{height} failed");
        Bits = (byte*)bits;
        _oldBitmap = Gdi32.SelectObject(Dc, _bitmap);
    }

    /// <summary>화면의 (x, y)부터 이 서피스 크기만큼 복사한다.</summary>
    public static GdiSurface CaptureScreen(int x, int y, int width, int height)
    {
        var s = new GdiSurface(width, height);
        nint screenDc = User32.GetDC(0);
        Gdi32.BitBlt(s.Dc, 0, 0, width, height, screenDc, x, y, Win.SRCCOPY | Win.CAPTUREBLT);
        User32.ReleaseDC(0, screenDc);
        Gdi32.GdiFlush();
        return s;
    }

    /// <summary>지정 영역을 잘라 불투명 BGRA 이미지로 복사한다.</summary>
    public PixelImage Crop(RECT r)
    {
        var img = new PixelImage(r.Width, r.Height);
        fixed (byte* dst = img.Bgra)
        {
            for (int y = 0; y < r.Height; y++)
            {
                uint* s = (uint*)(Bits + (r.Top + y) * Stride + r.Left * 4);
                uint* d = (uint*)(dst + y * img.Stride);
                for (int x = 0; x < r.Width; x++) d[x] = s[x] | 0xFF000000u;
            }
        }
        return img;
    }

    public void Dispose()
    {
        if (Dc != 0)
        {
            Gdi32.SelectObject(Dc, _oldBitmap);
            Gdi32.DeleteObject(_bitmap);
            Gdi32.DeleteDC(Dc);
            Dc = 0;
            Bits = null;
        }
    }
}

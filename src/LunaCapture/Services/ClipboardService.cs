using LunaCapture.Graphics;
using LunaCapture.Interop;

namespace LunaCapture.Services;

internal static unsafe class ClipboardService
{
    /// <summary>
    /// 파일 자체를 클립보드에 넣는다 (탐색기에서 Ctrl+C 한 것과 같음).
    /// 메신저·디스코드·슬랙 등에 Ctrl+V 하면 GIF 파일이 그대로 첨부된다.
    /// </summary>
    public static bool SetFile(nint owner, string path)
    {
        // DROPFILES 헤더(20바이트) + 유니코드 경로 + 이중 널
        const int headerSize = 20;
        int bytes = headerSize + (path.Length + 2) * 2;
        nint mem = Kernel32.GlobalAlloc(Win.GMEM_MOVEABLE | 0x0040 /* GMEM_ZEROINIT */, (nuint)bytes);
        if (mem == 0) return false;

        byte* p = (byte*)Kernel32.GlobalLock(mem);
        *(uint*)p = headerSize;   // pFiles
        *(int*)(p + 16) = 1;      // fWide
        char* dst = (char*)(p + headerSize);
        for (int i = 0; i < path.Length; i++) dst[i] = path[i];
        Kernel32.GlobalUnlock(mem);

        return SetData(owner, Win.CF_HDROP, mem);
    }

    static bool SetData(nint owner, uint format, nint mem)
    {
        for (int i = 0; i < 10; i++)
        {
            if (User32.OpenClipboard(owner) != 0)
            {
                User32.EmptyClipboard();
                bool ok = User32.SetClipboardData(format, mem) != 0;
                User32.CloseClipboard();
                if (!ok) Kernel32.GlobalFree(mem);
                return ok;
            }
            Thread.Sleep(20);
        }
        Kernel32.GlobalFree(mem);
        return false;
    }

    /// <summary>이미지를 CF_DIB(32bpp, bottom-up)로 클립보드에 넣는다.</summary>
    public static bool SetImage(nint owner, PixelImage img)
    {
        int headerSize = sizeof(BITMAPINFOHEADER);
        int pixelBytes = img.Stride * img.Height;
        nint mem = Kernel32.GlobalAlloc(Win.GMEM_MOVEABLE, (nuint)(headerSize + pixelBytes));
        if (mem == 0) return false;

        byte* p = (byte*)Kernel32.GlobalLock(mem);
        *(BITMAPINFOHEADER*)p = new BITMAPINFOHEADER
        {
            biSize = (uint)headerSize,
            biWidth = img.Width,
            biHeight = img.Height, // bottom-up (가장 호환성 좋음)
            biPlanes = 1,
            biBitCount = 32,
            biSizeImage = (uint)pixelBytes,
        };
        fixed (byte* src = img.Bgra)
        {
            byte* dst = p + headerSize;
            for (int y = 0; y < img.Height; y++)
                Buffer.MemoryCopy(src + (img.Height - 1 - y) * img.Stride, dst + y * img.Stride, img.Stride, img.Stride);
        }
        Kernel32.GlobalUnlock(mem);

        // 다른 프로그램이 클립보드를 잡고 있을 수 있어 SetData가 몇 번 재시도한다.
        return SetData(owner, Win.CF_DIB, mem);
    }
}

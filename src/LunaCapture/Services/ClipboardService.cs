using LunaCapture.Graphics;
using LunaCapture.Interop;

namespace LunaCapture.Services;

internal static unsafe class ClipboardService
{
    internal const int MaxObjectBytes = 64 * 1024 * 1024;
    static readonly uint ObjectFormat = User32.RegisterClipboardFormatW("LunaCapture.EditableObjects.v1");
    public static bool HasObjects => ObjectFormat != 0 && User32.IsClipboardFormatAvailable(ObjectFormat) != 0;
    /// <summary>
    /// 파일 자체를 클립보드에 넣는다 (탐색기에서 Ctrl+C 한 것과 같음).
    /// 메신저·디스코드·슬랙 등에 Ctrl+V 하면 GIF 파일이 그대로 첨부된다.
    /// </summary>
    public static bool SetFile(nint owner, string path, bool preserveObjects = false)
    {
        byte[]? objects = preserveObjects ? GetObjects(owner) : null;
        if (preserveObjects && HasObjects && objects == null) return false;
        // DROPFILES 헤더(20바이트) + 유니코드 경로 + 이중 널
        const int headerSize = 20;
        int bytes = headerSize + (path.Length + 2) * 2;
        nint mem = Kernel32.GlobalAlloc(Win.GMEM_MOVEABLE | 0x0040 /* GMEM_ZEROINIT */, (nuint)bytes);
        if (mem == 0) return false;

        byte* p = (byte*)Kernel32.GlobalLock(mem);
        if (p == null) { Kernel32.GlobalFree(mem); return false; }
        *(uint*)p = headerSize;   // pFiles
        *(int*)(p + 16) = 1;      // fWide
        char* dst = (char*)(p + headerSize);
        for (int i = 0; i < path.Length; i++) dst[i] = path[i];
        Kernel32.GlobalUnlock(mem);

        return SetData(owner, Win.CF_HDROP, mem, objects);
    }

    static bool SetData(nint owner, uint format, nint mem, byte[]? objects = null)
    {
        nint objectMemory = 0;
        if (objects != null)
        {
            if (ObjectFormat == 0 || objects.Length > MaxObjectBytes) { Kernel32.GlobalFree(mem); return false; }
            objectMemory = Kernel32.GlobalAlloc(Win.GMEM_MOVEABLE, (nuint)(objects.Length + 4));
            if (objectMemory == 0) { Kernel32.GlobalFree(mem); return false; }
            byte* destination = (byte*)Kernel32.GlobalLock(objectMemory);
            if (destination == null) { Kernel32.GlobalFree(mem); Kernel32.GlobalFree(objectMemory); return false; }
            *(int*)destination = objects.Length;
            objects.CopyTo(new Span<byte>(destination + 4, objects.Length));
            Kernel32.GlobalUnlock(objectMemory);
        }
        for (int i = 0; i < 10; i++)
        {
            if (User32.OpenClipboard(owner) != 0)
            {
                if (User32.EmptyClipboard() == 0)
                {
                    User32.CloseClipboard();
                    Kernel32.GlobalFree(mem);
                    if (objectMemory != 0) Kernel32.GlobalFree(objectMemory);
                    return false;
                }
                bool ok = User32.SetClipboardData(format, mem) != 0;
                bool objectOk = objectMemory == 0 || User32.SetClipboardData(ObjectFormat, objectMemory) != 0;
                User32.CloseClipboard();
                if (!ok) Kernel32.GlobalFree(mem);
                if (!objectOk) Kernel32.GlobalFree(objectMemory);
                return ok && objectOk;
            }
            Thread.Sleep(20);
        }
        Kernel32.GlobalFree(mem);
        if (objectMemory != 0) Kernel32.GlobalFree(objectMemory);
        return false;
    }

    public static byte[]? GetObjects(nint owner)
    {
        if (!HasObjects) return null;
        bool opened = false;
        for (int i = 0; i < 10; i++)
        {
            if (User32.OpenClipboard(owner) != 0) { opened = true; break; }
            Thread.Sleep(20);
        }
        if (!opened) return null;
        try
        {
            nint memory = User32.GetClipboardData(ObjectFormat);
            if (memory == 0 || Kernel32.GlobalSize(memory) < 4) return null;
            byte* data = (byte*)Kernel32.GlobalLock(memory);
            if (data == null) return null;
            try
            {
                int length = *(int*)data;
                if (length <= 0 || length > MaxObjectBytes || (ulong)length + 4 > (ulong)Kernel32.GlobalSize(memory)) return null;
                return new ReadOnlySpan<byte>(data + 4, length).ToArray();
            }
            finally { Kernel32.GlobalUnlock(memory); }
        }
        finally { User32.CloseClipboard(); }
    }

    /// <summary>이미지를 CF_DIB(32bpp, bottom-up)로 클립보드에 넣는다.</summary>
    public static bool SetImage(nint owner, PixelImage img, bool preserveObjects = false)
    {
        byte[]? objects = preserveObjects ? GetObjects(owner) : null;
        if (preserveObjects && HasObjects && objects == null) return false;
        return SetObjectImage(owner, img, objects);
    }

    public static bool SetObjectImage(nint owner, PixelImage img, byte[]? objects)
    {
        int headerSize = sizeof(BITMAPINFOHEADER);
        int pixelBytes = img.Stride * img.Height;
        nint mem = Kernel32.GlobalAlloc(Win.GMEM_MOVEABLE, (nuint)(headerSize + pixelBytes));
        if (mem == 0) return false;

        byte* p = (byte*)Kernel32.GlobalLock(mem);
        if (p == null) { Kernel32.GlobalFree(mem); return false; }
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
        return SetData(owner, Win.CF_DIB, mem, objects);
    }

    /// <summary>클립보드의 그림을 읽는다 (24/32bpp DIB). 그림이 없거나 읽을 수 없으면 null.</summary>
    public static PixelImage? GetImage(nint owner)
    {
        const uint CF_DIBV5 = 17;
        uint format = User32.IsClipboardFormatAvailable(CF_DIBV5) != 0 ? CF_DIBV5
            : User32.IsClipboardFormatAvailable(Win.CF_DIB) != 0 ? Win.CF_DIB : 0;
        if (format == 0) return null;

        for (int i = 0; i < 10 && User32.OpenClipboard(owner) == 0; i++) Thread.Sleep(20);
        try
        {
            nint h = User32.GetClipboardData(format);
            if (h == 0) return null;
            byte* p = (byte*)Kernel32.GlobalLock(h);
            if (p == null) return null;
            try { return ParseDib(p, (long)Kernel32.GlobalSize(h)); }
            finally { Kernel32.GlobalUnlock(h); }
        }
        catch (Exception ex) { Log.Error(ex); return null; }
        finally { User32.CloseClipboard(); }
    }

    static PixelImage? ParseDib(byte* p, long size)
    {
        uint hdr = *(uint*)p;
        int w = *(int*)(p + 4), hRaw = *(int*)(p + 8);
        int bpp = *(ushort*)(p + 14);
        uint compression = *(uint*)(p + 16), clrUsed = *(uint*)(p + 32);
        if (w <= 0 || hRaw == 0 || (bpp != 32 && bpp != 24) || (compression != 0 && compression != 3)) return null;
        int h = Math.Abs(hRaw);
        bool topDown = hRaw < 0;
        // BI_BITFIELDS면 색 마스크 12바이트가 헤더 뒤에 붙을 수 있다: 기본 헤더(40바이트)면 항상,
        // 큰 헤더(V4/V5)도 Windows가 변환해 넣은 경우엔 헤더 안 마스크를 한 번 더 붙인다 → 같은 값이면 건너뛴다
        bool masksFollow = compression == 3 && (hdr == 40 ||
            (hdr >= 52 && size >= hdr + 12 && *(uint*)(p + hdr) == *(uint*)(p + 40) && *(uint*)(p + hdr + 4) == *(uint*)(p + 44) && *(uint*)(p + hdr + 8) == *(uint*)(p + 48)));
        long offset = hdr + (masksFollow ? 12 : 0) + clrUsed * 4;
        int stride = ((w * bpp / 8) + 3) & ~3;
        if (offset + (long)stride * h > size) return null;

        var img = new PixelImage(w, h);
        bool anyAlpha = false;
        fixed (byte* dst = img.Bgra)
        {
            for (int y = 0; y < h; y++)
            {
                byte* s = p + offset + (long)(topDown ? y : h - 1 - y) * stride;
                byte* d = dst + y * img.Stride;
                if (bpp == 32)
                {
                    Buffer.MemoryCopy(s, d, img.Stride, img.Stride);
                    for (int x = 0; x < w && !anyAlpha; x++) anyAlpha = d[x * 4 + 3] != 0;
                }
                else
                {
                    for (int x = 0; x < w; x++)
                    {
                        d[x * 4] = s[x * 3]; d[x * 4 + 1] = s[x * 3 + 1]; d[x * 4 + 2] = s[x * 3 + 2]; d[x * 4 + 3] = 255;
                    }
                }
            }
            // 알파를 안 쓰는 프로그램이 많다 (전부 0) → 불투명으로
            if (bpp == 32 && !anyAlpha)
                for (int i = 3; i < img.Bgra.Length; i += 4) dst[i] = 255;
        }
        return img;
    }
}

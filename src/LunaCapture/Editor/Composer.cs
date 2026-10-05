using LunaCapture.Graphics;
using LunaCapture.Interop;

namespace LunaCapture.Editor;

/// <summary>원본 이미지 + 주석들 → 완성 이미지. 편집기와 캡처 기록이 같이 쓴다.</summary>
internal static unsafe class Composer
{
    /// <summary>주석 하나를 서피스에 그린다.</summary>
    public static void Draw(GdiSurface target, Annotation a)
    {
        // 주석마다 Graphics를 새로 만들어야 모자이크(픽셀 직접 수정) 전에 앞의 그리기가 반영된다.
        using var g = new Gfx(target.Dc, forImage: true);
        a.Draw(target, g);
    }

    public static PixelImage Compose(PixelImage original, IEnumerable<Annotation> items)
    {
        using var surf = new GdiSurface(original.Width, original.Height);
        fixed (byte* src = original.Bgra)
            Buffer.MemoryCopy(src, surf.Bits, (long)surf.Stride * surf.Height, original.Bgra.Length);
        foreach (var a in items) Draw(surf, a);
        Gdi32.GdiFlush();
        return surf.Crop(new RECT(0, 0, surf.Width, surf.Height));
    }

    /// <summary>긴 변이 maxSide 이하가 되게 줄인 미리보기 (부드럽게 축소).</summary>
    public static PixelImage Thumbnail(PixelImage image, int maxSide)
    {
        float k = MathF.Min(1f, maxSide / (float)Math.Max(image.Width, image.Height));
        int w = Math.Max(1, (int)(image.Width * k)), h = Math.Max(1, (int)(image.Height * k));
        using var src = new GdiSurface(image.Width, image.Height);
        fixed (byte* p = image.Bgra)
            Buffer.MemoryCopy(p, src.Bits, image.Bgra.Length, image.Bgra.Length);
        using var dst = new GdiSurface(w, h);
        Gdi32.SetStretchBltMode(dst.Dc, Win.HALFTONE);
        Gdi32.SetBrushOrgEx(dst.Dc, 0, 0, null);
        Gdi32.StretchBlt(dst.Dc, 0, 0, w, h, src.Dc, 0, 0, image.Width, image.Height, Win.SRCCOPY);
        Gdi32.GdiFlush();
        return dst.Crop(new RECT(0, 0, w, h));
    }
}

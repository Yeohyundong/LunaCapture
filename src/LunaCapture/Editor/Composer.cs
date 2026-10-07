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

    /// <summary>
    /// 주석 목록 전체를 순서대로 그린다. 강조(스포트라이트)는 여러 개여도 첫 강조 자리에서 한 번만,
    /// 모든 강조 영역을 밝게 남기고 어둡게 한다 (강조끼리 서로 어둡게 덮지 않도록).
    /// </summary>
    public static void DrawAll(GdiSurface target, IReadOnlyList<Annotation> items)
    {
        bool dimmed = false;
        foreach (var a in items)
        {
            if (a is SpotlightAnnotation)
            {
                if (dimmed) continue;
                dimmed = true;
                var holes = new List<RectF>();
                foreach (var s in items) if (s is SpotlightAnnotation sp) holes.Add(sp.Box);
                SpotlightAnnotation.DimOutside(target, holes);
                continue;
            }
            Draw(target, a);
        }
    }

    public static PixelImage Compose(PixelImage original, IReadOnlyList<Annotation> items)
    {
        using var surf = new GdiSurface(original.Width, original.Height);
        fixed (byte* src = original.Bgra)
            Buffer.MemoryCopy(src, surf.Bits, (long)surf.Stride * surf.Height, original.Bgra.Length);
        DrawAll(surf, items);
        Gdi32.GdiFlush();
        return surf.Crop(new RECT(0, 0, surf.Width, surf.Height));
    }

    /// <summary>이미지의 일부(r)를 잘라 새 이미지로.</summary>
    public static PixelImage Crop(PixelImage image, RECT r)
    {
        var img = new PixelImage(r.Width, r.Height);
        for (int y = 0; y < r.Height; y++)
            Buffer.BlockCopy(image.Bgra, ((r.Top + y) * image.Width + r.Left) * 4, img.Bgra, y * img.Stride, img.Stride);
        return img;
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

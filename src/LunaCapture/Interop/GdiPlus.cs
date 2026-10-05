using System.Runtime.InteropServices;

namespace LunaCapture.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct PointF
{
    public float X, Y;
    public PointF(float x, float y) { X = x; Y = y; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct RectF
{
    public float X, Y, Width, Height;
    public RectF(float x, float y, float w, float h) { X = x; Y = y; Width = w; Height = h; }
}

/// <summary>GDI+ flat API (윈도우 내장, 안티앨리어싱 도형/텍스트용).</summary>
internal static unsafe partial class GdiPlus
{
    const string Dll = "gdiplus.dll";

    [StructLayout(LayoutKind.Sequential)]
    struct StartupInput
    {
        public uint Version;
        public nint DebugCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }

    static nint s_token;
    static nint s_format;

    public static nint TypographicFormat => s_format;

    public static void Startup()
    {
        var input = new StartupInput { Version = 1 };
        nint token;
        if (GdiplusStartup(&token, &input, 0) != 0) throw new InvalidOperationException("GDI+ 초기화 실패");
        s_token = token;

        nint generic, fmt;
        GdipStringFormatGetGenericTypographic(&generic);
        GdipCloneStringFormat(generic, &fmt);
        // NoClip | NoFitBlackBox | MeasureTrailingSpaces | NoWrap
        GdipSetStringFormatFlags(fmt, 0x4000 | 0x0004 | 0x0800 | 0x1000);
        s_format = fmt;
    }

    public static void Shutdown()
    {
        if (s_format != 0) GdipDeleteStringFormat(s_format);
        if (s_token != 0) GdiplusShutdown(s_token);
        s_format = s_token = 0;
    }

    [LibraryImport(Dll)] private static partial int GdiplusStartup(nint* token, StartupInput* input, nint output);
    [LibraryImport(Dll)] private static partial void GdiplusShutdown(nint token);

    [LibraryImport(Dll)] public static partial int GdipCreateFromHDC(nint hdc, nint* graphics);
    [LibraryImport(Dll)] public static partial int GdipDeleteGraphics(nint graphics);
    [LibraryImport(Dll)] public static partial int GdipSetSmoothingMode(nint graphics, int mode);
    [LibraryImport(Dll)] public static partial int GdipSetPixelOffsetMode(nint graphics, int mode);
    [LibraryImport(Dll)] public static partial int GdipSetTextRenderingHint(nint graphics, int hint);

    [LibraryImport(Dll)] public static partial int GdipCreatePen1(uint argb, float width, int unit, nint* pen);
    [LibraryImport(Dll)] public static partial int GdipDeletePen(nint pen);
    [LibraryImport(Dll)] public static partial int GdipSetPenLineCap197819(nint pen, int startCap, int endCap, int dashCap);
    [LibraryImport(Dll)] public static partial int GdipSetPenLineJoin(nint pen, int join);
    [LibraryImport(Dll)] public static partial int GdipCreateSolidFill(uint argb, nint* brush);
    [LibraryImport(Dll)] public static partial int GdipDeleteBrush(nint brush);

    [LibraryImport(Dll)] public static partial int GdipDrawLine(nint g, nint pen, float x1, float y1, float x2, float y2);
    [LibraryImport(Dll)] public static partial int GdipDrawLines(nint g, nint pen, PointF* points, int count);
    [LibraryImport(Dll)] public static partial int GdipDrawRectangle(nint g, nint pen, float x, float y, float w, float h);
    [LibraryImport(Dll)] public static partial int GdipFillRectangle(nint g, nint brush, float x, float y, float w, float h);
    [LibraryImport(Dll)] public static partial int GdipFillEllipse(nint g, nint brush, float x, float y, float w, float h);
    [LibraryImport(Dll)] public static partial int GdipDrawEllipse(nint g, nint pen, float x, float y, float w, float h);
    [LibraryImport(Dll)] public static partial int GdipFillPolygon(nint g, nint brush, PointF* points, int count, int fillMode);

    [LibraryImport(Dll)] public static partial int GdipCreateBitmapFromScan0(int w, int h, int stride, int format, byte* scan0, nint* bitmap);
    [LibraryImport(Dll)] public static partial int GdipGetImageGraphicsContext(nint image, nint* graphics);
    [LibraryImport(Dll)] public static partial int GdipDisposeImage(nint image);
    [LibraryImport(Dll)] public static partial int GdipCreateLineBrush(PointF* p1, PointF* p2, uint c1, uint c2, int wrap, nint* brush);
    [LibraryImport(Dll)] public static partial int GdipAddPathEllipse(nint path, float x, float y, float w, float h);
    [LibraryImport(Dll)] public static partial int GdipCreateRegionPath(nint path, nint* region);
    [LibraryImport(Dll)] public static partial int GdipCombineRegionPath(nint region, nint path, int mode);
    [LibraryImport(Dll)] public static partial int GdipFillRegion(nint g, nint brush, nint region);
    [LibraryImport(Dll)] public static partial int GdipDeleteRegion(nint region);
    [StructLayout(LayoutKind.Sequential)]
    public struct GpRect { public int X, Y, Width, Height; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapData
    {
        public uint Width, Height;
        public int Stride, PixelFormat;
        public byte* Scan0;
        public nuint Reserved;
    }

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GdipCreateBitmapFromFile(string path, nint* bitmap);
    [LibraryImport(Dll)] public static partial int GdipGetImageWidth(nint image, uint* width);
    [LibraryImport(Dll)] public static partial int GdipGetImageHeight(nint image, uint* height);
    [LibraryImport(Dll)] public static partial int GdipBitmapLockBits(nint bitmap, GpRect* rect, uint flags, int format, BitmapData* data);
    [LibraryImport(Dll)] public static partial int GdipBitmapUnlockBits(nint bitmap, BitmapData* data);

    /// <summary>이미지 파일(PNG 등)을 읽어 BGRA 픽셀로. 파일은 바로 놓아준다(잠그지 않음).</summary>
    public static Graphics.PixelImage? LoadImage(string path)
    {
        if (!File.Exists(path)) return null;
        nint bmp;
        if (GdipCreateBitmapFromFile(path, &bmp) != 0) return null;
        try
        {
            uint w, h;
            GdipGetImageWidth(bmp, &w);
            GdipGetImageHeight(bmp, &h);
            var rect = new GpRect { Width = (int)w, Height = (int)h };
            BitmapData data;
            if (GdipBitmapLockBits(bmp, &rect, 1 /* Read */, 0x26200A /* 32bppARGB */, &data) != 0) return null;
            var img = new Graphics.PixelImage((int)w, (int)h);
            fixed (byte* dst = img.Bgra)
                for (int y = 0; y < h; y++)
                    Buffer.MemoryCopy(data.Scan0 + (long)y * data.Stride, dst + (long)y * img.Stride, img.Stride, img.Stride);
            GdipBitmapUnlockBits(bmp, &data);
            return img;
        }
        finally
        {
            GdipDisposeImage(bmp);
        }
    }

    [LibraryImport(Dll)] public static partial int GdipDrawArc(nint g, nint pen, float x, float y, float w, float h, float start, float sweep);
    [LibraryImport(Dll)] public static partial int GdipAddPathPolygon(nint path, PointF* points, int count);
    [LibraryImport(Dll)] public static partial int GdipAddPathBezier(nint path, float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4);
    [LibraryImport(Dll)] public static partial int GdipAddPathLine(nint path, float x1, float y1, float x2, float y2);
    [LibraryImport(Dll)] public static partial int GdipCreatePath(int fillMode, nint* path);
    [LibraryImport(Dll)] public static partial int GdipDeletePath(nint path);
    [LibraryImport(Dll)] public static partial int GdipAddPathArc(nint path, float x, float y, float w, float h, float start, float sweep);
    [LibraryImport(Dll)] public static partial int GdipClosePathFigure(nint path);
    [LibraryImport(Dll)] public static partial int GdipFillPath(nint g, nint brush, nint path);
    [LibraryImport(Dll)] public static partial int GdipDrawPath(nint g, nint pen, nint path);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GdipCreateFontFamilyFromName(string name, nint collection, nint* family);
    [LibraryImport(Dll)] public static partial int GdipDeleteFontFamily(nint family);
    [LibraryImport(Dll)] public static partial int GdipNewPrivateFontCollection(nint* collection);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GdipPrivateAddFontFile(nint collection, string path);
    [LibraryImport(Dll)] public static partial int GdipPrivateAddMemoryFont(nint collection, void* memory, int length);
    [LibraryImport(Dll)] public static partial int GdipCreateFont(nint family, float size, int style, int unit, nint* font);
    [LibraryImport(Dll)] public static partial int GdipDeleteFont(nint font);
    [LibraryImport(Dll)] public static partial int GdipGetFontHeight(nint font, nint graphics, float* height);

    [LibraryImport(Dll)] private static partial int GdipStringFormatGetGenericTypographic(nint* format);
    [LibraryImport(Dll)] private static partial int GdipCloneStringFormat(nint format, nint* clone);
    [LibraryImport(Dll)] private static partial int GdipSetStringFormatFlags(nint format, int flags);
    [LibraryImport(Dll)] private static partial int GdipDeleteStringFormat(nint format);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GdipDrawString(nint g, string text, int length, nint font, RectF* layout, nint format, nint brush);
    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GdipMeasureString(nint g, string text, int length, nint font, RectF* layout, nint format,
        RectF* bounds, int* codepointsFitted, int* linesFilled);
}

/// <summary>GDI+ Graphics를 감싼 간단한 그리기 도우미. 펜/브러시는 호출마다 만들고 바로 해제한다.</summary>
/// <summary>앱 전체에서 쓰는 글꼴 이름. 글꼴을 바꾸려면 여기만 고치면 된다.</summary>
internal static unsafe partial class UiFonts
{
    /// <summary>앱 화면 글꼴: Pretendard를 앱에 필요한 글자만 남겨 줄인 "Luna Sans" (exe에 내장, tools/make_ui_font.py로 생성).</summary>
    public const string Family = "Luna Sans";
    public const string BoldFamily = "Luna Sans Bold";
    /// <summary>편집기 텍스트 도구 글꼴: 사용자가 어떤 글자를 입력해도 깨지지 않게 모든 한글이 있는 글꼴.</summary>
    public const string Annotation = "Malgun Gothic";
    public const string Fallback = "Malgun Gothic"; // 원하는 글꼴이 없을 때
    public const string Mono = "Consolas";
    public const string Symbol = "Segoe UI Symbol";

    /// <summary>설치하지 않은 글꼴을 앱 안에서만 쓰기 위한 모음 (GDI+ 용).</summary>
    public static nint PrivateCollection { get; private set; }

    [LibraryImport("gdi32.dll")]
    private static partial nint AddFontMemResourceEx(void* font, uint length, nint reserved, uint* count);

    static void EnsureCollection()
    {
        if (PrivateCollection != 0) return;
        nint c;
        GdiPlus.GdipNewPrivateFontCollection(&c);
        PrivateCollection = c;
    }

    public static bool AddFontFile(string path)
    {
        EnsureCollection();
        return GdiPlus.GdipPrivateAddFontFile(PrivateCollection, path) == 0;
    }

    /// <summary>
    /// exe에 내장된 글꼴(Fonts.*.ttf)을 불러온다. GDI+ 와 GDI(캡처 화면 안내 문구) 양쪽에 등록하며,
    /// 이 프로세스 안에서만 보이고 시스템에는 설치되지 않는다. 글꼴 메모리는 앱이 끝날 때까지 유지한다.
    /// </summary>
    public static void LoadEmbedded()
    {
        try
        {
            EnsureCollection();
            var asm = typeof(UiFonts).Assembly;
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (!name.StartsWith("Fonts.", StringComparison.Ordinal) || !name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)) continue;
                using var s = asm.GetManifestResourceStream(name)!;
                int len = (int)s.Length;
                byte* mem = (byte*)NativeMemory.Alloc((nuint)len);
                s.ReadExactly(new Span<byte>(mem, len));
                GdiPlus.GdipPrivateAddMemoryFont(PrivateCollection, mem, len);
                uint count;
                AddFontMemResourceEx(mem, (uint)len, 0, &count);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex); // 실패하면 대체 글꼴(맑은 고딕)로 표시된다
        }
    }
}

internal sealed unsafe class Gfx : IDisposable
{
    const int UnitPixel = 2;
    const int LineCapRound = 2, LineJoinRound = 2;
    const int PixelOffsetNone = 3, PixelOffsetHalf = 4;
    const int TextAntiAliasGridFit = 3, TextClearTypeGridFit = 5;

    nint _g;

    /// <param name="forImage">
    /// true = 캡처 이미지에 직접 굽는 그림(주석). ClearType의 색 번짐이 이미지에 남지 않게 흑백 안티앨리어싱을 쓴다.
    /// false = 화면에 보이는 UI. 윈도우와 같은 ClearType으로 또렷하게.
    /// </param>
    public Gfx(nint hdc, bool forImage = false)
    {
        nint g;
        GdiPlus.GdipCreateFromHDC(hdc, &g);
        _g = g;
        Setup(forImage ? TextAntiAliasGridFit : TextClearTypeGridFit);
    }

    /// <summary>GDI+ 비트맵(알파 채널 유지)에 그리는 Graphics.</summary>
    public static Gfx FromImage(nint bitmap)
    {
        nint g;
        GdiPlus.GdipGetImageGraphicsContext(bitmap, &g);
        return new Gfx(g, TextAntiAliasGridFit);
    }

    Gfx(nint graphics, int textHint)
    {
        _g = graphics;
        Setup(textHint);
    }

    void Setup(int textHint)
    {
        GdiPlus.GdipSetSmoothingMode(_g, 4);              // 도형: 안티앨리어싱
        GdiPlus.GdipSetPixelOffsetMode(_g, PixelOffsetHalf); // 도형: 픽셀 중심 정렬
        GdiPlus.GdipSetTextRenderingHint(_g, textHint);
    }

    public nint Handle => _g;

    nint MakePen(uint argb, float width)
    {
        nint pen;
        GdiPlus.GdipCreatePen1(argb, width, UnitPixel, &pen);
        GdiPlus.GdipSetPenLineCap197819(pen, LineCapRound, LineCapRound, 0);
        GdiPlus.GdipSetPenLineJoin(pen, LineJoinRound);
        return pen;
    }

    nint MakeBrush(uint argb)
    {
        nint brush;
        GdiPlus.GdipCreateSolidFill(argb, &brush);
        return brush;
    }

    public void Line(uint argb, float width, float x1, float y1, float x2, float y2)
    {
        nint pen = MakePen(argb, width);
        GdiPlus.GdipDrawLine(_g, pen, x1, y1, x2, y2);
        GdiPlus.GdipDeletePen(pen);
    }

    public void Lines(uint argb, float width, ReadOnlySpan<PointF> points)
    {
        if (points.Length < 2) return;
        nint pen = MakePen(argb, width);
        fixed (PointF* p = points) GdiPlus.GdipDrawLines(_g, pen, p, points.Length);
        GdiPlus.GdipDeletePen(pen);
    }

    public void Rect(uint argb, float width, float x, float y, float w, float h)
    {
        nint pen = MakePen(argb, width);
        GdiPlus.GdipDrawRectangle(_g, pen, x, y, w, h);
        GdiPlus.GdipDeletePen(pen);
    }

    public void FillRect(uint argb, float x, float y, float w, float h)
    {
        nint b = MakeBrush(argb);
        GdiPlus.GdipFillRectangle(_g, b, x, y, w, h);
        GdiPlus.GdipDeleteBrush(b);
    }

    public void FillEllipse(uint argb, float x, float y, float w, float h)
    {
        nint b = MakeBrush(argb);
        GdiPlus.GdipFillEllipse(_g, b, x, y, w, h);
        GdiPlus.GdipDeleteBrush(b);
    }

    public void Ellipse(uint argb, float width, float x, float y, float w, float h)
    {
        nint pen = MakePen(argb, width);
        GdiPlus.GdipDrawEllipse(_g, pen, x, y, w, h);
        GdiPlus.GdipDeletePen(pen);
    }

    public void FillPolygon(uint argb, ReadOnlySpan<PointF> points)
    {
        nint b = MakeBrush(argb);
        fixed (PointF* p = points) GdiPlus.GdipFillPolygon(_g, b, p, points.Length, 0);
        GdiPlus.GdipDeleteBrush(b);
    }

    nint RoundPath(float x, float y, float w, float h, float r)
    {
        nint path;
        GdiPlus.GdipCreatePath(0, &path);
        float d = MathF.Min(r * 2, MathF.Min(w, h));
        GdiPlus.GdipAddPathArc(path, x, y, d, d, 180, 90);
        GdiPlus.GdipAddPathArc(path, x + w - d, y, d, d, 270, 90);
        GdiPlus.GdipAddPathArc(path, x + w - d, y + h - d, d, d, 0, 90);
        GdiPlus.GdipAddPathArc(path, x, y + h - d, d, d, 90, 90);
        GdiPlus.GdipClosePathFigure(path);
        return path;
    }

    public void FillRoundRect(uint argb, float x, float y, float w, float h, float radius)
    {
        nint path = RoundPath(x, y, w, h, radius);
        nint b = MakeBrush(argb);
        GdiPlus.GdipFillPath(_g, b, path);
        GdiPlus.GdipDeleteBrush(b);
        GdiPlus.GdipDeletePath(path);
    }

    public void RoundRect(uint argb, float width, float x, float y, float w, float h, float radius)
    {
        nint path = RoundPath(x, y, w, h, radius);
        nint pen = MakePen(argb, width);
        GdiPlus.GdipDrawPath(_g, pen, path);
        GdiPlus.GdipDeletePen(pen);
        GdiPlus.GdipDeletePath(path);
    }

    // ── 경로(Path): 스티커·말풍선처럼 곡선이 섞인 도형용 ──

    /// <summary>새 경로. fillMode 1(Winding) = 겹치는 부분도 채움 (말풍선 몸통+꼬리를 한 덩어리로).</summary>
    public static nint NewPath(int fillMode = 1)
    {
        nint p;
        GdiPlus.GdipCreatePath(fillMode, &p);
        return p;
    }

    public static void DeletePath(nint path) => GdiPlus.GdipDeletePath(path);

    public static void AddEllipse(nint path, float x, float y, float w, float h) => GdiPlus.GdipAddPathEllipse(path, x, y, w, h);

    public static void AddPolygon(nint path, ReadOnlySpan<PointF> points)
    {
        fixed (PointF* p = points) GdiPlus.GdipAddPathPolygon(path, p, points.Length);
    }

    public static void AddRoundRect(nint path, float x, float y, float w, float h, float r)
    {
        float d = MathF.Min(r * 2, MathF.Min(w, h));
        GdiPlus.GdipAddPathArc(path, x, y, d, d, 180, 90);
        GdiPlus.GdipAddPathArc(path, x + w - d, y, d, d, 270, 90);
        GdiPlus.GdipAddPathArc(path, x + w - d, y + h - d, d, d, 0, 90);
        GdiPlus.GdipAddPathArc(path, x, y + h - d, d, d, 90, 90);
        GdiPlus.GdipClosePathFigure(path);
    }

    public void FillPath(nint path, uint argb)
    {
        nint b = MakeBrush(argb);
        GdiPlus.GdipFillPath(_g, b, path);
        GdiPlus.GdipDeleteBrush(b);
    }

    /// <summary>위(top)→아래(bottom)로 색이 바뀌는 세로 그라데이션으로 칠한다.</summary>
    public void FillPathGradient(nint path, uint top, uint bottom, float y0, float y1)
    {
        var p1 = new PointF(0, y0 - 1);
        var p2 = new PointF(0, y1 + 1);
        nint b;
        GdiPlus.GdipCreateLineBrush(&p1, &p2, top, bottom, 3 /* TileFlipXY */, &b);
        GdiPlus.GdipFillPath(_g, b, path);
        GdiPlus.GdipDeleteBrush(b);
    }

    public void DrawPath(nint path, uint argb, float width)
    {
        nint pen = MakePen(argb, width);
        GdiPlus.GdipDrawPath(_g, pen, path);
        GdiPlus.GdipDeletePen(pen);
    }

    /// <summary>사각형 안에 텍스트를 가운데 정렬로 그린다.</summary>
    public void CenterText(string text, GdiFont font, uint argb, float x, float y, float w, float h)
    {
        float tw = MeasureWidth(text, font), th = LineHeight(font);
        Text(text, font, argb, x + (w - tw) / 2, y + (h - th) / 2);
    }

    public void Text(string text, GdiFont font, uint argb, float x, float y)
    {
        if (text.Length == 0) return;
        nint b = MakeBrush(argb);
        // 글자는 반 픽셀 보정을 끄고 정수 좌표에 그려야 번지지 않는다.
        var layout = new RectF(MathF.Round(x), MathF.Round(y), 0, 0);
        GdiPlus.GdipSetPixelOffsetMode(_g, PixelOffsetNone);
        GdiPlus.GdipDrawString(_g, text, text.Length, font.Handle, &layout, GdiPlus.TypographicFormat, b);
        GdiPlus.GdipSetPixelOffsetMode(_g, PixelOffsetHalf);
        GdiPlus.GdipDeleteBrush(b);
    }

    /// <summary>
    /// 숫자를 같은 폭 칸에 그린다 (타이머처럼 계속 바뀌는 숫자가 좌우로 흔들리지 않게). 반환값 = 그린 전체 폭.
    /// </summary>
    public float TextTabular(string text, GdiFont font, uint argb, float x, float y)
    {
        float digitW = MeasureWidth("0", font);
        float cx = x;
        foreach (char c in text)
        {
            string s = c.ToString();
            float w = MeasureWidth(s, font);
            if (char.IsAsciiDigit(c))
            {
                Text(s, font, argb, cx + (digitW - w) / 2, y);
                cx += digitW;
            }
            else
            {
                Text(s, font, argb, cx, y);
                cx += c == ' ' ? MeasureWidth("a a", font) - MeasureWidth("aa", font) : w;
            }
        }
        return cx - x;
    }

    public float MeasureTabular(string text, GdiFont font)
    {
        float digitW = MeasureWidth("0", font), total = 0;
        foreach (char c in text)
            total += char.IsAsciiDigit(c) ? digitW
                : c == ' ' ? MeasureWidth("a a", font) - MeasureWidth("aa", font)
                : MeasureWidth(c.ToString(), font);
        return total;
    }

    public void Arc(uint argb, float width, float x, float y, float w, float h, float start, float sweep)
    {
        nint pen = MakePen(argb, width);
        GdiPlus.GdipDrawArc(_g, pen, x, y, w, h, start, sweep);
        GdiPlus.GdipDeletePen(pen);
    }

    public float MeasureWidth(string text, GdiFont font)
    {
        if (text.Length == 0) return 0;
        var layout = new RectF(0, 0, 100000, 100000);
        RectF bounds;
        int fitted, lines;
        GdiPlus.GdipMeasureString(_g, text, text.Length, font.Handle, &layout, GdiPlus.TypographicFormat, &bounds, &fitted, &lines);
        return bounds.Width;
    }

    public float LineHeight(GdiFont font)
    {
        float h;
        GdiPlus.GdipGetFontHeight(font.Handle, _g, &h);
        return h;
    }

    public void Dispose()
    {
        if (_g != 0) { GdiPlus.GdipDeleteGraphics(_g); _g = 0; }
    }
}

internal sealed unsafe class GdiFont : IDisposable
{
    nint _family, _font;
    public nint Handle => _font;
    public float Size { get; }

    public GdiFont(string face, float pixelSize, bool bold)
    {
        Size = pixelSize;
        nint family = 0, font;
        // 내장 UI 글꼴의 굵은 글씨는 별도 패밀리("Luna Sans Bold")의 보통 스타일로 들어 있다.
        if (bold && face == UiFonts.Family && UiFonts.PrivateCollection != 0 &&
            GdiPlus.GdipCreateFontFamilyFromName(UiFonts.BoldFamily, UiFonts.PrivateCollection, &family) == 0)
        {
            GdiPlus.GdipCreateFont(family, pixelSize, 0, 2 /* UnitPixel */, &font);
            _family = family;
            _font = font;
            return;
        }
        // 1) 앱이 직접 불러온 글꼴 → 2) 설치된 글꼴 → 3) 대체 글꼴
        bool found = UiFonts.PrivateCollection != 0 &&
            GdiPlus.GdipCreateFontFamilyFromName(face, UiFonts.PrivateCollection, &family) == 0;
        if (!found && GdiPlus.GdipCreateFontFamilyFromName(face, 0, &family) != 0)
            GdiPlus.GdipCreateFontFamilyFromName(UiFonts.Fallback, 0, &family);
        GdiPlus.GdipCreateFont(family, pixelSize, bold ? 1 : 0, 2 /* UnitPixel */, &font);
        _family = family;
        _font = font;
    }

    public void Dispose()
    {
        if (_font != 0) GdiPlus.GdipDeleteFont(_font);
        if (_family != 0) GdiPlus.GdipDeleteFontFamily(_family);
        _font = _family = 0;
    }
}

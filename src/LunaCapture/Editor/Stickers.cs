using LunaCapture.Interop;

namespace LunaCapture.Editor;

internal enum StickerKind { Check, Cross, Warning, Question, Heart, Star, Smile }

/// <summary>말풍선 꼬리가 나오는 곳 (None = 꼬리 없는 박스).</summary>
internal enum TailDir { BottomLeft, BottomRight, TopLeft, TopRight, Left, Right, None }

/// <summary>
/// 편집기의 스티커 · 번호 배지 · 말풍선 그림.
/// 이미지 파일 없이 도형으로 그린다(앱 용량·메모리 증가 없음). 납작하지 않게
/// 그림자 + 흰 테두리 + 위가 밝은 그라데이션 + 윗부분 광택으로 "스티커" 느낌을 낸다.
/// </summary>
internal static class Stickers
{
    public static readonly StickerKind[] All =
        [StickerKind.Check, StickerKind.Cross, StickerKind.Warning, StickerKind.Question, StickerKind.Heart, StickerKind.Star, StickerKind.Smile];

    public static readonly TailDir[] Tails =
        [TailDir.BottomLeft, TailDir.BottomRight, TailDir.TopLeft, TailDir.TopRight, TailDir.Left, TailDir.Right, TailDir.None];

    public static string Name(StickerKind k) => k switch
    {
        StickerKind.Check => "체크",
        StickerKind.Cross => "엑스",
        StickerKind.Warning => "주의",
        StickerKind.Question => "물음표",
        StickerKind.Heart => "하트",
        StickerKind.Star => "별",
        _ => "웃음",
    };

    const uint White = 0xFFFFFFFF;

    /// <summary>두 색을 섞는다 (t=0 → a, t=1 → b). 알파는 a 기준.</summary>
    public static uint Mix(uint a, uint b, float t)
    {
        uint Ch(int shift) => (uint)MathF.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return (a & 0xFF000000) | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    /// <summary>배경색 위에서 잘 보이는 글자색 (밝은 배경 → 진한 글자).</summary>
    public static uint ContrastText(uint bg)
    {
        float r = (bg >> 16) & 0xFF, g = (bg >> 8) & 0xFF, b = bg & 0xFF;
        float lum = (0.299f * r + 0.587f * g + 0.114f * b) / 255f;
        return lum > 0.68f ? 0xFF1F1F24 : White;
    }

    // ───────────────────────── 스티커 ─────────────────────────

    public static void Draw(Gfx g, StickerKind kind, float cx, float cy, float size)
    {
        switch (kind)
        {
            case StickerKind.Check:
                RoundBadge(g, cx, cy, size, 0xFF4ADE80, 0xFF16A34A);
                g.Lines(White, size * 0.11f, [new(cx - size * 0.2f, cy + size * 0.01f), new(cx - size * 0.05f, cy + size * 0.16f), new(cx + size * 0.22f, cy - size * 0.15f)]);
                break;

            case StickerKind.Cross:
            {
                RoundBadge(g, cx, cy, size, 0xFFFF7A7A, 0xFFE03131);
                float d = size * 0.15f;
                g.Line(White, size * 0.11f, cx - d, cy - d, cx + d, cy + d);
                g.Line(White, size * 0.11f, cx + d, cy - d, cx - d, cy + d);
                break;
            }

            case StickerKind.Question:
                RoundBadge(g, cx, cy, size, 0xFF6CC4FF, 0xFF1C7ED6);
                Glyph(g, "?", cx, cy, size * 0.5f, White);
                break;

            case StickerKind.Warning:
                Warning(g, cx, cy, size);
                break;

            case StickerKind.Heart:
                Heart(g, cx, cy, size);
                break;

            case StickerKind.Star:
                Star(g, cx, cy, size);
                break;

            case StickerKind.Smile:
                Smile(g, cx, cy, size);
                break;
        }
    }

    /// <summary>원형 배지: 그림자 → 흰 테두리 → 그라데이션 원 → 윗부분 광택.</summary>
    static void RoundBadge(Gfx g, float cx, float cy, float size, uint top, uint bottom)
    {
        float r = size / 2;
        Shadow(g, p => Gfx.AddEllipse(p, cx - r, cy - r, size, size), size);
        g.FillEllipse(White, cx - r, cy - r, size, size);
        float ir = r * 0.86f;
        nint body = Gfx.NewPath();
        Gfx.AddEllipse(body, cx - ir, cy - ir, ir * 2, ir * 2);
        g.FillPathGradient(body, top, bottom, cy - ir, cy + ir);
        Gfx.DeletePath(body);
        Gloss(g, cx, cy - ir * 0.52f, ir * 1.25f, ir * 0.62f);
    }

    /// <summary>부드러운 그림자: 아래로 조금씩 비켜서 옅게 두 번 칠한다.</summary>
    static void Shadow(Gfx g, Action<nint> shape, float size)
    {
        foreach (var (dy, alpha) in new[] { (size * 0.06f, 0x22u), (size * 0.03f, 0x30u) })
        {
            nint p = Gfx.NewPath();
            shape(p);
            using (var m = new PathOffset(p, 0, dy)) g.FillPath(p, alpha << 24);
            Gfx.DeletePath(p);
        }
    }

    static void Gloss(Gfx g, float cx, float cy, float w, float h)
    {
        nint p = Gfx.NewPath();
        Gfx.AddEllipse(p, cx - w / 2, cy - h / 2, w, h);
        g.FillPathGradient(p, 0x70FFFFFF, 0x00FFFFFF, cy - h / 2, cy + h / 2);
        Gfx.DeletePath(p);
    }

    static void Glyph(Gfx g, string text, float cx, float cy, float px, uint color)
    {
        using var font = new GdiFont(UiFonts.Family, px, bold: true);
        float w = g.MeasureWidth(text, font), h = g.LineHeight(font);
        g.Text(text, font, color, cx - w / 2, cy - h / 2);
    }

    static void Warning(Gfx g, float cx, float cy, float size)
    {
        // 둥근 삼각형: 흰 테두리(두꺼운 둥근 선) 위에 노란 삼각형
        float r = size * 0.5f;
        PointF[] tri = [new(cx, cy - r * 0.86f), new(cx + r * 0.95f, cy + r * 0.72f), new(cx - r * 0.95f, cy + r * 0.72f)];
        Shadow(g, p => Gfx.AddPolygon(p, tri), size);
        nint outline = Gfx.NewPath();
        Gfx.AddPolygon(outline, tri);
        g.DrawPath(outline, White, size * 0.16f);
        g.FillPath(outline, White);
        Gfx.DeletePath(outline);

        PointF[] inner = [new(cx, cy - r * 0.62f), new(cx + r * 0.72f, cy + r * 0.6f), new(cx - r * 0.72f, cy + r * 0.6f)];
        nint body = Gfx.NewPath();
        Gfx.AddPolygon(body, inner);
        g.DrawPath(body, 0xFFFFC53D, size * 0.08f); // 모서리를 둥글게
        g.FillPathGradient(body, 0xFFFFDE59, 0xFFF59F00, cy - r * 0.62f, cy + r * 0.6f);
        Gfx.DeletePath(body);

        uint ink = 0xFF4A3300;
        g.Line(ink, size * 0.09f, cx, cy - r * 0.2f, cx, cy + r * 0.2f);
        float dot = size * 0.1f;
        g.FillEllipse(ink, cx - dot / 2, cy + r * 0.36f, dot, dot);
    }

    static nint HeartPath(float cx, float cy, float s)
    {
        // 두 개의 베지어 곡선으로 만든 하트 (s = 폭)
        float w = s / 2, top = cy - s * 0.3f, bottom = cy + s * 0.42f;
        nint p = Gfx.NewPath();
        GdiPlus.GdipAddPathBezier(p, cx, bottom, cx - w * 0.2f, bottom - s * 0.16f, cx - w * 1.1f, cy, cx - w * 0.92f, top + s * 0.02f);
        GdiPlus.GdipAddPathBezier(p, cx - w * 0.92f, top + s * 0.02f, cx - w * 0.75f, top - s * 0.28f, cx - w * 0.05f, top - s * 0.2f, cx, top + s * 0.06f);
        GdiPlus.GdipAddPathBezier(p, cx, top + s * 0.06f, cx + w * 0.05f, top - s * 0.2f, cx + w * 0.75f, top - s * 0.28f, cx + w * 0.92f, top + s * 0.02f);
        GdiPlus.GdipAddPathBezier(p, cx + w * 0.92f, top + s * 0.02f, cx + w * 1.1f, cy, cx + w * 0.2f, bottom - s * 0.16f, cx, bottom);
        GdiPlus.GdipClosePathFigure(p);
        return p;
    }

    static void Heart(Gfx g, float cx, float cy, float size)
    {
        float s = size * 0.98f;
        foreach (var (dy, alpha) in new[] { (size * 0.06f, 0x22u), (size * 0.03f, 0x30u) })
        {
            nint sh = HeartPath(cx, cy + dy, s);
            g.FillPath(sh, alpha << 24);
            Gfx.DeletePath(sh);
        }
        nint outer = HeartPath(cx, cy, s);
        g.DrawPath(outer, White, size * 0.1f);
        g.FillPath(outer, White);
        Gfx.DeletePath(outer);
        nint body = HeartPath(cx, cy + size * 0.01f, s * 0.8f);
        g.FillPathGradient(body, 0xFFFF8FB8, 0xFFE64980, cy - s * 0.35f, cy + s * 0.4f);
        Gfx.DeletePath(body);
        Gloss(g, cx - size * 0.16f, cy - size * 0.17f, size * 0.26f, size * 0.16f);
    }

    static PointF[] StarPoints(float cx, float cy, float outer, float inner)
    {
        var pts = new PointF[10];
        for (int i = 0; i < 10; i++)
        {
            float rad = (i % 2 == 0 ? outer : inner);
            double a = -Math.PI / 2 + i * Math.PI / 5;
            pts[i] = new PointF(cx + rad * (float)Math.Cos(a), cy + rad * (float)Math.Sin(a));
        }
        return pts;
    }

    static void Star(Gfx g, float cx, float cy, float size)
    {
        float r = size * 0.52f;
        var outer = StarPoints(cx, cy + size * 0.03f, r, r * 0.48f);
        Shadow(g, p => Gfx.AddPolygon(p, outer), size);
        nint o = Gfx.NewPath();
        Gfx.AddPolygon(o, outer);
        g.DrawPath(o, White, size * 0.12f);
        g.FillPath(o, White);
        Gfx.DeletePath(o);
        var inner = StarPoints(cx, cy + size * 0.03f, r * 0.8f, r * 0.8f * 0.48f);
        nint b = Gfx.NewPath();
        Gfx.AddPolygon(b, inner);
        g.DrawPath(b, 0xFFFFC53D, size * 0.05f);
        g.FillPathGradient(b, 0xFFFFE680, 0xFFF59F00, cy - r, cy + r);
        Gfx.DeletePath(b);
        Gloss(g, cx, cy - size * 0.14f, size * 0.3f, size * 0.14f);
    }

    static void Smile(Gfx g, float cx, float cy, float size)
    {
        RoundBadge(g, cx, cy, size, 0xFFFFE066, 0xFFFAB005);
        uint ink = 0xFF5C3B00;
        float e = size * 0.085f;
        g.FillEllipse(ink, cx - size * 0.16f - e / 2, cy - size * 0.12f - e * 0.7f, e, e * 1.4f);
        g.FillEllipse(ink, cx + size * 0.16f - e / 2, cy - size * 0.12f - e * 0.7f, e, e * 1.4f);
        float sw = size * 0.44f;
        g.Arc(ink, size * 0.06f, cx - sw / 2, cy - sw * 0.42f, sw, sw * 0.82f, 25, 130);
        float ch = size * 0.11f; // 볼터치
        g.FillEllipse(0x66FF6B8A, cx - size * 0.3f - ch / 2, cy + size * 0.04f, ch, ch * 0.7f);
        g.FillEllipse(0x66FF6B8A, cx + size * 0.3f - ch / 2, cy + size * 0.04f, ch, ch * 0.7f);
    }

    // ───────────────────────── 번호 배지 ─────────────────────────

    public static uint WithOpacity(uint color, float opacity) => (color & 0xFFFFFF) | (uint)MathF.Round((color >> 24) * opacity) << 24;

    public static void NumberOutlineGlow(Gfx g, RectF box, float width, uint color)
    {
        // Several low-opacity strokes form a soft edge without obscuring the captured content.
        float spread = Math.Clamp(width, 2f, 4f);
        for (int i = 3; i >= 1; i--)
        {
            uint shadow = WithOpacity((color & 0xFF000000), 0.10f * (4 - i));
            g.Rect(shadow, width + spread * i * 2, box.X, box.Y + spread * 0.6f, box.Width, box.Height);
            g.Rect(WithOpacity(Mix(color, 0xFFFFFFFF, 0.6f), 0.10f * (4 - i)), width + spread * i, box.X, box.Y, box.Width, box.Height);
        }
        g.Rect(WithOpacity(color, 0.9f), width + 3, box.X, box.Y, box.Width, box.Height);
        g.Rect(WithOpacity(0xFF000000, (color >> 24) / 255f * 0.6f), width + 1.5f, box.X, box.Y, box.Width, box.Height);
    }

    public static void Number(Gfx g, int number, float cx, float cy, float size, uint color, bool square = false, bool glow = false)
    {
        var box = new RectF(cx - size / 2, cy - size / 2, size, size);
        if (glow && square) NumberOutlineGlow(g, box, MathF.Max(1, size * 0.06f), color);
        if (glow && square) g.FillRect(WithOpacity((color & 0xFF000000) | 0xFFFFFF, 0.95f), box.X - 1.5f, box.Y - 1.5f, box.Width + 3, box.Height + 3);
        if (square) g.FillRect(color, cx - size / 2, cy - size / 2, size, size);
        else g.FillEllipse(color, cx - size / 2, cy - size / 2, size, size);
        string text = number.ToString();
        Glyph(g, text, cx, cy, size * MathF.Min(0.5f, 0.82f / text.Length), (ContrastText(color) & 0xFFFFFF) | (color & 0xFF000000));
    }

    // ───────────────────────── 말풍선 ─────────────────────────

    /// <summary>
    /// 둥근 몸통 + 꼬리를 "하나로 이어진 윤곽선"으로 만든다.
    /// (도형 두 개를 겹치면 테두리를 그릴 때 몸통과 꼬리 사이에 이음매 선이 보인다)
    /// 몸통 둘레를 시계 방향으로 돌다가, 꼬리가 붙는 변에서 꼬리 끝(tip)을 찍고 돌아온다.
    /// </summary>
    public static nint BubblePath(RectF body, PointF tip, float radius, TailDir dir)
    {
        float x = body.X, y = body.Y, w = body.Width, h = body.Height;
        float r = MathF.Min(radius, MathF.Min(w, h) / 2), d = r * 2;
        bool horizontal = dir is TailDir.Left or TailDir.Right;
        float baseW = horizontal ? MathF.Min(h * 0.45f, r * 1.4f) : MathF.Min(w * 0.35f, r * 1.6f);

        // 꼬리 밑변의 시작·끝 (변을 따라 시계 방향 순서)
        float b0, b1;
        switch (dir)
        {
            case TailDir.BottomLeft: case TailDir.TopLeft:
                b0 = x + r * 0.9f; b1 = b0 + baseW; break;
            case TailDir.BottomRight: case TailDir.TopRight:
                b1 = x + w - r * 0.9f; b0 = b1 - baseW; break;
            default: // 좌우: 세로 가운데
                b0 = y + (h - baseW) / 2; b1 = b0 + baseW; break;
        }
        bool top = dir is TailDir.TopLeft or TailDir.TopRight;
        bool bottom = dir is TailDir.BottomLeft or TailDir.BottomRight;

        nint p = Gfx.NewPath();
        GdiPlus.GdipAddPathArc(p, x, y, d, d, 180, 90);                       // 왼쪽 위 모서리
        if (top) { Line(p, b0, y, tip.X, tip.Y); Line(p, tip.X, tip.Y, b1, y); }
        GdiPlus.GdipAddPathArc(p, x + w - d, y, d, d, 270, 90);               // 오른쪽 위
        if (dir == TailDir.Right) { Line(p, x + w, b0, tip.X, tip.Y); Line(p, tip.X, tip.Y, x + w, b1); }
        GdiPlus.GdipAddPathArc(p, x + w - d, y + h - d, d, d, 0, 90);         // 오른쪽 아래
        if (bottom) { Line(p, b1, y + h, tip.X, tip.Y); Line(p, tip.X, tip.Y, b0, y + h); }
        GdiPlus.GdipAddPathArc(p, x, y + h - d, d, d, 90, 90);                // 왼쪽 아래
        if (dir == TailDir.Left) { Line(p, x, b1, tip.X, tip.Y); Line(p, tip.X, tip.Y, x, b0); }
        GdiPlus.GdipClosePathFigure(p);
        return p;
    }

    static void Line(nint path, float x1, float y1, float x2, float y2) => GdiPlus.GdipAddPathLine(path, x1, y1, x2, y2);

    public static void Bubble(Gfx g, RectF body, PointF tip, float radius, uint color, TailDir dir)
    {
        foreach (var (dy, alpha) in new[] { (radius * 0.45f, 0x1Cu), (radius * 0.22f, 0x2Au) })
        {
            nint sh = BubblePath(new RectF(body.X, body.Y + dy, body.Width, body.Height), new PointF(tip.X, tip.Y + dy), radius, dir);
            g.FillPath(sh, alpha << 24);
            Gfx.DeletePath(sh);
        }
        nint p = BubblePath(body, tip, radius, dir);
        g.FillPathGradient(p, Mix(color, White, 0.12f), color, body.Y, body.Y + body.Height);
        if (ContrastText(color) != White) g.DrawPath(p, 0x26000000, 1); // 흰·노랑 풍선은 이어진 테두리를 아주 옅게
        Gfx.DeletePath(p);
    }

    /// <summary>
    /// 몸통 기준으로 꼬리 끝 위치 (선택 줄의 작은 미리보기용).
    /// 실제 말풍선은 반대로 "클릭한 곳 = 꼬리 끝"에 맞춰 몸통을 놓는다 (BubbleAnnotation).
    /// </summary>
    public static PointF TipFor(RectF body, TailDir dir, float len) => dir switch
    {
        TailDir.BottomLeft => new(body.X + body.Width * 0.22f, body.Y + body.Height + len),
        TailDir.BottomRight => new(body.X + body.Width * 0.78f, body.Y + body.Height + len),
        TailDir.TopLeft => new(body.X + body.Width * 0.22f, body.Y - len),
        TailDir.TopRight => new(body.X + body.Width * 0.78f, body.Y - len),
        TailDir.Left => new(body.X - len, body.Y + body.Height / 2),
        TailDir.Right => new(body.X + body.Width + len, body.Y + body.Height / 2),
        _ => default,
    };

    /// <summary>경로를 잠시 (dx, dy)만큼 옮겼다가 Dispose 때 되돌린다 (그림자용).</summary>
    readonly unsafe struct PathOffset : IDisposable
    {
        readonly nint _path;
        readonly float _dx, _dy;

        public PathOffset(nint path, float dx, float dy)
        {
            _path = path; _dx = dx; _dy = dy;
            Translate(path, dx, dy);
        }

        public void Dispose() => Translate(_path, -_dx, -_dy);

        static void Translate(nint path, float dx, float dy)
        {
            nint m;
            GdipCreateMatrix2(1, 0, 0, 1, dx, dy, &m);
            GdipTransformPath(path, m);
            GdipDeleteMatrix(m);
        }

        [System.Runtime.InteropServices.DllImport("gdiplus.dll")] static extern int GdipCreateMatrix2(float m11, float m12, float m21, float m22, float dx, float dy, nint* matrix);
        [System.Runtime.InteropServices.DllImport("gdiplus.dll")] static extern int GdipTransformPath(nint path, nint matrix);
        [System.Runtime.InteropServices.DllImport("gdiplus.dll")] static extern int GdipDeleteMatrix(nint matrix);
    }
}

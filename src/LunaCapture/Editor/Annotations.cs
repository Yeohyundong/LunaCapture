using LunaCapture.Graphics;
using LunaCapture.Interop;

namespace LunaCapture.Editor;

internal enum Tool { Arrow, Rect, Pen, Highlighter, Text, Mosaic, Bubble, Number, Sticker, Select }

/// <summary>이미지 좌표계(실제 픽셀)에 그려지는 주석 하나.</summary>
internal abstract class Annotation
{
    public uint Color;   // ARGB
    public float Width;  // 선 굵기 (이미지 픽셀)

    public abstract void Draw(GdiSurface target, Gfx g);

    /// <summary>너무 작아서 의미 없는 주석(실수로 클릭한 것 등)은 버린다.</summary>
    public abstract bool IsMeaningful { get; }

    /// <summary>선택 테두리를 그릴 영역 (이미지 좌표).</summary>
    public abstract RectF Bounds(Gfx g);

    /// <summary>p를 클릭했을 때 이 주석이 잡히는지. tol = 허용 오차(이미지 픽셀).</summary>
    public virtual bool HitTest(PointF p, float tol, Gfx g) => Geo.Inside(Geo.Inflate(Bounds(g), tol), p);

    public abstract void Offset(float dx, float dy);

    /// <summary>되돌리기용 복사본.</summary>
    public virtual Annotation Clone() => (Annotation)MemberwiseClone();
}

/// <summary>선택·클릭 판정용 작은 기하 함수들.</summary>
internal static class Geo
{
    public static RectF FromPoints(PointF a, PointF b) =>
        new(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Abs(a.X - b.X), MathF.Abs(a.Y - b.Y));

    public static RectF Inflate(RectF r, float d) => new(r.X - d, r.Y - d, r.Width + d * 2, r.Height + d * 2);

    public static bool Inside(RectF r, PointF p) => p.X >= r.X && p.X <= r.X + r.Width && p.Y >= r.Y && p.Y <= r.Y + r.Height;

    /// <summary>점 p에서 선분 ab까지의 거리.</summary>
    public static float SegmentDistance(PointF p, PointF a, PointF b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
        float t = len2 < 1e-6f ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        float cx = a.X + t * dx - p.X, cy = a.Y + t * dy - p.Y;
        return MathF.Sqrt(cx * cx + cy * cy);
    }

    /// <summary>사각형 테두리(네 변) 근처인지.</summary>
    public static bool NearEdge(RectF r, PointF p, float tol)
    {
        PointF a = new(r.X, r.Y), b = new(r.X + r.Width, r.Y), c = new(r.X + r.Width, r.Y + r.Height), d = new(r.X, r.Y + r.Height);
        return SegmentDistance(p, a, b) <= tol || SegmentDistance(p, b, c) <= tol || SegmentDistance(p, c, d) <= tol || SegmentDistance(p, d, a) <= tol;
    }
}

internal sealed class ArrowAnnotation : Annotation
{
    public PointF From, To;

    public override bool IsMeaningful => Dist(From, To) > 4;

    public override void Draw(GdiSurface target, Gfx g)
    {
        float dx = To.X - From.X, dy = To.Y - From.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        float ux = dx / len, uy = dy / len;

        // 머리 크기는 굵기에 비례하되, 짧은 화살표에서는 길이의 절반을 넘지 않게.
        float head = MathF.Min(MathF.Max(Width * 4.2f, 12f), len * 0.5f);
        float half = head * 0.48f;
        float bx = To.X - ux * head, by = To.Y - uy * head;

        // 몸통은 머리 안쪽까지만 그려서 끝이 뭉툭해지지 않게.
        g.Line(Color, Width, From.X, From.Y, bx + ux * head * 0.3f, by + uy * head * 0.3f);
        g.FillPolygon(Color, [
            new PointF(To.X, To.Y),
            new PointF(bx - uy * half, by + ux * half),
            new PointF(bx + ux * head * 0.12f, by + uy * head * 0.12f),
            new PointF(bx + uy * half, by - ux * half),
        ]);
    }

    static float Dist(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    public override RectF Bounds(Gfx g) => Geo.Inflate(Geo.FromPoints(From, To), Width * 2);
    public override bool HitTest(PointF p, float tol, Gfx g) => Geo.SegmentDistance(p, From, To) <= tol + Width * 1.5f;
    public override void Offset(float dx, float dy) { From = new(From.X + dx, From.Y + dy); To = new(To.X + dx, To.Y + dy); }
}

internal enum ShapeKind { Rect, Ellipse, RoundedRect }
internal enum FillStyle { Outline, Solid, Tint }

/// <summary>
/// 도형 (사각형/원 × 테두리/채움/반투명). 더블클릭하면 가운데에 글자를 넣을 수 있어
/// 텍스트 주석(TextAnnotation)의 입력 기능을 그대로 물려받는다.
/// </summary>
internal sealed class RectAnnotation : TextAnnotation
{
    public PointF A, B;
    public ShapeKind Shape;
    public FillStyle Fill;

    public override bool IsMeaningful => MathF.Abs(A.X - B.X) > 3 && MathF.Abs(A.Y - B.Y) > 3;

    RectF Box => Geo.FromPoints(A, B);
    float Radius => Shape == ShapeKind.RoundedRect ? MathF.Min(16, MathF.Min(Box.Width, Box.Height) * 0.2f) : 0;

    // 채운 도형은 배경에 맞춰 글자색 자동, 테두리·반투명은 도형 색
    protected override uint TextColor => Fill == FillStyle.Solid ? Stickers.ContrastText(Color) : Color;

    public override void Draw(GdiSurface target, Gfx g)
    {
        var b = Box;
        if (Shape == ShapeKind.Ellipse)
        {
            if (Fill != FillStyle.Outline) g.FillEllipse(Fill == FillStyle.Solid ? Color : (Color & 0x00FFFFFF) | (uint)((Color >> 24) * 80 / 255) << 24, b.X, b.Y, b.Width, b.Height);
            if (Fill != FillStyle.Solid) g.Ellipse(Color, Width, b.X, b.Y, b.Width, b.Height);
        }
        else
        {
            if (Fill == FillStyle.Outline)
            {
                if (Radius > 0) g.RoundRect(Color, Width, b.X, b.Y, b.Width, b.Height, Radius);
                else g.Rect(Color, Width, b.X, b.Y, b.Width, b.Height);
            }
            else
            {
                uint fillColor = Fill == FillStyle.Solid ? Color : (Color & 0x00FFFFFF) | (uint)((Color >> 24) * 80 / 255) << 24;
                if (Radius > 0) g.FillRoundRect(fillColor, b.X, b.Y, b.Width, b.Height, Radius);
                else g.FillRect(fillColor, b.X, b.Y, b.Width, b.Height);
                if (Fill == FillStyle.Tint)
                {
                    if (Radius > 0) g.RoundRect(Color, Width, b.X, b.Y, b.Width, b.Height, Radius);
                    else g.Rect(Color, Width, b.X, b.Y, b.Width, b.Height);
                }
            }
        }
        DrawCenteredText(g);
    }

    /// <summary>줄마다 가운데 정렬, 전체는 도형 세로 가운데.</summary>
    void DrawCenteredText(Gfx g)
    {
        string s = Display;
        if (s.Length == 0 || FontSize <= 0) return;
        using var font = new GdiFont(UiFonts.Annotation, FontSize, bold: true);
        var (lines, lh, y0) = Lines(g, font);
        var b = Box;
        for (int i = 0; i < lines.Length; i++)
        {
            float w = g.MeasureWidth(lines[i], font);
            g.Text(lines[i], font, TextColor, b.X + (b.Width - w) / 2, y0 + i * lh);
        }
    }

    (string[] lines, float lh, float y0) Lines(Gfx g, GdiFont font)
    {
        string[] lines = Display.Split('\n');
        float lh = g.LineHeight(font);
        var b = Box;
        return (lines, lh, b.Y + (b.Height - lh * lines.Length) / 2);
    }

    public override (float x, float y, float h) Caret(Gfx g)
    {
        using var font = new GdiFont(UiFonts.Annotation, FontSize, bold: true);
        var (lines, lh, y0) = Lines(g, font);
        float w = g.MeasureWidth(lines[^1], font);
        var b = Box;
        return (b.X + b.Width / 2 + w / 2, y0 + (lines.Length - 1) * lh, lh);
    }

    public override RectF Bounds(Gfx g) => Box;

    public override bool HitTest(PointF p, float tol, Gfx g)
    {
        var b = Box;
        if (Fill != FillStyle.Outline)
            return Shape == ShapeKind.Ellipse ? EllipseDistance(b, p) <= 1 + tol / MathF.Max(1, MathF.Min(b.Width, b.Height) / 2) : Geo.Inside(Geo.Inflate(b, tol), p);
        // 테두리만 있는 도형은 테두리 근처만 → 안쪽에 다른 도형을 그릴 수 있다
        if (Shape == ShapeKind.Ellipse)
        {
            float r = MathF.Max(1, MathF.Min(b.Width, b.Height) / 2);
            return MathF.Abs(EllipseDistance(b, p) - 1) * r <= tol + Width / 2;
        }
        return Geo.NearEdge(b, p, tol + Width / 2);
    }

    /// <summary>타원 안에서 1 미만, 둘레에서 1 (정규화한 거리).</summary>
    static float EllipseDistance(RectF b, PointF p)
    {
        float rx = MathF.Max(1, b.Width / 2), ry = MathF.Max(1, b.Height / 2);
        float dx = (p.X - (b.X + rx)) / rx, dy = (p.Y - (b.Y + ry)) / ry;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>도형 안쪽인지 (테두리만 있는 도형을 더블클릭해 글자를 넣을 때).</summary>
    public bool ContainsInside(PointF p) =>
        Shape == ShapeKind.Ellipse ? EllipseDistance(Box, p) <= 1 : Geo.Inside(Box, p);

    public override void Offset(float dx, float dy) { A = new(A.X + dx, A.Y + dy); B = new(B.X + dx, B.Y + dy); }
}

internal sealed class PenAnnotation : Annotation
{
    public List<PointF> Points = new();
    public bool Highlighter;

    public override bool IsMeaningful => Points.Count > 1;

    public void Add(PointF p)
    {
        if (Points.Count > 0)
        {
            var last = Points[^1];
            if (MathF.Abs(last.X - p.X) < 1 && MathF.Abs(last.Y - p.Y) < 1) return;
        }
        Points.Add(p);
    }

    public override void Draw(GdiSurface target, Gfx g)
    {
        if (Points.Count == 1)
        {
            var p = Points[0];
            g.FillEllipse(Color, p.X - Width / 2, p.Y - Width / 2, Width, Width);
            return;
        }
        // 하나의 폴리라인으로 그려야 형광펜이 겹치는 곳이 진해지지 않는다.
        g.Lines(Color, Width, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Points));
    }

    public override RectF Bounds(Gfx g)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var p in Points) { x0 = MathF.Min(x0, p.X); y0 = MathF.Min(y0, p.Y); x1 = MathF.Max(x1, p.X); y1 = MathF.Max(y1, p.Y); }
        return Geo.Inflate(new RectF(x0, y0, x1 - x0, y1 - y0), Width / 2);
    }

    public override bool HitTest(PointF p, float tol, Gfx g)
    {
        if (Points.Count == 1) return Geo.SegmentDistance(p, Points[0], Points[0]) <= tol + Width / 2;
        for (int i = 1; i < Points.Count; i++)
            if (Geo.SegmentDistance(p, Points[i - 1], Points[i]) <= tol + Width / 2) return true;
        return false;
    }

    public override void Offset(float dx, float dy)
    {
        for (int i = 0; i < Points.Count; i++) Points[i] = new(Points[i].X + dx, Points[i].Y + dy);
    }

    public override Annotation Clone()
    {
        var c = (PenAnnotation)MemberwiseClone();
        c.Points = new List<PointF>(Points);
        return c;
    }
}

internal class TextAnnotation : Annotation
{
    public PointF Position;
    public string Text = "";
    public string Composing = ""; // IME 조합 중인 글자 (편집 중에만)
    public float FontSize;

    public override bool IsMeaningful => Text.Trim().Length > 0;

    public string Display => Text + Composing;

    /// <summary>글자를 그리기 시작하는 위치 (말풍선은 풍선 안쪽).</summary>
    protected virtual PointF TextOrigin(Gfx g) => Position;

    protected virtual uint TextColor => Color;

    public override void Draw(GdiSurface target, Gfx g)
    {
        string s = Display;
        if (s.Length == 0) return;
        using var font = new GdiFont(UiFonts.Annotation, FontSize, bold: true);
        var o = TextOrigin(g);
        g.Text(s, font, TextColor, o.X, o.Y);
    }

    /// <summary>캐럿(입력 위치) 좌표: 마지막 줄의 끝.</summary>
    public virtual (float x, float y, float h) Caret(Gfx g)
    {
        using var font = new GdiFont(UiFonts.Annotation, FontSize, bold: true);
        string s = Display;
        int lastBreak = s.LastIndexOf('\n');
        int lineIndex = s.Count(c => c == '\n');
        string lastLine = lastBreak >= 0 ? s[(lastBreak + 1)..] : s;
        float lh = g.LineHeight(font);
        var o = TextOrigin(g);
        return (o.X + g.MeasureWidth(lastLine, font), o.Y + lineIndex * lh, lh);
    }

    public override RectF Bounds(Gfx g)
    {
        var (w, h) = Measure(g);
        var o = TextOrigin(g);
        return new RectF(o.X, o.Y, MathF.Max(w, FontSize * 0.5f), h);
    }

    public override void Offset(float dx, float dy) => Position = new(Position.X + dx, Position.Y + dy);

    /// <summary>여러 줄 글자의 크기 (가장 긴 줄 폭, 전체 높이).</summary>
    protected (float w, float h) Measure(Gfx g)
    {
        using var font = new GdiFont(UiFonts.Annotation, FontSize, bold: true);
        string[] lines = Display.Split('\n');
        float w = 0;
        foreach (var line in lines) w = MathF.Max(w, g.MeasureWidth(line, font));
        return (w, g.LineHeight(font) * lines.Length);
    }
}

/// <summary>
/// 말풍선: 클릭한 곳(Tail)을 꼬리가 가리키고, 풍선은 그 오른쪽 위에 글자 크기에 맞춰 늘어난다.
/// 풍선 색 = 선택한 색, 글자 색은 잘 보이게 자동(밝은 풍선 → 진한 글자).
/// </summary>
internal sealed class BubbleAnnotation : TextAnnotation
{
    /// <summary>꼬리 끝 = 클릭한 곳. 꼬리 없음(None)이면 풍선의 왼쪽 위 기준점.</summary>
    public PointF Tail;
    public TailDir Dir = TailDir.BottomLeft;

    (RectF body, float pad, float radius) Layout(Gfx g)
    {
        var (tw, th) = Measure(g);
        float pad = FontSize * 0.7f, radius = FontSize * 0.8f;
        float w = MathF.Max(tw, FontSize * 2.2f) + pad * 2, h = th + pad * 1.4f;
        float gap = FontSize * 1.3f;   // 꼬리 길이
        float edge = radius * 0.6f;    // 꼬리가 몸통 모서리에서 떨어진 정도

        // 꼬리 방향의 반대쪽으로 몸통을 놓는다 (예: 꼬리가 왼쪽 아래 → 몸통은 클릭한 곳의 오른쪽 위)
        (float x, float y) = Dir switch
        {
            TailDir.BottomLeft => (Tail.X - edge, Tail.Y - gap - h),
            TailDir.BottomRight => (Tail.X + edge - w, Tail.Y - gap - h),
            TailDir.TopLeft => (Tail.X - edge, Tail.Y + gap),
            TailDir.TopRight => (Tail.X + edge - w, Tail.Y + gap),
            TailDir.Left => (Tail.X + gap, Tail.Y - h / 2),
            TailDir.Right => (Tail.X - gap - w, Tail.Y - h / 2),
            _ => (Tail.X, Tail.Y - h / 2),
        };
        return (new RectF(x, y, w, h), pad, radius);
    }

    protected override PointF TextOrigin(Gfx g)
    {
        var (body, pad, _) = Layout(g);
        return new PointF(body.X + pad, body.Y + pad * 0.7f);
    }

    protected override uint TextColor => Stickers.ContrastText(Color);

    public override RectF Bounds(Gfx g)
    {
        var (body, _, _) = Layout(g);
        if (Dir == TailDir.None) return body;
        float x0 = MathF.Min(body.X, Tail.X), y0 = MathF.Min(body.Y, Tail.Y);
        float x1 = MathF.Max(body.X + body.Width, Tail.X), y1 = MathF.Max(body.Y + body.Height, Tail.Y);
        return new RectF(x0, y0, x1 - x0, y1 - y0);
    }

    public override bool HitTest(PointF p, float tol, Gfx g)
    {
        var (body, _, _) = Layout(g);
        return Geo.Inside(Geo.Inflate(body, tol), p) || (Dir != TailDir.None && Geo.SegmentDistance(p, Tail, Tail) <= tol * 2);
    }

    public override void Offset(float dx, float dy) => Tail = new(Tail.X + dx, Tail.Y + dy);

    public override void Draw(GdiSurface target, Gfx g)
    {
        var (body, _, radius) = Layout(g);
        Stickers.Bubble(g, body, Tail, radius, Color, Dir);
        base.Draw(target, g);
    }
}

/// <summary>스티커 하나 (클릭한 곳이 가운데).</summary>
internal sealed class StickerAnnotation : Annotation
{
    public StickerKind Kind;
    public PointF Center;
    public float Size;

    public override bool IsMeaningful => true;

    public override void Draw(GdiSurface target, Gfx g) => Stickers.Draw(g, Kind, Center.X, Center.Y, Size);

    public override RectF Bounds(Gfx g) => new(Center.X - Size / 2, Center.Y - Size / 2, Size, Size);
    public override bool HitTest(PointF p, float tol, Gfx g) => Geo.SegmentDistance(p, Center, Center) <= Size / 2 + tol;
    public override void Offset(float dx, float dy) => Center = new(Center.X + dx, Center.Y + dy);
}

/// <summary>번호 배지 (1, 2, 3… 단계 표시용).</summary>
internal sealed class NumberAnnotation : Annotation
{
    public int Number;
    public PointF Center;
    public float Size;
    public bool Square;
    public bool Glow;
    public bool Outside;
    public int? Corner;
    // Clockwise from upper-left, relative to a down-right drag; mirrored with drag direction.
    public int? Quadrant;
    public int EffectiveQuadrant => Quadrant is >= 0 and <= 3 ? Quadrant.Value : Outside ? 0 : 2;
    public float FontSize;
    public int EffectiveCorner => Corner is >= 0 and <= 3 ? Corner.Value : (Center.X > End.X ? 1 : 0) + (Center.Y > End.Y ? 2 : 0);
    public float EffectiveFontSize => FontSize > 0 ? FontSize : Size * MathF.Min(0.5f, 0.82f / Number.ToString().Length);
    public PointF End;
    public bool HasBox;
    public PointF? BadgePosition;

    public override bool IsMeaningful => true;

    public float BadgeSize => FontSize > 0 ? MeasureBadge() : !HasBox || Outside ? Size : MathF.Min(Size, MathF.Max(2,
        MathF.Min(MathF.Abs(End.X - Center.X), MathF.Abs(End.Y - Center.Y)) - MathF.Max(2, Width) - (Square ? 0 : 4)));

    float MeasureBadge()
    {
        nint dc = User32.GetDC(0);
        try
        {
            using var g = new Gfx(dc);
            using var font = new GdiFont(UiFonts.Annotation, FontSize, bold: true);
            float w = g.MeasureWidth(Number.ToString(), font), h = g.LineHeight(font);
            return (Square ? MathF.Max(w, h) : MathF.Sqrt(w * w + h * h)) + FontSize * 0.6f;
        }
        finally { User32.ReleaseDC(0, dc); }
    }

    public PointF BadgeCenter
    {
        get
        {
            if (BadgePosition is { } position) return position;
            if (!HasBox) return Center;
            float margin = Square ? 0 : 2;
            float offset = BadgeSize / 2 + margin + MathF.Max(2, Width) / 2;
            if (Quadrant is >= 0 and <= 3)
            {
                float sx = End.X >= Center.X ? 1 : -1, sy = End.Y >= Center.Y ? 1 : -1;
                float qx = Quadrant is 0 or 3 ? -1 : 1, qy = Quadrant is 0 or 1 ? -1 : 1;
                return new(Center.X + sx * qx * offset, Center.Y + sy * qy * offset);
            }
            if (Outside) offset = -offset;
            var box = Geo.FromPoints(Center, End);
            bool right = (EffectiveCorner & 1) != 0, bottom = (EffectiveCorner & 2) != 0;
            return new(right ? box.X + box.Width - offset : box.X + offset, bottom ? box.Y + box.Height - offset : box.Y + offset);
        }
    }

    public override void Draw(GdiSurface target, Gfx g)
    {
        if (HasBox)
        {
            var box = Geo.FromPoints(Center, End);
            if (Glow) Stickers.NumberOutlineGlow(g, box, MathF.Max(2, Width), Color);
            if (Glow) g.Rect(Stickers.WithOpacity((Color & 0xFF000000) | 0xFFFFFF, 0.95f), MathF.Max(2, Width) + 3, box.X, box.Y, box.Width, box.Height);
            g.Rect(Color, MathF.Max(2, Width), box.X, box.Y, box.Width, box.Height);
        }
        var c = BadgeCenter;
        Stickers.Number(g, Number, c.X, c.Y, BadgeSize, Color, Square, Glow, FontSize);
    }

    public override RectF Bounds(Gfx g)
    {
        var c = BadgeCenter;
        var badge = new RectF(c.X - BadgeSize / 2, c.Y - BadgeSize / 2, BadgeSize, BadgeSize);
        if (!HasBox) return badge;
        var b = Geo.FromPoints(Center, End);
        float x = MathF.Min(b.X, badge.X), y = MathF.Min(b.Y, badge.Y);
        return new(x, y, MathF.Max(b.X + b.Width, badge.X + badge.Width) - x, MathF.Max(b.Y + b.Height, badge.Y + badge.Height) - y);
    }
    public bool HitBadge(PointF p, float tol)
    {
        var c = BadgeCenter;
        return Square ? Geo.Inside(new(c.X - BadgeSize / 2 - tol, c.Y - BadgeSize / 2 - tol, BadgeSize + tol * 2, BadgeSize + tol * 2), p)
            : Geo.SegmentDistance(p, c, c) <= BadgeSize / 2 + tol;
    }
    public override bool HitTest(PointF p, float tol, Gfx g) => HitBadge(p, tol) || (HasBox && Geo.NearEdge(Geo.FromPoints(Center, End), p, tol + MathF.Max(2, Width)));

    public void ResizeBox(int handle, PointF point)
    {
        bool right = Center.X > End.X, bottom = Center.Y > End.Y;
        var b = Geo.FromPoints(Center, End);
        float l = b.X, t = b.Y, r = b.X + b.Width, d = b.Y + b.Height;
        if (handle is 0 or 2) l = MathF.Min(point.X, r - 8); else r = MathF.Max(point.X, l + 8);
        if (handle is 0 or 1) t = MathF.Min(point.Y, d - 8); else d = MathF.Max(point.Y, t + 8);
        Center = new(right ? r : l, bottom ? d : t);
        End = new(right ? l : r, bottom ? t : d);
    }

    public override void Offset(float dx, float dy)
    {
        Center = new(Center.X + dx, Center.Y + dy);
        End = new(End.X + dx, End.Y + dy);
        if (BadgePosition is { } position) BadgePosition = new(position.X + dx, position.Y + dy);
    }

    public void MoveBadge(float dx, float dy)
    {
        if (!HasBox) { Offset(dx, dy); return; }
        var position = BadgeCenter;
        BadgePosition = new(position.X + dx, position.Y + dy);
    }
}

internal sealed unsafe class MosaicAnnotation : Annotation
{
    public PointF A, B;
    public int BlockSize = 12;

    public override bool IsMeaningful => MathF.Abs(A.X - B.X) > 3 && MathF.Abs(A.Y - B.Y) > 3;

    public override RectF Bounds(Gfx g) => Geo.FromPoints(A, B);
    public override void Offset(float dx, float dy) { A = new(A.X + dx, A.Y + dy); B = new(B.X + dx, B.Y + dy); }

    public override void Draw(GdiSurface target, Gfx g)
    {
        Gdi32.GdiFlush(); // 앞서 GDI+로 그린 내용이 픽셀에 반영되도록
        int x0 = Math.Clamp((int)MathF.Min(A.X, B.X), 0, target.Width);
        int y0 = Math.Clamp((int)MathF.Min(A.Y, B.Y), 0, target.Height);
        int x1 = Math.Clamp((int)MathF.Ceiling(MathF.Max(A.X, B.X)), 0, target.Width);
        int y1 = Math.Clamp((int)MathF.Ceiling(MathF.Max(A.Y, B.Y)), 0, target.Height);
        int bs = Math.Max(2, BlockSize);

        for (int by = y0; by < y1; by += bs)
        {
            int bh = Math.Min(bs, y1 - by);
            for (int bx = x0; bx < x1; bx += bs)
            {
                int bw = Math.Min(bs, x1 - bx);
                long r = 0, gg = 0, b = 0;
                for (int y = by; y < by + bh; y++)
                {
                    byte* row = target.Bits + y * target.Stride + bx * 4;
                    for (int x = 0; x < bw; x++)
                    {
                        b += row[x * 4]; gg += row[x * 4 + 1]; r += row[x * 4 + 2];
                    }
                }
                int n = bw * bh;
                uint avg = 0xFF000000u | (uint)(r / n) << 16 | (uint)(gg / n) << 8 | (uint)(b / n);
                for (int y = by; y < by + bh; y++)
                {
                    uint* row = (uint*)(target.Bits + y * target.Stride) + bx;
                    for (int x = 0; x < bw; x++) row[x] = avg;
                }
            }
        }
    }
}

/// <summary>
/// 그림 조각: 오려낸 영역이나 붙여넣은 그림. 옮기고, 모서리를 끌어 비율을 유지한 채 크기를 바꾼다.
/// 그림은 바뀌지 않으므로 되돌리기용 복사본끼리 같은 픽셀을 함께 쓴다(가볍다).
/// </summary>
internal sealed unsafe class ImageAnnotation : Annotation
{
    public required PixelImage Image;
    public PointF A, B;

    public RectF Box => Geo.FromPoints(A, B);
    public override bool IsMeaningful => true;
    public override RectF Bounds(Gfx g) => Box;
    public override bool HitTest(PointF p, float tol, Gfx g) => Geo.Inside(Geo.Inflate(Box, tol), p);
    public override void Offset(float dx, float dy) { A = new(A.X + dx, A.Y + dy); B = new(B.X + dx, B.Y + dy); }

    /// <summary>픽셀을 직접 옮겨 그린다: 크기가 같으면 그대로, 다르면 부드럽게(쌍선형) 늘이거나 줄인다. 투명도도 반영.</summary>
    public override void Draw(GdiSurface t, Gfx g)
    {
        var r = Box;
        if (r.Width < 1 || r.Height < 1) return;
        Gdi32.GdiFlush();
        int x0 = Math.Clamp((int)MathF.Floor(r.X), 0, t.Width), x1 = Math.Clamp((int)MathF.Ceiling(r.X + r.Width), 0, t.Width);
        int y0 = Math.Clamp((int)MathF.Floor(r.Y), 0, t.Height), y1 = Math.Clamp((int)MathF.Ceiling(r.Y + r.Height), 0, t.Height);
        int iw = Image.Width, ih = Image.Height;
        float kx = iw / r.Width, ky = ih / r.Height;
        bool same = MathF.Abs(kx - 1) < 0.001f && MathF.Abs(ky - 1) < 0.001f;

        fixed (byte* srcBytes = Image.Bgra)
        {
            uint* src = (uint*)srcBytes;
            for (int y = y0; y < y1; y++)
            {
                uint* row = (uint*)(t.Bits + y * t.Stride);
                for (int x = x0; x < x1; x++)
                {
                    uint c;
                    if (same)
                    {
                        int ix = (int)MathF.Floor(x - r.X + 0.5f), iy = (int)MathF.Floor(y - r.Y + 0.5f);
                        if ((uint)ix >= (uint)iw || (uint)iy >= (uint)ih) continue;
                        c = src[iy * iw + ix];
                    }
                    else
                    {
                        float fx = (x + 0.5f - r.X) * kx - 0.5f, fy = (y + 0.5f - r.Y) * ky - 0.5f;
                        if (fx < -0.5f || fy < -0.5f || fx > iw - 0.5f || fy > ih - 0.5f) continue;
                        c = Bilinear(src, iw, ih, fx, fy);
                    }
                    uint a = c >> 24;
                    if (a == 255) row[x] = c;
                    else if (a > 0) row[x] = Blend(row[x], c, a);
                }
            }
        }
    }

    static uint Bilinear(uint* src, int w, int h, float fx, float fy)
    {
        int ix = (int)MathF.Floor(fx), iy = (int)MathF.Floor(fy);
        float tx = fx - ix, ty = fy - iy;
        int xa = Math.Clamp(ix, 0, w - 1), xb = Math.Clamp(ix + 1, 0, w - 1);
        int ya = Math.Clamp(iy, 0, h - 1), yb = Math.Clamp(iy + 1, 0, h - 1);
        uint c00 = src[ya * w + xa], c10 = src[ya * w + xb], c01 = src[yb * w + xa], c11 = src[yb * w + xb];
        uint result = 0;
        for (int s = 0; s < 32; s += 8)
        {
            float top = ((c00 >> s) & 0xFF) * (1 - tx) + ((c10 >> s) & 0xFF) * tx;
            float bottom = ((c01 >> s) & 0xFF) * (1 - tx) + ((c11 >> s) & 0xFF) * tx;
            result |= (uint)Math.Clamp((int)(top * (1 - ty) + bottom * ty + 0.5f), 0, 255) << s;
        }
        return result;
    }

    static uint Blend(uint dst, uint src, uint a)
    {
        uint inv = 255 - a, result = 0xFF000000;
        for (int s = 0; s < 24; s += 8)
            result |= ((((src >> s) & 0xFF) * a + ((dst >> s) & 0xFF) * inv) / 255) << s;
        return result;
    }
}

/// <summary>오려낸 자리를 메우는 칸 (주변 색으로 칠함). 클릭해서 잡히지 않는다.</summary>
internal sealed class PatchAnnotation : Annotation
{
    public PointF A, B;

    public override bool IsMeaningful => true;
    public override RectF Bounds(Gfx g) => Geo.FromPoints(A, B);
    public override bool HitTest(PointF p, float tol, Gfx g) => false;
    public override void Offset(float dx, float dy) { A = new(A.X + dx, A.Y + dy); B = new(B.X + dx, B.Y + dy); }

    public override void Draw(GdiSurface target, Gfx g)
    {
        var r = Geo.FromPoints(A, B);
        g.FillRect(Color, r.X, r.Y, r.Width, r.Height);
    }
}

/// <summary>
/// 강조(스포트라이트): 이 영역만 밝게 두고 나머지를 어둡게. 여러 개면 모두 밝게 남긴 채 한 번만 어둡게 한다
/// (Composer.DrawAll이 처리). 테두리 근처를 잡아 옮기거나 모서리로 크기를 바꾼다 — 안쪽은 비워 둬서 그 위에 그릴 수 있다.
/// </summary>
internal sealed unsafe class SpotlightAnnotation : Annotation
{
    public PointF A, B;

    public RectF Box => Geo.FromPoints(A, B);
    public override bool IsMeaningful => MathF.Abs(A.X - B.X) > 3 && MathF.Abs(A.Y - B.Y) > 3;
    public override RectF Bounds(Gfx g) => Box;
    public override bool HitTest(PointF p, float tol, Gfx g) => Geo.NearEdge(Box, p, tol + 2);
    public override void Offset(float dx, float dy) { A = new(A.X + dx, A.Y + dy); B = new(B.X + dx, B.Y + dy); }

    public override void Draw(GdiSurface target, Gfx g) => DimOutside(target, [Box]);

    /// <summary>구멍(holes) 바깥의 픽셀을 어둡게 (밝기 40%).</summary>
    public static void DimOutside(GdiSurface t, List<RectF> holes)
    {
        Gdi32.GdiFlush();
        Span<int> hx0 = stackalloc int[holes.Count], hx1 = stackalloc int[holes.Count];
        Span<int> hy0 = stackalloc int[holes.Count], hy1 = stackalloc int[holes.Count];
        for (int i = 0; i < holes.Count; i++)
        {
            var r = holes[i];
            hx0[i] = (int)MathF.Round(r.X); hx1[i] = (int)MathF.Round(r.X + r.Width);
            hy0[i] = (int)MathF.Round(r.Y); hy1[i] = (int)MathF.Round(r.Y + r.Height);
        }
        for (int y = 0; y < t.Height; y++)
        {
            uint* row = (uint*)(t.Bits + y * t.Stride);
            for (int x = 0; x < t.Width; x++)
            {
                bool inside = false;
                for (int i = 0; i < holes.Count && !inside; i++)
                    inside = x >= hx0[i] && x < hx1[i] && y >= hy0[i] && y < hy1[i];
                if (inside) continue;
                uint c = row[x];
                row[x] = 0xFF000000 | ((((c >> 16) & 0xFF) * 102 >> 8) << 16) | ((((c >> 8) & 0xFF) * 102 >> 8) << 8) | ((c & 0xFF) * 102 >> 8);
            }
        }
    }
}

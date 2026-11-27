using System.Text.Json.Serialization;
using LunaCapture.Interop;

namespace LunaCapture.Editor;

/// <summary>
/// 주석 하나를 파일에 저장하기 위한 단순한 형태. 종류마다 필요한 칸만 채운다.
/// (Native AOT에서도 되도록 다형성 없이 납작한 한 가지 모양으로)
/// </summary>
internal sealed class AnnotationDto
{
    public string Type { get; set; } = "";
    public uint Color { get; set; }
    public float Width { get; set; }
    public float DetailScale { get; set; } = 1;
    /// <summary>좌표들 [x0, y0, x1, y1, …] (이미지 픽셀)</summary>
    public float[] Pts { get; set; } = [];
    public string? Text { get; set; }
    public float FontSize { get; set; }
    public int Kind { get; set; }      // 스티커 종류 / 말풍선 꼬리 방향 / 도형 모양
    public int Fill { get; set; }      // 도형 채우기
    public int Number { get; set; }
    public bool HasBox { get; set; }
    public bool Glow { get; set; }
    public bool Outside { get; set; }
    public int? Corner { get; set; }
    public int? Quadrant { get; set; }
    public float? BadgeX { get; set; }
    public float? BadgeY { get; set; }
    public float[]? BubbleBody { get; set; }
    public float[]? BubbleTip { get; set; }
    public int? TailSide { get; set; }
    public float? TailPosition { get; set; }
    public float? TailWidth { get; set; }
    public float Size { get; set; }
    public int BlockSize { get; set; }
    public bool Highlighter { get; set; }
    /// <summary>그림 조각: 크기 + 픽셀(BGRA를 압축해 base64)</summary>
    public int Iw { get; set; }
    public int Ih { get; set; }
    public string? Data { get; set; }
}

internal static class AnnotationJson
{
    static float[] P(params PointF[] pts)
    {
        var a = new float[pts.Length * 2];
        for (int i = 0; i < pts.Length; i++) { a[i * 2] = pts[i].X; a[i * 2 + 1] = pts[i].Y; }
        return a;
    }

    static PointF Pt(float[] a, int i) => a.Length >= i * 2 + 2 ? new PointF(a[i * 2], a[i * 2 + 1]) : default;
    static PointF? OptionalPoint(float[]? points) =>
        points is { Length: 2 } && float.IsFinite(points[0]) && float.IsFinite(points[1])
            ? new PointF(points[0], points[1]) : null;

    public static AnnotationDto ToDto(Annotation a)
    {
        AnnotationDto dto = a switch
    {
        ArrowAnnotation x => new() { Type = "arrow", Color = x.Color, Width = x.Width, Pts = P(x.From, x.To) },
        RectAnnotation x => new() { Type = "shape", Color = x.Color, Width = x.Width, Pts = P(x.A, x.B), Kind = (int)x.Shape, Fill = (int)x.Fill, Text = x.Text, FontSize = x.FontSize },
        BubbleAnnotation x => new()
        {
            Type = "bubble", Color = x.Color, Pts = P(x.Tail), Kind = (int)x.Dir, Text = x.Text, FontSize = x.FontSize,
            BubbleBody = x.BodyPosition is { } body ? P(body) : null,
            BubbleTip = x.TailTip is { } tip ? P(tip) : null,
            TailSide = (int?)x.TailSide, TailPosition = x.TailPosition, TailWidth = x.TailWidth,
        },
        TextAnnotation x => new() { Type = "text", Color = x.Color, Pts = P(x.Position), Text = x.Text, FontSize = x.FontSize },
        PenAnnotation x => new() { Type = "pen", Color = x.Color, Width = x.Width, Pts = P([.. x.Points]), Highlighter = x.Highlighter },
        StickerAnnotation x => new() { Type = "sticker", Pts = P(x.Center), Kind = (int)x.Kind, Size = x.Size },
        NumberAnnotation x => new() { Type = "number", Color = x.Color, Width = x.Width, Pts = P(x.Center, x.End), Number = x.Number, Size = x.Size, Kind = x.Square ? 1 : 0, Glow = x.Glow, Outside = x.Outside, Corner = x.Corner, Quadrant = x.Quadrant, FontSize = x.FontSize, HasBox = x.HasBox, BadgeX = x.BadgePosition?.X, BadgeY = x.BadgePosition?.Y },
        MosaicAnnotation x => new() { Type = "mosaic", Pts = P(x.A, x.B), BlockSize = x.BlockSize },
        ImageAnnotation x => new() { Type = "image", Pts = P(x.A, x.B), Iw = x.Image.Width, Ih = x.Image.Height, Data = Pack(x.Image.Bgra) },
        PatchAnnotation x => new() { Type = "patch", Color = x.Color, Pts = P(x.A, x.B) },
        SpotlightAnnotation x => new() { Type = "spotlight", Pts = P(x.A, x.B) },
        _ => new() { Type = "unknown" },
        };
        dto.DetailScale = a.DetailScale;
        return dto;
    }

    static string Pack(byte[] bgra)
    {
        using var ms = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Fastest)) z.Write(bgra);
        return Convert.ToBase64String(ms.ToArray());
    }

    static ImageAnnotation? Unpack(AnnotationDto d)
    {
        if (d.Iw <= 0 || d.Ih <= 0 || d.Data == null) return null;
        var img = new Graphics.PixelImage(d.Iw, d.Ih);
        using var z = new System.IO.Compression.ZLibStream(new MemoryStream(Convert.FromBase64String(d.Data)), System.IO.Compression.CompressionMode.Decompress);
        z.ReadExactly(img.Bgra);
        return new ImageAnnotation { Image = img, A = Pt(d.Pts, 0), B = Pt(d.Pts, 1) };
    }

    public static Annotation? FromDto(AnnotationDto d)
    {
        var annotation = ReadDto(d);
        if (annotation != null)
            annotation.DetailScale = float.IsFinite(d.DetailScale) && d.DetailScale > 0 ? d.DetailScale : 1;
        return annotation;
    }

    static Annotation? ReadDto(AnnotationDto d)
    {
        switch (d.Type)
        {
            case "arrow": return new ArrowAnnotation { Color = d.Color, Width = d.Width, From = Pt(d.Pts, 0), To = Pt(d.Pts, 1) };
            case "shape":
                return new RectAnnotation
                {
                    Color = d.Color, Width = d.Width, A = Pt(d.Pts, 0), B = Pt(d.Pts, 1),
                    Shape = (ShapeKind)d.Kind, Fill = (FillStyle)d.Fill, Text = d.Text ?? "", FontSize = d.FontSize,
                };
            case "bubble": return new BubbleAnnotation
            {
                Color = d.Color, Tail = Pt(d.Pts, 0), Dir = (TailDir)d.Kind, Text = d.Text ?? "", FontSize = d.FontSize,
                BodyPosition = OptionalPoint(d.BubbleBody), TailTip = OptionalPoint(d.BubbleTip),
                TailSide = d.TailSide is >= 0 and < 6 ? (TailDir)d.TailSide.Value : null,
                TailPosition = d.TailPosition is { } ratio && float.IsFinite(ratio) ? Math.Clamp(ratio, 0, 1) : null,
                TailWidth = d.TailWidth is { } width && float.IsFinite(width) ? MathF.Max(0, width) : null,
            };
            case "text": return new TextAnnotation { Color = d.Color, Position = Pt(d.Pts, 0), Text = d.Text ?? "", FontSize = d.FontSize };
            case "pen":
            {
                var pen = new PenAnnotation { Color = d.Color, Width = d.Width, Highlighter = d.Highlighter };
                for (int i = 0; i < d.Pts.Length / 2; i++) pen.Points.Add(Pt(d.Pts, i));
                return pen;
            }
            case "sticker": return new StickerAnnotation { Center = Pt(d.Pts, 0), Kind = (StickerKind)d.Kind, Size = d.Size };
            case "number": return new NumberAnnotation { Color = d.Color, Width = d.Width, Center = Pt(d.Pts, 0), End = Pt(d.Pts, 1), Number = d.Number, Size = d.Size, Square = d.Kind == 1, Glow = d.Glow, Outside = d.Outside, Corner = d.Corner, Quadrant = d.Quadrant, FontSize = d.FontSize, HasBox = d.HasBox, BadgePosition = d.BadgeX is { } x && d.BadgeY is { } y && float.IsFinite(x) && float.IsFinite(y) ? new(x, y) : null };
            case "mosaic": return new MosaicAnnotation { A = Pt(d.Pts, 0), B = Pt(d.Pts, 1), BlockSize = d.BlockSize };
            case "image": try { return Unpack(d); } catch { return null; }
            case "patch": return new PatchAnnotation { Color = d.Color, A = Pt(d.Pts, 0), B = Pt(d.Pts, 1) };
            case "spotlight": return new SpotlightAnnotation { A = Pt(d.Pts, 0), B = Pt(d.Pts, 1) };
            default: return null;
        }
    }
}

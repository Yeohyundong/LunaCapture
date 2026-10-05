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
    /// <summary>좌표들 [x0, y0, x1, y1, …] (이미지 픽셀)</summary>
    public float[] Pts { get; set; } = [];
    public string? Text { get; set; }
    public float FontSize { get; set; }
    public int Kind { get; set; }      // 스티커 종류 / 말풍선 꼬리 방향 / 도형 모양
    public int Fill { get; set; }      // 도형 채우기
    public int Number { get; set; }
    public float Size { get; set; }
    public int BlockSize { get; set; }
    public bool Highlighter { get; set; }
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

    public static AnnotationDto ToDto(Annotation a) => a switch
    {
        ArrowAnnotation x => new() { Type = "arrow", Color = x.Color, Width = x.Width, Pts = P(x.From, x.To) },
        RectAnnotation x => new() { Type = "shape", Color = x.Color, Width = x.Width, Pts = P(x.A, x.B), Kind = (int)x.Shape, Fill = (int)x.Fill, Text = x.Text, FontSize = x.FontSize },
        BubbleAnnotation x => new() { Type = "bubble", Color = x.Color, Pts = P(x.Tail), Kind = (int)x.Dir, Text = x.Text, FontSize = x.FontSize },
        TextAnnotation x => new() { Type = "text", Color = x.Color, Pts = P(x.Position), Text = x.Text, FontSize = x.FontSize },
        PenAnnotation x => new() { Type = "pen", Color = x.Color, Width = x.Width, Pts = P([.. x.Points]), Highlighter = x.Highlighter },
        StickerAnnotation x => new() { Type = "sticker", Pts = P(x.Center), Kind = (int)x.Kind, Size = x.Size },
        NumberAnnotation x => new() { Type = "number", Color = x.Color, Pts = P(x.Center), Number = x.Number, Size = x.Size },
        MosaicAnnotation x => new() { Type = "mosaic", Pts = P(x.A, x.B), BlockSize = x.BlockSize },
        _ => new() { Type = "unknown" },
    };

    public static Annotation? FromDto(AnnotationDto d)
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
            case "bubble": return new BubbleAnnotation { Color = d.Color, Tail = Pt(d.Pts, 0), Dir = (TailDir)d.Kind, Text = d.Text ?? "", FontSize = d.FontSize };
            case "text": return new TextAnnotation { Color = d.Color, Position = Pt(d.Pts, 0), Text = d.Text ?? "", FontSize = d.FontSize };
            case "pen":
            {
                var pen = new PenAnnotation { Color = d.Color, Width = d.Width, Highlighter = d.Highlighter };
                for (int i = 0; i < d.Pts.Length / 2; i++) pen.Points.Add(Pt(d.Pts, i));
                return pen;
            }
            case "sticker": return new StickerAnnotation { Center = Pt(d.Pts, 0), Kind = (StickerKind)d.Kind, Size = d.Size };
            case "number": return new NumberAnnotation { Color = d.Color, Center = Pt(d.Pts, 0), Number = d.Number, Size = d.Size };
            case "mosaic": return new MosaicAnnotation { A = Pt(d.Pts, 0), B = Pt(d.Pts, 1), BlockSize = d.BlockSize };
            default: return null;
        }
    }
}

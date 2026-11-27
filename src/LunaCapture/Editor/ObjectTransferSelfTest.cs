using LunaCapture.Graphics;
using LunaCapture.Interop;

namespace LunaCapture.Editor;

internal static class ObjectTransferSelfTest
{
    public static void Run(PixelImage background, IReadOnlyList<Annotation> source)
    {
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
        byte[] data = ObjectTransfer.Encode(source);
        var pasted = ObjectTransfer.Decode(data);
        Check(pasted.Count == source.Count, "객체 복사 개수 불일치");
        Check(pasted.Select(x => x.GetType()).SequenceEqual(source.Select(x => x.GetType())), "객체 종류·겹침 순서 불일치");
        for (int i = 0; i < pasted.Count; i++) Check(!ReferenceEquals(source[i], pasted[i]), "붙여넣은 객체가 원본과 공유됨");
        var before = Composer.Compose(background, source);
        var after = Composer.Compose(background, pasted);
        Check(before.Bgra.SequenceEqual(after.Bgra), "객체 복사 후 모양 불일치");

        var pen = pasted.OfType<PenAnnotation>().First();
        var sourcePen = source.OfType<PenAnnotation>().First();
        var oldPoint = sourcePen.Points[0];
        pen.Offset(11, 13);
        Check(sourcePen.Points[0].X == oldPoint.X && sourcePen.Points[0].Y == oldPoint.Y, "붙여넣은 펜 이동이 원본에 반영됨");
        var pastedImage = pasted.OfType<ImageAnnotation>().First();
        var sourceImage = source.OfType<ImageAnnotation>().First();
        byte oldPixel = sourceImage.Image.Bgra[0];
        pastedImage.Image.Bgra[0] ^= 255;
        Check(sourceImage.Image.Bgra[0] == oldPixel, "붙여넣은 이미지가 원본과 공유됨");

        var number = pasted.OfType<NumberAnnotation>().First();
        int sourceNumber = source.OfType<NumberAnnotation>().First().Number;
        number.Number = 99;
        Check(source.OfType<NumberAnnotation>().First().Number == sourceNumber, "붙여넣은 번호 개별 수정 실패");
        var bubble = pasted.OfType<BubbleAnnotation>().Last();
        var sourceBubble = source.OfType<BubbleAnnotation>().Last();
        string text = sourceBubble.Text;
        bubble.Text = "붙여넣은 말풍선만 수정";
        Check(sourceBubble.Text == text, "붙여넣은 말풍선 개별 수정 실패");

        using var measure = new GdiSurface(16, 16);
        using var g = new Gfx(measure.Dc, forImage: true);
        var shapes = new List<Annotation>
        {
            new RectAnnotation { A = new(500, 400), B = new(600, 500), Width = 3, Color = 0xFFFF0000 },
            new NumberAnnotation { Center = new(520, 430), End = new(580, 480), Size = 20, Number = 5, HasBox = true },
        };
        var originalBounds = ObjectTransfer.Bounds(shapes, g);
        var firstRelative = new PointF(shapes[1].Bounds(g).X - originalBounds.X, shapes[1].Bounds(g).Y - originalBounds.Y);
        ObjectTransfer.FitInto(shapes, 200, 150, g);
        var fitted = ObjectTransfer.Bounds(shapes, g);
        Check(ObjectTransfer.Contains(new(0, 0, 200, 150), fitted), "작은 이미지에 객체 위치 맞추기 실패");
        Check(MathF.Abs(fitted.Width - originalBounds.Width) < 0.01f && MathF.Abs(fitted.Height - originalBounds.Height) < 0.01f, "붙여넣기에서 객체 크기 변경");
        Check(MathF.Abs(shapes[1].Bounds(g).X - fitted.X - firstRelative.X) < 0.01f, "객체 상대 위치 변경");
        Check(ObjectTransfer.Contains(new(0, 0, 300, 200), new(10, 10, 20, 20)) &&
            !ObjectTransfer.Contains(new(0, 0, 30, 30), new(10, 10, 50, 50)), "드래그 객체 선택 범위 실패");
        var repeated = ObjectTransfer.Decode(data);
        Check(Composer.Compose(background, repeated).Bgra.SequenceEqual(before.Bgra), "반복 붙여넣기에 원본 변경 반영");
        try { ObjectTransfer.Decode("{}"u8.ToArray()); throw new Exception("잘못된 객체 데이터 허용"); }
        catch (InvalidDataException) { }
    }
}

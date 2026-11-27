using LunaCapture.Graphics;
using LunaCapture.Interop;

namespace LunaCapture.Editor;

internal static class ObjectScaleSelfTest
{
    public static void Run(PixelImage background, IReadOnlyList<Annotation> source)
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
        static bool Near(float a, float b) => MathF.Abs(a - b) < 0.002f;
        static bool Same(PointF a, PointF b) => Near(a.X, b.X) && Near(a.Y, b.Y);
        using var surface = new GdiSurface(16, 16);
        using var g = new Gfx(surface.Dc, forImage: true);
        RectF bounds = new(100, 70, 300, 200);
        PointF[] corners = [new(100, 70), new(400, 70), new(100, 270), new(400, 270)];
        for (int i = 0; i < 4; i++)
        {
            var fixedPoint = corners[3 - i];
            var initial = new PointF(corners[i].X + 3, corners[i].Y - 2);
            var moved = new PointF(initial.X + (corners[i].X - fixedPoint.X) * 0.7f,
                initial.Y + (corners[i].Y - fixedPoint.Y) * 0.7f);
            Check(Near(ObjectScale.FromPointer(bounds, i, initial, initial), 1), "손잡이 클릭 시 크기 변경");
            Check(Near(ObjectScale.FromPointer(bounds, i, initial, moved), 1.7f), "모서리 비율 계산 실패");
            Check(Same(ObjectScale.Point(fixedPoint, fixedPoint, 1.7f), fixedPoint), "반대 모서리 고정 실패");
        }
        var limits = ObjectScale.Limits([new TextAnnotation { FontSize = 8, Width = 2 }], bounds, 1);
        Check(Near(limits.Min, 1), "최소 글자 크기 제한 실패");
        var anchor = new PointF(25, 15);
        const float factor = 1.6f;
        var scaled = source.Select(x => ObjectScale.Apply(x, anchor, factor, g)).ToList();
        for (int i = 0; i < source.Count; i++)
        {
            Check(!ReferenceEquals(source[i], scaled[i]), "크기 조절 원본 공유");
            Check(Near(scaled[i].Width, source[i].Width * factor), "선 굵기 배율 불일치");
            Check(Near(scaled[i].DetailScale, source[i].DetailScale * factor), "강조 테두리 배율 불일치");
            if (source[i] is BubbleAnnotation original && scaled[i] is BubbleAnnotation bubble)
            {
                var oldBody = original.BodyBounds(g);
                Check(Same(bubble.BodyPosition!.Value, ObjectScale.Point(new(oldBody.X, oldBody.Y), anchor, factor)), "말풍선 몸통 위치 불일치");
                Check(Same(bubble.TailHandles(g).End, ObjectScale.Point(original.TailHandles(g).End, anchor, factor)), "말풍선 꼬리 끝점 불일치");
                Check(Near(bubble.FontSize, original.FontSize * factor), "말풍선 글자 배율 불일치");
                if (original.TailWidth is { } width) Check(Near(bubble.TailWidth!.Value, width * factor), "말풍선 꼬리 폭 불일치");
            }
        }
        var pen = scaled.OfType<PenAnnotation>().First();
        var originalPen = source.OfType<PenAnnotation>().First();
        Check(Same(pen.Points[0], ObjectScale.Point(originalPen.Points[0], anchor, factor)), "펜 좌표 배율 불일치");
        var savedPoint = originalPen.Points[0];
        pen.Points[0] = new(0, 0);
        Check(Same(originalPen.Points[0], savedPoint), "펜 원본 변경");
        scaled = source.Select(x => ObjectScale.Apply(x, anchor, factor, g)).ToList();
        var number = new NumberAnnotation { Center = new(80, 60), End = new(240, 180),
            BadgePosition = new(60, 50), FontSize = 20, Size = 32, Width = 3, Number = 12, Square = true, Glow = true, HasBox = true };
        var resizedNumber = (NumberAnnotation)ObjectScale.Apply(number, anchor, factor, g);
        Check(Near(resizedNumber.FontSize, 32) && Same(resizedNumber.BadgePosition!.Value,
            ObjectScale.Point(number.BadgePosition!.Value, anchor, factor)), "번호 글자·수동 위치 불일치");
        var restored = ObjectTransfer.Decode(ObjectTransfer.Encode(scaled));
        Check(Composer.Compose(background, scaled).Bgra.SequenceEqual(Composer.Compose(background, restored).Bgra), "확대 객체 복사·저장 모양 불일치");
        var reduced = source.Select(x => ObjectScale.Apply(x, anchor, 0.65f, g)).ToList();
        var reducedRestored = ObjectTransfer.Decode(ObjectTransfer.Encode(reduced));
        Check(Composer.Compose(background, reduced).Bgra.SequenceEqual(Composer.Compose(background, reducedRestored).Bgra), "축소 객체 복사·저장 모양 불일치");
        var unchanged = source.Select(x => ObjectScale.Apply(x, anchor, 1, g)).ToList();
        Check(Composer.Compose(background, source).Bgra.SequenceEqual(Composer.Compose(background, unchanged).Bgra), "배율 100%에서 원본 모양 변경");
    }
}

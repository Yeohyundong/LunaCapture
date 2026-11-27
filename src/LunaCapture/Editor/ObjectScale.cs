using LunaCapture.Interop;

namespace LunaCapture.Editor;

internal static class ObjectScale
{
    public static PointF Point(PointF point, PointF anchor, float factor) =>
        new(anchor.X + (point.X - anchor.X) * factor, anchor.Y + (point.Y - anchor.Y) * factor);

    public static float FromPointer(RectF bounds, int corner, PointF initialPointer, PointF pointer)
    {
        PointF[] corners = [new(bounds.X, bounds.Y), new(bounds.X + bounds.Width, bounds.Y),
            new(bounds.X, bounds.Y + bounds.Height), new(bounds.X + bounds.Width, bounds.Y + bounds.Height)];
        var fixedPoint = corners[3 - corner];
        var movingPoint = corners[corner];
        float dx = movingPoint.X - fixedPoint.X, dy = movingPoint.Y - fixedPoint.Y;
        float lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 0.001f) return 1;
        return 1 + ((pointer.X - initialPointer.X) * dx + (pointer.Y - initialPointer.Y) * dy) / lengthSquared;
    }

    public static (float Min, float Max) Limits(IReadOnlyList<Annotation> items, RectF bounds, float editorScale)
    {
        float minimum = MathF.Min(1, 8 * editorScale / MathF.Max(0.001f, MathF.Min(bounds.Width, bounds.Height)));
        float maximum = float.MaxValue;
        void Include(float value, float min, float max = float.MaxValue)
        {
            if (value <= 0) return;
            minimum = MathF.Max(minimum, MathF.Min(1, min / value));
            maximum = MathF.Min(maximum, MathF.Max(1, max / value));
        }
        foreach (var item in items)
        {
            if (item is TextAnnotation text) Include(text.FontSize, 8 * editorScale, 144 * editorScale);
            if (item is NumberAnnotation number) Include(number.EffectiveFontSize, 8 * editorScale, 144 * editorScale);
            if (item is StickerAnnotation sticker) Include(sticker.Size, 12 * editorScale);
            if (item is MosaicAnnotation mosaic) Include(mosaic.BlockSize, 2);
            Include(item.Width, 0.5f * editorScale);
        }
        return (minimum, maximum);
    }

    public static Annotation Apply(Annotation original, PointF anchor, float factor, Gfx g)
    {
        if (!float.IsFinite(factor) || factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
        var item = original.Clone();
        PointF Map(PointF p) => Point(p, anchor, factor);
        switch (item)
        {
            case RectAnnotation shape:
                shape.A = Map(shape.A); shape.B = Map(shape.B);
                shape.FontSize *= factor;
                break;
            case BubbleAnnotation bubble:
                bubble.AnchorBody(g);
                bubble.BodyPosition = Map(bubble.BodyPosition!.Value);
                bubble.Tail = Map(bubble.Tail);
                if (bubble.TailTip is { } tip) bubble.TailTip = Map(tip);
                if (bubble.TailWidth is { } width) bubble.TailWidth = width * factor;
                bubble.FontSize *= factor;
                break;
            case TextAnnotation text:
                text.Position = Map(text.Position); text.FontSize *= factor;
                break;
            case ArrowAnnotation arrow:
                arrow.From = Map(arrow.From); arrow.To = Map(arrow.To);
                break;
            case PenAnnotation pen:
                for (int i = 0; i < pen.Points.Count; i++) pen.Points[i] = Map(pen.Points[i]);
                break;
            case StickerAnnotation sticker:
                sticker.Center = Map(sticker.Center); sticker.Size *= factor;
                break;
            case NumberAnnotation number:
                number.Center = Map(number.Center); number.End = Map(number.End);
                number.Size *= factor; number.FontSize *= factor;
                if (number.BadgePosition is { } badge) number.BadgePosition = Map(badge);
                break;
            case MosaicAnnotation mosaic:
                mosaic.A = Map(mosaic.A); mosaic.B = Map(mosaic.B);
                mosaic.BlockSize = (int)Math.Clamp(MathF.Round(mosaic.BlockSize * factor), 2, int.MaxValue);
                break;
            case ImageAnnotation image:
                image.A = Map(image.A); image.B = Map(image.B);
                break;
            case PatchAnnotation patch:
                patch.A = Map(patch.A); patch.B = Map(patch.B);
                break;
            case SpotlightAnnotation spotlight:
                spotlight.A = Map(spotlight.A); spotlight.B = Map(spotlight.B);
                break;
            default: throw new InvalidOperationException("지원하지 않는 객체입니다.");
        }
        item.Width *= factor;
        item.DetailScale *= factor;
        return item;
    }
}

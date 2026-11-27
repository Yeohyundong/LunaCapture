using LunaCapture.Graphics;
using LunaCapture.Interop;
using LunaCapture.Services;

namespace LunaCapture.Editor;

internal static class BubbleSelfTest
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static bool Near(float a, float b) => MathF.Abs(a - b) < 0.01f;
    static bool Same(PointF a, PointF b) => Near(a.X, b.X) && Near(a.Y, b.Y);
    static bool Same(RectF a, RectF b) => Near(a.X, b.X) && Near(a.Y, b.Y) &&
        Near(a.Width, b.Width) && Near(a.Height, b.Height);

    public static void Run(string outputDirectory)
    {
        using var surface = new GdiSurface(1000, 600);
        using var g = new Gfx(surface.Dc, forImage: true);
        var bubble = new BubbleAnnotation
        {
            BodyPosition = new(86, 90.2f), Text = "", FontSize = 20,
            Color = 0xFF8B7CF6, Dir = TailDir.BottomLeft,
        };
        var caret = bubble.Caret(g);
        Check(Near(caret.x, 100) && Near(caret.y, 100), "말풍선 클릭 위치와 글자 시작 위치 불일치");
        Check(bubble.IsMeaningful, "빈 말풍선 몸통 유지 실패");
        var empty = bubble.BodyBounds(g);
        bubble.Text = "말풍선 몸통";
        var body = bubble.BodyBounds(g);
        Check(Near(empty.X, body.X) && Near(empty.Y, body.Y), "글자 입력 시 몸통 이동");
        foreach (var direction in Stickers.Tails)
        {
            bubble.SetTailDirection(direction, g);
            Check(Same(body, bubble.BodyBounds(g)), "꼬리 프리셋 변경 시 몸통 이동");
        }
        bubble.SetTailDirection(TailDir.BottomLeft, g);
        var old = (BubbleAnnotation)bubble.Clone();
        var tip = new PointF(40, 240);
        bubble.DragTail(1, tip, g);
        Check(Same(bubble.TailHandles(g).End, tip) && Same(body, bubble.BodyBounds(g)), "꼬리 끝점 독립 이동 실패");
        Check(old.TailTip == null, "꼬리 이동 시 되돌리기 복사본 변경");
        bubble.DragTail(0, new(body.X - 15, body.Y + body.Height * 0.5f), g);
        var handles = bubble.TailHandles(g);
        Check(bubble.TailSide == TailDir.Left && Same(handles.End, tip) && Same(body, bubble.BodyBounds(g)), "꼬리 시작점 이동 실패");
        var start = handles.Start;
        bubble.DragTail(2, new(handles.Width.X, handles.Start.Y + 3), g);
        handles = bubble.TailHandles(g);
        Check(Same(start, handles.Start) && Same(handles.End, tip) &&
            Near(MathF.Abs(handles.BaseB.Y - handles.BaseA.Y), 6), "꼬리 폭 독립 조절 실패");
        Check(bubble.HitTest(tip, 2, g), "꼬리 클릭 판정 실패");
        var restored = (BubbleAnnotation)AnnotationJson.FromDto(AnnotationJson.ToDto(bubble))!;
        Check(Same(body, restored.BodyBounds(g)) && Same(restored.TailHandles(g).End, tip) &&
            restored.TailWidth == bubble.TailWidth, "말풍선 사용자 꼬리 복원 실패");
        restored.Offset(12, 18);
        Check(Near(restored.BodyBounds(g).X, body.X + 12) &&
            Same(restored.TailHandles(g).End, new(tip.X + 12, tip.Y + 18)), "말풍선 전체 이동 실패");

        var legacy = new BubbleAnnotation { Tail = new(400, 350), Dir = TailDir.BottomRight, Text = "이전 기록", FontSize = 20 };
        var legacyBody = legacy.BodyBounds(g);
        var legacyRestored = (BubbleAnnotation)AnnotationJson.FromDto(AnnotationJson.ToDto(legacy))!;
        Check(legacyRestored.BodyPosition == null && Same(legacyBody, legacyRestored.BodyBounds(g)), "이전 말풍선 기록 호환 실패");
        legacyRestored.DragTail(1, new(430, 390), g);
        Check(Same(legacyBody, legacyRestored.BodyBounds(g)), "이전 말풍선 꼬리 수정 시 몸통 이동");

        // Render the same production paths used by the editor for visual inspection.
        g.FillRect(0xFFF3F1F7, 0, 0, 1000, 600);
        for (int i = 0; i < 4; i++)
        {
            var sample = new BubbleAnnotation
            {
                BodyPosition = new(70 + i % 2 * 470, 65 + i / 2 * 270),
                Text = i switch { 0 => "클릭 위치에 몸통", 1 => "꼬리 끝점 이동", 2 => "시작점 옮기기", _ => "꼬리 폭 조절" },
                FontSize = 23, Color = 0xFF8B7CF6,
            };
            var b = sample.BodyBounds(g);
            if (i > 0) sample.DragTail(1, new(b.X + 220, b.Y + 135), g);
            if (i > 1) sample.DragTail(0, new(b.X + b.Width + 20, b.Y + b.Height / 2), g);
            if (i == 3)
            {
                var h = sample.TailHandles(g);
                sample.DragTail(2, new(h.Width.X, h.Start.Y + 10), g);
            }
            sample.Draw(surface, g);
        }
        Gdi32.GdiFlush();
        PngEncoder.Save(surface.Crop(new RECT(0, 0, 1000, 600)), Path.Combine(outputDirectory, "bubble-layout.png"));
    }
}

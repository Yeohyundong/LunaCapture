namespace LunaCapture.Interop;

internal enum IconKind { None, Play, Pause, Stop, Close, RecordDot, Undo, Redo }

/// <summary>
/// 버튼용 작은 아이콘을 도형으로 직접 그린다.
/// 글꼴 기호(▶ ❚❚ ✕ ↶)는 글꼴마다 모양·굵기가 달라 섞여 보이므로, 어디서나 같은 모양이 되도록 벡터로 그린다.
/// </summary>
internal static class Icons
{
    /// <param name="size">아이콘이 들어갈 정사각형 한 변 (px)</param>
    public static void Draw(Gfx g, IconKind kind, float cx, float cy, float size, uint color)
    {
        float r = size / 2;
        float stroke = MathF.Max(1.5f, size / 8);
        switch (kind)
        {
            case IconKind.Play:
                g.FillPolygon(color, [new(cx - r * 0.55f, cy - r * 0.75f), new(cx + r * 0.8f, cy), new(cx - r * 0.55f, cy + r * 0.75f)]);
                break;

            case IconKind.Pause:
            {
                float bw = size * 0.22f, bh = size * 0.8f, gap = size * 0.18f;
                g.FillRoundRect(color, cx - gap / 2 - bw, cy - bh / 2, bw, bh, bw * 0.3f);
                g.FillRoundRect(color, cx + gap / 2, cy - bh / 2, bw, bh, bw * 0.3f);
                break;
            }

            case IconKind.Stop:
            {
                float s = size * 0.7f;
                g.FillRoundRect(color, cx - s / 2, cy - s / 2, s, s, s * 0.18f);
                break;
            }

            case IconKind.Close:
            {
                float d = r * 0.62f;
                g.Line(color, stroke, cx - d, cy - d, cx + d, cy + d);
                g.Line(color, stroke, cx + d, cy - d, cx - d, cy + d);
                break;
            }

            case IconKind.RecordDot:
            {
                float d = size * 0.62f;
                g.FillEllipse(color, cx - d / 2, cy - d / 2, d, d);
                break;
            }

            case IconKind.Undo:
            case IconKind.Redo:
            {
                // 위쪽으로 휘는 반원 화살표. Undo는 왼쪽 끝, Redo는 오른쪽 끝에 화살촉.
                float ar = r * 0.62f, ay = cy + r * 0.2f;
                g.Arc(color, stroke, cx - ar, ay - ar, ar * 2, ar * 2, 180, 180);
                float hx = kind == IconKind.Undo ? cx - ar : cx + ar;
                float hs = r * 0.42f;
                g.FillPolygon(color, [new(hx - hs, ay - stroke * 0.3f), new(hx + hs, ay - stroke * 0.3f), new(hx, ay + hs)]);
                break;
            }
        }
    }
}

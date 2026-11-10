using LunaCapture.Interop;

namespace LunaCapture.Editor;

internal enum ItemKind { Tool, Color, Thickness, FontSize, Undo, Redo, History, Copy, SaveAs, OpenFolder, Settings, Done }

internal struct ToolbarItem
{
    public ItemKind Kind;
    public int Value;
    public RECT Bounds;
    public string Tip;
}

/// <summary>편집기 상단 툴바: 배치, 그리기, 클릭 판정.</summary>
internal sealed class Toolbar : IDisposable
{
    public static readonly uint[] Palette =
    [
        0xFFE5484D, // 빨강
        0xFFF76B15, // 주황
        0xFFFFC53D, // 노랑
        0xFF30A46C, // 초록
        0xFF0090FF, // 파랑
        0xFF8B7CF6, // 보라
        0xFF1C1C1C, // 검정
        0xFFFFFFFF, // 흰색
    ];
    static readonly string[] PaletteNames = ["빨강", "주황", "노랑", "초록", "파랑", "보라", "검정", "흰색"];

    public const uint Accent = 0xFF8B7CF6;
    const uint Bg = 0xFF2A2A30, Hover = 0xFF3A3A44, IconColor = 0xFFE6E6EC, Dim = 0xFF6A6A74, Separator = 0xFF3C3C44;

    public readonly List<ToolbarItem> Items = new();
    public int Height { get; private set; }
    public int MinWidth { get; private set; }

    // 그리기에 필요한 현재 상태 (편집기가 채워 넣음)
    public Tool CurrentTool;
    public int ThicknessIndex;
    public uint CurrentColor = 0xFFE5484D;
    public float TextSize = 24;
    public bool CanUndo, CanRedo;
    public int HoverIndex = -1;
    public string Status = "";
    public bool PanelVisible;

    float _s = 1f;
    GdiFont? _label, _iconT;

    public void SetScale(float scale)
    {
        _s = scale;
        _label?.Dispose(); _iconT?.Dispose();
        _label = new GdiFont(UiFonts.Family, 14 * scale, bold: false);
        _iconT = new GdiFont(UiFonts.Family, 17 * scale, bold: true);
        Height = (int)(48 * scale);
    }

    int S(float v) => (int)MathF.Round(v * _s);

    public void Layout(int width)
    {
        Items.Clear();
        int btn = S(34), gap = S(2), sep = S(14), top = (Height - btn) / 2;
        int x = S(8);

        void Add(ItemKind kind, int value, int w, string tip)
        {
            Items.Add(new ToolbarItem { Kind = kind, Value = value, Bounds = new RECT(x, top, x + w, top + btn), Tip = tip });
            x += w + gap;
        }

        Add(ItemKind.Tool, (int)Tool.Rect, btn, "도형 (R) · 다시 누르면 옵션 열기/닫기 · 더블클릭하면 글자 넣기");
        Add(ItemKind.Tool, (int)Tool.Select, btn, "영역 선택 (E) · 복사·저장·오려내기·자르기·강조 · Ctrl+A 전체");
        Add(ItemKind.Tool, (int)Tool.Arrow, btn, "화살표 (A)");
        Add(ItemKind.Tool, (int)Tool.Pen, btn, "펜 (P)");
        Add(ItemKind.Tool, (int)Tool.Highlighter, btn, "형광펜 (H)");
        Add(ItemKind.Tool, (int)Tool.Text, btn, "텍스트 (T)");
        Add(ItemKind.Tool, (int)Tool.Bubble, btn, "말풍선 (B) · 다시 누르면 옵션 열기/닫기");
        Add(ItemKind.Tool, (int)Tool.Number, btn, "번호 (N) · 다시 누르면 옵션 열기/닫기 · Esc로 접기");
        Add(ItemKind.Tool, (int)Tool.Sticker, btn, "스티커 (S) · 다시 누르면 옵션 열기/닫기");
        Add(ItemKind.Tool, (int)Tool.Mosaic, btn, "모자이크 (M)");
        x += sep;
        Add(ItemKind.Color, 0, S(36), "색상 팔레트 · 맞춤 색상 · 추천 색상");
        x += sep;
        Add(ItemKind.Thickness, 0, S(30), "얇게 (1)");
        Add(ItemKind.Thickness, 1, S(30), "보통 (2)");
        Add(ItemKind.Thickness, 2, S(30), "굵게 (3)");
        x += sep;
        Add(ItemKind.FontSize, -1, S(30), "글자 작게 · 텍스트·말풍선·도형 안 글자 (최소 8)");
        Add(ItemKind.FontSize, 0, S(64), "글자 크기 · 입력 중이거나 선택한 글자에 적용");
        Add(ItemKind.FontSize, 1, S(30), "글자 크게 · 텍스트·말풍선·도형 안 글자 (최대 144)");
        x += sep;
        Add(ItemKind.Undo, 0, btn, "되돌리기 (Ctrl+Z)");
        Add(ItemKind.Redo, 0, btn, "다시 실행 (Ctrl+Y)");
        int leftEnd = x;

        // 오른쪽 정렬 버튼들
        int tw = S(58);
        int rightWidth = tw * 5 + S(90) + gap * 5 + S(8);
        int rx = Math.Max(leftEnd + S(24), width - rightWidth);
        x = rx;
        Add(ItemKind.History, 0, tw, "캡처 기록 패널 열기/닫기");
        Add(ItemKind.Copy, 0, tw, "클립보드에 복사 (Ctrl+C)");
        Add(ItemKind.SaveAs, 0, tw, "다른 이름으로 저장 (Ctrl+S)");
        Add(ItemKind.OpenFolder, 0, S(90), "캡처 사진 저장 폴더 열기");
        Add(ItemKind.Settings, 0, tw, "설정 · 업데이트 · 이거 고쳐주세요!");
        Add(ItemKind.Done, 0, tw, "완료 (Enter) · 편집 내용이 자동 저장돼요");

        MinWidth = leftEnd + S(24) + rightWidth;
    }

    public int HitTest(int x, int y)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            var b = Items[i].Bounds;
            if (x >= b.Left && x < b.Right && y >= b.Top && y < b.Bottom) return i;
        }
        return -1;
    }

    public void Draw(Gfx g, int width)
    {
        g.FillRect(Bg, 0, 0, width, Height);
        g.FillRect(Separator, 0, Height - 1, width, 1);

        for (int i = 0; i < Items.Count; i++)
        {
            var it = Items[i];
            var b = it.Bounds;
            bool selected = it.Kind switch
            {
                ItemKind.Tool => it.Value == (int)CurrentTool,
                ItemKind.Thickness => it.Value == ThicknessIndex,
                ItemKind.History => PanelVisible,
                _ => false,
            };
            bool enabled = it.Kind switch
            {
                ItemKind.Undo => CanUndo,
                ItemKind.Redo => CanRedo,
                _ => true,
            };

            if (it.Kind == ItemKind.Done)
                g.FillRect(i == HoverIndex ? 0xFF9D90FF : Accent, b.Left, b.Top, b.Width, b.Height);
            else if (selected)
                g.FillRect(it.Kind == ItemKind.Tool ? Accent : Hover, b.Left, b.Top, b.Width, b.Height);
            else if (i == HoverIndex && enabled && it.Kind != ItemKind.Color)
                g.FillRect(Hover, b.Left, b.Top, b.Width, b.Height);

            uint fg = !enabled ? Dim : (selected && it.Kind == ItemKind.Tool) ? 0xFFFFFFFF : IconColor;
            DrawItem(g, it, fg, i == HoverIndex);
        }

        // 상태 메시지: 왼쪽 그룹과 오른쪽 버튼 사이 빈 공간에.
        string text = HoverIndex >= 0 ? Items[HoverIndex].Tip : Status;
        if (text.Length > 0 && _label != null)
        {
            int left = Items.Last(x => x.Kind == ItemKind.Redo).Bounds.Right + S(16);
            int right = Items.First(x => x.Kind == ItemKind.History).Bounds.Left - S(12);
            float w = g.MeasureWidth(text, _label);
            if (w <= right - left)
                g.Text(text, _label, 0xFFA8A8B4, left, (Height - g.LineHeight(_label)) / 2);
        }
    }

    void DrawItem(Gfx g, ToolbarItem it, uint fg, bool hover)
    {
        var b = it.Bounds;
        float cx = (b.Left + b.Right) / 2f, cy = (b.Top + b.Bottom) / 2f;
        float r = S(8); // 아이콘 반경

        switch (it.Kind)
        {
            case ItemKind.Tool:
                DrawToolIcon(g, (Tool)it.Value, fg, cx, cy, r);
                break;

            case ItemKind.Color:
            {
                float d = S(hover ? 18 : 16);
                float ring = S(22);
                g.Ellipse(0xFFFFFFFF, S(2), cx - ring / 2, cy - ring / 2, ring, ring);
                g.FillEllipse(0xFFBBBBBB, cx - d / 2, cy - d / 2, d, d);
                g.FillEllipse(CurrentColor, cx - d / 2, cy - d / 2, d, d);
                if (CurrentColor == 0xFF1C1C1C) g.Ellipse(0xFF5A5A64, 1, cx - d / 2, cy - d / 2, d, d);
                break;
            }

            case ItemKind.Thickness:
            {
                float d = S(4 + it.Value * 4);
                g.FillEllipse(fg, cx - d / 2, cy - d / 2, d, d);
                break;
            }

            case ItemKind.FontSize:
                CenterText(g, it.Value < 0 ? "A−" : it.Value > 0 ? "A+" : $"{TextSize:0.#} px", _label!, fg, b);
                break;

            case ItemKind.Undo:
            case ItemKind.Redo:
                Icons.Draw(g, it.Kind == ItemKind.Undo ? IconKind.Undo : IconKind.Redo, cx, cy, S(16), fg);
                break;

            case ItemKind.History:
                CenterText(g, "기록", _label!, fg, b);
                break;
            case ItemKind.Copy:
                CenterText(g, "복사", _label!, fg, b);
                break;
            case ItemKind.SaveAs:
                CenterText(g, "저장", _label!, fg, b);
                break;
            case ItemKind.OpenFolder:
                CenterText(g, "저장 폴더", _label!, fg, b);
                break;
            case ItemKind.Settings:
                CenterText(g, "설정", _label!, fg, b);
                break;
            case ItemKind.Done:
                CenterText(g, "완료", _label!, 0xFFFFFFFF, b);
                break;
        }
    }

    void DrawToolIcon(Gfx g, Tool tool, uint fg, float cx, float cy, float r)
    {
        switch (tool)
        {
            case Tool.Select:
            {
                // 점선 사각형 (영역 선택)
                float x0 = cx - r, y0 = cy - r * 0.8f, x1 = cx + r, y1 = cy + r * 0.8f, w = S(1.6f);
                float dash = (x1 - x0) / 5;
                for (int i = 0; i < 5; i += 2)
                {
                    g.Line(fg, w, x0 + dash * i, y0, x0 + dash * (i + 1), y0);
                    g.Line(fg, w, x0 + dash * i, y1, x0 + dash * (i + 1), y1);
                }
                float vd = (y1 - y0) / 3;
                for (int i = 0; i < 3; i += 2)
                {
                    g.Line(fg, w, x0, y0 + vd * i, x0, y0 + vd * (i + 1));
                    g.Line(fg, w, x1, y0 + vd * i, x1, y0 + vd * (i + 1));
                }
                break;
            }
            case Tool.Arrow:
                new ArrowAnnotation { Color = fg, Width = S(2), From = new(cx - r, cy + r), To = new(cx + r, cy - r) }.Draw(null!, g);
                break;
            case Tool.Rect:
                g.Rect(fg, S(2), cx - r, cy - r * 0.75f, r * 2, r * 1.5f);
                break;
            case Tool.Pen:
                Pencil(g, fg, cx, cy, r);
                break;
            case Tool.Highlighter:
                Marker(g, fg, cx, cy, r);
                break;
            case Tool.Bubble:
            {
                var body = new RectF(cx - r * 1.05f, cy - r * 0.85f, r * 2.1f, r * 1.35f);
                nint p = Stickers.BubblePath(body, new PointF(cx - r * 0.55f, cy + r * 1.0f), r * 0.45f, TailDir.BottomLeft);
                g.DrawPath(p, fg, S(1.6f));
                Gfx.DeletePath(p);
                for (int i = 0; i < 3; i++)
                    g.FillEllipse(fg, cx - r * 0.5f + i * r * 0.5f - S(1.3f), cy - r * 0.2f - S(1.3f), S(2.6f), S(2.6f));
                break;
            }
            case Tool.Number:
            {
                g.Ellipse(fg, S(1.6f), cx - r, cy - r, r * 2, r * 2);
                CenterText(g, "1", _iconT!, fg, new RECT((int)(cx - r), (int)(cy - r), (int)(cx + r), (int)(cy + r)));
                break;
            }
            case Tool.Sticker:
                Stickers.Draw(g, StickerKind.Smile, cx, cy, r * 2.3f);
                break;
            case Tool.Text:
                CenterText(g, "T", _iconT!, fg, new RECT((int)(cx - r * 2), (int)(cy - r * 2), (int)(cx + r * 2), (int)(cy + r * 2)));
                break;
            case Tool.Mosaic:
            {
                float c = r * 2 / 3;
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 3; x++)
                        g.FillRect(((x + y) & 1) == 0 ? fg : (fg & 0x00FFFFFF) | 0x66000000,
                            cx - r + x * c, cy - r + y * c, c, c);
                break;
            }
        }
    }

    /// <summary>45° 기울어진 점을 계산 (아이콘을 대각선으로 눕히기 위해). x = 길이 방향, y = 폭 방향.</summary>
    static PointF Diag(float cx, float cy, float x, float y)
    {
        const float k = 0.70710678f; // cos 45°
        return new PointF(cx + (x + y) * k, cy + (y - x) * k);
    }

    /// <summary>연필: 왼쪽 아래를 향한 뾰족한 심 + 몸통 + 지우개.</summary>
    void Pencil(Gfx g, uint fg, float cx, float cy, float r)
    {
        float L = r * 2.5f, W = r * 0.62f, tip = r * 0.7f, cap = r * 0.45f;
        float x0 = -L / 2, x1 = x0 + tip, x2 = L / 2 - cap, x3 = L / 2, h = W / 2;
        g.FillPolygon(fg, [Diag(cx, cy, x0, 0), Diag(cx, cy, x1, -h), Diag(cx, cy, x1, h)]);
        g.FillPolygon((fg & 0x00FFFFFF) | 0x55000000, [Diag(cx, cy, x1, -h), Diag(cx, cy, x2, -h), Diag(cx, cy, x2, h), Diag(cx, cy, x1, h)]);
        g.FillPolygon(fg, [Diag(cx, cy, x2 + S(1), -h), Diag(cx, cy, x3, -h), Diag(cx, cy, x3, h), Diag(cx, cy, x2 + S(1), h)]);
        var outline = Gfx.NewPath();
        Gfx.AddPolygon(outline, [Diag(cx, cy, x0, 0), Diag(cx, cy, x1, -h), Diag(cx, cy, x3, -h), Diag(cx, cy, x3, h), Diag(cx, cy, x1, h)]);
        g.DrawPath(outline, fg, S(1.4f));
        Gfx.DeletePath(outline);
    }

    /// <summary>형광펜: 굵은 몸통 + 비스듬한 납작 심(현재 색) + 아래에 반투명 칠한 자국.</summary>
    void Marker(Gfx g, uint fg, float cx, float cy, float r)
    {
        uint ink = CurrentColor == 0xFF1C1C1C ? 0xFFFFC53D : CurrentColor;
        g.Line((ink & 0x00FFFFFF) | 0x99000000, S(5), cx - r * 1.05f, cy + r * 0.95f, cx + r * 0.2f, cy + r * 0.95f);
        float L = r * 2.0f, W = r * 0.95f, tip = r * 0.55f;
        float x0 = -L / 2 - r * 0.15f, x1 = x0 + tip, x3 = L / 2, h = W / 2;
        float ox = cx + r * 0.12f, oy = cy - r * 0.12f;
        g.FillPolygon(ink, [Diag(ox, oy, x0, -h * 0.15f), Diag(ox, oy, x0 + tip * 0.35f, -h * 0.55f), Diag(ox, oy, x1, -h * 0.55f), Diag(ox, oy, x1, h * 0.55f), Diag(ox, oy, x0, h * 0.35f)]);
        var body = Gfx.NewPath();
        Gfx.AddPolygon(body, [Diag(ox, oy, x1, -h), Diag(ox, oy, x3, -h), Diag(ox, oy, x3, h), Diag(ox, oy, x1, h)]);
        g.FillPath(body, (fg & 0x00FFFFFF) | 0x40000000);
        g.DrawPath(body, fg, S(1.4f));
        Gfx.DeletePath(body);
        g.Line(fg, S(1.4f), Diag(ox, oy, x1 + r * 0.5f, -h).X, Diag(ox, oy, x1 + r * 0.5f, -h).Y, Diag(ox, oy, x1 + r * 0.5f, h).X, Diag(ox, oy, x1 + r * 0.5f, h).Y);
    }

    static void CenterText(Gfx g, string text, GdiFont font, uint color, RECT b)
    {
        float w = g.MeasureWidth(text, font), h = g.LineHeight(font);
        g.Text(text, font, color, (b.Left + b.Right - w) / 2, (b.Top + b.Bottom - h) / 2);
    }

    public void Dispose()
    {
        _label?.Dispose(); _iconT?.Dispose();
        _label = _iconT = null;
    }
}

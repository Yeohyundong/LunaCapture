using LunaCapture.Interop;
using LunaCapture.Services;

namespace LunaCapture.Editor;

internal sealed class ColorPaletteWindow : ValuePanel
{
    readonly Action<uint> _apply;
    uint _color;
    NativeWindow? _detail;
    bool _choosingMood;
    static readonly (string Name, uint[] Colors)[] Moods =
    [
        ("Soft lavender · 라벤더", [0xFFDCD7EB, 0xFFA6B3D8, 0xFFB88EAC, 0xFF986578, 0xFF49465E, 0xFFF1EDF6]),
        ("Peach sunset · 피치 선셋", [0xFF48224E, 0xFFAA4465, 0xFFFCA7A2, 0xFFFFD5BD, 0xFFFFF4E7, 0xFFC87373]),
        ("Garden picnic · 가든", [0xFFBC4B51, 0xFF5E8C75, 0xFFF4A259, 0xFFF6E8A6, 0xFF8CB369, 0xFFF4F1DE]),
        ("Vintage rose · 빈티지 로즈", [0xFF87B9AB, 0xFFFFF0E9, 0xFFF4C4D6, 0xFFAF7291, 0xFF542E3C, 0xFFC7A8B5]),
        ("Pop summer · 팝 서머", [0xFFD71938, 0xFFFFBD39, 0xFF398BBC, 0xFF174D69, 0xFF13786B, 0xFFFFEAC2]),
        ("Coastal dusk · 해안의 저녁", [0xFF7C2D19, 0xFFFF7A27, 0xFFFFE4A5, 0xFF176A71, 0xFF082B3B, 0xFF89B7B2]),
        ("Ocean breeze · 오션", [0xFFFFAA2B, 0xFFF4EAD1, 0xFF8DC2CC, 0xFF3F9DB6, 0xFF0F5C71, 0xFFD9EBE7]),
        ("Modern contrast · 모던", [0xFFFFC438, 0xFFE91F47, 0xFF38B6AB, 0xFFF1F8F8, 0xFF102535, 0xFF81A6BA]),
        ("Warm neutral · 뉴트럴", [0xFFA47864, 0xFFFFBE98, 0xFF955251, 0xFFE5D5C5, 0xFF78685C, 0xFFF5EFE7])
    ];

    public ColorPaletteWindow(nint owner, uint color, Action<uint> apply, Action closed) : base(owner, 430, 610, closed)
    {
        _color = color; _apply = apply; Show();
    }
    // Shared conversion helpers used by palette generation and verification.
    internal static uint Hsv(double h, double s, double v, byte alpha = 255) => DetailedColorWindow.Hsv(h, s, v, alpha);
    internal static bool TryParseHex(string text, out uint color) => DetailedColorWindow.TryParseHex(text, out color);
    void Choose(uint color) { _apply(color); Destroy(); }
    void Line(Gfx g, int y) => g.FillRect(0xFF484850, Px(20), Px(y), Px(390), 1);

    void Swatch(Gfx g, string id, uint color, int x, int y, int size = 24)
    {
        // The same subtle ring surrounds every swatch; selection uses an outer accent ring.
        g.FillEllipse(0xFFBBBBBB, Px(x), Px(y), Px(size), Px(size));
        g.FillEllipse(color, Px(x), Px(y), Px(size), Px(size));
        g.Ellipse(0xFF85858E, S, Px(x), Px(y), Px(size), Px(size));
        if (color == _color) g.Ellipse(Accent, Px(2), Px(x - 3), Px(y - 3), Px(size + 6), Px(size + 6));
        AddItem(id, new RECT(Px(x - 2), Px(y - 2), Px(x + size + 2), Px(y + size + 2)), () => Choose(color));
    }

    void OpenDetail()
    {
        if (_detail != null) return;
        _detail = new DetailedColorWindow(Handle, _color, color =>
        {
            _color = color;
            _apply(color);
            Invalidate();
        }, () => { _detail = null; Invalidate(); });
    }

    protected override void OnDestroyed() { _detail?.Destroy(); base.OnDestroyed(); }

    protected override void Render(Gfx g)
    {
        Label(g, "색상 팔레트", 20, 18, 17);
        Button(g, "close", "×", 370, 10, 40, Destroy);
        if (_choosingMood) { DrawMoods(g); return; }

        Label(g, "My color", 20, 65, 14);
        Button(g, "add", "+", 20, 94, 32, OpenDetail);
        var colors = AppSettings.Current.CustomColors;
        for (int i = 0; i < Math.Min(colors.Count, 24); i++)
            Swatch(g, "custom" + i, colors[i], 65 + i % 12 * 28, 97 + i / 12 * 32, 20);
        Line(g, 168);

        Label(g, "추천 컬러", 20, 185, 14);
        Button(g, "moods", "팔레트 선택", 285, 177, 125, () => { _choosingMood = true; Invalidate(); });
        var mood = Moods[Math.Clamp(AppSettings.Current.RecommendedPalette, 0, Moods.Length - 1)];
        Label(g, mood.Name, 20, 219, 12);
        for (int i = 0; i < mood.Colors.Length; i++) Swatch(g, "mood" + i, mood.Colors[i], 22 + i * 65, 246, 32);
        Line(g, 297);

        Label(g, "기본 팔레트", 20, 315, 14);
        for (int row = 0; row < 7; row++) for (int col = 0; col < 12; col++)
        {
            uint c = row == 0 ? Hsv(0, 0, col / 11.0) : Hsv(col * 30, row == 1 ? 1 : 0.25 + (row - 2) * 0.18, row <= 2 ? 1 : 1 - (row - 2) * 0.14);
            Swatch(g, $"basic{row}-{col}", c, 22 + col * 32, 346 + row * 29, 22);
        }
        Line(g, 556);
        Button(g, "transparent", "투명", 20, 563, 85, () => Choose(0));
        Label(g, $"#{_color & 0xFFFFFF:X6} · A {Math.Round((_color >> 24) * 100.0 / 255)}%", 160, 570, 12);
    }

    void DrawMoods(Gfx g)
    {
        Label(g, "무드에 맞는 팔레트를 선택하세요", 20, 65, 14);
        Button(g, "back", "뒤로", 325, 59, 85, () => { _choosingMood = false; Invalidate(); });
        for (int i = 0; i < Moods.Length; i++)
        {
            int index = i, x = 20 + i % 3 * 134, y = 113 + i / 3 * 142;
            var mood = Moods[i];
            g.FillRoundRect(IsHover("theme" + i) ? ItemHover : ItemBg, Px(x - 4), Px(y - 4), Px(126), Px(119), Px(6));
            for (int c = 0; c < mood.Colors.Length; c++)
                g.FillRect(mood.Colors[c], Px(x + c * 19), Px(y), Px(19), Px(64));
            var name = mood.Name.Split(" · ");
            Label(g, name[0], x, y + 74, 11);
            Label(g, name[1], x, y + 92, 11);
            if (AppSettings.Current.RecommendedPalette == i) g.Rect(Accent, Px(2), Px(x - 4), Px(y - 4), Px(126), Px(119));
            AddItem("theme" + i, new RECT(Px(x - 4), Px(y - 4), Px(x + 122), Px(y + 115)), () =>
            {
                AppSettings.Current.RecommendedPalette = index;
                AppSettings.Save(); _choosingMood = false; Invalidate();
            });
        }
        Label(g, "선택한 무드는 추천 컬러 영역에 고정됩니다", 20, 555, 12);
    }
}

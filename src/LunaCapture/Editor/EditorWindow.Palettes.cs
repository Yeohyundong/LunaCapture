using LunaCapture.Interop;
using LunaCapture.Services;

namespace LunaCapture.Editor;

internal sealed unsafe partial class EditorWindow
{
    NativeWindow? _options;

    void OpenColorPalette()
    {
        if (_options != null) return;
        _options = new ColorPaletteWindow(Handle, _color, color =>
        {
            _color = color;
            AppSettings.Current.SelectedColor = color;
            AppSettings.Save();
            RecolorActiveText();
            RestyleSelected(color: true);
            Invalidate();
        }, () => _options = null);
    }

    NumberAnnotation? _numberEditing;
    string _numberDraft = "";
    bool _numberReplace;

    void OpenNumberEditor(NumberAnnotation number)
    {
        _numberEditing = number;
        _numberDraft = number.Number.ToString();
        _numberReplace = true;
        User32.SetFocus(Handle);
        ShowStatus("번호 입력 · Enter 확정 · Esc 취소 · Ctrl+A 전체 선택");
        Invalidate();
    }

    bool FinishNumberEdit(bool apply)
    {
        if (_numberEditing is not { } number) return true;
        if (apply)
        {
            if (!int.TryParse(_numberDraft, out int value) || value < 1)
            {
                ShowStatus("1~2147483647 사이의 번호를 입력하세요 · Esc 취소");
                return false;
            }
            if (number.Number != value) { PushUndo(); number.Number = value; RebuildCommitted(); }
        }
        _numberEditing = null;
        Invalidate();
        return true;
    }

    void NumberChar(char c)
    {
        if (c == '\b')
        {
            _numberDraft = _numberReplace || _numberDraft.Length == 0 ? "" : _numberDraft[..^1];
            _numberReplace = false;
        }
        else if (c >= '0' && c <= '9')
        {
            if (_numberReplace) _numberDraft = "";
            _numberReplace = false;
            if (_numberDraft.Length < 10) _numberDraft += c;
        }
        Invalidate();
    }

    void DrawNumberEdit(Gfx g)
    {
        if (_numberEditing is not { } n) return;
        var preview = (NumberAnnotation)n.Clone();
        if (int.TryParse(_numberDraft, out int draft) && draft > 0) preview.Number = draft;
        var center = preview.BadgeCenter;
        float size = preview.BadgeSize * _zoom;
        float cx = _imgX + center.X * _zoom, cy = _imgY + center.Y * _zoom;
        float x = cx - size / 2, y = cy - size / 2;
        uint color = (n.Color & 0xFFFFFF) | 0xFF000000;
        if (n.Square) g.FillRect(color, x, y, size, size);
        else g.FillEllipse(color, x, y, size, size);
        g.Rect(0xFFFFFFFF, MathF.Max(1, _scale), x - 2, y - 2, size + 4, size + 4);
        using var font = new GdiFont(UiFonts.Annotation, preview.EffectiveFontSize * _zoom, bold: true);
        float w = g.MeasureWidth(_numberDraft, font), h = g.LineHeight(font);
        if (_numberReplace) g.FillRect(0xFF285CB5, cx - w / 2 - 1, cy - h / 2, w + 2, h);
        g.Text(_numberDraft, font, _numberReplace ? 0xFFFFFFFF : Stickers.ContrastText(color), cx - w / 2, cy - h / 2);
        if (!_numberReplace) g.Line(0xFFFFFFFF, MathF.Max(1, _scale), cx + w / 2 + 1, cy - h / 2, cx + w / 2 + 1, cy + h / 2);
    }
}

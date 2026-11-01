using LunaCapture.Graphics;
using LunaCapture.Interop;
using LunaCapture.Services;

namespace LunaCapture.Editor;

/// <summary>
/// 캡처 직후 열리는 간단 편집기. 주석은 이미지 좌표로 보관하고, 창을 닫으면 결과 이미지를 콜백으로 넘긴다.
/// (편집한 게 없으면 null)
/// </summary>
internal sealed unsafe partial class EditorWindow : NativeWindow
{
    static readonly float[] LineWidths = [2.5f, 4.5f, 8f];
    float _textSize = 24f;
    static readonly int[] MosaicBlocks = [7, 12, 20];
    const uint CanvasBg = 0xFF1C1C20, ImageBorder = 0xFF3A3A44;
    const nuint StatusTimer = 1;

    PixelImage _base;
    EditorDocument _doc;
    readonly Func<HistoryEntry, EditorDocument?>? _openEntry; // 기록 패널에서 다른 캡처를 열 때
    readonly HistoryPanel? _panel;
    readonly Action _onClosed;
    readonly List<Annotation> _items = new();
    // 되돌리기: 바뀌기 직전의 주석 목록 전체를 복사해 둔다(주석 수가 적어 가볍다). 옮기기·수정·삭제도 모두 되돌릴 수 있다.
    // 원본 그림은 자르기를 할 때만 바뀌고 그림 자체는 고치지 않으므로, 참조만 들고 있어도 된다.
    readonly Stack<(PixelImage Base, List<Annotation> Items)> _undo = new(), _redo = new();

    // 선택 (그려 둔 주석을 클릭해서 옮기거나 고치기)
    enum DragMode { None, Draw, Move, Handle, Marquee, MarqueeMove }
    Annotation? _selected;
    DragMode _mode;
    int _handle = -1;
    PointF _dragLast;
    bool _dragChanged;
    bool _reEditing;            // 텍스트·말풍선·도형을 더블클릭해 다시 편집하는 중
    bool _typing;               // 글자 입력 중 (도형을 그리는 중과 구분: 도형도 TextAnnotation이라서)
    TextAnnotation? Editing => _typing ? _active as TextAnnotation : null;
    long _lastClickTick;
    POINT _lastClickPos;
    Annotation? _lastClickItem;
    readonly Toolbar _toolbar = new();

    GdiSurface? _committed, _work, _back;
    Annotation? _active;
    bool _dragging;
    bool _closing;

    Tool _tool = Tool.Rect;
    static StickerKind s_sticker = (StickerKind)Math.Clamp(Services.AppSettings.Current.Sticker, 0, Stickers.All.Length - 1); // 마지막으로 고른 스티커
    static readonly float[] NumberSizes = [24f, 32f, 44f];
    static readonly float[] StickerSizes = [36f, 52f, 72f];
    int _colorIndex, _thickness = 1;
    float _scale = 1f;
    float _zoom = 1f;
    int _imgX, _imgY, _dispW, _dispH;
    int _clientW, _clientH;
    nint _imeContext;

    /// <param name="openEntry">캡처 기록 패널에서 항목을 눌렀을 때 그 캡처를 문서로 만들어 준다 (null이면 패널 없음)</param>
    public EditorWindow(EditorDocument doc, string status, Action onClosed, Func<HistoryEntry, EditorDocument?>? openEntry = null)
    {
        _doc = doc;
        _base = doc.Original;
        _onClosed = onClosed;
        _openEntry = openEntry;
        _items.AddRange(doc.Items);
        var image = _base;

        _committed = new GdiSurface(image.Width, image.Height);
        _work = new GdiSurface(image.Width, image.Height);
        CopyBaseInto(_committed);
        Composer.DrawAll(_committed, _items);

        if (openEntry != null)
        {
            _panel = new HistoryPanel { CurrentId = doc.HistoryId };
            _panel.Load();
        }

        // 저장한 위치의 모니터에 복원한다. 첫 실행만 커서 위치를 따른다.
        POINT cursor;
        User32.GetCursorPos(&cursor);
        var settings = AppSettings.Current;
        bool restore = settings.EditorLeft.HasValue && settings.EditorTop.HasValue && settings.EditorWidth > 0 && settings.EditorHeight > 0;
        if (restore) cursor = new POINT { X = settings.EditorLeft!.Value, Y = settings.EditorTop!.Value };
        var mi = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        User32.GetMonitorInfoW(User32.MonitorFromPoint(cursor, Win.MONITOR_DEFAULTTONEAREST), &mi);
        var work = mi.rcWork;

        CreateHandle(0, Win.WS_OVERLAPPEDWINDOW, work.Left, work.Top, 200, 200, $"LunaCapture 편집 — {image.Width}×{image.Height}");
        int dark = 1;
        Dwmapi.DwmSetWindowAttribute(Handle, Dwmapi.DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, sizeof(int));

        // 단축키(A, R…)가 한글 입력 상태에서도 먹도록, 텍스트 입력 중이 아닐 때는 IME를 떼어 둔다.
        _imeContext = Imm32.ImmAssociateContext(Handle, 0);

        ApplyScale(User32.GetDpiForWindow(Handle) / 96f);
        _toolbar.Status = status;
        if (restore)
        {
            int w = Math.Min(settings.EditorWidth, work.Width), h = Math.Min(settings.EditorHeight, work.Height);
            int x = Math.Clamp(settings.EditorLeft!.Value, work.Left, work.Right - w);
            int y = Math.Clamp(settings.EditorTop!.Value, work.Top, work.Bottom - h);
            User32.SetWindowPos(Handle, 0, x, y, w, h, Win.SWP_NOZORDER | Win.SWP_NOACTIVATE);
        }
        else PlaceWindow(work);

        User32.ShowWindow(Handle, restore && settings.EditorMaximized ? 3 /* SW_MAXIMIZE */ : Win.SW_SHOW);
        User32.ForceForeground(Handle);
        User32.SetTimer(Handle, StatusTimer, 3000, 0);
    }

    void ApplyScale(float scale)
    {
        _scale = scale;
        _toolbar.SetScale(scale);
        _toolbar.Layout(1000);
        _barFont?.Dispose();
        _barFont = new GdiFont(UiFonts.Family, 13 * scale, bold: false);
        _panel?.SetScale(scale);
    }

    bool PanelShown => _panel != null && Services.AppSettings.Current.EditorHistoryPanel;
    int PanelWidth => PanelShown ? (int)(HistoryPanel.BaseWidth * _scale) : 0;

    /// <summary>
    /// 지금 문서를 (바뀐 게 있으면) 저장하고 다른 캡처로 바꾼다. 창은 그대로, 그림·주석·되돌리기 기록만 교체.
    /// </summary>
    void SwitchTo(EditorDocument doc)
    {
        CommitText();
        SaveIfChanged();
        _doc = doc;
        _base = doc.Original;
        _items.Clear();
        _items.AddRange(doc.Items);
        _undo.Clear();
        _redo.Clear();
        _selected = null;
        _active = null;
        _marquee = null;
        if (_panel != null) { _panel.CurrentId = doc.HistoryId; _panel.RevealCurrent(); }
        SetBase(_base);
        ShowStatus("기록에서 열었어요 · 고치면 원래 파일에 다시 저장돼요");
    }

    /// <summary>바꾼 게 있으면 문서의 저장 동작(파일·클립보드·기록 갱신)을 부른다.</summary>
    void SaveIfChanged()
    {
        try
        {
            if (_undo.Count == 0 && _redo.Count == 0) return;
            _doc.Save(Composite(), _base, Snapshot());
            _undo.Clear();
            _redo.Clear();
            if (_doc.HistoryId != null) _panel?.RefreshThumb(_doc.HistoryId);
        }
        catch (Exception ex) { Log.Error(ex); }
    }

    void SetWindowTitle(string title)
    {
        fixed (char* t = title) SetWindowTextW(Handle, t);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern int SetWindowTextW(nint hwnd, char* text);

    void OnPanelClick(int x, int y)
    {
        var e = _panel!.EntryAt(_panel.Hit(x, y));
        if (e == null || e.Id == _doc.HistoryId) return;
        if (e.Kind != "image")
        {
            if (e.FilePath != null && File.Exists(e.FilePath)) Shell32.ShellExecuteW(0, "open", e.FilePath, null, null, Win.SW_SHOW);
            else ShowStatus("파일이 없어요 (지워졌거나 옮겨졌어요)");
            return;
        }
        var doc = _openEntry!(e);
        if (doc == null) { ShowStatus("이 캡처의 원본을 찾을 수 없어요"); return; }
        SwitchTo(doc);
    }

    void PlaceWindow(RECT work)
    {
        int pad = (int)(16 * _scale);
        int availW = (int)(work.Width * 0.9) - pad * 2 - PanelWidth;
        int availH = (int)(work.Height * 0.9) - _toolbar.Height - pad * 2;
        float zoom = MathF.Min(1f, MathF.Min((float)availW / _base.Width, (float)availH / _base.Height));

        int cw = Math.Max(_toolbar.MinWidth, (int)(_base.Width * zoom) + pad * 2 + PanelWidth);
        int ch = _toolbar.Height + Math.Max((int)(_base.Height * zoom) + pad * 2, (int)(200 * _scale));

        var r = new RECT(0, 0, cw, ch);
        User32.AdjustWindowRectExForDpi(&r, Win.WS_OVERLAPPEDWINDOW, 0, 0, User32.GetDpiForWindow(Handle));
        int w = Math.Min(r.Width, work.Width), h = Math.Min(r.Height, work.Height);
        User32.SetWindowPos(Handle, 0, work.Left + (work.Width - w) / 2, work.Top + (work.Height - h) / 2, w, h,
            Win.SWP_NOZORDER | Win.SWP_NOACTIVATE);
    }

    // ───────────────────────── 메시지 처리 ─────────────────────────

    protected override nint WndProc(uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case Win.WM_ERASEBKGND:
                return 1;

            case Win.WM_PAINT:
                Paint();
                return 0;

            case Win.WM_SIZE:
                OnResize(Win.LoWord(lParam) & 0xFFFF, Win.HiWord(lParam) & 0xFFFF);
                return 0;

            case Win.WM_GETMINMAXINFO:
            {
                var min = (POINT*)((byte*)lParam + sizeof(POINT) * 3); // MINMAXINFO.ptMinTrackSize
                min->X = _toolbar.MinWidth + (int)(20 * _scale);
                min->Y = (int)(260 * _scale);
                return 0;
            }

            case Win.WM_DPICHANGED:
            {
                ApplyScale(Win.HiWord(wParam) / 96f);
                var r = (RECT*)lParam;
                User32.SetWindowPos(Handle, 0, r->Left, r->Top, r->Width, r->Height, Win.SWP_NOZORDER | Win.SWP_NOACTIVATE);
                return 0;
            }

            case Win.WM_SETCURSOR:
                if (Win.LoWord(lParam) == Win.HTCLIENT)
                {
                    UpdateCursor();
                    return 1;
                }
                break;

            case 0x020A: // WM_MOUSEWHEEL: 기록 패널 스크롤
            {
                var wp = new POINT { X = Win.LoWord(lParam), Y = Win.HiWord(lParam) };
                User32.ScreenToClient(Handle, &wp);
                if (PanelShown && _panel!.Contains(wp.X, wp.Y))
                {
                    _panel.Scroll(-Win.HiWord(wParam) * (int)(90 * _scale) / 120);
                    Invalidate();
                }
                return 0;
            }

            case Win.WM_MOUSEMOVE:
                OnMouseMove(Win.LoWord(lParam), Win.HiWord(lParam));
                return 0;

            case Win.WM_LBUTTONDOWN:
                OnMouseDown(Win.LoWord(lParam), Win.HiWord(lParam));
                return 0;

            case Win.WM_LBUTTONUP:
                OnMouseUp();
                return 0;

            case Win.WM_RBUTTONUP:
                OnRightClick(Win.LoWord(lParam), Win.HiWord(lParam));
                return 0;

            case Win.WM_KEYDOWN:
                OnKeyDown((int)wParam);
                return 0;

            case Win.WM_CHAR:
                OnChar((char)wParam);
                return 0;

            case Win.WM_IME_SETCONTEXT:
                // 기본 조합 창을 숨기고 우리가 직접 이미지 위에 그린다.
                return base.WndProc(msg, wParam, lParam & ~unchecked((nint)Imm32.ISC_SHOWUICOMPOSITIONWINDOW));

            case Win.WM_IME_STARTCOMPOSITION:
                return 0;

            case Win.WM_IME_COMPOSITION:
                OnImeComposition((uint)lParam);
                return 0;

            case Win.WM_IME_ENDCOMPOSITION:
                if (Editing is { } t) { t.Composing = ""; RenderActive(); }
                return 0;

            case Win.WM_TIMER:
                if (wParam == (nint)StatusTimer)
                {
                    User32.KillTimer(Handle, StatusTimer);
                    _toolbar.Status = "";
                    Invalidate();
                }
                return 0;

            case Win.WM_CLOSE:
                Close();
                return 0;

            case Win.WM_DESTROY:
                Imm32.ImmAssociateContext(Handle, _imeContext);
                User32.KillTimer(Handle, StatusTimer);
                ReleaseResources();
                return 0;
        }
        return base.WndProc(msg, wParam, lParam);
    }

    // ───────────────────────── 마우스 ─────────────────────────

    void OnMouseMove(int x, int y)
    {
        if (PanelShown && !_dragging && _panel!.SetHover(x, y)) Invalidate();
        int hover = y < _toolbar.Height && !_dragging ? _toolbar.HitTest(x, y) : -1;
        if (hover != _toolbar.HoverIndex)
        {
            _toolbar.HoverIndex = hover;
            Invalidate();
        }

        if (_marquee != null && !_dragging && HitBar(x, y) is var bh && bh != _barHover)
        {
            _barHover = bh;
            if (bh >= 0) ShowStatus(BarActions[bh].Tip);
            Invalidate();
        }

        if (!_dragging) return;
        var p = ToImage(x, y);
        if (_mode is DragMode.Marquee or DragMode.MarqueeMove)
        {
            DragMarquee(p);
            return;
        }
        if (_mode is DragMode.Move or DragMode.Handle)
        {
            DragSelected(p);
            return;
        }
        if (_active == null) return;
        switch (_active)
        {
            case ArrowAnnotation a: a.To = p; break;
            case RectAnnotation r: r.B = p; break;
            case MosaicAnnotation m: m.B = p; break;
            case PenAnnotation pen: pen.Add(p); break;
        }
        RenderActive();
    }

    void OnMouseDown(int x, int y)
    {
        if (y < _toolbar.Height)
        {
            int i = _toolbar.HitTest(x, y);
            if (i >= 0) OnToolbarClick(_toolbar.Items[i]);
            return;
        }
        if (HitStrip(x, y) is var si and >= 0)
        {
            OnStripClick(si);
            return;
        }
        if (PanelShown && _panel!.Contains(x, y))
        {
            OnPanelClick(x, y);
            return;
        }
        if (_marquee != null && HitBar(x, y) is var bi and >= 0)
        {
            RunBarAction(bi);
            return;
        }

        CommitText();
        var p = ToImage(x, y);
        if (_tool == Tool.Select)
        {
            OnSelectToolDown(x, y, p);
            return;
        }
        if (TrySelectAt(x, y, p)) return;

        uint color = Toolbar.Palette[_colorIndex];
        float width = LineWidths[_thickness] * _scale;

        switch (_tool)
        {
            case Tool.Arrow:
                _active = new ArrowAnnotation { Color = color, Width = width, From = p, To = p };
                break;
            case Tool.Rect:
                _active = new RectAnnotation
                {
                    Color = color, Width = width, A = p, B = p, Shape = s_shape, Fill = s_fill,
                    FontSize = _textSize * _scale,
                };
                break;
            case Tool.Mosaic:
                _active = new MosaicAnnotation { A = p, B = p, BlockSize = (int)(MosaicBlocks[_thickness] * _scale) };
                break;
            case Tool.Pen:
            case Tool.Highlighter:
            {
                bool hl = _tool == Tool.Highlighter;
                var pen = new PenAnnotation
                {
                    Color = hl ? (color & 0x00FFFFFF) | 0x70000000 : color,
                    Width = hl ? (LineWidths[_thickness] * 3 + 8) * _scale : width,
                    Highlighter = hl,
                };
                pen.Add(p);
                _active = pen;
                break;
            }
            case Tool.Text:
                StartText(p);
                return;
            case Tool.Bubble:
                StartBubble(p);
                return;
            case Tool.Number:
                Commit(new NumberAnnotation { Number = NextNumber(), Center = p, Size = NumberSizes[_thickness] * _scale, Color = color });
                return;
            case Tool.Sticker:
                Commit(new StickerAnnotation { Kind = s_sticker, Center = p, Size = StickerSizes[_thickness] * _scale });
                return;
        }

        _dragging = true;
        _mode = DragMode.Draw;
        User32.SetCapture(Handle);
        RenderActive();
    }

    void OnMouseUp()
    {
        if (!_dragging) return;
        _dragging = false;
        User32.ReleaseCapture();
        if (_mode == DragMode.Marquee)
        {
            _mode = DragMode.None;
            if (_marquee is { } m && (m.Width < 2 || m.Height < 2)) _marquee = null;
            if (_marquee != null) ShowStatus("Ctrl+C 복사 · Ctrl+X 오려내기 · Ctrl+S 저장 · 화살표로 옮기기 · Esc 취소");
            Invalidate();
            return;
        }
        if (_mode != DragMode.Draw)
        {
            _mode = DragMode.None;
            Invalidate();
            return;
        }
        _mode = DragMode.None;
        var a = _active;
        _active = null;
        if (a != null && a.IsMeaningful) Commit(a);
        else Invalidate();
    }

    void UpdateCursor()
    {
        POINT pt;
        User32.GetCursorPos(&pt);
        User32.ScreenToClient(Handle, &pt);
        int id;
        if (pt.Y < _toolbar.Height)
            id = _toolbar.HitTest(pt.X, pt.Y) >= 0 ? Win.IDC_HAND : Win.IDC_ARROW;
        else if (HitStrip(pt.X, pt.Y) >= 0 || (PanelShown && _panel!.Hit(pt.X, pt.Y) >= 0))
            id = Win.IDC_HAND;
        else if (PanelShown && _panel!.Contains(pt.X, pt.Y))
            id = Win.IDC_ARROW;
        else if (_marquee != null && HitBar(pt.X, pt.Y) >= 0)
            id = Win.IDC_HAND;
        else if (_marquee != null && HitMarqueeHandle(pt.X, pt.Y) is var mh and >= 0)
            id = mh is 1 or 2 ? 32643 : 32642;
        else if (_mode == DragMode.MarqueeMove || (!_dragging && _marquee is { } mq && Geo.Inside(mq, ToImage(pt.X, pt.Y))))
            id = 32646;
        else if (_selected != null && HitHandle(pt.X, pt.Y) is var h and >= 0)
            id = HandleCursor(_selected, h);
        else if (_mode == DragMode.Move || (_active == null && HitItem(ToImage(pt.X, pt.Y)) is { } hover && (_tool != Tool.Select || SelectToolPicks(hover))))
            id = 32646; // IDC_SIZEALL: 옮기기
        else
            id = _tool is Tool.Text or Tool.Bubble ? Win.IDC_IBEAM : Win.IDC_CROSS;
        User32.SetCursor(User32.LoadCursorW(0, id));
    }

    /// <summary>
    /// 손잡이 위치에 맞는 크기 조절 커서.
    /// 사각형·모자이크 모서리 순서: 0 왼쪽 위, 1 오른쪽 위, 2 왼쪽 아래, 3 오른쪽 아래.
    /// </summary>
    static int HandleCursor(Annotation a, int handle)
    {
        const int SizeNwse = 32642, SizeNesw = 32643, SizeAll = 32646;
        return a switch
        {
            RectAnnotation or MosaicAnnotation or ImageAnnotation or SpotlightAnnotation => handle is 1 or 2 ? SizeNesw : SizeNwse, // ↗↙ / ↖↘
            ArrowAnnotation => SizeAll, // 화살표 끝점은 어느 방향으로든 움직인다
            _ => SizeNwse,              // 스티커·번호: 오른쪽 아래 손잡이
        };
    }

    PointF ToImage(int x, int y) => new((x - _imgX) / _zoom, (y - _imgY) / _zoom);

    // ───────────────────────── 툴바 / 키보드 ─────────────────────────

    void OnToolbarClick(ToolbarItem item)
    {
        switch (item.Kind)
        {
            case ItemKind.Tool: SetTool((Tool)item.Value); break;
            case ItemKind.Color: _colorIndex = item.Value; RecolorActiveText(); RestyleSelected(color: true); break;
            case ItemKind.Thickness: _thickness = item.Value; RecolorActiveText(); RestyleSelected(color: false); break;
            case ItemKind.FontSize: if (item.Value != 0) ChangeTextSize(item.Value * 2); break;
            case ItemKind.Undo: Undo(); break;
            case ItemKind.Redo: Redo(); break;
            case ItemKind.Copy: if (_marquee != null) CopyRegion(); else CopyToClipboard(); break;
            case ItemKind.SaveAs: if (_marquee != null) SaveRegion(); else SaveAs(); break;
            case ItemKind.OpenFolder: OpenSaveFolder(); break;
            case ItemKind.Done: Close(); break;
            case ItemKind.History:
                if (_panel == null) { ShowStatus("이 편집 창에서는 기록을 볼 수 없어요"); break; }
                Services.AppSettings.Current.EditorHistoryPanel = !Services.AppSettings.Current.EditorHistoryPanel;
                Services.AppSettings.Save();
                OnResize(_clientW, _clientH);
                break;
        }
        Invalidate();
    }

    void OnKeyDown(int vk)
    {
        bool ctrl = Win.IsKeyDown(Win.VK_CONTROL);
        bool shift = Win.IsKeyDown(Win.VK_SHIFT);

        // Ctrl 단축키는 글자를 입력하는 중(텍스트·말풍선)에도 동작한다.
        // Ctrl+Z: 입력 중인 것을 지우고(Undo가 처리), 한 번 더 누르면 그 전 주석을 되돌린다.
        if (ctrl)
        {
            switch (vk)
            {
                case 'Z': if (shift) Redo(); else Undo(); break;
                case 'Y': Redo(); break;
                case 'C': if (_marquee != null) CopyRegion(); else CopyToClipboard(); break;
                case 'S': if (_marquee != null) SaveRegion(); else SaveAs(); break;
                case 'X': if (_marquee != null) CutRegion(); break;
                case 'V': if (Editing == null) Paste(); break;
                case 'A': if (Editing == null) SelectAll(); break;
                // 겹치는 순서: Ctrl+↑ 앞으로, Ctrl+↓ 뒤로, Shift와 함께면 맨 앞/맨 뒤 (Ctrl 없이 화살표만 누르면 위치 이동)
                case 0x26: Reorder(shift ? ZMove.Front : ZMove.Forward); break;   // ↑
                case 0x28: Reorder(shift ? ZMove.Back : ZMove.Backward); break;   // ↓
            }
            return;
        }

        if (Editing != null)
        {
            if (vk == Win.VK_ESCAPE) CommitText();
            else if (vk == 0x09 && _active is BubbleAnnotation b) // Tab: 말풍선 꼬리 방향 돌려 보기
            {
                int next = (Array.IndexOf(Stickers.Tails, b.Dir) + (Win.IsKeyDown(Win.VK_SHIFT) ? Stickers.Tails.Length - 1 : 1)) % Stickers.Tails.Length;
                s_tail = b.Dir = Stickers.Tails[next];
                RenderActive();
            }
            return; // 나머지는 WM_CHAR / IME 에서 처리
        }

        if (_marquee != null && _selected == null)
        {
            int mstep = shift ? 10 : 1;
            switch (vk)
            {
                case Win.VK_ESCAPE: _marquee = null; Invalidate(); return;
                case 0x25: MoveMarquee(-mstep, 0); return;
                case 0x26: MoveMarquee(0, -mstep); return;
                case 0x27: MoveMarquee(mstep, 0); return;
                case 0x28: MoveMarquee(0, mstep); return;
            }
        }

        if (_selected != null)
        {
            int step = shift ? 10 : 1;
            switch (vk)
            {
                case Win.VK_DELETE:
                case Win.VK_BACK: DeleteSelected(); return;
                case Win.VK_ESCAPE: _selected = null; Invalidate(); return;
                case 0x25: Nudge(-step, 0); return; // ←
                case 0x26: Nudge(0, -step); return; // ↑
                case 0x27: Nudge(step, 0); return;  // →
                case 0x28: Nudge(0, step); return;  // ↓
            }
        }

        switch (vk)
        {
            case Win.VK_ESCAPE:
            case Win.VK_RETURN: Close(); break;
            case 'A': SetTool(Tool.Arrow); break;
            case 'E': SetTool(Tool.Select); break;
            case 'R': SetTool(Tool.Rect); break;
            case 'P': SetTool(Tool.Pen); break;
            case 'H': SetTool(Tool.Highlighter); break;
            case 'T': SetTool(Tool.Text); break;
            case 'M': SetTool(Tool.Mosaic); break;
            case 'B': SetTool(Tool.Bubble); break;
            case 'N': SetTool(Tool.Number); break;
            case 'S': SetTool(Tool.Sticker); break;
            case '1': case '2': case '3': _thickness = vk - '1'; Invalidate(); break;
        }
    }

    void SetTool(Tool tool)
    {
        CommitText();
        _tool = tool;
        _selected = null;
        _marquee = null;
        Invalidate();
    }

    // ───────────────────────── 텍스트 입력 ─────────────────────────

    void StartText(PointF p)
    {
        float size = _textSize * _scale;
        _active = new TextAnnotation
        {
            Color = Toolbar.Palette[_colorIndex],
            FontSize = size,
            Position = new PointF(p.X, p.Y - size * 0.65f),
        };
        _typing = true;
        Imm32.ImmAssociateContext(Handle, _imeContext);
        RenderActive();
    }

    /// <summary>말풍선: 클릭한 곳을 꼬리가 가리키고, 글자는 텍스트 도구처럼 입력한다.</summary>
    void StartBubble(PointF p)
    {
        _active = new BubbleAnnotation
        {
            Color = Toolbar.Palette[_colorIndex],
            FontSize = _textSize * _scale,
            Tail = p,
            Dir = s_tail,
        };
        _typing = true;
        Imm32.ImmAssociateContext(Handle, _imeContext);
        RenderActive();
    }

    /// <summary>다음 번호 = 지금까지 찍은 번호 중 가장 큰 것 + 1 (되돌리기 하면 자연히 다시 줄어든다).</summary>
    int NextNumber()
    {
        int max = 0;
        foreach (var a in _items)
            if (a is NumberAnnotation n && n.Number > max) max = n.Number;
        return max + 1;
    }

    // ── 고르기 줄: 스티커(어떤 스티커) / 말풍선(꼬리 방향) 도구일 때 그 버튼 바로 아래에 뜸 ──

    static TailDir s_tail = (TailDir)Math.Clamp(Services.AppSettings.Current.BubbleTail, 0, Stickers.Tails.Length - 1); // 말풍선 꼬리 방향 (마지막 선택 기억)

    // 도형 스타일: (모양, 채우기) 6가지
    static readonly (ShapeKind shape, FillStyle fill)[] ShapeStyles =
    [
        (ShapeKind.Rect, FillStyle.Outline), (ShapeKind.Rect, FillStyle.Solid), (ShapeKind.Rect, FillStyle.Tint),
        (ShapeKind.Ellipse, FillStyle.Outline), (ShapeKind.Ellipse, FillStyle.Solid), (ShapeKind.Ellipse, FillStyle.Tint),
    ];
    static int s_shapeStyle = Math.Clamp(Services.AppSettings.Current.ShapeStyle, 0, ShapeStyles.Length - 1);
    static ShapeKind s_shape => ShapeStyles[s_shapeStyle].shape;
    static FillStyle s_fill => ShapeStyles[s_shapeStyle].fill;

    /// <summary>0 = 없음, 1 = 스티커 고르기, 2 = 말풍선 꼬리 방향, 3 = 도형 스타일. 선택한 주석이 있으면 그 종류를 따른다.</summary>
    int StripMode
    {
        get
        {
            if (_selected is StickerAnnotation) return 1;
            if (_selected is BubbleAnnotation || _active is BubbleAnnotation) return 2;
            if (_selected is RectAnnotation || Editing is RectAnnotation) return 3;
            if (_selected != null || Editing != null) return 0;
            return _tool switch { Tool.Sticker => 1, Tool.Bubble => 2, Tool.Rect => 3, _ => 0 };
        }
    }

    bool HasStrip => StripMode != 0;
    int StripCount => StripMode switch { 1 => Stickers.All.Length, 2 => Stickers.Tails.Length, _ => ShapeStyles.Length };
    int StripSelected => StripMode switch
    {
        1 => Array.IndexOf(Stickers.All, _selected is StickerAnnotation st ? st.Kind : s_sticker),
        2 => Array.IndexOf(Stickers.Tails, _selected is BubbleAnnotation sb ? sb.Dir : _active is BubbleAnnotation ab ? ab.Dir : s_tail),
        _ => (_selected as RectAnnotation ?? Editing as RectAnnotation) is { } r
            ? Array.IndexOf(ShapeStyles, (r.Shape, r.Fill))
            : s_shapeStyle,
    };

    RECT StripRect()
    {
        int cell = (int)(40 * _scale), pad = (int)(6 * _scale);
        int w = cell * StripCount + pad * 2, h = cell + pad * 2;
        int x = (int)(8 * _scale);
        var anchor = StripMode switch { 1 => Tool.Sticker, 2 => Tool.Bubble, _ => Tool.Rect };
        foreach (var it in _toolbar.Items)
            if (it.Kind == ItemKind.Tool && it.Value == (int)anchor) x = Math.Max(x, it.Bounds.Left - w / 2 + it.Bounds.Width / 2);
        x = Math.Min(x, Math.Max(0, _clientW - w - (int)(8 * _scale)));
        int y = _toolbar.Height + (int)(6 * _scale);
        return new RECT(x, y, x + w, y + h);
    }

    int HitStrip(int x, int y)
    {
        if (!HasStrip) return -1;
        var r = StripRect();
        if (x < r.Left || x >= r.Right || y < r.Top || y >= r.Bottom) return -1;
        int cell = (int)(40 * _scale), pad = (int)(6 * _scale);
        int i = (x - r.Left - pad) / cell;
        return i >= 0 && i < StripCount ? i : -1;
    }

    void OnStripClick(int i)
    {
        if (StripMode == 1)
        {
            s_sticker = Stickers.All[i];
            if (_selected is StickerAnnotation st) { PushUndo(); st.Kind = s_sticker; RebuildCommitted(); }
        }
        else if (StripMode == 2)
        {
            s_tail = Stickers.Tails[i];
            if (_active is BubbleAnnotation b) { b.Dir = s_tail; RenderActive(); } // 입력 중인 말풍선에도 바로 적용
            else if (_selected is BubbleAnnotation sb) { PushUndo(); sb.Dir = s_tail; RebuildCommitted(); }
        }
        else
        {
            s_shapeStyle = i;
            if (Editing is RectAnnotation er) { (er.Shape, er.Fill) = ShapeStyles[i]; RenderActive(); }
            else if (_selected is RectAnnotation sr) { PushUndo(); (sr.Shape, sr.Fill) = ShapeStyles[i]; RebuildCommitted(); }
        }
        Services.AppSettings.Current.Sticker = (int)s_sticker;
        Services.AppSettings.Current.BubbleTail = (int)s_tail;
        Services.AppSettings.Current.ShapeStyle = s_shapeStyle;
        Services.AppSettings.Save();
        Invalidate();
    }

    void DrawStrip(Gfx g)
    {
        var r = StripRect();
        int cell = (int)(40 * _scale), pad = (int)(6 * _scale);
        g.FillRoundRect(0x33000000, r.Left, r.Top + 2 * _scale, r.Width, r.Height, 10 * _scale);
        g.FillRoundRect(0xF22A2A30, r.Left, r.Top, r.Width, r.Height, 10 * _scale);
        int selected = StripSelected;
        for (int i = 0; i < StripCount; i++)
        {
            float cx = r.Left + pad + cell * i + cell / 2f, cy = r.Top + pad + cell / 2f;
            if (i == selected)
                g.FillRoundRect(0xFF3D3A5C, cx - cell / 2f + 2, cy - cell / 2f + 2, cell - 4, cell - 4, 8 * _scale);
            if (StripMode == 1)
            {
                Stickers.Draw(g, Stickers.All[i], cx, cy, cell * 0.7f);
            }
            else if (StripMode == 3)
            {
                // 도형 스타일 미리보기
                var (shape, fill) = ShapeStyles[i];
                uint fg = i == selected ? 0xFFFFFFFF : 0xFFC8C8D0;
                float bw = cell * 0.5f, bh = cell * 0.4f, x0 = cx - bw / 2, y0 = cy - bh / 2;
                uint fillColor = fill == FillStyle.Solid ? fg : (fg & 0x00FFFFFF) | 0x50000000;
                if (shape == ShapeKind.Ellipse)
                {
                    if (fill != FillStyle.Outline) g.FillEllipse(fillColor, x0, y0, bw, bh);
                    if (fill != FillStyle.Solid) g.Ellipse(fg, 1.6f * _scale, x0, y0, bw, bh);
                }
                else
                {
                    if (fill == FillStyle.Outline) g.Rect(fg, 1.6f * _scale, x0, y0, bw, bh);
                    else
                    {
                        g.FillRoundRect(fillColor, x0, y0, bw, bh, 3 * _scale);
                        if (fill == FillStyle.Tint) g.RoundRect(fg, 1.6f * _scale, x0, y0, bw, bh, 3 * _scale);
                    }
                }
            }
            else
            {
                // 꼬리 방향 미리보기: 작은 말풍선 (꼬리 없음 = 박스)
                var dir = Stickers.Tails[i];
                float bw = cell * 0.52f, bh = cell * 0.36f, len = cell * 0.17f;
                float ox = dir switch { TailDir.Left => len / 2, TailDir.Right => -len / 2, _ => 0 };
                float oy = dir switch { TailDir.BottomLeft or TailDir.BottomRight => -len / 2, TailDir.TopLeft or TailDir.TopRight => len / 2, _ => 0 };
                var body = new RectF(cx - bw / 2 + ox, cy - bh / 2 + oy, bw, bh);
                nint path = Stickers.BubblePath(body, Stickers.TipFor(body, dir, len), bh * 0.35f, dir);
                uint fg = i == selected ? 0xFFFFFFFF : 0xFFC8C8D0;
                g.FillPath(path, (fg & 0x00FFFFFF) | 0x30000000);
                g.DrawPath(path, fg, 1.4f * _scale);
                Gfx.DeletePath(path);
            }
        }
    }

    void OnChar(char c)
    {
        if (Editing is not { } t) return;
        switch (c)
        {
            case '\b':
                if (t.Text.Length > 0)
                {
                    int cut = t.Text.Length >= 2 && char.IsLowSurrogate(t.Text[^1]) ? 2 : 1;
                    t.Text = t.Text[..^cut];
                }
                break;
            case '\r':
                if (Win.IsKeyDown(Win.VK_SHIFT)) t.Text += "\n";
                else { CommitText(); return; }
                break;
            default:
                if (c < 0x20) return;
                t.Text += c;
                break;
        }
        RenderActive();
    }

    void OnImeComposition(uint flags)
    {
        if (Editing is not { } t) return;
        nint himc = Imm32.ImmGetContext(Handle);
        if ((flags & Imm32.GCS_RESULTSTR) != 0)
        {
            t.Text += Imm32.GetString(himc, Imm32.GCS_RESULTSTR);
            t.Composing = "";
        }
        if ((flags & Imm32.GCS_COMPSTR) != 0)
            t.Composing = Imm32.GetString(himc, Imm32.GCS_COMPSTR);
        Imm32.ImmReleaseContext(Handle, himc);
        RenderActive();
    }

    float CurrentTextSize => (Editing ?? _selected as TextAnnotation) is { FontSize: > 0 } t
        ? t.FontSize / _scale : _textSize;

    void ChangeTextSize(float delta)
    {
        float size = Math.Clamp(CurrentTextSize + delta, 8f, 144f);
        _textSize = size;
        if (Editing is { } editing)
        {
            editing.FontSize = size * _scale;
            RenderActive();
        }
        else if (_selected is TextAnnotation selected && selected.FontSize != size * _scale)
        {
            PushUndo();
            selected.FontSize = size * _scale;
            RebuildCommitted();
        }
        Invalidate();
    }

    void RecolorActiveText()
    {
        if (Editing is not { } t) return;
        t.Color = Toolbar.Palette[_colorIndex];
        if (t is RectAnnotation r) r.Width = LineWidths[_thickness] * _scale;
        RenderActive();
    }

    void CommitText()
    {
        if (Editing is not { } t) return;
        _typing = false;
        // 조합 중이던 글자도 확정해서 넣는다.
        t.Text += t.Composing;
        t.Composing = "";
        _active = null;
        Imm32.ImmAssociateContext(Handle, 0);
        if (t.IsMeaningful) Commit(t);
        else { _reEditing = false; Invalidate(); } // 다시 편집하다 글자를 다 지우면 삭제 (되돌리기로 복구 가능)
    }

    // ───────────────────────── 문서 (주석 / 실행 취소) ─────────────────────────

    void Commit(Annotation a)
    {
        if (!_reEditing) PushUndo(); // 다시 편집이면 시작할 때 이미 저장해 둠
        _reEditing = false;
        _items.Add(a);
        DrawOnto(_committed!, a);
        Invalidate();
    }

    List<Annotation> Snapshot() => _items.ConvertAll(a => a.Clone());

    /// <summary>바꾸기 직전에 부른다: 지금 상태를 되돌리기 목록에 넣는다.</summary>
    void PushUndo()
    {
        _undo.Push((_base, Snapshot()));
        _redo.Clear();
    }

    void RestoreFrom((PixelImage Base, List<Annotation> Items) state)
    {
        _items.Clear();
        _items.AddRange(state.Items);
        _selected = null;
        if (ReferenceEquals(state.Base, _base)) RebuildCommitted();
        else { _marquee = null; SetBase(state.Base); } // 자르기 되돌리기
    }

    /// <summary>원본 그림을 바꾼다 (문서 전환·자르기·되돌리기). 크기가 다르면 그림판도 새로 만든다.</summary>
    void SetBase(PixelImage image)
    {
        _base = image;
        if (_committed!.Width != image.Width || _committed.Height != image.Height)
        {
            _committed.Dispose();
            _work!.Dispose();
            _committed = new GdiSurface(image.Width, image.Height);
            _work = new GdiSurface(image.Width, image.Height);
        }
        RebuildCommitted();
        SetWindowTitle($"LunaCapture 편집 — {image.Width}×{image.Height}");
        OnResize(_clientW, _clientH);
    }

    void Undo()
    {
        if (Editing != null)
        {
            _typing = false;
            // 입력 중인 텍스트·말풍선을 버린다. 한글 조합 중이던 글자도 취소해야 나중에 뒤늦게 들어오지 않는다.
            nint himc = Imm32.ImmGetContext(Handle);
            if (himc != 0)
            {
                Imm32.ImmNotifyIME(himc, Imm32.NI_COMPOSITIONSTR, Imm32.CPS_CANCEL, 0);
                Imm32.ImmReleaseContext(Handle, himc);
            }
            _active = null;
            Imm32.ImmAssociateContext(Handle, 0);
            if (_reEditing) { _reEditing = false; RestoreFrom(_undo.Pop()); } // 다시 편집 취소 → 원래대로
            Invalidate();
            return;
        }
        if (_undo.Count == 0) return;
        _redo.Push((_base, Snapshot()));
        RestoreFrom(_undo.Pop());
    }

    void Redo()
    {
        CommitText();
        if (_redo.Count == 0) return;
        _undo.Push((_base, Snapshot()));
        RestoreFrom(_redo.Pop());
    }

    // ───────────────────────── 선택 · 옮기기 · 크기 조절 ─────────────────────────

    Gfx MeasureGfx() => new(_work!.Dc, forImage: true);

    float HitTolerance => 6 * _scale / _zoom;

    /// <summary>맨 위(나중에 그린 것)부터 클릭 판정.</summary>
    Annotation? HitItem(PointF p)
    {
        if (_items.Count == 0) return null;
        using var mg = MeasureGfx();
        for (int i = _items.Count - 1; i >= 0; i--)
            if (_items[i].HitTest(p, HitTolerance, mg)) return _items[i];
        return null;
    }

    /// <summary>클릭이 주석이나 손잡이에 닿으면 선택/옮기기/크기 조절을 시작하고 true. 빈 곳이면 선택 해제 후 false(→ 새로 그리기).</summary>
    bool TrySelectAt(int x, int y, PointF p)
    {
        if (_selected != null && HitHandle(x, y) is var h and >= 0)
        {
            NormalizeForHandles(_selected);
            _handle = h;
            BeginDrag(DragMode.Handle, p);
            return true;
        }

        var item = HitItem(p);
        long now = Environment.TickCount64;
        bool quickSecondClick = now - _lastClickTick <= User32.GetDoubleClickTime()
            && Math.Abs(x - _lastClickPos.X) <= 4 * _scale && Math.Abs(y - _lastClickPos.Y) <= 4 * _scale;
        _lastClickTick = now;
        _lastClickPos = new POINT { X = x, Y = y };

        if (item == null)
        {
            bool wasEmptyClick = _lastClickItem == null;
            _lastClickItem = null;
            // 테두리만 있는 도형의 안쪽을 더블클릭 → 그 도형에 글자 넣기
            if (quickSecondClick && wasEmptyClick)
            {
                for (int i = _items.Count - 1; i >= 0; i--)
                    if (_items[i] is RectAnnotation shape && shape.ContainsInside(p)) { ReEdit(shape); return true; }
            }
            if (_selected != null) { _selected = null; Invalidate(); }
            return false;
        }

        bool doubleClick = quickSecondClick && item == _lastClickItem;
        _lastClickItem = item;

        if (doubleClick && item is TextAnnotation t)
        {
            ReEdit(t);
            return true;
        }

        if (_selected != item) ShowStatus("Ctrl+↑ 앞으로 · Ctrl+↓ 뒤로 · Shift와 함께 맨 앞/뒤 · Del 삭제 · 오른쪽 클릭 메뉴");
        _selected = item;
        BeginDrag(DragMode.Move, p);
        Invalidate();
        return true;
    }

    enum ZMove { Front, Forward, Backward, Back }

    /// <summary>선택한 주석의 겹치는 순서를 바꾼다 (목록 뒤쪽일수록 위에 그려짐).</summary>
    void Reorder(ZMove move)
    {
        if (_selected == null) return;
        int from = _items.IndexOf(_selected), last = _items.Count - 1;
        int to = move switch
        {
            ZMove.Front => last,
            ZMove.Forward => Math.Min(last, from + 1),
            ZMove.Backward => Math.Max(0, from - 1),
            _ => 0,
        };
        if (from < 0 || to == from)
        {
            ShowStatus(to == from && from == last && move is ZMove.Front or ZMove.Forward ? "이미 맨 앞이에요" : "이미 맨 뒤예요");
            return;
        }
        PushUndo();
        _items.RemoveAt(from);
        _items.Insert(to, _selected);
        RebuildCommitted();
        ShowStatus(move switch
        {
            ZMove.Front => "맨 앞으로 옮겼어요",
            ZMove.Forward => "한 칸 앞으로 옮겼어요",
            ZMove.Backward => "한 칸 뒤로 옮겼어요",
            _ => "맨 뒤로 옮겼어요",
        });
    }

    /// <summary>주석을 오른쪽 클릭: 선택하고 순서·삭제 메뉴를 띄운다.</summary>
    void OnRightClick(int x, int y)
    {
        if (y < _toolbar.Height || _active != null || _dragging) return;
        if (_marquee is { } mq && Geo.Inside(mq, ToImage(x, y)))
        {
            ShowRegionMenu(x, y);
            return;
        }
        var item = HitItem(ToImage(x, y));
        if (item == null)
        {
            if (PanelShown && _panel!.Contains(x, y)) return;
            ShowCanvasMenu(x, y);
            return;
        }
        _selected = item;
        Invalidate();

        int index = _items.IndexOf(item), last = _items.Count - 1;
        uint Enabled(bool on) => Win.MF_STRING | (on ? 0u : 0x1u /* MF_GRAYED */);
        nint menu = User32.CreatePopupMenu();
        User32.AppendMenuW(menu, Enabled(index < last), 1, "맨 앞으로\tCtrl+Shift+↑");
        User32.AppendMenuW(menu, Enabled(index < last), 2, "앞으로\tCtrl+↑");
        User32.AppendMenuW(menu, Enabled(index > 0), 3, "뒤로\tCtrl+↓");
        User32.AppendMenuW(menu, Enabled(index > 0), 4, "맨 뒤로\tCtrl+Shift+↓");
        User32.AppendMenuW(menu, Win.MF_SEPARATOR, 0, null);
        User32.AppendMenuW(menu, Win.MF_STRING, 5, "삭제\tDel");

        POINT pt = new() { X = x, Y = y };
        User32.ClientToScreen(Handle, &pt);
        int cmd = User32.TrackPopupMenu(menu, Win.TPM_RIGHTBUTTON | Win.TPM_RETURNCMD | Win.TPM_NONOTIFY, pt.X, pt.Y, 0, Handle, 0);
        User32.DestroyMenu(menu);
        switch (cmd)
        {
            case 1: Reorder(ZMove.Front); break;
            case 2: Reorder(ZMove.Forward); break;
            case 3: Reorder(ZMove.Backward); break;
            case 4: Reorder(ZMove.Back); break;
            case 5: DeleteSelected(); break;
        }
    }

    void BeginDrag(DragMode mode, PointF p)
    {
        _mode = mode;
        _dragging = true;
        _dragLast = p;
        _dragChanged = false;
        User32.SetCapture(Handle);
    }

    void DragSelected(PointF p)
    {
        if (_selected == null) return;
        if (!_dragChanged)
        {
            if (MathF.Abs(p.X - _dragLast.X) < 0.5f && MathF.Abs(p.Y - _dragLast.Y) < 0.5f) return;
            PushUndo(); // 실제로 움직이기 시작할 때 한 번만
            _dragChanged = true;
        }
        if (_mode == DragMode.Move)
        {
            _selected.Offset(p.X - _dragLast.X, p.Y - _dragLast.Y);
            _dragLast = p;
        }
        else
        {
            ApplyHandle(_selected, _handle, p);
        }
        RebuildCommitted();
    }

    /// <summary>크기 조절 손잡이 (이미지 좌표). 사각형·모자이크 = 네 모서리, 화살표 = 양 끝, 스티커·번호 = 오른쪽 아래.</summary>
    static PointF[] HandlePoints(Annotation a)
    {
        switch (a)
        {
            case RectAnnotation r: return Corners(Geo.FromPoints(r.A, r.B));
            case MosaicAnnotation m: return Corners(Geo.FromPoints(m.A, m.B));
            case ImageAnnotation im: return Corners(im.Box);
            case SpotlightAnnotation sp: return Corners(sp.Box);
            case ArrowAnnotation ar: return [ar.From, ar.To];
            case StickerAnnotation st: return [new(st.Center.X + st.Size / 2, st.Center.Y + st.Size / 2)];
            case NumberAnnotation n: return [new(n.Center.X + n.Size / 2, n.Center.Y + n.Size / 2)];
            default: return [];
        }
    }

    static PointF[] Corners(RectF r) =>
        [new(r.X, r.Y), new(r.X + r.Width, r.Y), new(r.X, r.Y + r.Height), new(r.X + r.Width, r.Y + r.Height)];

    int HitHandle(int x, int y)
    {
        if (_selected == null) return -1;
        var hs = HandlePoints(_selected);
        float reach = 7 * _scale;
        for (int i = 0; i < hs.Length; i++)
        {
            float sx = _imgX + hs[i].X * _zoom, sy = _imgY + hs[i].Y * _zoom;
            if (MathF.Abs(x - sx) <= reach && MathF.Abs(y - sy) <= reach) return i;
        }
        return -1;
    }

    /// <summary>사각형·모자이크는 A = 왼쪽 위, B = 오른쪽 아래로 맞춰 둔다 (손잡이 번호가 모서리와 맞게).</summary>
    static void NormalizeForHandles(Annotation a)
    {
        if (a is RectAnnotation r) { var b = Geo.FromPoints(r.A, r.B); r.A = new(b.X, b.Y); r.B = new(b.X + b.Width, b.Y + b.Height); }
        if (a is MosaicAnnotation m) { var b = Geo.FromPoints(m.A, m.B); m.A = new(b.X, b.Y); m.B = new(b.X + b.Width, b.Y + b.Height); }
        if (a is SpotlightAnnotation s) { var b = s.Box; s.A = new(b.X, b.Y); s.B = new(b.X + b.Width, b.Y + b.Height); }
    }

    static void ApplyHandle(Annotation a, int h, PointF p)
    {
        static (PointF A, PointF B) Corner(PointF A, PointF B, int h, PointF p) => h switch
        {
            0 => (p, B),
            1 => (new(A.X, p.Y), new(p.X, B.Y)),
            2 => (new(p.X, A.Y), new(B.X, p.Y)),
            _ => (A, p),
        };
        switch (a)
        {
            case RectAnnotation r: (r.A, r.B) = Corner(r.A, r.B, h, p); break;
            case MosaicAnnotation m: (m.A, m.B) = Corner(m.A, m.B, h, p); break;
            case SpotlightAnnotation s: (s.A, s.B) = Corner(s.A, s.B, h, p); break;
            case ImageAnnotation im:
            {
                // 그림은 비율을 지킨 채 크기만: 반대쪽 모서리를 고정하고, 뒤집히지 않게 같은 방향으로만 늘이고 줄인다
                var o = Corners(im.Box)[3 - h];
                float sx = h is 1 or 3 ? 1 : -1, sy = h >= 2 ? 1 : -1;
                float w = MathF.Max(8, (p.X - o.X) * sx), hh = MathF.Max(8, (p.Y - o.Y) * sy);
                float aspect = im.Image.Width / (float)im.Image.Height;
                if (w / hh > aspect) w = hh * aspect; else hh = w / aspect;
                (im.A, im.B) = (o, new PointF(o.X + w * sx, o.Y + hh * sy));
                break;
            }
            case ArrowAnnotation ar: if (h == 0) ar.From = p; else ar.To = p; break;
            case StickerAnnotation st: st.Size = MathF.Max(12, 2 * MathF.Max(MathF.Abs(p.X - st.Center.X), MathF.Abs(p.Y - st.Center.Y))); break;
            case NumberAnnotation n: n.Size = MathF.Max(12, 2 * MathF.Max(MathF.Abs(p.X - n.Center.X), MathF.Abs(p.Y - n.Center.Y))); break;
        }
    }

    /// <summary>더블클릭한 텍스트·말풍선을 다시 편집: 목록에서 잠시 빼서 입력 중 상태로 만든다.</summary>
    void ReEdit(TextAnnotation t)
    {
        PushUndo();
        _items.Remove(t);
        _selected = null;
        RebuildCommitted();
        _reEditing = true;
        _active = t;
        _typing = true;
        if (t.FontSize <= 0) t.FontSize = _textSize * _scale;
        Imm32.ImmAssociateContext(Handle, _imeContext);
        RenderActive();
    }

    void DeleteSelected()
    {
        if (_selected == null) return;
        PushUndo();
        _items.Remove(_selected);
        _selected = null;
        RebuildCommitted();
    }

    void Nudge(float dx, float dy)
    {
        if (_selected == null) return;
        PushUndo();
        _selected.Offset(dx / _zoom, dy / _zoom);
        RebuildCommitted();
    }

    /// <summary>툴바에서 색 또는 굵기를 누르면 선택한 주석에도 적용.</summary>
    void RestyleSelected(bool color)
    {
        if (_selected == null) return;
        PushUndo();
        uint c = Toolbar.Palette[_colorIndex];
        float width = LineWidths[_thickness] * _scale;
        switch (_selected)
        {
            case RectAnnotation r:
                if (color) r.Color = c;
                else r.Width = width;
                break;
            case TextAnnotation t:
                if (color) t.Color = c;
                break;
            case PenAnnotation pen:
                if (color) pen.Color = pen.Highlighter ? (c & 0x00FFFFFF) | 0x70000000 : c;
                else pen.Width = pen.Highlighter ? (LineWidths[_thickness] * 3 + 8) * _scale : width;
                break;
            case ArrowAnnotation:
                if (color) _selected.Color = c; else _selected.Width = width;
                break;
            case NumberAnnotation n:
                if (color) n.Color = c; else n.Size = NumberSizes[_thickness] * _scale;
                break;
            case StickerAnnotation st:
                if (!color) st.Size = StickerSizes[_thickness] * _scale;
                break;
            case MosaicAnnotation m:
                if (!color) m.BlockSize = (int)(MosaicBlocks[_thickness] * _scale);
                break;
        }
        RebuildCommitted();
    }

    /// <summary>선택 표시: 점선 대신 두 겹 테두리(어두운 바깥 + 보라 안쪽) + 흰 손잡이.</summary>
    void DrawSelection(Gfx g)
    {
        if (_selected == null || _active != null) return;
        RectF b;
        using (var mg = MeasureGfx()) b = _selected.Bounds(mg);
        float pad = 4 * _scale;
        float x = _imgX + b.X * _zoom - pad, y = _imgY + b.Y * _zoom - pad;
        float w = b.Width * _zoom + pad * 2, h = b.Height * _zoom + pad * 2;
        g.Rect(0x66000000, 3 * _scale, x, y, w, h);
        g.Rect(Toolbar.Accent, 1.5f * _scale, x, y, w, h);
        float hs = 8 * _scale;
        foreach (var hp in HandlePoints(_selected))
        {
            float sx = _imgX + hp.X * _zoom, sy = _imgY + hp.Y * _zoom;
            g.FillRoundRect(0xFFFFFFFF, sx - hs / 2, sy - hs / 2, hs, hs, 2 * _scale);
            g.RoundRect(Toolbar.Accent, 1.5f * _scale, sx - hs / 2, sy - hs / 2, hs, hs, 2 * _scale);
        }
    }

    void RebuildCommitted()
    {
        CopyBaseInto(_committed!);
        Composer.DrawAll(_committed!, _items);
        Invalidate();
    }

    static void DrawOnto(GdiSurface target, Annotation a) => Composer.Draw(target, a);

    void CopyBaseInto(GdiSurface s)
    {
        fixed (byte* src = _base.Bgra)
            Buffer.MemoryCopy(src, s.Bits, (long)s.Stride * s.Height, _base.Bgra.Length);
    }

    /// <summary>진행 중인 주석을 committed 위에 얹은 미리보기를 _work에 그린다.</summary>
    void RenderActive()
    {
        if (_active != null && _work != null && _committed != null)
        {
            Gdi32.GdiFlush();
            Buffer.MemoryCopy(_committed.Bits, _work.Bits, (long)_work.Stride * _work.Height, (long)_work.Stride * _work.Height);
            DrawOnto(_work, _active);
        }
        Invalidate();
    }

    PixelImage Composite()
    {
        CommitText();
        Gdi32.GdiFlush();
        return _committed!.Crop(new RECT(0, 0, _committed.Width, _committed.Height));
    }

    // ───────────────────────── 출력 ─────────────────────────

    void CopyToClipboard()
    {
        bool ok = ClipboardService.SetImage(Handle, Composite());
        ShowStatus(ok ? "클립보드에 복사했어요" : "클립보드 복사에 실패했어요");
    }

    void OpenSaveFolder()
    {
        try
        {
            string folder = AppSettings.Current.ImageFolderOrDefault;
            Directory.CreateDirectory(folder);
            if (Shell32.ShellExecuteW(Handle, "open", folder, null, null, Win.SW_SHOW) <= 32)
                ShowStatus("저장 폴더를 열지 못했어요");
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            ShowStatus("저장 폴더를 열지 못했어요");
        }
    }

    void SaveAs() => SaveImageAs(Composite());

    void SaveImageAs(PixelImage image)
    {
        string? path = FileDialog.SavePng(Handle, $"Luna_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        if (path == null) return;
        try
        {
            PngEncoder.Save(image, path);
            ShowStatus("저장했어요: " + Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            ShowStatus("저장 실패: " + ex.Message);
        }
    }

    void ShowStatus(string text)
    {
        _toolbar.Status = text;
        _toolbar.HoverIndex = -1;
        User32.SetTimer(Handle, StatusTimer, 3000, 0);
        Invalidate();
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct EditorPlacement
    {
        public uint Length, Flags, Show;
        public POINT Min, Max;
        public RECT Normal;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern int GetWindowPlacement(nint hwnd, ref EditorPlacement placement);

    void SaveWindowPlacement()
    {
        var placement = new EditorPlacement { Length = (uint)sizeof(EditorPlacement) };
        if (GetWindowPlacement(Handle, ref placement) == 0) return;
        var r = placement.Normal;
        if (r.Width <= 0 || r.Height <= 0) return;
        RECT current;
        if (User32.GetWindowRect(Handle, &current) == 0) return;
        // WINDOWPLACEMENT uses workspace coordinates; settings store screen coordinates.
        var point = User32.IsIconic(Handle) != 0
            ? new POINT { X = r.Left + r.Width / 2, Y = r.Top + r.Height / 2 }
            : new POINT { X = current.Left + current.Width / 2, Y = current.Top + current.Height / 2 };
        var monitor = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        User32.GetMonitorInfoW(User32.MonitorFromPoint(point, Win.MONITOR_DEFAULTTONEAREST), &monitor);
        var settings = AppSettings.Current;
        settings.EditorLeft = r.Left + monitor.rcWork.Left - monitor.rcMonitor.Left;
        settings.EditorTop = r.Top + monitor.rcWork.Top - monitor.rcMonitor.Top;
        settings.EditorWidth = r.Width;
        settings.EditorHeight = r.Height;
        settings.EditorMaximized = placement.Show == 3 || (placement.Flags & 2) != 0;
        AppSettings.Save();
    }

    internal void Close()
    {
        if (_closing) return;
        _closing = true;
        SaveWindowPlacement();
        if (_dragging) OnMouseUp();
        try { CommitText(); } catch (Exception ex) { Log.Error(ex); }
        SaveIfChanged();
        Destroy();
        _onClosed();
    }

    // ───────────────────────── 그리기 ─────────────────────────

    void OnResize(int w, int h)
    {
        if (w <= 0 || h <= 0) return;
        _clientW = w; _clientH = h;
        if (_back == null || _back.Width != w || _back.Height != h)
        {
            _back?.Dispose();
            _back = new GdiSurface(w, h);
        }
        _toolbar.Layout(w);

        int pad = (int)(16 * _scale);
        int canvasW = Math.Max(1, w - PanelWidth);
        int availW = Math.Max(1, canvasW - pad * 2), availH = Math.Max(1, h - _toolbar.Height - pad * 2);
        _zoom = MathF.Min(1f, MathF.Min((float)availW / _base.Width, (float)availH / _base.Height));
        _dispW = Math.Max(1, (int)MathF.Round(_base.Width * _zoom));
        _dispH = Math.Max(1, (int)MathF.Round(_base.Height * _zoom));
        _imgX = (canvasW - _dispW) / 2;
        _panel?.Layout(new RECT(canvasW, _toolbar.Height, w, h));
        _imgY = _toolbar.Height + (h - _toolbar.Height - _dispH) / 2;
        Invalidate();
    }

    void Paint()
    {
        PAINTSTRUCT ps;
        nint hdc = User32.BeginPaint(Handle, &ps);
        if (_back == null || _committed == null || _work == null) { User32.EndPaint(Handle, &ps); return; }

        nint dc = _back.Dc;
        using (var g = new Gfx(dc))
        {
            g.FillRect(CanvasBg, 0, 0, _clientW, _clientH);
            g.FillRect(ImageBorder, _imgX - 1, _imgY - 1, _dispW + 2, _dispH + 2);
        }

        var src = _active != null ? _work : _committed;
        Gdi32.GdiFlush();
        if (_dispW == src.Width && _dispH == src.Height)
        {
            Gdi32.BitBlt(dc, _imgX, _imgY, _dispW, _dispH, src.Dc, 0, 0, Win.SRCCOPY);
        }
        else
        {
            Gdi32.SetStretchBltMode(dc, Win.HALFTONE);
            Gdi32.SetBrushOrgEx(dc, 0, 0, null);
            Gdi32.StretchBlt(dc, _imgX, _imgY, _dispW, _dispH, src.Dc, 0, 0, src.Width, src.Height, Win.SRCCOPY);
        }

        if (PanelShown) _panel!.Draw(dc);

        using (var g = new Gfx(dc))
        {
            if (Editing is { } t)
            {
                using var mg = new Gfx(_work.Dc, forImage: true);
                var (cx, cy, ch) = t.Caret(mg);
                float x = _imgX + cx * _zoom + 1, y = _imgY + cy * _zoom, h = ch * _zoom;
                g.Line(0xFF000000, 4f, x, y, x, y + h);
                g.Line(0xFFFFFFFF, 2f, x, y, x, y + h);
            }

            _toolbar.CurrentTool = _tool;
            _toolbar.ColorIndex = _colorIndex;
            _toolbar.ThicknessIndex = _thickness;
            _toolbar.TextSize = CurrentTextSize;
            DrawSelection(g);
            DrawMarquee(g);
            _toolbar.CanUndo = _undo.Count > 0 || Editing != null;
            _toolbar.CanRedo = _redo.Count > 0;
            _toolbar.PanelVisible = PanelShown;
            _toolbar.Draw(g, _clientW);
            if (HasStrip) DrawStrip(g);
        }

        var pr = ps.rcPaint;
        Gdi32.BitBlt(hdc, pr.Left, pr.Top, pr.Width, pr.Height, dc, pr.Left, pr.Top, Win.SRCCOPY);
        User32.EndPaint(Handle, &ps);
    }

    void Invalidate()
    {
        if (Handle != 0) User32.InvalidateRect(Handle, null, 0);
    }

    void ReleaseResources()
    {
        _committed?.Dispose(); _committed = null;
        _work?.Dispose(); _work = null;
        _back?.Dispose(); _back = null;
        _toolbar.Dispose();
        _panel?.Dispose();
        _barFont?.Dispose(); _barFont = null;
    }
}

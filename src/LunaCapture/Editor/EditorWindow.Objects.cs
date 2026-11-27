using LunaCapture.Graphics;
using LunaCapture.Interop;
using LunaCapture.Services;

namespace LunaCapture.Editor;

internal sealed unsafe partial class EditorWindow
{
    readonly HashSet<Annotation> _objectSelection = [];
    RectF? _objectMarquee;
    PointF _objectAnchor;
    List<Annotation> _objectSelectionBeforeDrag = [];
    Annotation? _objectClicked;
    List<(int Index, Annotation Original)>? _objectResizeSource;
    RectF _objectResizeBounds;
    PointF _objectResizeAnchor, _objectResizePointer;
    int _objectResizeCorner;
    float _objectResizeFactor = 1;
    (float Min, float Max) _objectResizeLimits;

    bool HasObjectSelection => _objectSelection.Count > 0 || _selected != null;

    List<Annotation> SelectedObjects() => _items.Where(x => _objectSelection.Contains(x) || ReferenceEquals(x, _selected)).ToList();

    void ClearSelection()
    {
        _selected = null;
        _objectSelection.Clear();
        _objectMarquee = null;
        _objectClicked = null;
        _objectResizeSource = null;
    }

    void SetObjectSelection(IEnumerable<Annotation> items)
    {
        var selection = items.Distinct().ToArray();
        ClearSelection();
        if (selection.Length == 1) _selected = selection[0];
        else _objectSelection.UnionWith(selection);
        Invalidate();
    }

    void SelectAllObjects()
    {
        if (!FinishNumberEdit(true)) return;
        CommitText();
        _tool = Tool.Objects;
        _stripOpen = false;
        _marquee = null;
        SetObjectSelection(_items);
        ShowStatus(_items.Count == 0 ? "선택할 객체 없음" : $"{_items.Count}개 객체 선택 · 모서리로 크기 조절 · Ctrl+C 복사");
    }

    void OnObjectToolDown(int x, int y, PointF p)
    {
        int resizeHandle = HitObjectResizeHandle(x, y);
        if (resizeHandle >= 0)
        {
            StartObjectResize(resizeHandle, p);
            return;
        }
        bool shift = Win.IsKeyDown(Win.VK_SHIFT);
        var hit = HitItem(p);
        if (shift && hit != null)
        {
            var selection = SelectedObjects();
            if (!selection.Remove(hit)) selection.Add(hit);
            SetObjectSelection(selection);
            return;
        }
        if (_objectSelection.Count > 1 && hit != null && _objectSelection.Contains(hit))
        {
            _lastClickTick = Environment.TickCount64;
            _lastClickPos = new POINT { X = x, Y = y };
            _lastClickItem = hit;
            BeginDrag(DragMode.ObjectMove, p);
            _objectClicked = hit;
            return;
        }
        if (hit != null || (_selected != null && HitHandle(x, y) >= 0))
        {
            _objectSelection.Clear();
            TrySelectAt(x, y, p);
            return;
        }
        _objectSelectionBeforeDrag = shift ? SelectedObjects() : [];
        ClearSelection();
        _objectAnchor = p;
        _objectMarquee = new(p.X, p.Y, 0, 0);
        BeginDrag(DragMode.ObjectMarquee, p);
        Invalidate();
    }

    void FinishObjectMarquee()
    {
        if (_objectMarquee is not { } rectangle) return;
        using var g = MeasureGfx();
        var selected = _items.Where(x => ObjectTransfer.Contains(rectangle, x.Bounds(g))).ToArray();
        SetObjectSelection(_objectSelectionBeforeDrag.Concat(selected));
        _objectSelectionBeforeDrag.Clear();
        ShowStatus($"{SelectedObjects().Count}개 객체 선택 · 모서리로 크기 조절 · Shift+클릭 추가·해제");
    }

    void MoveObjects(float dx, float dy)
    {
        foreach (var item in SelectedObjects()) item.Offset(dx, dy);
        RebuildCommitted();
    }

    void DragObjects(PointF p)
    {
        float dx = p.X - _dragLast.X, dy = p.Y - _dragLast.Y;
        if (!_dragChanged)
        {
            if (MathF.Abs(dx) < 0.5f && MathF.Abs(dy) < 0.5f) return;
            PushUndo();
            _dragChanged = true;
        }
        MoveObjects(dx, dy);
        _dragLast = p;
    }

    void DeleteObjects()
    {
        var selected = SelectedObjects();
        if (selected.Count == 0) return;
        PushUndo();
        foreach (var item in selected) _items.Remove(item);
        ClearSelection();
        RebuildCommitted();
    }

    int HitObjectResizeHandle(int x, int y)
    {
        if (_objectSelection.Count < 2) return -1;
        using var g = MeasureGfx();
        var corners = Corners(ObjectTransfer.Bounds(SelectedObjects(), g));
        float distance = float.MaxValue, reach = 8 * _scale;
        int nearest = -1;
        for (int i = 0; i < corners.Length; i++)
        {
            float dx = x - (_imgX + corners[i].X * _zoom), dy = y - (_imgY + corners[i].Y * _zoom);
            float candidate = dx * dx + dy * dy;
            if (MathF.Abs(dx) <= reach && MathF.Abs(dy) <= reach && candidate < distance)
            {
                nearest = i;
                distance = candidate;
            }
        }
        return nearest;
    }

    void StartObjectResize(int corner, PointF pointer)
    {
        var items = SelectedObjects();
        using var g = MeasureGfx();
        _objectResizeBounds = ObjectTransfer.Bounds(items, g);
        if (_objectResizeBounds.Width <= 0 || _objectResizeBounds.Height <= 0) return;
        _objectResizeSource = items.Select(x => (_items.IndexOf(x), x.Clone())).ToList();
        _objectResizeAnchor = Corners(_objectResizeBounds)[3 - corner];
        _objectResizeCorner = corner;
        _objectResizePointer = pointer;
        _objectResizeFactor = 1;
        _objectResizeLimits = ObjectScale.Limits(items, _objectResizeBounds, _scale);
        BeginDrag(DragMode.ObjectResize, pointer);
    }

    void ResizeObjects(PointF pointer)
    {
        if (_objectResizeSource == null) return;
        float requested = ObjectScale.FromPointer(_objectResizeBounds, _objectResizeCorner, _objectResizePointer, pointer);
        float factor = Math.Clamp(requested, _objectResizeLimits.Min, _objectResizeLimits.Max);
        if (MathF.Abs(factor - _objectResizeFactor) < 0.0001f) return;
        using var g = MeasureGfx();
        var resized = _objectResizeSource.Select(x => ObjectScale.Apply(x.Original, _objectResizeAnchor, factor, g)).ToArray();
        if (!_dragChanged) { PushUndo(); _dragChanged = true; }
        _objectSelection.Clear();
        for (int i = 0; i < resized.Length; i++)
        {
            _items[_objectResizeSource[i].Index] = resized[i];
            _objectSelection.Add(resized[i]);
        }
        _objectResizeFactor = factor;
        RebuildCommitted();
    }

    void FinishObjectResize()
    {
        _objectResizeSource = null;
        ShowStatus($"객체 크기 {MathF.Round(_objectResizeFactor * 100)}% · Ctrl+Z 되돌리기");
        Invalidate();
    }

    void CancelObjectResize()
    {
        var indices = _objectResizeSource?.Select(x => x.Index).ToArray() ?? [];
        _dragging = false;
        _mode = DragMode.None;
        User32.ReleaseCapture();
        if (_dragChanged)
        {
            Undo();
            SetObjectSelection(indices.Select(x => _items[x]));
        }
        _objectResizeSource = null;
        Invalidate();
    }

    void CopySelectionOrImage()
    {
        if (Editing != null) CopyToClipboard();
        else if (HasObjectSelection || _tool == Tool.Objects) CopyObjects();
        else if (_marquee != null) CopyRegion();
        else CopyToClipboard();
    }

    void CopyObjects()
    {
        var items = SelectedObjects();
        if (items.Count == 0) { ShowStatus("복사할 객체 선택 필요"); return; }
        try
        {
            byte[] data = ObjectTransfer.Encode(items);
            var preview = Composer.Compose(new PixelImage(_base.Width, _base.Height), items);
            bool copied = ClipboardService.SetObjectImage(Handle, preview, data);
            ShowStatus(copied ? $"{items.Count}개 객체 복사 · 새 캡처에도 유지 · Ctrl+V 붙여넣기" : "객체 복사 실패");
        }
        catch (Exception ex) { Log.Error(ex); ShowStatus("객체 복사 실패"); }
    }

    bool TryPasteObjects()
    {
        if (!ClipboardService.HasObjects) return false;
        try
        {
            byte[] data = ClipboardService.GetObjects(Handle) ?? throw new InvalidDataException("객체 읽기 실패");
            var items = ObjectTransfer.Decode(data);
            using var g = MeasureGfx();
            ObjectTransfer.FitInto(items, _base.Width, _base.Height, g);
            CommitText();
            PushUndo();
            _items.AddRange(items);
            _tool = Tool.Objects;
            _marquee = null;
            _stripOpen = false;
            SetObjectSelection(items);
            RebuildCommitted();
            ShowStatus($"{items.Count}개 객체 붙여넣기 · 드래그로 함께 이동 · 클릭으로 개별 선택");
        }
        catch (Exception ex) { Log.Error(ex); ShowStatus("객체 붙여넣기 실패"); }
        return true;
    }

    void ShowObjectMenu(int x, int y)
    {
        nint menu = User32.CreatePopupMenu();
        try
        {
            uint available = Win.MF_STRING | (HasObjectSelection ? 0u : 1u);
            User32.AppendMenuW(menu, available, 1, "객체 복사\tCtrl+C");
            bool canPaste = ClipboardService.HasObjects || User32.IsClipboardFormatAvailable(Win.CF_DIB) != 0 ||
                User32.IsClipboardFormatAvailable(17 /* CF_DIBV5 */) != 0;
            User32.AppendMenuW(menu, Win.MF_STRING | (canPaste ? 0u : 1u), 2, "붙여넣기\tCtrl+V");
            User32.AppendMenuW(menu, Win.MF_STRING, 3, "추가한 객체 모두 선택");
            User32.AppendMenuW(menu, available, 4, "선택 객체 삭제\tDel");
            POINT point = new() { X = x, Y = y };
            User32.ClientToScreen(Handle, &point);
            int command = User32.TrackPopupMenu(menu, Win.TPM_RIGHTBUTTON | Win.TPM_RETURNCMD | Win.TPM_NONOTIFY, point.X, point.Y, 0, Handle, 0);
            switch (command)
            {
                case 1: CopyObjects(); break;
                case 2: Paste(); break;
                case 3: SelectAllObjects(); break;
                case 4: DeleteObjects(); break;
            }
        }
        finally { User32.DestroyMenu(menu); }
    }

    void DrawObjectSelection(Gfx g)
    {
        if (_objectSelection.Count > 1)
        {
            using var mg = MeasureGfx();
            foreach (var item in SelectedObjects())
            {
                var b = item.Bounds(mg);
                g.Rect(Toolbar.Accent, _scale, _imgX + b.X * _zoom, _imgY + b.Y * _zoom,
                    b.Width * _zoom, b.Height * _zoom);
            }
            var bounds = ObjectTransfer.Bounds(SelectedObjects(), mg);
            g.Rect(0xFFFFFFFF, _scale, _imgX + bounds.X * _zoom - 3, _imgY + bounds.Y * _zoom - 3,
                bounds.Width * _zoom + 6, bounds.Height * _zoom + 6);
            float size = 9 * _scale;
            foreach (var corner in Corners(bounds))
            {
                float x = _imgX + corner.X * _zoom, y = _imgY + corner.Y * _zoom;
                g.FillRoundRect(0xFFFFFFFF, x - size / 2, y - size / 2, size, size, 2 * _scale);
                g.RoundRect(Toolbar.Accent, 1.5f * _scale, x - size / 2, y - size / 2, size, size, 2 * _scale);
            }
        }
        if (_objectMarquee is { } rectangle)
        {
            float x = _imgX + rectangle.X * _zoom, y = _imgY + rectangle.Y * _zoom;
            g.FillRect(0x228B7CF6, x, y, rectangle.Width * _zoom, rectangle.Height * _zoom);
            g.Rect(Toolbar.Accent, _scale, x, y, rectangle.Width * _zoom, rectangle.Height * _zoom);
        }
    }
}

using LunaCapture.Interop;
using LunaCapture.Recording;

namespace LunaCapture.Capture;

/// <summary>"PrtSc를 다른 프로그램이 쓰고 있어요. LunaCapture가 우선 사용할까요?" 물어보는 작은 창.</summary>
internal sealed class TakeoverPrompt : PanelWindow
{
    readonly string _owner;
    readonly Action<bool> _onAnswer;
    readonly GdiFont _title, _body, _small;
    bool _answered;

    public TakeoverPrompt(string? owner, Action<bool> onAnswer)
    {
        _owner = owner != null ? $"다른 프로그램({owner})" : "다른 프로그램";
        _onAnswer = onAnswer;

        unsafe
        {
            var mi = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
            User32.GetMonitorInfoW(User32.MonitorFromPoint(default, 1 /* PRIMARY */), &mi);
            CreatePanel(mi.rcWork.Left, mi.rcWork.Top, noActivate: false);
            _title = new GdiFont(UiFonts.Family, 16 * S, true);
            _body = new GdiFont(UiFonts.Family, 14 * S, false);
            _small = new GdiFont(UiFonts.Family, 13 * S, false);

            int w = Px(460), h = Px(250);
            var work = mi.rcWork;
            SetBounds(work.Left + (work.Width - w) / 2, work.Top + (work.Height - h) / 2, w, h);
        }
        User32.ShowWindow(Handle, Win.SW_SHOW);
        User32.ForceForeground(Handle);
    }

    protected override void Render(Gfx g)
    {
        int pad = Px(22), y = Px(20);
        g.Text("PrtSc 키 설정", _title, TextMain, pad, y);
        y += Px(36);
        g.Text($"PrtSc 키를 {_owner}이 쓰고 있어요.", _body, TextMain, pad, y);
        y += Px(24);
        g.Text("LunaCapture가 PrtSc를 우선 사용하도록 할까요?", _body, TextMain, pad, y);
        y += Px(32);
        foreach (var line in new[]
        {
            "· LunaCapture가 켜져 있는 동안 PrtSc는 LunaCapture가 받아요",
            "· LunaCapture를 끄면 원래대로 돌아가요",
            "· 트레이 메뉴 › 설정에서 언제든 바꿀 수 있어요",
        })
        {
            g.Text(line, _small, TextSub, pad, y);
            y += Px(21);
        }

        int bh = Px(34), by = Height - pad - bh + Px(4);
        var take = new RECT(Width - pad - Px(110), by, Width - pad, by + bh);
        var keep = new RECT(take.Left - Px(8) - Px(96), by, take.Left - Px(8), by + bh);
        Button(g, "keep", keep, "그대로 두기", _body, () => Answer(false));
        Button(g, "take", take, "우선 사용", _body, () => Answer(true), primary: true);
    }

    void Answer(bool takeOver)
    {
        if (_answered) return;
        _answered = true;
        Destroy();
        _onAnswer(takeOver);
    }

    protected override nint WndProc(uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case Win.WM_KEYDOWN:
                if (wParam == Win.VK_RETURN) Answer(true);
                else if (wParam == Win.VK_ESCAPE) Answer(false);
                return 0;
            case Win.WM_CLOSE:
                Answer(false);
                return 0;
            case Win.WM_DESTROY:
                _title.Dispose(); _body.Dispose(); _small.Dispose();
                break;
        }
        return base.WndProc(msg, wParam, lParam);
    }
}

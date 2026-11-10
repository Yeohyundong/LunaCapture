using LunaCapture.Interop;
using LunaCapture.Recording;
using LunaCapture.Services;
using System.Runtime.InteropServices;

namespace LunaCapture.Capture;

/// <summary>처음 실행(또는 새 버전 실행) 때 뜨는 "설치할까요?" 창.</summary>
internal sealed class InstallPrompt : PanelWindow
{
    readonly bool _update;
    readonly string _installedVersion;
    readonly Action<bool, bool> _onAnswer; // (설치함, 자동 실행)
    readonly GdiFont _title, _body, _small;
    nint _notesFont;
    bool _autoStart = true;
    bool _answered;

    public InstallPrompt(bool update, string installedVersion, Action<bool, bool> onAnswer)
    {
        _update = update;
        _installedVersion = installedVersion;
        _onAnswer = onAnswer;
        _autoStart = !update || StartupRegistration.IsRegistered;

        unsafe
        {
            var mi = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
            User32.GetMonitorInfoW(User32.MonitorFromPoint(default, 1 /* PRIMARY */), &mi);
            CreatePanel(mi.rcWork.Left, mi.rcWork.Top, noActivate: false);
            _title = new GdiFont(UiFonts.Family, 17 * S, true);
            _body = new GdiFont(UiFonts.Family, 14 * S, false);
            _small = new GdiFont(UiFonts.Family, 13 * S, false);

            int w = Px(update ? 680 : 470), h = Px(update ? 560 : 300);
            var work = mi.rcWork;
            SetBounds(work.Left + (work.Width - w) / 2, work.Top + (work.Height - h) / 2, w, h);
        }
        if (update)
        {
            var current = Version.TryParse(installedVersion, out var parsed) ? parsed : new Version(0, 0, 0);
            string notes = ReleaseNotes.Between(current, Version.Parse(Installer.Version));
            nint viewer = User32.CreateWindowExW(0x200, "EDIT", "", 0x50210844,
                Px(24), Px(174), Px(632), Px(294), Handle, 302, Kernel32.GetModuleHandleW(0), 0);
            _notesFont = Gdi32.CreateFontW(-Px(15), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "맑은 고딕");
            SendMessageW(viewer, 0x30 /* WM_SETFONT */, _notesFont, 1);
            SendMessageW(viewer, 0xC5 /* EM_SETLIMITTEXT */, Math.Max(32767, notes.Length + 1), 0);
            SetWindowTextW(viewer, notes);
        }
        User32.ShowWindow(Handle, Win.SW_SHOW);
        User32.ForceForeground(Handle);
    }

    protected override void Render(Gfx g)
    {
        int pad = Px(24), y = Px(22);

        // 달 아이콘 + 제목
        float icon = Px(34);
        using (var bmpIcon = new IconPainter(g)) bmpIcon.Draw(pad, y, icon);
        string title = _update ? "LunaCapture 업데이트" : "LunaCapture 설치";
        g.Text(title, _title, TextMain, pad + icon + Px(12), y + (icon - g.LineHeight(_title)) / 2);
        string ver = _update ? $"{_installedVersion} → {Installer.Version}" : $"버전 {Installer.Version}";
        g.Text(ver, _small, TextSub, Width - pad - g.MeasureWidth(ver, _small), y + (icon - g.LineHeight(_small)) / 2);
        y += (int)icon + Px(20);

        if (_update)
        {
            g.Text("새 버전 업데이트", _body, TextMain, pad, y);
            y += Px(26);
            g.Text("· 업데이트 후 앱 다시 시작", _small, TextSub, pad, y);
            y += Px(21);
            g.Text("· 설정·저장 파일 유지", _small, TextSub, pad, y);
        }
        else
        {
            g.Text("내 PC에 설치하면 더 편하게 쓸 수 있어요.", _body, TextMain, pad, y);
            y += Px(28);
            foreach (var line in new[]
            {
                "· 시작 메뉴에 LunaCapture가 생겨요",
                "· '설치된 앱' 목록에서 언제든 제거할 수 있어요",
                "· 관리자 권한 없이 내 계정에만 설치돼요",
            })
            {
                g.Text(line, _small, TextSub, pad, y);
                y += Px(21);
            }
            y += Px(10);

            // 자동 실행 체크박스
            float box = Px(18);
            var row = new RECT(pad - Px(4), y - Px(4), pad + Px(260), y + (int)box + Px(4));
            AddItem("autostart", row, () => { _autoStart = !_autoStart; Invalidate(); });
            g.FillRoundRect(_autoStart ? Accent : ItemBg, pad, y, box, box, Px(4));
            if (!_autoStart) g.RoundRect(0xFF5A5A66, 1, pad, y, box, box, Px(4));
            else g.Lines(0xFFFFFFFF, Px(2), [new(pad + box * 0.25f, y + box * 0.52f), new(pad + box * 0.43f, y + box * 0.7f), new(pad + box * 0.76f, y + box * 0.32f)]);
            g.Text("Windows 시작할 때 자동으로 실행", _body, TextMain, pad + box + Px(10), y + (box - g.LineHeight(_body)) / 2);
        }

        int bh = Px(36), by = Height - pad - bh + Px(4);
        var ok = new RECT(Width - pad - Px(120), by, Width - pad, by + bh);
        var no = new RECT(ok.Left - Px(8) - Px(130), by, ok.Left - Px(8), by + bh);
        Button(g, "no", no, _update ? "나중에" : "설치 없이 실행", _body, () => Answer(false));
        Button(g, "ok", ok, _update ? "업데이트" : "설치하기", _body, () => Answer(true), primary: true);
    }

    void Answer(bool install)
    {
        if (_answered) return;
        _answered = true;
        Destroy();
        _onAnswer(install, _autoStart);
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

    protected override void OnDestroyed()
    {
        if (_notesFont != 0) Gdi32.DeleteObject(_notesFont);
        base.OnDestroyed();
    }

    [DllImport("user32.dll")]
    static extern nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTextW(nint hwnd, string text);

    /// <summary>앱 아이콘을 작은 크기로 그려 넣는다.</summary>
    sealed unsafe class IconPainter(Gfx g) : IDisposable
    {
        public void Draw(float x, float y, float size)
        {
            var img = AppIcon.Render((int)size);
            fixed (byte* p = img.Bgra)
            {
                nint bmp;
                GdiPlus.GdipCreateBitmapFromScan0(img.Width, img.Height, img.Stride, 0x26200A, p, &bmp);
                GdipDrawImageRectI(g.Handle, bmp, (int)x, (int)y, img.Width, img.Height);
                GdiPlus.GdipDisposeImage(bmp);
            }
        }

        public void Dispose() { }

        [System.Runtime.InteropServices.DllImport("gdiplus.dll")]
        static extern int GdipDrawImageRectI(nint g, nint image, int x, int y, int w, int h);
    }
}

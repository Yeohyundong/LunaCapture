using System.Runtime.InteropServices;
using LunaCapture.Interop;
using LunaCapture.Services;

namespace LunaCapture.Editor;

internal sealed class UpdateNotesWindow : ValuePanel
{
    readonly string _title, _subtitle;
    readonly bool _update;
    readonly nint _font;

    [DllImport("user32.dll")]
    static extern nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);

    public UpdateNotesWindow(nint owner, PendingUpdate? update, Action closed) : base(owner, 680, 560, closed)
    {
        _update = update != null;
        _title = _update ? "업데이트 준비 완료" : "업데이트 내역";
        _subtitle = _update ? Installer.Version + "  →  " + update!.Version : "현재 버전  " + Installer.Version;
        string notes = update?.Notes ?? ReleaseNotes.History(Version.Parse(Installer.Version));
        if (string.IsNullOrWhiteSpace(notes)) notes = "변경 내역 불러오기 실패";
        nint viewer = User32.CreateWindowExW(0x200, "EDIT", "", 0x50210844,
            Px(20), Px(98), Px(640), Px(370), Handle, 301, Kernel32.GetModuleHandleW(0), 0);
        Fields.Add(viewer);
        _font = Gdi32.CreateFontW(-Px(15), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "맑은 고딕");
        SendMessageW(viewer, 0x30 /* WM_SETFONT */, _font, 1);
        SendMessageW(viewer, 0xC5 /* EM_SETLIMITTEXT */, Math.Max(32767, notes.Length + 1), 0);
        Write(viewer, notes);
        Show();
    }

    protected override void Render(Gfx g)
    {
        Label(g, _title, 20, 20, 20);
        Button(g, "close", "×", 620, 12, 40, Destroy);
        Label(g, _subtitle, 20, 62, 14);
        Label(g, _update ? "창을 닫고 작업 종료 후 업데이트" : "스크롤해서 이전 내역 보기", 20, 486, 12);
        Button(g, "done", "확인", 550, 520, 110, Destroy);
    }

    protected override void OnDestroyed()
    {
        if (_font != 0) Gdi32.DeleteObject(_font);
        base.OnDestroyed();
    }
}

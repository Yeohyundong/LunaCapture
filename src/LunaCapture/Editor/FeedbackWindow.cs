using System.Runtime.InteropServices;
using LunaCapture.Interop;
using LunaCapture.Services;

namespace LunaCapture.Editor;

internal sealed unsafe class FeedbackWindow : ValuePanel
{
    const nuint SendTimer = 81;
    readonly nint _content, _email;
    readonly nint _font;
    Task<FeedbackResult>? _sending;
    string _status = "";
    bool _sent;
    [DllImport("user32.dll")] static extern nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] static extern int EnableWindow(nint hwnd, int enable);

    public FeedbackWindow(nint owner, Action closed) : base(owner, 530, 500, closed)
    {
        _content = User32.CreateWindowExW(0x200, "EDIT", FeedbackClient.DraftContent, 0x50211044, Px(20), Px(102), Px(490), Px(220), Handle, 201, Kernel32.GetModuleHandleW(0), 0);
        Fields.Add(_content);
        _email = Edit(FeedbackClient.DraftEmail, 20, 365, 490, 202);
        _font = Gdi32.CreateFontW(-Px(15), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "맑은 고딕");
        SendMessageW(_content, 0x30 /* WM_SETFONT */, _font, 1);
        SendMessageW(_email, 0x30, _font, 1);
        SendMessageW(_content, 0xC5 /* EM_SETLIMITTEXT */, FeedbackClient.MaxContent, 0);
        SendMessageW(_email, 0xC5, 254, 0);
        Show();
    }

    void RememberDraft()
    {
        string content = Read(_content), email = Read(_email);
        if (content != FeedbackClient.DraftContent || email != FeedbackClient.DraftEmail)
            FeedbackClient.DraftRequestId = Guid.NewGuid().ToString("N");
        FeedbackClient.DraftContent = content; FeedbackClient.DraftEmail = email;
    }

    void Send()
    {
        if (_sending != null) return;
        if (_sent) { Destroy(); return; }
        RememberDraft();
        string content = FeedbackClient.DraftContent.Trim(), email = FeedbackClient.DraftEmail.Trim();
        if (FeedbackClient.Validate(content, email) is { } error) { _status = error; Invalidate(); return; }
        EnableWindow(_content, 0); EnableWindow(_email, 0);
        _status = "보내는 중이에요…";
        var payload = new FeedbackPayload(content, email.Length == 0 ? null : email, Installer.Version, FeedbackClient.DraftRequestId);
        _sending = Task.Run(() => FeedbackClient.SendAsync(payload));
        User32.SetTimer(Handle, SendTimer, 200, 0);
        Invalidate();
    }

    protected override void Render(Gfx g)
    {
        Label(g, "이거 고쳐주세요!", 20, 20, 20);
        Button(g, "close", "×", 470, 12, 40, CloseForm);
        Label(g, "불편했던 점이나 아이디어를 편하게 적어주세요.", 20, 60, 13);
        Label(g, "내용 (최대 4,000자)", 20, 82, 12);
        Label(g, "답장받을 이메일 (선택)", 20, 340, 13);
        Label(g, "작성 내용·선택 이메일·앱 버전만 전달돼요. 사진·로그는 보내지 않아요.", 20, 406, 11);
        Label(g, _status, 20, 430, 12, _sent ? 0xFF8DE0B2 : 0xFFD6D0ED);
        Button(g, "cancel", "닫기", 300, 460, 90, CloseForm);
        Button(g, "send", _sent ? "확인" : _sending != null ? "전송 중…" : "보내기", 400, 460, 110, Send);
    }

    void CloseForm()
    {
        if (_sending != null) { _status = "전송 결과를 기다리는 중이에요. 잠시만 기다려주세요."; Invalidate(); return; }
        Destroy();
    }
    protected override nint WndProc(uint msg, nint wParam, nint lParam)
    {
        if (msg == 0x10 || (msg == Win.WM_KEYDOWN && wParam == Win.VK_ESCAPE)) { CloseForm(); return 0; }
        if (msg == Win.WM_TIMER && wParam == (nint)SendTimer && _sending is { IsCompleted: true } task)
        {
            User32.KillTimer(Handle, SendTimer); _sending = null;
            var result = task.GetAwaiter().GetResult();
            _status = result.Message; _sent = result.Success;
            if (_sent) { FeedbackClient.DraftContent = ""; FeedbackClient.DraftEmail = ""; FeedbackClient.DraftRequestId = Guid.NewGuid().ToString("N"); }
            else { EnableWindow(_content, 1); EnableWindow(_email, 1); }
            Invalidate(); return 0;
        }
        return base.WndProc(msg, wParam, lParam);
    }
    protected override void OnDestroyed()
    {
        User32.KillTimer(Handle, SendTimer);
        if (!_sent) RememberDraft();
        if (_font != 0) Gdi32.DeleteObject(_font);
        base.OnDestroyed();
    }
}

using LunaCapture.Capture;
using LunaCapture.Interop;
using LunaCapture.Services;

namespace LunaCapture;

/// <summary>
/// PrtSc 키 확보.
/// 1) 보통은 일반 단축키로 등록한다.
/// 2) 다른 프로그램(PicPick 등)이나 윈도우 캡처 도구가 쓰고 있으면, 사용자에게 우선 사용할지 물어본다(답은 기억).
///    - 우선 사용: 키보드 훅으로 PrtSc만 먼저 받는다.
///    - 그냥 두기: 10초마다 다시 시도해서, 그 프로그램이 꺼지면 이어받는다.
/// </summary>
internal sealed partial class TrayApp
{
    const uint MsgHookPrintScreen = Win.WM_APP + 2;
    const nuint RetryKeyTimer = 1;
    const int HotkeyHistory = 2;

    bool _hasCaptureKey;       // 일반 단축키 등록 성공
    TakeoverPrompt? _prompt;

    const string ReadyText = "PrtSc : 캡처 (Tab으로 사진 ↔ 녹화 전환)";

    void RegisterCaptureKey()
    {
        _hasCaptureKey = User32.RegisterHotKey(Handle, HotkeyCapture, Win.MOD_NOREPEAT, VK_SNAPSHOT) != 0;
        User32.RegisterHotKey(Handle, HotkeyHistory, Win.MOD_SHIFT | Win.MOD_NOREPEAT, VK_SNAPSHOT); // 실패해도 트레이 메뉴로 열 수 있음
        bool snipping = SnippingToolOwnsPrintScreen();

        if (_hasCaptureKey && !snipping)
        {
            Notify("LunaCapture 실행 중", ReadyText, Win.NIIF_INFO);
            return;
        }

        string? owner = snipping && _hasCaptureKey ? "윈도우 캡처 도구" : PrintScreenHook.GuessOwner();
        switch (Settings.PrintScreenTakeover)
        {
            case true:
                EnableTakeover(notify: true);
                break;
            case false:
                StayPassive(owner);
                break;
            default:
                _prompt = new TakeoverPrompt(owner, takeOver =>
                {
                    _prompt = null;
                    Settings.PrintScreenTakeover = takeOver;
                    AppSettings.Save();
                    if (takeOver) EnableTakeover(notify: true);
                    else StayPassive(owner);
                });
                break;
        }
    }

    /// <summary>다른 프로그램이 PrtSc를 쓰고 있는지 (일반 등록 실패 또는 윈도우 캡처 도구 설정 켜짐).</summary>
    bool PrintScreenOccupied => !_hasCaptureKey || SnippingToolOwnsPrintScreen();

    void EnableTakeover(bool notify)
    {
        if (!PrintScreenHook.Install(Handle, MsgHookPrintScreen))
        {
            Notify("PrtSc 우선 사용을 설정하지 못했어요", "트레이 아이콘을 클릭해 캡처하세요.", Win.NIIF_WARNING);
            return;
        }
        // 일반 등록이 안 된 상태라면, 상대 프로그램이 꺼졌을 때 가벼운 일반 단축키로 갈아타기 위해 계속 시도한다.
        if (!_hasCaptureKey) User32.SetTimer(Handle, RetryKeyTimer, 10_000, 0);
        if (notify) Notify("LunaCapture 실행 중 (PrtSc 우선 사용)", ReadyText, Win.NIIF_INFO);
    }

    void StayPassive(string? owner)
    {
        PrintScreenHook.Uninstall();
        if (!_hasCaptureKey) User32.SetTimer(Handle, RetryKeyTimer, 10_000, 0);

        if (SnippingToolOwnsPrintScreen() && _hasCaptureKey)
        {
            Notify("PrtSc 키 설정이 필요해요",
                "윈도우 설정에서 'Print Screen 키를 사용하여 화면 캡처 열기'를 꺼 주세요.\n이 알림을 클릭하면 설정 화면이 열려요.", Win.NIIF_WARNING,
                () => Shell32.ShellExecuteW(0, "open", "ms-settings:easeofaccess-keyboard", null, null, Win.SW_SHOW));
        }
        else
        {
            Notify($"PrtSc 키를 {owner ?? "다른 프로그램"}에서 쓰고 있어요",
                "그 프로그램의 PrtSc 단축키를 끄거나 종료하면 자동으로 이어받아요.\n그 전까지는 트레이 아이콘을 클릭해 캡처하세요.", Win.NIIF_WARNING);
        }
    }

    /// <summary>트레이 메뉴에서 우선 사용을 켜고 끌 때.</summary>
    void SetTakeover(bool on)
    {
        Settings.PrintScreenTakeover = on;
        AppSettings.Save();
        if (on)
        {
            if (PrintScreenOccupied) EnableTakeover(notify: true);
        }
        else if (PrintScreenHook.IsActive)
        {
            StayPassive(PrintScreenHook.GuessOwner());
        }
    }

    void RetryCaptureKey()
    {
        if (_hasCaptureKey || User32.RegisterHotKey(Handle, HotkeyCapture, Win.MOD_NOREPEAT, VK_SNAPSHOT) == 0) return;

        _hasCaptureKey = true;
        User32.KillTimer(Handle, RetryKeyTimer);
        if (SnippingToolOwnsPrintScreen()) return; // 윈도우 캡처 도구가 켜져 있으면 우선 사용을 계속 유지

        bool wasHooked = PrintScreenHook.IsActive;
        PrintScreenHook.Uninstall(); // 이제 일반 단축키로 충분
        if (!wasHooked) Notify("이제 PrtSc로 캡처할 수 있어요", ReadyText, Win.NIIF_INFO);
    }

    static bool SnippingToolOwnsPrintScreen()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard");
        // 값이 없으면 윈도우 11에서는 기본으로 켜져 있다.
        return key?.GetValue("PrintScreenKeyForSnippingEnabled") is not int v || v != 0;
    }
}

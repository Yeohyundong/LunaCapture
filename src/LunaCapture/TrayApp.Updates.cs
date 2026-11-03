using System.Diagnostics;
using LunaCapture.Interop;
using LunaCapture.Services;

namespace LunaCapture;

internal sealed unsafe partial class TrayApp
{
    const nuint UpdateTimer = 71;
    const uint CmdCheckUpdate = 170, CmdAutoUpdate = 171, CmdFeedback = 172;
    Task<UpdateResult>? _updateCheck;
    PendingUpdate? _pendingUpdate;
    Process? _updateHelper;
    string? _helperReady;
    DateTimeOffset _helperStarted, _nextUpdateCheck = DateTimeOffset.MaxValue;
    bool _manualCheck, _manualApply, _menuOpen, _applyingUpdate;
    bool _finishingCapture, _finishingRecording;
    bool IsUpdateBusy => _busy || _finishingCapture || _finishingRecording;

    void StartUpdates(bool skip)
    {
        User32.SetTimer(Handle, UpdateTimer, 10000, 0);
        _nextUpdateCheck = skip ? DateTimeOffset.UtcNow.AddDays(1) : DateTimeOffset.UtcNow;
        if (!skip && Settings.AutoUpdate && Installer.IsRunningInstalled) BeginUpdateCheck(false);
    }

    void BeginUpdateCheck(bool manual)
    {
        if (!Installer.IsRunningInstalled)
        {
            if (manual) Notify("설치 후 자동 업데이트를 사용할 수 있어요", "설치된 앱은 실행 시와 하루 한 번 업데이트를 확인해요.", Win.NIIF_INFO);
            return;
        }
        if (_updateCheck != null) { if (manual) Notify("업데이트 확인 중", "잠시만 기다려 주세요.", Win.NIIF_INFO); return; }
        if (_pendingUpdate != null) { if (manual) _manualApply = true; if (manual) Notify("업데이트 준비 완료", "편집·녹화·열린 창이 모두 끝나면 자동으로 적용해요.", Win.NIIF_INFO); return; }
        _manualCheck = manual;
        _manualApply = manual;
        _updateCheck = Task.Run(UpdateService.CheckAsync);
    }

    void UpdateTick()
    {
        if (_applyingUpdate) return;
        if (_updateCheck is { IsCompleted: true } completed)
        {
            _updateCheck = null;
            var result = completed.GetAwaiter().GetResult();
            _nextUpdateCheck = DateTimeOffset.UtcNow.AddHours(result.Failed ? 1 : 24);
            _pendingUpdate = result.Update;
            if (_manualCheck || result.Update != null) Notify(result.Update == null ? "업데이트 확인" : "업데이트 준비 완료", result.Message, Win.NIIF_INFO);
        }
        if (_updateHelper != null && _updateHelper.HasExited)
        {
            _updateHelper.Dispose(); _updateHelper = null; _helperReady = null; _pendingUpdate = null;
            _nextUpdateCheck = DateTimeOffset.UtcNow.AddHours(1);
            Notify("업데이트를 적용하지 못했어요", "현재 버전을 계속 사용해요. 나중에 다시 확인할게요.", Win.NIIF_INFO);
        }
        if ((!Settings.AutoUpdate && !_manualApply) || !Installer.IsRunningInstalled) return;
        if (_pendingUpdate == null)
        {
            if (DateTimeOffset.UtcNow >= _nextUpdateCheck) BeginUpdateCheck(false);
            return;
        }
        // Every application-owned window (including GIF trim/palettes/dialogs) blocks replacement.
        if (!UpdateService.CanApply(IsUpdateBusy, IsRecording, _recSetup != null || _recBar != null, NativeWindow.HasOtherWindows(Handle), _menuOpen)) return;
        if (_updateHelper == null)
        {
            try
            {
                (_updateHelper, _helperReady) = UpdateService.StartHelper(_pendingUpdate);
                _helperStarted = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                Log.Error(ex); _pendingUpdate = null; _nextUpdateCheck = DateTimeOffset.UtcNow.AddHours(1);
                Notify("업데이트를 적용하지 못했어요", "현재 버전으로 계속 사용할 수 있어요.", Win.NIIF_INFO);
            }
            return;
        }
        if (_helperReady != null && File.Exists(_helperReady))
        {
            _applyingUpdate = true;
            Destroy(); // helper waits for actual process exit; never force-closes an editor/recorder.
        }
        else if (DateTimeOffset.UtcNow - _helperStarted > TimeSpan.FromSeconds(40))
        {
            CancelUpdateHelper(); _pendingUpdate = null; _nextUpdateCheck = DateTimeOffset.UtcNow.AddHours(1);
        }
    }

    void CancelUpdateHelper()
    {
        _manualApply = false;
        if (_pendingUpdate != null && _updateHelper != null)
            try { File.WriteAllText(Path.Combine(_pendingUpdate.Directory, "cancel"), "cancel"); } catch (Exception ex) { Log.Error(ex); }
        _updateHelper?.Dispose(); _updateHelper = null; _helperReady = null;
    }

    void OpenFeedback()
    {
        if (Shell32.ShellExecuteW(Handle, "open", ReleaseChannel.FeedbackUrl, null, null, Win.SW_SHOW) <= 32)
            User32.MessageBoxW(Handle, "기본 메일 앱을 열지 못했어요.\n\n" + ReleaseChannel.FeedbackEmail + " 으로 앱 버전(" + Installer.Version + ")과 함께 피드백을 보내주세요.", "피드백 보내기", 0x40);
    }
}

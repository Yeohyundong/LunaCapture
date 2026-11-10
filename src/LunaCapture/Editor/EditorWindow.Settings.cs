using LunaCapture.Interop;
using LunaCapture.Services;

namespace LunaCapture.Editor;

internal enum EditorSettingAction { CheckUpdates, ToggleAutoUpdate, Feedback }

internal sealed unsafe partial class EditorWindow
{
    readonly Action<EditorSettingAction>? _settingAction;

    void ShowSettings()
    {
        var settings = AppSettings.Current;
        nint menu = User32.CreatePopupMenu();
        try
        {
            uint Check(bool value) => Win.MF_STRING | (value ? Win.MF_CHECKED : 0);
            User32.AppendMenuW(menu, Win.MF_STRING | 1, 0, "LunaCapture " + Installer.Version);
            User32.AppendMenuW(menu, Win.MF_SEPARATOR, 0, null);
            User32.AppendMenuW(menu, Check(settings.OpenEditor), 1, "캡처 후 편집 창 열기");
            User32.AppendMenuW(menu, Check(settings.OpenGifTrim), 2, "GIF 녹화 후 자르기 창 열기");
            User32.AppendMenuW(menu, Win.MF_STRING, 3, "이미지·GIF 저장 폴더 변경…");
            User32.AppendMenuW(menu, Win.MF_SEPARATOR, 0, null);
            User32.AppendMenuW(menu, Check(settings.AutoUpdate), 4, "자동 업데이트 (작업 종료 후 적용)");
            User32.AppendMenuW(menu, Win.MF_STRING, 5, "업데이트 확인");
            User32.AppendMenuW(menu, Win.MF_STRING, 7, "업데이트 내역");
            User32.AppendMenuW(menu, Win.MF_SEPARATOR, 0, null);
            User32.AppendMenuW(menu, Win.MF_STRING, 6, "이거 고쳐주세요! ✉");
            var button = _toolbar.Items.First(x => x.Kind == ItemKind.Settings).Bounds;
            var position = new POINT { X = button.Left, Y = button.Bottom };
            User32.ClientToScreen(Handle, &position);
            uint command = (uint)User32.TrackPopupMenu(menu, Win.TPM_RIGHTBUTTON | Win.TPM_RETURNCMD | Win.TPM_NONOTIFY,
                position.X, position.Y, 0, Handle, 0);
            switch (command)
            {
                case 1: settings.OpenEditor = !settings.OpenEditor; AppSettings.Save(); break;
                case 2: settings.OpenGifTrim = !settings.OpenGifTrim; AppSettings.Save(); break;
                case 3:
                    if (FolderPicker.Pick(Handle, "이미지·GIF 저장 폴더", settings.ImageFolderOrDefault) is { } folder)
                    { settings.ImageFolder = folder; AppSettings.Save(); }
                    break;
                case 4:
                    if (_settingAction != null) _settingAction(EditorSettingAction.ToggleAutoUpdate);
                    else { settings.AutoUpdate = !settings.AutoUpdate; AppSettings.Save(); }
                    break;
                case 5:
                    if (_settingAction != null) _settingAction(EditorSettingAction.CheckUpdates);
                    else ShowStatus("설치된 앱에서 업데이트를 확인할 수 있어요");
                    break;
                case 6:
                    if (_options == null) _options = new FeedbackWindow(Handle, () => _options = null);
                    break;
                case 7:
                    if (_options == null) _options = new UpdateNotesWindow(Handle, null, () => _options = null);
                    break;
            }
        }
        finally { User32.DestroyMenu(menu); }
    }
}

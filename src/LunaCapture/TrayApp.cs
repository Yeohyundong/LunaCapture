using LunaCapture.Capture;
using LunaCapture.Editor;
using LunaCapture.Graphics;
using LunaCapture.Interop;
using LunaCapture.Services;

namespace LunaCapture;

/// <summary>
/// 보이지 않는 메인 창. 트레이 아이콘, 전역 단축키, 캡처 흐름을 관리한다.
/// </summary>
internal sealed unsafe partial class TrayApp : NativeWindow
{
    const uint TrayCallback = Win.WM_APP + 1;
    const int HotkeyCapture = 1;
    const uint VK_SNAPSHOT = 0x2C; // PrtSc

    const uint CmdRegion = 100, CmdRecord = 103,
        CmdOpenImageFolder = 110, CmdOpenVideoFolder = 111,
        CmdToggleEditor = 120, CmdToggleTrim = 121, CmdToggleCursor = 122, CmdToggleStartup = 123, CmdToggleTakeover = 124,
        CmdUninstall = 140, CmdHistory = 150, CmdHistoryMax = 160, // 160~163: 보관 개수 선택
        CmdChangeImageFolder = 130, CmdChangeVideoFolder = 131,
        CmdExit = 199;

    static AppSettings Settings => AppSettings.Current;
    static string SaveFolder => Settings.ImageFolderOrDefault;
    static string VideoFolder => Settings.VideoFolderOrDefault;

    readonly uint _taskbarCreatedMsg;
    nint _icon;
    bool _busy;
    EditorWindow? _editor;
    string? _lastSavedPath;

    public TrayApp(bool skipUpdate = false)
    {
        _taskbarCreatedMsg = User32.RegisterWindowMessageW("TaskbarCreated");
        CreateHandle(Win.WS_EX_TOOLWINDOW, Win.WS_POPUP, 0, 0, 0, 0, "LunaCapture");
        _icon = AppIcon.Load(User32.GetSystemMetrics(49 /* SM_CXSMICON */));
        AddTrayIcon();

        RegisterCaptureKey();
        StartUpdates(skipUpdate);
    }

    protected override nint WndProc(uint msg, nint wParam, nint lParam)
    {
        if (msg == _taskbarCreatedMsg)
        {
            AddTrayIcon(); // 탐색기가 재시작되면 아이콘을 다시 등록
            return 0;
        }

        switch (msg)
        {
            case Installer.SafeUpdateClose:
                if (!UpdateService.CanApply(IsUpdateBusy, IsRecording, _recSetup != null || _recBar != null, NativeWindow.HasOtherWindows(Handle), _menuOpen)) return 0;
                User32.PostMessageW(Handle, Win.WM_CLOSE, 0, 0);
                return 1;
            case Win.WM_HOTKEY:
                if (wParam == HotkeyCapture) OnCaptureKey();
                else if (wParam == HotkeyHistory) OpenHistory();
                return 0;

            case MsgHookPrintScreen: // 우선 사용 모드에서 PrtSc (wParam 1 = Shift+PrtSc)
                if (wParam == 1) OpenHistory(); else OnCaptureKey();
                return 0;

            case Win.WM_TIMER:
                if (wParam == (nint)UpdateTimer) { UpdateTick(); return 0; }
                if (wParam == (nint)RetryKeyTimer) RetryCaptureKey();
                return 0;

            case TrayCallback:
                switch ((uint)Win.LoWord(lParam))
                {
                    case Win.NIN_SELECT:
                    case Win.NIN_KEYSELECT:
                        OnCaptureKey();
                        break;
                    case Win.WM_CONTEXTMENU:
                        ShowMenu();
                        break;
                    case Win.NIN_BALLOONUSERCLICK:
                        if (_balloonClick != null) _balloonClick();
                        else if (_lastSavedPath != null) OpenInExplorer(_lastSavedPath);
                        break;
                }
                return 0;

            case Win.WM_DESTROY:
                // Explicit Exit/uninstall must stay closed, even when a helper is waiting.
                if (!_applyingUpdate) CancelUpdateHelper();
                User32.UnregisterHotKey(Handle, HotkeyCapture);
                User32.UnregisterHotKey(Handle, HotkeyHistory);
                User32.KillTimer(Handle, RetryKeyTimer);
                PrintScreenHook.Uninstall();
                ShutdownRecording();
                User32.KillTimer(Handle, UpdateTimer);
                RemoveTrayIcon();
                User32.PostQuitMessage(0);
                return 0;
        }
        return base.WndProc(msg, wParam, lParam);
    }

    void OnCaptureKey()
    {
        if (_recBar != null) { _recBar.Stop(); return; } // 녹화 중 → 정지
        StartCapture(CaptureMode.Image);
    }

    void StartCapture(CaptureMode mode)
    {
        if (_busy || _recSetup != null || IsRecording) return;
        _busy = true;
        try
        {
            RegionOverlay.Start(mode, OnOverlayDone);
        }
        catch (Exception ex)
        {
            _busy = false;
            Log.Error(ex);
            Notify("캡처 실패", ex.Message, Win.NIIF_WARNING);
        }
    }

    void OnOverlayDone(OverlayResult? result)
    {
        switch (result?.Mode)
        {
            case CaptureMode.Image:
                OnRegionCaptured(result.Image, result.AppName);
                break;
            case CaptureMode.Record:
                OpenRecordSetup(result.ScreenRect, result.AppName);
                break;
            default:
                _busy = false;
                MemoryTrimmer.Trim();
                break;
        }
    }

    void OnRegionCaptured(PixelImage? image, string? appName)
    {
        _finishingCapture = true;
        try { OnRegionCapturedCore(image, appName); } finally { _finishingCapture = false; }
    }

    void OnRegionCapturedCore(PixelImage? image, string? appName)
    {
        _busy = false;
        if (image == null)
        {
            MemoryTrimmer.Trim();
            return;
        }

        string? path = null, histId = null;
        string fileName = CaptureFileName.Create(appName, DateTime.Now, "png");
        // 이전 편집본을 먼저 저장한다. 이후 새 캡처를 복사해야 클립보드도 최신 캡처를 유지한다.
        if (Settings.OpenEditor) _editor?.Close();
        bool copied = false;
        try
        {
            // 편집기를 열더라도 원본은 먼저 복사·저장해 둔다 (편집기를 안 써도 바로 붙여넣기 가능).
            copied = ClipboardService.SetImage(Handle, image);
            path = SaveImage(image, Path.Combine(SaveFolder, fileName));
            histId = HistoryStore.AddImage(image, path);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            Notify("저장 실패", ex.Message, Win.NIIF_WARNING);
        }

        if (Settings.OpenEditor)
        {
            try
            {
                string status = (copied ? "클립보드에 복사했고, " : "") + (path != null ? "자동 저장했어요" : "저장하지 못했어요");
                var savedPath = path;
                OpenEditor(MakeDocument(image, [], savedPath, histId, fileName), status);
                return;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        if (path != null)
            Notify(copied ? "캡처 완료 · 클립보드에 복사됨" : "캡처 완료",
                $"{image.Width}×{image.Height}  ·  {Path.GetFileName(path)}\n클릭하면 폴더를 엽니다.", Win.NIIF_INFO);
        MemoryTrimmer.Trim();
    }

    /// <summary>편집기 문서: 바뀐 게 있으면 같은 파일에 덮어쓰고 클립보드·캡처 기록도 갱신한다.</summary>
    EditorDocument MakeDocument(PixelImage original, List<Annotation> items, string? savedPath, string? historyId, string? fileName = null)
    {
        string? path = savedPath;
        var savedOriginal = original;
        return new EditorDocument
        {
            Original = original,
            Items = items,
            HistoryId = historyId,
            SuggestedFileName = fileName ?? Path.GetFileName(savedPath),
            Save = (edited, newOriginal, finalItems) =>
            {
                try
                {
                    ClipboardService.SetImage(Handle, edited);
                    path = SaveImage(edited, path ?? Path.Combine(SaveFolder,
                        fileName ?? CaptureFileName.Create(null, DateTime.Now, "png")));
                    // 자르기로 원본이 바뀌었을 때만 원본 파일도 다시 쓴다
                    var changed = ReferenceEquals(newOriginal, savedOriginal) ? null : newOriginal;
                    savedOriginal = newOriginal;
                    if (historyId != null) HistoryStore.UpdateImage(historyId, edited, finalItems, path, changed);
                    _history?.Reload();
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                    Notify("편집본 저장 실패", ex.Message, Win.NIIF_WARNING);
                }
            },
        };
    }

    /// <summary>편집 창을 연다. 캡처 기록이 켜져 있으면 오른쪽에 기록 패널이 붙는다.</summary>
    void OpenEditor(EditorDocument doc, string status)
    {
        _editor?.Close();
        _editor = new EditorWindow(doc, status, () => { _editor = null; MemoryTrimmer.Trim(); },
            Settings.HistoryMax > 0 ? OpenEntryAsDocument : null, HandleEditorSetting);
    }

    /// <summary>기록 항목 → 편집기 문서 (원본 + 그려 둔 주석).</summary>
    EditorDocument? OpenEntryAsDocument(HistoryEntry e)
    {
        var original = HistoryStore.LoadOriginal(e.Id);
        return original == null ? null : MakeDocument(original, HistoryStore.LoadItems(e), e.FilePath, e.Id);
    }

    string SaveImage(PixelImage image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        PngEncoder.Save(image, path);
        _lastSavedPath = path;
        return path;
    }

    void ShowMenu()
    {
        _menuOpen = true;
        try { ShowMenuCore(); } finally { _menuOpen = false; }
    }

    void ShowMenuCore()
    {
        nint menu = User32.CreatePopupMenu();
        User32.AppendMenuW(menu, Win.MF_STRING | 0x1 /* MF_GRAYED */, 0, $"LunaCapture {Installer.Version}");
        User32.AppendMenuW(menu, Win.MF_SEPARATOR, 0, null);
        if (IsRecording)
        {
            User32.AppendMenuW(menu, Win.MF_STRING, CmdRecord, "녹화 정지\tPrtSc");
        }
        else
        {
            User32.AppendMenuW(menu, Win.MF_STRING, CmdRegion, "사진 캡처\tPrtSc");
            User32.AppendMenuW(menu, Win.MF_STRING, CmdRecord, "녹화 (GIF · 동영상)\tPrtSc → Tab");
        }
        User32.AppendMenuW(menu, Win.MF_STRING, CmdHistory, "캡처 기록\tShift+PrtSc");
        User32.AppendMenuW(menu, Win.MF_SEPARATOR, 0, null);
        User32.AppendMenuW(menu, Win.MF_STRING, CmdOpenImageFolder, "이미지·GIF 폴더 열기");
        User32.AppendMenuW(menu, Win.MF_STRING, CmdOpenVideoFolder, "동영상 폴더 열기");

        User32.AppendMenuW(menu, Win.MF_STRING, CmdCheckUpdate, "업데이트 확인");
        User32.AppendMenuW(menu, Win.MF_STRING, CmdUpdateHistory, "업데이트 내역");
        User32.AppendMenuW(menu, Win.MF_STRING, CmdFeedback, "이거 고쳐주세요! ✉");
        nint settings = User32.CreatePopupMenu();
        uint Check(bool on) => Win.MF_STRING | (on ? Win.MF_CHECKED : 0);
        User32.AppendMenuW(settings, Check(Settings.AutoUpdate), CmdAutoUpdate, "자동 업데이트 (작업 종료 후 적용)");
        User32.AppendMenuW(settings, Check(Settings.OpenEditor), CmdToggleEditor, "캡처 후 편집기 열기");
        User32.AppendMenuW(settings, Check(Settings.OpenGifTrim), CmdToggleTrim, "GIF 녹화 후 자르기 창 열기");
        User32.AppendMenuW(settings, Check(Settings.ShowCursor), CmdToggleCursor, "녹화할 때 마우스 커서 표시");
        nint historyMenu = User32.CreatePopupMenu();
        for (int i = 0; i < HistoryStore.MaxOptions.Length; i++)
        {
            int n = HistoryStore.MaxOptions[i];
            User32.AppendMenuW(historyMenu, Check(Settings.HistoryMax == n), CmdHistoryMax + (uint)i, n == 0 ? "기록 안 함" : $"최근 {n}개");
        }
        User32.AppendMenuW(settings, 0x10 /* MF_POPUP */, (nuint)historyMenu, "캡처 기록 보관");
        User32.AppendMenuW(settings, Win.MF_SEPARATOR, 0, null);
        User32.AppendMenuW(settings, Win.MF_STRING, CmdChangeImageFolder, "이미지·GIF 저장 폴더 바꾸기…\t" + ShortPath(SaveFolder));
        User32.AppendMenuW(settings, Win.MF_STRING, CmdChangeVideoFolder, "동영상 저장 폴더 바꾸기…\t" + ShortPath(VideoFolder));
        User32.AppendMenuW(settings, Win.MF_SEPARATOR, 0, null);
        User32.AppendMenuW(settings, Check(Settings.PrintScreenTakeover == true), CmdToggleTakeover, "PrtSc 키를 LunaCapture가 우선 사용");
        User32.AppendMenuW(settings, Check(StartupRegistration.IsEnabled), CmdToggleStartup, "Windows 시작 시 자동 실행");
        if (Installer.IsRunningInstalled)
        {
            User32.AppendMenuW(settings, Win.MF_SEPARATOR, 0, null);
            User32.AppendMenuW(settings, Win.MF_STRING, CmdUninstall, "LunaCapture 제거…");
        }
        User32.AppendMenuW(menu, 0x10 /* MF_POPUP */, (nuint)settings, "설정");

        User32.AppendMenuW(menu, Win.MF_SEPARATOR, 0, null);
        User32.AppendMenuW(menu, Win.MF_STRING, CmdExit, "종료");

        POINT pt;
        User32.GetCursorPos(&pt);
        User32.SetForegroundWindow(Handle); // 없으면 메뉴 밖 클릭 시 메뉴가 안 닫힘
        uint cmd = (uint)User32.TrackPopupMenu(menu, Win.TPM_RIGHTBUTTON | Win.TPM_RETURNCMD | Win.TPM_NONOTIFY,
            pt.X, pt.Y, 0, Handle, 0);
        User32.PostMessageW(Handle, Win.WM_NULL, 0, 0);
        User32.DestroyMenu(menu);

        switch (cmd)
        {
            case CmdCheckUpdate: BeginUpdateCheck(true); break;
            case CmdUpdateHistory: ShowUpdateNotes(); break;
            case CmdFeedback: OpenFeedback(); break;
            case CmdAutoUpdate:
                ToggleAutoUpdate();
                break;
            case CmdRegion: StartCapture(CaptureMode.Image); break;
            case CmdRecord: if (IsRecording) _recBar?.Stop(); else StartCapture(CaptureMode.Record); break;
            case CmdOpenImageFolder: OpenFolder(SaveFolder); break;
            case CmdOpenVideoFolder: OpenFolder(VideoFolder); break;
            case CmdToggleEditor: Settings.OpenEditor = !Settings.OpenEditor; AppSettings.Save(); break;
            case CmdToggleTrim: Settings.OpenGifTrim = !Settings.OpenGifTrim; AppSettings.Save(); break;
            case CmdToggleCursor: Settings.ShowCursor = !Settings.ShowCursor; AppSettings.Save(); break;
            case CmdChangeImageFolder:
                if (FolderPicker.Pick(Handle, "이미지·GIF를 저장할 폴더", SaveFolder) is { } img) { Settings.ImageFolder = img; AppSettings.Save(); }
                break;
            case CmdChangeVideoFolder:
                if (FolderPicker.Pick(Handle, "동영상을 저장할 폴더", VideoFolder) is { } vid) { Settings.VideoFolder = vid; AppSettings.Save(); }
                break;
            case CmdHistory: OpenHistory(); break;
            case >= CmdHistoryMax and < CmdHistoryMax + 4:
                Settings.HistoryMax = HistoryStore.MaxOptions[cmd - CmdHistoryMax];
                AppSettings.Save();
                HistoryStore.TrimToMax();
                _history?.Reload();
                break;
            case CmdUninstall:
                if (User32.MessageBoxW(Handle, "LunaCapture를 제거할까요?\n\n찍어 둔 캡처 파일과 설정은 지워지지 않아요.", "LunaCapture 제거", 0x4 | 0x20) == 6)
                {
                    Installer.Uninstall();
                    Destroy(); // 앱 종료 → 잠시 뒤 설치 폴더 삭제
                }
                break;
            case CmdToggleTakeover:
                SetTakeover(Settings.PrintScreenTakeover != true);
                break;
            case CmdToggleStartup:
                try { StartupRegistration.Set(!StartupRegistration.IsEnabled); }
                catch (Exception ex) { Log.Error(ex); Notify("자동 실행 설정 실패", ex.Message, Win.NIIF_WARNING); }
                break;
            case CmdExit: Destroy(); break;
        }
        User32.DestroyMenu(settings);
    }

    static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Shell32.ShellExecuteW(0, "open", folder, null, null, Win.SW_SHOW);
    }

    /// <summary>메뉴에 보여 줄 짧은 경로: 사용자 폴더는 ~ 로, 너무 길면 앞을 줄인다.</summary>
    static string ShortPath(string path)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith(home, StringComparison.OrdinalIgnoreCase)) path = "~" + path[home.Length..];
        return path.Length > 40 ? "…" + path[^39..] : path;
    }

    static void OpenInExplorer(string file) =>
        Shell32.ShellExecuteW(0, "open", "explorer.exe", $"/select,\"{file}\"", null, Win.SW_SHOW);

    void AddTrayIcon()
    {
        var nid = NewIconData();
        nid.uFlags = Win.NIF_MESSAGE | Win.NIF_ICON | Win.NIF_TIP | Win.NIF_SHOWTIP;
        nid.uCallbackMessage = TrayCallback;
        nid.hIcon = _icon;
        Copy($"LunaCapture {Installer.Version}  (PrtSc)", nid.szTip, 128);
        Shell32.Shell_NotifyIconW(Win.NIM_ADD, &nid);

        nid.uVersion = Win.NOTIFYICON_VERSION_4;
        Shell32.Shell_NotifyIconW(Win.NIM_SETVERSION, &nid);
    }

    void RemoveTrayIcon()
    {
        var nid = NewIconData();
        Shell32.Shell_NotifyIconW(Win.NIM_DELETE, &nid);
    }

    Action? _balloonClick;

    /// <summary>트레이 알림. onClick을 주면 알림을 클릭했을 때 실행(없으면 마지막 저장 파일 위치를 연다).</summary>
    void Notify(string title, string text, uint kind, Action? onClick = null)
    {
        _balloonClick = onClick;
        var nid = NewIconData();
        nid.uFlags = Win.NIF_INFO;
        nid.dwInfoFlags = kind;
        Copy(title, nid.szInfoTitle, 64);
        Copy(text, nid.szInfo, 256);
        Shell32.Shell_NotifyIconW(Win.NIM_MODIFY, &nid);
    }

    NOTIFYICONDATAW NewIconData() => new()
    {
        cbSize = (uint)sizeof(NOTIFYICONDATAW),
        hWnd = Handle,
        uID = 1,
    };

    static void Copy(string s, char* dst, int capacity)
    {
        int n = Math.Min(s.Length, capacity - 1);
        for (int i = 0; i < n; i++) dst[i] = s[i];
        dst[n] = '\0';
    }
}

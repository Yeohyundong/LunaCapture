using System.Diagnostics;
using System.Runtime.InteropServices;
using LunaCapture.Interop;
using Microsoft.Win32;

namespace LunaCapture.Services;

/// <summary>
/// 설치 · 업데이트 · 제거. 별도 설치 프로그램 없이 exe 자신이 처리한다 (관리자 권한 불필요, 현재 사용자에게만).
/// - 설치 위치: %LOCALAPPDATA%\Programs\LunaCapture (윈도우가 개인용 프로그램에 권장하는 곳)
/// - 시작 메뉴 바로가기, "설치된 앱" 목록 등록(여기서 제거 가능), 원하면 Windows 시작 시 자동 실행
/// </summary>
internal static unsafe partial class Installer
{
    public const string AppName = "LunaCapture";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\LunaCapture";

    public static string InstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);

    public static string InstalledExe => Path.Combine(InstallDir, "LunaCapture.exe");

    static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "LunaCapture.lnk");

    static string CurrentExe => Environment.ProcessPath ?? "";

    /// <summary>이 exe의 버전 (예: "1.0.0").</summary>
    public static string Version
    {
        get
        {
            var v = typeof(Installer).Assembly.GetName().Version ?? new Version(1, 0, 0);
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static bool IsRunningInstalled =>
        string.Equals(Path.GetFullPath(CurrentExe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    public static bool IsInstalled => File.Exists(InstalledExe);

    /// <summary>설치된 exe의 버전 (설치 안 됐으면 null).</summary>
    public static Version? InstalledVersion
    {
        get
        {
            if (!IsInstalled) return null;
            try
            {
                var info = FileVersionInfo.GetVersionInfo(InstalledExe);
                return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
            }
            catch { return null; }
        }
    }

    static System.Version CurrentVersion => System.Version.Parse(Version);

    /// <summary>개발 중 빌드 폴더에서 실행할 때는 설치를 권하지 않는다.</summary>
    static bool IsDevBuild
    {
        get
        {
            string p = CurrentExe.Replace('/', '\\');
            return p.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase)
                || p.Contains(@"\build\publish\", StringComparison.OrdinalIgnoreCase)
                || p.Contains(@"\LunaCapture\dist\", StringComparison.OrdinalIgnoreCase);
        }
    }

    public enum Offer { None, Install, Update }

    /// <summary>지금 설치(또는 업데이트)를 권할지.</summary>
    public static Offer ShouldOffer()
    {
        if (IsRunningInstalled || IsDevBuild) return Offer.None;
        var installed = InstalledVersion;
        if (installed == null)
            return AppSettings.Current.InstallDeclinedVersion == Version ? Offer.None : Offer.Install;
        return installed < CurrentVersion ? Offer.Update : Offer.None;
    }

    /// <summary>설치(또는 업데이트). 실행 중인 다른 LunaCapture가 있으면 먼저 종료시킨다.</summary>
    public static void Install(bool autoStart)
    {
        CloseRunningInstance();
        Directory.CreateDirectory(InstallDir);
        File.Copy(CurrentExe, InstalledExe, overwrite: true);

        // 같은 폴더의 글꼴 라이선스 안내도 함께 (OFL)
        string srcDir = Path.GetDirectoryName(CurrentExe)!;
        foreach (var txt in new[] { "Pretendard-LICENSE.txt", "LunaSans-NOTICE.txt" })
        {
            string src = Path.Combine(srcDir, txt);
            if (File.Exists(src)) File.Copy(src, Path.Combine(InstallDir, txt), overwrite: true);
        }

        CreateShortcut(ShortcutPath, InstalledExe, "가벼운 화면 캡처 · GIF · 동영상 녹화");
        RegisterUninstall();
        if (autoStart) StartupRegistration.Set(true, InstalledExe);
        else if (StartupRegistration.IsRegistered) StartupRegistration.Set(true, InstalledExe); // 이미 켜져 있었으면 새 위치로
        Log.Info($"설치 완료 {Version} → {InstallDir}");
    }

    /// <summary>설치된 exe를 실행한다 (설치 직후).</summary>
    public static void LaunchInstalled() =>
        Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true, WorkingDirectory = InstallDir });

    /// <summary>
    /// 제거: 자동 실행·바로가기·"설치된 앱" 등록을 지우고, 앱이 끝난 뒤 설치 폴더를 지운다.
    /// 찍어 둔 캡처 파일과 설정은 남긴다.
    /// </summary>
    public static void Uninstall()
    {
        StartupRegistration.Set(false);
        try { File.Delete(ShortcutPath); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); } catch { }

        // 실행 중인 exe는 스스로 지울 수 없으므로, 잠시 뒤 지우는 작업을 맡기고 종료한다.
        string dir = InstallDir;
        var psi = new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 > nul & rd /s /q \"{dir}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        Process.Start(psi);
        Log.Info("제거 완료");
    }

    /// <summary>실행 중인 LunaCapture(트레이)에 종료를 요청하고 끝날 때까지 잠시 기다린다.</summary>
    public static void CloseRunningInstance()
    {
        nint wnd = FindWindowW("LunaCaptureWnd", "LunaCapture");
        if (wnd == 0) return;
        uint pid;
        User32.GetWindowThreadProcessId(wnd, &pid);
        User32.PostMessageW(wnd, Win.WM_CLOSE, 0, 0);
        try
        {
            using var p = Process.GetProcessById((int)pid);
            if (p.Id != Environment.ProcessId && !p.WaitForExit(5000)) p.Kill();
        }
        catch { }
    }

    static void RegisterUninstall()
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", AppName);
        key.SetValue("DisplayIcon", InstalledExe);
        key.SetValue("InstallLocation", InstallDir);
        key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
    }

    /// <summary>개발용: 바로가기 만들기만 임시 위치에서 시험한다 (`--test-shortcut 경로.lnk`).</summary>
    public static void TestShortcut(string lnkPath) => CreateShortcut(lnkPath, CurrentExe, "LunaCapture 바로가기 시험");

    // ── 시작 메뉴 바로가기 (.lnk): IShellLink + IPersistFile, vtable 슬롯으로 직접 호출 ──

    static void CreateShortcut(string lnkPath, string target, string description)
    {
        Guid clsid = new("00021401-0000-0000-c000-000000000046");   // CLSID_ShellLink
        Guid iidLink = new("000214f9-0000-0000-c000-000000000046"); // IID_IShellLinkW
        Guid iidFile = new("0000010b-0000-0000-c000-000000000046"); // IID_IPersistFile

        CoInitializeEx(0, 2);
        nint link = 0, file = 0;
        try
        {
            if (CoCreateInstance(&clsid, 0, 1, &iidLink, &link) < 0) return;
            fixed (char* t = target)
            fixed (char* d = description)
            fixed (char* wd = Path.GetDirectoryName(target))
            {
                ((delegate* unmanaged<nint, char*, int>)Slot(link, 20))(link, t);   // SetPath
                ((delegate* unmanaged<nint, char*, int>)Slot(link, 7))(link, d);    // SetDescription
                ((delegate* unmanaged<nint, char*, int>)Slot(link, 9))(link, wd);   // SetWorkingDirectory
                ((delegate* unmanaged<nint, char*, int, int>)Slot(link, 17))(link, t, 0); // SetIconLocation
            }
            if (((delegate* unmanaged<nint, Guid*, nint*, int>)Slot(link, 0))(link, &iidFile, &file) < 0) return;
            Directory.CreateDirectory(Path.GetDirectoryName(lnkPath)!);
            fixed (char* path = lnkPath)
                ((delegate* unmanaged<nint, char*, int, int>)Slot(file, 6))(file, path, 1); // IPersistFile.Save
        }
        finally
        {
            if (file != 0) ((delegate* unmanaged<nint, uint>)Slot(file, 2))(file);
            if (link != 0) ((delegate* unmanaged<nint, uint>)Slot(link, 2))(link);
        }
    }

    static void* Slot(nint obj, int i) => (*(void***)obj)[i];

    [LibraryImport("ole32.dll")] private static partial int CoInitializeEx(nint reserved, uint coinit);
    [LibraryImport("ole32.dll")] private static partial int CoCreateInstance(Guid* clsid, nint outer, uint ctx, Guid* iid, nint* obj);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowW(string className, string windowName);
}

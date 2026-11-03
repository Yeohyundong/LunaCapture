using System.Diagnostics;
using System.Security.Cryptography;

namespace LunaCapture.Services;

/// <summary>Runs from a private staging directory. Never kills the application or touches captures/settings.</summary>
internal static class UpdateInstaller
{
    internal static bool IsStageDirectory(string directory)
    {
        string full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(UpdateService.CacheRoot).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0;
    }

    public static void Apply(string[] args)
    {
        try
        {
            if (args.Length != 6 || !IsStageDirectory(args[1])) throw new InvalidDataException("잘못된 업데이트 경로");
            string dir = Path.GetFullPath(args[1]), source = Path.Combine(dir, "LunaCapture.exe");
            if (!string.Equals(Environment.ProcessPath, Path.Combine(dir, "UpdateHelper.exe"), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("도우미 실행 위치 오류");
            if (!Version.TryParse(args[5], out var version) || version <= Version.Parse(Installer.Version)) throw new InvalidDataException("업데이트 버전 오류");
            var info = FileVersionInfo.GetVersionInfo(source);
            if (new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart) != version || !UpdateService.HashMatches(File.ReadAllBytes(source), args[4])) throw new InvalidDataException("업데이트 실행 파일 검증 실패");
            using var singleHelper = new Mutex(true, "LunaCapture.UpdateInstaller", out bool acquired);
            if (!acquired) return;
            using var parent = Process.GetProcessById(int.Parse(args[2]));
            if (parent.StartTime.ToUniversalTime().Ticks != long.Parse(args[3]) || !string.Equals(parent.MainModule?.FileName, Installer.InstalledExe, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("업데이트 대상 프로세스 오류");
            File.WriteAllText(Path.Combine(dir, "version"), args[5]);
            File.WriteAllText(Path.Combine(dir, "ready"), "ready");
            // The tray process exits voluntarily only after all editing/recording windows are closed.
            var wait = Stopwatch.StartNew();
            while (!parent.WaitForExit(1000))
                if (File.Exists(Path.Combine(dir, "cancel")) || wait.Elapsed > TimeSpan.FromDays(1)) return;
            if (File.Exists(Path.Combine(dir, "cancel"))) return;

            string target = Installer.InstalledExe, next = target + ".next", backup = target + ".previous";
            bool replaced = false;
            try
            {
                File.Copy(source, next, true);
                if (!UpdateService.HashMatches(File.ReadAllBytes(next), args[4])) throw new InvalidDataException("복사된 실행 파일 검증 실패");
                ReplaceExecutable(next, target, backup);
                replaced = true;
                foreach (string name in new[] { "Pretendard-LICENSE.txt", "LunaSans-NOTICE.txt" }) File.Copy(Path.Combine(dir, name), Path.Combine(Installer.InstallDir, name), true);
                var start = new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Installer.InstallDir };
                start.ArgumentList.Add("--update-health"); start.ArgumentList.Add(dir);
                using var restarted = Process.Start(start) ?? throw new IOException("새 버전 실행 실패");
                var timeout = Stopwatch.StartNew();
                while (timeout.Elapsed < TimeSpan.FromSeconds(30))
                {
                    if (File.Exists(Path.Combine(dir, "healthy"))) { Log.Info("자동 업데이트 완료 " + args[5]); return; }
                    if (restarted.HasExited) throw new IOException("새 버전이 시작 중 종료됐습니다");
                    Thread.Sleep(250);
                }
                // A running application is never killed, even when startup acknowledgement times out.
                Log.Info("업데이트 시작 확인 지연. 이전 실행 파일을 .previous에 보관합니다");
            }
            catch
            {
                if (replaced) File.Copy(backup, target, true);
                Process.Start(new ProcessStartInfo(target, "--skip-update-once") { UseShellExecute = false, WorkingDirectory = Installer.InstallDir });
                throw;
            }
        }
        catch (Exception ex) { Log.Error(ex); }
    }

    internal static void ReplaceExecutable(string next, string target, string backup) => File.Replace(next, target, backup, true);

    public static void SignalHealthy(string[] args)
    {
        if (args.Length != 2 || args[0] != "--update-health" || !Installer.IsRunningInstalled) return;
        try
        {
            if (IsStageDirectory(args[1]) && File.ReadAllText(Path.Combine(args[1], "version")) == Installer.Version)
                File.WriteAllText(Path.Combine(args[1], "healthy"), Installer.Version);
        }
        catch (Exception ex) { Log.Error(ex); }
    }
}

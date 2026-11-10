using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LunaCapture.Services;

internal static class UpdateSelfTest
{
    public static void Run(string output)
    {
        string dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "update-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        void Reject(Action action, string message)
        {
            try { action(); } catch (InvalidDataException) { return; }
            throw new Exception(message);
        }
        string valid = """
        {"tag_name":"v9.0.0","draft":false,"prerelease":false,"assets":[{"name":"LunaCapture-9.0.0.zip","size":100,"browser_download_url":"https://github.com/REPOSITORY/releases/download/v9.0.0/LunaCapture-9.0.0.zip","digest":"sha256:HASH"}]}
        """.Replace("REPOSITORY", ReleaseChannel.Repository).Replace("HASH", new string('a', 64));
        (string Version, string Url, string Hash)? Parse(string json, Version? current = null)
        {
            using var doc = JsonDocument.Parse(json);
            return UpdateService.ParseRelease(doc.RootElement, current ?? new Version(1, 2, 0));
        }
        Check(Parse(valid)?.Version == "9.0.0", "stable release parsing");
        Check(Parse(valid, new Version(9, 0, 0)) == null, "same version must not update");
        Check(Parse(valid, new Version(10, 0, 0)) == null, "no downgrade");
        Check(Parse(valid.Replace("\"prerelease\":false", "\"prerelease\":true")) == null, "prerelease excluded");
        Check(Parse(valid.Replace("\"draft\":false", "\"draft\":true")) == null, "draft excluded");
        Reject(() => Parse(valid.Replace("https://github.com/", "http://github.com/")), "HTTP asset accepted");
        Reject(() => Parse(valid.Replace("https://github.com/", "https://attacker.invalid/")), "foreign host accepted");
        Reject(() => Parse(valid.Replace("sha256:", "sha512:")), "unknown digest accepted");
        Reject(() => Parse(valid.Replace("\"size\":100", "\"size\":999999999")), "oversize asset accepted");
        byte[] bytes = Encoding.UTF8.GetBytes("test payload");
        Check(UpdateService.HashMatches(bytes, Convert.ToHexString(SHA256.HashData(bytes))), "valid digest");
        Check(!UpdateService.HashMatches(bytes, new string('0', 64)), "corrupt payload");

        byte[] MakeZip(bool includeExe)
        {
            using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            {
                foreach (string name in new[] { includeExe ? "LunaCapture.exe" : "../LunaCapture.exe", "Pretendard-LICENSE.txt", "LunaSans-NOTICE.txt", "../escape.txt" })
                {
                    using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write("test");
                }
            }
            return memory.ToArray();
        }
        string stage = Path.Combine(dir, "stage"); Directory.CreateDirectory(stage);
        Check(File.Exists(UpdateService.StageArchive(MakeZip(true), stage)), "staging required files");
        Check(!File.Exists(Path.Combine(dir, "escape.txt")) && Directory.GetFiles(stage).Length == 3, "ZIP traversal was extracted");
        string bad = Path.Combine(dir, "bad"); Directory.CreateDirectory(bad);
        Reject(() => UpdateService.StageArchive(MakeZip(false), bad), "ZIP without root executable accepted");
        Check(!UpdateService.CanApply(true, false, false, false, false), "capture must block update");
        Check(!UpdateService.CanApply(false, true, false, false, false), "recording must block update");
        Check(!UpdateService.CanApply(false, false, true, false, false), "setup must block update");
        Check(!UpdateService.CanApply(false, false, false, true, false), "editing/GIF trim must block update");
        Check(!UpdateService.CanApply(false, false, false, false, true), "menu/modal must block update");
        Check(UpdateService.CanApply(false, false, false, false, false), "idle update should proceed");
        string target = Path.Combine(dir, "target.exe"), next = Path.Combine(dir, "next.exe"), backup = Path.Combine(dir, "backup.exe");
        File.WriteAllText(target, "old"); File.WriteAllText(next, "new");
        UpdateInstaller.ReplaceExecutable(next, target, backup);
        Check(File.ReadAllText(target) == "new" && File.ReadAllText(backup) == "old", "atomic replacement/backup");
        File.Copy(backup, target, true);
        Check(File.ReadAllText(target) == "old", "rollback copy");
        File.WriteAllText(next, "second");
        UpdateInstaller.ReplaceExecutable(next, target, backup);
        Check(File.ReadAllText(target) == "second" && File.ReadAllText(backup) == "old", "subsequent replacement with an existing backup");
        Check(!UpdateInstaller.IsStageDirectory(dir), "arbitrary helper directory accepted");
        string skippedVersions = ReleaseNotes.Between(new Version(1, 2, 2), new Version(1, 2, 4));
        Check(skippedVersions.Contains("1.2.3") && skippedVersions.Contains("1.2.4"), "skipped versions must all be included");
        Check(!skippedVersions.Contains("1.2.2") && !skippedVersions.Contains("1.2.5"), "exclude current and future versions");
        Check(skippedVersions.IndexOf("1.2.4", StringComparison.Ordinal) < skippedVersions.IndexOf("1.2.3", StringComparison.Ordinal), "newest notes first");
        Check(ReleaseNotes.Between(new Version(1, 2, 4), new Version(1, 2, 4)) == "", "no notes for unchanged version");
        Check(ReleaseNotes.History(new Version(1, 2, 4)).Contains("1.0.0"), "past version history");
        string notesJson = """
        [{"version":"9.0.0","date":"2026-11-10","changes":["future update"]},
        {"version":"8.0.0","date":"2026-11-09","changes":["intermediate update"]}]
        """;
        using var notesZip = new MemoryStream();
        using (var archive = new ZipArchive(notesZip, ZipArchiveMode.Create, true))
        {
            foreach (string name in new[] { "LunaCapture.exe", "Pretendard-LICENSE.txt", "LunaSans-NOTICE.txt", "CHANGELOG.json" })
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(name == "CHANGELOG.json" ? notesJson : "test");
            }
        }
        string notesStage = Path.Combine(dir, "notes");
        Directory.CreateDirectory(notesStage);
        UpdateService.StageArchive(notesZip.ToArray(), notesStage);
        string downloadedNotes = ReleaseNotes.ForUpdate(new Version(7, 0, 0), new Version(9, 0, 0), notesStage);
        Check(downloadedNotes.Contains("future update") && downloadedNotes.Contains("intermediate update"), "load future notes from verified ZIP");
        File.WriteAllText(Path.Combine(notesStage, "CHANGELOG.json"), "invalid");
        Check(ReleaseNotes.ForUpdate(new Version(1, 2, 2), new Version(1, 2, 4), notesStage).Contains("1.2.3"), "bad notes fall back without blocking updates");
        Reject(() => ReleaseNotes.Parse(notesJson.Replace("8.0.0", "9.0.0")), "duplicate notes versions accepted");
        Reject(() => ReleaseNotes.Parse(notesJson.Replace("future update", "")), "empty notes accepted");
        File.WriteAllText(Path.Combine(dir, "release-notes-example.txt"), skippedVersions);
        File.WriteAllText(output, "업데이트 검증 통과: 버전 비교·초안/프리릴리스 제외·URL/크기/SHA-256·안전한 ZIP 추출·작업 중 적용 차단·원자적 교체/백업/복구·건너뛴 버전 누적 내역·검증된 ZIP의 새 변경 내역·구버전 ZIP 호환\n");
    }
}

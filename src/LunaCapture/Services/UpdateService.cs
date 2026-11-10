using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace LunaCapture.Services;

internal sealed record PendingUpdate(string Directory, string Exe, string Version, string Sha256, string Notes = "");
internal sealed record UpdateResult(PendingUpdate? Update, string Message, bool Failed = false);

/// <summary>Only downloads/stages files. UI thread separately decides when it is safe to restart.</summary>
internal static class UpdateService
{
    internal const long MaxDownload = 128 * 1024 * 1024;
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    public static string CacheRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunaCapture", "Updates");

    public static Task<UpdateResult> CheckAsync() => CheckAsync(Version.Parse(Installer.Version));

    internal static async Task<UpdateResult> CheckAsync(Version current, string? testCache = null)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseChannel.LatestApi);
            request.Headers.UserAgent.ParseAdd("LunaCapture/" + Installer.Version);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return new(null, "아직 공개된 업데이트가 없어요");
            response.EnsureSuccessStatusCode();
            byte[] json = await ReadLimitedAsync(await response.Content.ReadAsStreamAsync().ConfigureAwait(false), 2 * 1024 * 1024).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var release = ParseRelease(doc.RootElement, current);
            if (release == null) return new(null, "최신 버전이에요");

            using var assetRequest = new HttpRequestMessage(HttpMethod.Get, release.Value.Url);
            assetRequest.Headers.UserAgent.ParseAdd("LunaCapture/" + Installer.Version);
            using var asset = await Http.SendAsync(assetRequest, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            asset.EnsureSuccessStatusCode();
            if (asset.Content.Headers.ContentLength > MaxDownload) throw new InvalidDataException("업데이트 파일이 너무 큽니다");
            byte[] zip = await ReadLimitedAsync(await asset.Content.ReadAsStreamAsync().ConfigureAwait(false), MaxDownload).ConfigureAwait(false);
            if (!HashMatches(zip, release.Value.Hash)) throw new InvalidDataException("업데이트 SHA-256 검증 실패");
            string dir = Path.Combine(testCache ?? CacheRoot, release.Value.Version + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string exe = StageArchive(zip, dir);
            var version = FileVersionInfo.GetVersionInfo(exe);
            if (new Version(version.FileMajorPart, version.FileMinorPart, version.FileBuildPart) != Version.Parse(release.Value.Version))
                throw new InvalidDataException("업데이트 파일 버전이 Release 버전과 다릅니다");
            string hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(exe).ConfigureAwait(false)));
            string notes = ReleaseNotes.ForUpdate(current, Version.Parse(release.Value.Version), dir);
            return new(new PendingUpdate(dir, exe, release.Value.Version, hash, notes), "새 버전을 준비했어요. 작업이 끝나면 자동으로 적용돼요");
        }
        catch (Exception ex) { Log.Error(ex); return new(null, "업데이트를 확인하지 못했어요. 기존 버전은 계속 사용할 수 있어요", true); }
    }

    internal static (string Version, string Url, string Hash)? ParseRelease(JsonElement root, Version current)
    {
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v'), out var latest) || latest.Build < 0 || latest.Revision > 0 || latest <= current) return null;
        string version = latest.ToString(3), expectedName = $"LunaCapture-{version}.zip";
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != expectedName) continue;
            long size = asset.GetProperty("size").GetInt64();
            string url = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (size <= 0 || size > MaxDownload || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com"
                || !uri.AbsolutePath.StartsWith("/" + ReleaseChannel.Repository + "/releases/download/", StringComparison.Ordinal)) throw new InvalidDataException("허용되지 않은 Release 파일");
            string digest = asset.GetProperty("digest").GetString() ?? "";
            if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit)) throw new InvalidDataException("SHA-256 정보가 없습니다");
            return (version, url, digest[7..]);
        }
        throw new InvalidDataException("업데이트 ZIP을 찾지 못했습니다");
    }

    internal static bool HashMatches(byte[] data, string expected) => string.Equals(Convert.ToHexString(SHA256.HashData(data)), expected, StringComparison.OrdinalIgnoreCase);

    internal static string StageArchive(byte[] data, string dir)
    {
        using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
        foreach (string name in new[] { "LunaCapture.exe", "Pretendard-LICENSE.txt", "LunaSans-NOTICE.txt" })
        {
            var entries = zip.Entries.Where(e => e.FullName == name).ToArray();
            if (entries.Length != 1 || entries[0].Length <= 0 || entries[0].Length > (name.EndsWith(".exe") ? MaxDownload : 1024 * 1024))
                throw new InvalidDataException("업데이트 구성 파일이 잘못되었습니다: " + name);
            using var input = entries[0].Open();
            using var output = new FileStream(Path.Combine(dir, name), FileMode.CreateNew, FileAccess.Write);
            CopyLimited(input, output, name.EndsWith(".exe") ? MaxDownload : 1024 * 1024);
        }
        var changelogs = zip.Entries.Where(e => e.FullName == "CHANGELOG.json").ToArray();
        if (changelogs.Length > 1 || (changelogs.Length == 1 && changelogs[0].Length > ReleaseNotes.MaxBytes))
            throw new InvalidDataException("변경 이력 파일이 잘못되었습니다.");
        if (changelogs.Length == 1)
        {
            using var input = changelogs[0].Open();
            using var output = new FileStream(Path.Combine(dir, "CHANGELOG.json"), FileMode.CreateNew, FileAccess.Write);
            CopyLimited(input, output, ReleaseNotes.MaxBytes);
        }
        return Path.Combine(dir, "LunaCapture.exe");
    }

    static void CopyLimited(Stream input, Stream output, long limit)
    {
        byte[] buffer = new byte[65536]; long total = 0; int read;
        while ((read = input.Read(buffer)) != 0) { total += read; if (total > limit) throw new InvalidDataException("파일 크기 제한 초과"); output.Write(buffer, 0, read); }
    }
    static async Task<byte[]> ReadLimitedAsync(Stream stream, long limit)
    {
        using (stream)
        using (var result = new MemoryStream())
        {
            byte[] buffer = new byte[65536]; int read;
            while ((read = await stream.ReadAsync(buffer).ConfigureAwait(false)) != 0)
            {
                if (result.Length + read > limit) throw new InvalidDataException("다운로드 크기 제한 초과");
                result.Write(buffer, 0, read);
            }
            return result.ToArray();
        }
    }

    public static bool CanApply(bool capturing, bool recording, bool setupOpen, bool otherWindow, bool menuOpen) =>
        !capturing && !recording && !setupOpen && !otherWindow && !menuOpen;

    public static (Process Helper, string Ready) StartHelper(PendingUpdate update)
    {
        string helper = Path.Combine(update.Directory, "UpdateHelper.exe"), ready = Path.Combine(update.Directory, "ready");
        foreach (string name in new[] { "ready", "cancel", "healthy" }) File.Delete(Path.Combine(update.Directory, name));
        File.Copy(Environment.ProcessPath!, helper, true);
        var psi = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (string value in new[] { "--apply-update", update.Directory, Environment.ProcessId.ToString(), Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString(), update.Sha256, update.Version }) psi.ArgumentList.Add(value);
        return (Process.Start(psi) ?? throw new IOException("업데이트 도우미 실행 실패"), ready);
    }
}

using System.Reflection;
using System.Text;
using System.Text.Json;

namespace LunaCapture.Services;

internal sealed record ReleaseNote(Version Version, string Date, string[] Changes);

internal static class ReleaseNotes
{
    internal const int MaxBytes = 2 * 1024 * 1024;
    static readonly ReleaseNote[] Bundled = ReadBundled();

    static ReleaseNote[] ReadBundled()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CHANGELOG.json")
            ?? throw new InvalidDataException("변경 이력이 없습니다.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    internal static ReleaseNote[] Parse(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new InvalidDataException("변경 이력이 너무 큽니다.");
        using var doc = JsonDocument.Parse(json);
        var entries = new List<ReleaseNote>();
        var versions = new HashSet<Version>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (entries.Count >= 1000 ||
                !Version.TryParse(item.GetProperty("version").GetString(), out var version) ||
                version.Build < 0 || version.Revision > 0 || !versions.Add(version))
                throw new InvalidDataException("변경 이력 버전이 잘못되었습니다.");
            string date = item.GetProperty("date").GetString() ?? "";
            string[] changes = item.GetProperty("changes").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
            if (changes.Length == 0 || changes.Length > 100 || changes.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 4000))
                throw new InvalidDataException("변경 이력 내용이 잘못되었습니다.");
            entries.Add(new(version, date, changes));
        }
        return entries.ToArray();
    }

    internal static ReleaseNote[] Select(IEnumerable<ReleaseNote> entries, Version? current, Version target) =>
        entries.Where(x => (current == null || x.Version > current) && x.Version <= target)
            .OrderByDescending(x => x.Version).ToArray();

    internal static string Format(IEnumerable<ReleaseNote> entries)
    {
        var text = new StringBuilder();
        foreach (var entry in entries)
        {
            text.Append(entry.Version.ToString(3)).Append("  ·  ").AppendLine(entry.Date);
            foreach (string change in entry.Changes) text.Append("• ").AppendLine(change);
            text.AppendLine();
        }
        return text.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    public static string History(Version current) => Format(Select(Bundled, null, current));
    public static string Between(Version current, Version target) => Format(Select(Bundled, current, target));

    internal static string ForUpdate(Version current, Version target, string directory)
    {
        IEnumerable<ReleaseNote> entries = Bundled;
        string path = Path.Combine(directory, "CHANGELOG.json");
        if (File.Exists(path))
        {
            try
            {
                if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException("변경 이력이 너무 큽니다.");
                var downloaded = Parse(File.ReadAllText(path));
                entries = downloaded.Concat(Bundled).GroupBy(x => x.Version).Select(x => x.First()).ToArray();
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException)
            {
                Log.Error(ex);
            }
        }
        var selected = Select(entries, current, target);
        string text = Format(selected);
        if (!selected.Any(x => x.Version == target))
            text = target.ToString(3) + "\r\n변경 내역 불러오기 실패\r\n\r\n" + text;
        return text;
    }
}

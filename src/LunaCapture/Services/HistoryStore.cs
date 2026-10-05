using System.Text.Json;
using System.Text.Json.Serialization;
using LunaCapture.Editor;
using LunaCapture.Graphics;

namespace LunaCapture.Services;

/// <summary>기록 한 건 (파일에 저장되는 모양).</summary>
internal sealed class HistoryEntry
{
    public string Id { get; set; } = "";
    /// <summary>"image" / "gif" / "mp4"</summary>
    public string Kind { get; set; } = "image";
    /// <summary>저장된 결과 파일 (사진은 편집본 PNG, 녹화는 GIF/MP4)</summary>
    public string? FilePath { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTime Created { get; set; }
    /// <summary>사진: 원본 위에 그린 주석들 (다시 편집할 때 되살린다)</summary>
    public List<AnnotationDto> Items { get; set; } = [];
}

[JsonSerializable(typeof(HistoryEntry))]
internal sealed partial class HistoryJson : JsonSerializerContext;

/// <summary>
/// 캡처 기록. 메모리에 들고 있지 않고 디스크(%LOCALAPPDATA%\LunaCapture\History)에만 둔다.
/// 기록 하나 = {id}.json (정보 + 주석) + {id}.thumb.png (미리보기) + 사진이면 {id}.orig.png (편집 전 원본).
/// 최대 개수를 넘으면 가장 오래된 것부터 지운다(밀어내기).
/// </summary>
internal static class HistoryStore
{
    public const int ThumbSide = 240;
    public static readonly int[] MaxOptions = [0, 50, 100, 200]; // 0 = 기록 끔

    public static string Dir =>
        Environment.GetEnvironmentVariable("LUNA_HISTORY_DIR") is { Length: > 0 } test // 개발용 시험: 임시 폴더
            ? test
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunaCapture", "History");

    static int Max => AppSettings.Current.HistoryMax;
    static string Json(string id) => Path.Combine(Dir, id + ".json");
    static string Orig(string id) => Path.Combine(Dir, id + ".orig.png");
    public static string Thumb(string id) => Path.Combine(Dir, id + ".thumb.png");

    static string NewId() => DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");

    /// <summary>사진 캡처 직후: 원본·미리보기 저장. 기록이 꺼져 있으면 null.</summary>
    public static string? AddImage(PixelImage original, string? savedPath)
    {
        if (Max <= 0) return null;
        try
        {
            Directory.CreateDirectory(Dir);
            string id = NewId();
            PngEncoder.Save(original, Orig(id));
            PngEncoder.Save(Composer.Thumbnail(original, ThumbSide), Thumb(id));
            Write(new HistoryEntry { Id = id, Kind = "image", FilePath = savedPath, Width = original.Width, Height = original.Height, Created = DateTime.Now });
            TrimToMax();
            return id;
        }
        catch (Exception ex) { Log.Error(ex); return null; }
    }

    /// <summary>편집이 끝나면: 주석 목록과 미리보기(편집본)를 갱신.</summary>
    public static void UpdateImage(string id, PixelImage composite, IEnumerable<Annotation> items, string? savedPath)
    {
        try
        {
            var e = Read(id);
            if (e == null) return;
            e.Items = items.Select(AnnotationJson.ToDto).ToList();
            if (savedPath != null) e.FilePath = savedPath;
            PngEncoder.Save(Composer.Thumbnail(composite, ThumbSide), Thumb(id));
            Write(e);
        }
        catch (Exception ex) { Log.Error(ex); }
    }

    /// <summary>GIF·동영상: 파일 위치와 미리보기만 기록 (영상 자체는 복사하지 않음).</summary>
    public static void AddRecording(string kind, string filePath, PixelImage? thumbSource, int width, int height)
    {
        if (Max <= 0) return;
        try
        {
            Directory.CreateDirectory(Dir);
            string id = NewId();
            if (thumbSource != null) PngEncoder.Save(Composer.Thumbnail(thumbSource, ThumbSide), Thumb(id));
            Write(new HistoryEntry { Id = id, Kind = kind, FilePath = filePath, Width = width, Height = height, Created = DateTime.Now });
            TrimToMax();
        }
        catch (Exception ex) { Log.Error(ex); }
    }

    /// <summary>모든 기록 (최신 순).</summary>
    public static List<HistoryEntry> LoadAll()
    {
        var list = new List<HistoryEntry>();
        if (!Directory.Exists(Dir)) return list;
        foreach (var f in Directory.GetFiles(Dir, "*.json").OrderByDescending(f => f, StringComparer.Ordinal))
        {
            var e = Read(Path.GetFileNameWithoutExtension(f));
            if (e != null) list.Add(e);
        }
        return list;
    }

    public static PixelImage? LoadOriginal(string id) => Interop.GdiPlus.LoadImage(Orig(id));

    public static List<Annotation> LoadItems(HistoryEntry e) =>
        e.Items.Select(AnnotationJson.FromDto).Where(a => a != null).Select(a => a!).ToList();

    /// <summary>기록에서만 지운다 (사진·GIF·동영상 저장 파일은 그대로).</summary>
    public static void Delete(string id)
    {
        foreach (var f in new[] { Json(id), Orig(id), Thumb(id) })
            try { if (File.Exists(f)) File.Delete(f); } catch (Exception ex) { Log.Error(ex); }
    }

    /// <summary>최대 개수를 넘는 오래된 기록을 지운다. (설정에서 개수를 줄였을 때도 호출)</summary>
    public static void TrimToMax()
    {
        if (!Directory.Exists(Dir)) return;
        var ids = Directory.GetFiles(Dir, "*.json").Select(Path.GetFileNameWithoutExtension).OrderByDescending(x => x, StringComparer.Ordinal).ToList();
        int keep = Math.Max(0, Max);
        foreach (var id in ids.Skip(keep)) Delete(id!);
    }

    static HistoryEntry? Read(string id)
    {
        try { return JsonSerializer.Deserialize(File.ReadAllText(Json(id)), HistoryJson.Default.HistoryEntry); }
        catch { return null; }
    }

    static void Write(HistoryEntry e) =>
        File.WriteAllText(Json(e.Id), JsonSerializer.Serialize(e, HistoryJson.Default.HistoryEntry));
}

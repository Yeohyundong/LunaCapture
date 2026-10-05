using System.Text.Json;
using System.Text.Json.Serialization;

namespace LunaCapture.Services;

/// <summary>
/// 앱 설정. %APPDATA%\LunaCapture\settings.json 에 저장한다.
/// (Native AOT에서도 동작하도록 JSON 소스 생성기를 사용)
/// </summary>
internal sealed class AppSettings
{
    public bool OpenEditor { get; set; } = true;
    public bool OpenGifTrim { get; set; } = true;
    public bool ShowCursor { get; set; } = true;
    /// <summary>다른 프로그램이 PrtSc를 쓰고 있을 때 LunaCapture가 우선 사용할지. null = 아직 안 물어봄.</summary>
    public bool? PrintScreenTakeover { get; set; }
    public string? ImageFolder { get; set; }
    public string? VideoFolder { get; set; }

    /// <summary>캡처 기록 보관 개수 (0 = 기록 안 함)</summary>
    public int HistoryMax { get; set; } = 100;

    /// <summary>"설치 없이 실행"을 고른 버전 (그 버전에서는 다시 묻지 않음).</summary>
    public string? InstallDeclinedVersion { get; set; }

    // 편집기: 마지막으로 고른 스티커 / 말풍선 꼬리 방향
    public int Sticker { get; set; }
    public int BubbleTail { get; set; }
    public int ShapeStyle { get; set; }
    /// <summary>편집 창 오른쪽 캡처 기록 패널을 보일지</summary>
    public bool EditorHistoryPanel { get; set; } = true;

    /// <summary>마지막으로 고른 녹화 형식: 0 = GIF, 1 = 동영상(MP4)</summary>
    public int RecordFormat { get; set; }

    // GIF 녹화 설정 창 (선택지 인덱스)
    public int GifTarget { get; set; }
    public int GifLength { get; set; } = 3;
    public int GifMotion { get; set; }
    public int GifFps { get; set; } = 1;
    public int GifScale { get; set; }

    // 동영상 녹화 설정 창 (선택지 인덱스)
    public int VideoTarget { get; set; }
    public int VideoLength { get; set; } = 4;
    public int VideoQuality { get; set; } = 1;
    public int VideoFps { get; set; } = 1;
    public int VideoScale { get; set; }

    [JsonIgnore]
    public string ImageFolderOrDefault => string.IsNullOrWhiteSpace(ImageFolder)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "LunaCapture")
        : ImageFolder;

    [JsonIgnore]
    public string VideoFolderOrDefault => string.IsNullOrWhiteSpace(VideoFolder)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "LunaCapture")
        : VideoFolder;

    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LunaCapture", "settings.json");

    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJson.Default.AppSettings) ?? new();
        }
        catch (Exception ex)
        {
            Log.Error(ex); // 깨진 설정 파일이면 기본값으로
            Current = new();
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, SettingsJson.Default.AppSettings));
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    /// <summary>배열 인덱스로 쓰는 값이 범위를 벗어나지 않게 (설정 파일을 손으로 고친 경우 대비).</summary>
    public static int Clamp(int value, int count) => Math.Clamp(value, 0, count - 1);
}

[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class SettingsJson : JsonSerializerContext;

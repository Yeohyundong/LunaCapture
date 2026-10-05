namespace LunaCapture;

/// <summary>최소한의 파일 로그 (%LOCALAPPDATA%\LunaCapture\log.txt).</summary>
internal static class Log
{
    static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunaCapture", "log.txt");

    public static void Error(Exception ex) => Write("ERROR " + ex);

    public static void Info(string message) => Write("INFO  " + message);

    static void Write(string line)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch { /* 로그 실패는 무시 */ }
    }
}

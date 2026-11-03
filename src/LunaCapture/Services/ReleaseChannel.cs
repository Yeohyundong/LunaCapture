namespace LunaCapture.Services;

internal static class ReleaseChannel
{
    public const string Repository = "Yeohyundong/LunaCapture";
    public const string WebUrl = "https://github.com/" + Repository;
    public const string LatestApi = "https://api.github.com/repos/" + Repository + "/releases/latest";
    public const string FeedbackEmail = "hyundongy@gmail.com";
    public static string FeedbackUrl => "mailto:" + FeedbackEmail + "?subject=" + Uri.EscapeDataString("LunaCapture " + Installer.Version + " 피드백")
        + "&body=" + Uri.EscapeDataString("LunaCapture 버전: " + Installer.Version + "\r\n\r\n어떤 점이 불편했나요?\r\n\r\n재현 방법 또는 제안:\r\n\r\n");
}

using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LunaCapture.Services;

internal sealed record FeedbackPayload(string Content, string? ReplyTo, string Version, string RequestId);
internal sealed record FeedbackResult(bool Success, string Message);

[JsonSerializable(typeof(FeedbackPayload))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class FeedbackJson : JsonSerializerContext;

internal static class FeedbackClient
{
    public const int MaxContent = 4000;
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
    public static string DraftContent = "", DraftEmail = "", DraftRequestId = Guid.NewGuid().ToString("N");
    public static string? Validate(string content, string? email)
    {
        if (string.IsNullOrWhiteSpace(content)) return "어떤 점을 고치면 좋을지 적어주세요.";
        if (content.Length > MaxContent) return "내용은 4,000자 이내로 적어주세요.";
        if (!string.IsNullOrEmpty(email) && (email.Length > 254 || !MailAddress.TryCreate(email, out var address) || address.Address != email || email.Contains('\r') || email.Contains('\n')))
            return "답장받을 이메일 주소를 확인해주세요. 비워두셔도 돼요.";
        return null;
    }

    public static async Task<FeedbackResult> SendAsync(FeedbackPayload payload, HttpClient? client = null, string? endpoint = null)
    {
        if (Validate(payload.Content, payload.ReplyTo) is { } error) return new(false, error);
        endpoint ??= ReleaseChannel.FeedbackEndpoint;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https") return new(false, "아직 피드백 서버가 연결되지 않았어요. 입력은 그대로 보관돼요.");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Content = JsonContent.Create(payload, FeedbackJson.Default.FeedbackPayload);
            using var response = await (client ?? Http).SendAsync(request).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) return new(false, "요청이 잠시 몰렸어요. 조금 뒤 다시 보내주세요. 입력은 그대로예요.");
            if (!response.IsSuccessStatusCode) return new(false, "지금은 보내지 못했어요. 입력은 그대로예요. 잠시 뒤 다시 시도해주세요.");
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True) return new(false, "전송 완료를 확인하지 못했어요. 입력은 그대로 보관돼요.");
            return new(true, "잘 받았어요! 보내주신 의견을 확인할게요.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return new(false, "연결이 원활하지 않아요. 입력은 그대로예요. 다시 보내기를 눌러주세요.");
        }
    }
}

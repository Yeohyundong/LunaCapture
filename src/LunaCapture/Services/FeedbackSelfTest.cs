using System.Net;
using System.Net.Http;
using System.Text;

namespace LunaCapture.Services;

internal static class FeedbackSelfTest
{
    sealed class Stub(HttpStatusCode status, string body, bool offline = false) : HttpMessageHandler
    {
        public string Sent = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Sent = await request.Content!.ReadAsStringAsync(token);
            if (offline) throw new HttpRequestException("offline");
            return new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
    public static void Run(string output)
    {
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
        Check(FeedbackClient.Validate("개선해주세요", null) == null, "optional email");
        Check(FeedbackClient.Validate("내용", "reply@example.com") == null, "valid email");
        Check(FeedbackClient.Validate(" ", null) != null, "empty content");
        Check(FeedbackClient.Validate(new string('가', 4001), null) != null, "length limit");
        Check(FeedbackClient.Validate("내용", "bad\n@example.com") != null, "email injection");
        var payload = new FeedbackPayload(new string('가', 4000), null, "1.2.2", new string('a', 32));
        FeedbackClient.DraftContent = payload.Content; FeedbackClient.DraftEmail = "";
        foreach (var (status, body, offline, success) in new[] {
            (HttpStatusCode.OK, "{\"ok\":true}", false, true),
            (HttpStatusCode.OK, "{\"ok\":false}", false, false),
            (HttpStatusCode.OK, "invalid", false, false),
            (HttpStatusCode.OK, "[]", false, false),
            (HttpStatusCode.ServiceUnavailable, "{}", false, false),
            (HttpStatusCode.TooManyRequests, "{}", false, false),
            (HttpStatusCode.OK, "{}", true, false) })
        {
            using var stub = new Stub(status, body, offline);
            using var http = new HttpClient(stub);
            var result = FeedbackClient.SendAsync(payload, http, "https://example.com/api/feedback").GetAwaiter().GetResult();
            Check(result.Success == success, "response handling");
            Check(FeedbackClient.DraftContent == payload.Content, "draft preserved");
            Check(stub.Sent.Contains("\"requestId\":\"" + payload.RequestId), "retry identifier serialized");
        }
        File.WriteAllText(output, "피드백 검증 통과: 선택 이메일·4,000자·AOT JSON·전송 성공·서버 오류·빈도 제한·오프라인·실패 시 초안 유지\n");
    }
}

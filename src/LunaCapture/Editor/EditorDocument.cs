using LunaCapture.Graphics;

namespace LunaCapture.Editor;

/// <summary>
/// 편집기가 지금 다루는 캡처 한 장: 원본 + 그려 둔 주석 + "바꾼 게 있으면 어떻게 저장할지".
/// 편집 창은 그대로 두고 이 문서만 바꿔 끼워서 기록 패널의 다른 캡처로 넘어간다.
/// </summary>
internal sealed class EditorDocument
{
    public required PixelImage Original { get; init; }
    public List<Annotation> Items { get; init; } = [];
    public string? HistoryId { get; init; }
    /// <summary>바뀐 결과 저장: (완성 이미지, 최종 주석 목록)</summary>
    public required Action<PixelImage, List<Annotation>> Save { get; init; }
}

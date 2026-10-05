using LunaCapture.Editor;
using LunaCapture.Graphics;
using LunaCapture.Interop;
using LunaCapture.Services;

namespace LunaCapture;

/// <summary>캡처 기록: 기록 창 열기, 기록에서 다시 편집·복사.</summary>
internal sealed partial class TrayApp
{
    HistoryWindow? _history;

    void OpenHistory()
    {
        if (_history != null) { _history.BringToFront(); return; }
        if (Settings.HistoryMax <= 0)
        {
            Notify("캡처 기록이 꺼져 있어요", "트레이 메뉴 › 설정 › 캡처 기록 보관 에서 켤 수 있어요.", Win.NIIF_INFO);
            return;
        }
        try
        {
            _history = new HistoryWindow(EditFromHistory, CopyFromHistory, () => { _history = null; MemoryTrimmer.Trim(); });
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            Notify("캡처 기록을 열지 못했어요", ex.Message, Win.NIIF_WARNING);
        }
    }

    /// <summary>기록의 사진을 편집기로 다시 연다: 원본 + 그려 둔 주석(다시 고칠 수 있음).</summary>
    void EditFromHistory(HistoryEntry e)
    {
        var doc = OpenEntryAsDocument(e);
        if (doc == null)
        {
            Notify("원본을 찾을 수 없어요", "기록 파일이 지워졌어요.", Win.NIIF_WARNING);
            return;
        }
        OpenEditor(doc, "기록에서 열었어요 · 고치면 원래 파일에 다시 저장돼요");
    }

    /// <summary>복사: 사진은 이미지로(편집 포함), GIF·동영상은 파일로.</summary>
    void CopyFromHistory(HistoryEntry e)
    {
        if (e.Kind != "image")
        {
            if (e.FilePath != null && File.Exists(e.FilePath)) ClipboardService.SetFile(Handle, e.FilePath);
            return;
        }
        // 저장된 편집본이 있으면 그걸, 없으면 원본 + 주석으로 다시 합성
        PixelImage? img = e.FilePath != null ? GdiPlus.LoadImage(e.FilePath) : null;
        if (img == null)
        {
            var original = HistoryStore.LoadOriginal(e.Id);
            if (original != null) img = Composer.Compose(original, HistoryStore.LoadItems(e));
        }
        if (img != null) ClipboardService.SetImage(Handle, img);
        MemoryTrimmer.Trim();
    }
}

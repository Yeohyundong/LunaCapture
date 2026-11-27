using LunaCapture.Interop;
using LunaCapture.Services;

namespace LunaCapture.Editor;

internal static class HistoryDeleteButton
{
    public static RECT Bounds(RECT cell, float scale)
    {
        int size = (int)MathF.Round(24 * scale), gap = (int)MathF.Round(2 * scale);
        return new(cell.Right - size, cell.Bottom - size - gap, cell.Right, cell.Bottom - gap);
    }

    public static bool Contains(RECT cell, float scale, int x, int y)
    {
        var r = Bounds(cell, scale);
        return x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
    }

    public static void Draw(Gfx g, RECT cell, float scale, bool hover)
    {
        var r = Bounds(cell, scale);
        if (hover) g.FillRoundRect(0xFF583039, r.Left, r.Top, r.Width, r.Height, 4 * scale);
        uint color = hover ? 0xFFFF8C96 : 0xFFAAAAB6;
        float x = r.Left + 6 * scale, y = r.Top + 5 * scale;
        g.Rect(color, scale, x + 2 * scale, y + 4 * scale, 8 * scale, 10 * scale);
        g.FillRect(color, x, y + 2 * scale, 12 * scale, scale);
        g.Rect(color, scale, x + 4 * scale, y, 4 * scale, 2 * scale);
        g.FillRect(color, x + 5 * scale, y + 6 * scale, scale, 6 * scale);
        g.FillRect(color, x + 8 * scale, y + 6 * scale, scale, 6 * scale);
    }

    public static bool Delete(nint owner, HistoryEntry entry)
    {
        if (User32.MessageBoxW(owner,
            "이 캡처 기록을 삭제할까요?\n기록의 원본과 편집 정보가 삭제되며 되돌릴 수 없어요.\n별도로 저장한 이미지·녹화 파일은 그대로 유지돼요.",
            "캡처 기록 삭제", 0x00000134 /* YESNO · WARNING · 기본값 아니요 */) != 6) return false;
        bool deleted = HistoryStore.Delete(entry.Id);
        if (!deleted) User32.MessageBoxW(owner, "기록을 완전히 삭제하지 못했어요. 잠시 후 다시 시도해 주세요.", "삭제 실패", 0x10);
        return deleted;
    }
}

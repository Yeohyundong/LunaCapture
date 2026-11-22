using LunaCapture.Interop;
using LunaCapture.Services;

namespace LunaCapture.Capture;

internal static class CaptureFileNameMenu
{
    const uint FirstCommand = 180;
    static readonly (CaptureNameStyle Style, string Label)[] Options =
    [
        (CaptureNameStyle.DateFirst, "날짜·시간 먼저"),
        (CaptureNameStyle.AppFirst, "앱 이름 먼저"),
        (CaptureNameStyle.LunaOnly, "Luna 고정"),
    ];

    public static void AppendTo(nint parent)
    {
        nint menu = User32.CreatePopupMenu();
        if (menu == 0) return;
        var current = AppSettings.Current.FileNameStyle;
        if (!Enum.IsDefined(current)) current = CaptureNameStyle.AppFirst;
        var exampleTime = new DateTime(2026, 11, 13, 14, 30, 52, 123);
        for (int i = 0; i < Options.Length; i++)
        {
            var (style, label) = Options[i];
            string example = CaptureFileName.Create("Chrome", exampleTime, "png", style);
            User32.AppendMenuW(menu, Win.MF_STRING | (style == current ? Win.MF_CHECKED : 0),
                FirstCommand + (uint)i, $"{label}  ·  {example}");
        }
        if (User32.AppendMenuW(parent, 0x10 /* MF_POPUP */, (nuint)menu, "저장 파일 이름") == 0)
            User32.DestroyMenu(menu);
    }

    public static bool TryApply(uint command)
    {
        if (command < FirstCommand || command >= FirstCommand + Options.Length) return false;
        AppSettings.Current.FileNameStyle = Options[command - FirstCommand].Style;
        AppSettings.Save();
        return true;
    }
}

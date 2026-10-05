using LunaCapture.Interop;

namespace LunaCapture.Services;

internal static unsafe class FileDialog
{
    /// <summary>"다른 이름으로 저장" 대화상자. 취소하면 null.</summary>
    public static string? SavePng(nint owner, string defaultName)
    {
        const int MaxPath = 1024;
        char* file = stackalloc char[MaxPath];
        int n = Math.Min(defaultName.Length, MaxPath - 1);
        for (int i = 0; i < n; i++) file[i] = defaultName[i];
        file[n] = '\0';

        fixed (char* filter = "PNG 이미지 (*.png)\0*.png\0\0")
        fixed (char* ext = "png")
        fixed (char* title = "다른 이름으로 저장")
        {
            var ofn = new OPENFILENAMEW
            {
                lStructSize = (uint)sizeof(OPENFILENAMEW),
                hwndOwner = owner,
                lpstrFilter = filter,
                nFilterIndex = 1,
                lpstrFile = file,
                nMaxFile = MaxPath,
                lpstrDefExt = ext,
                lpstrTitle = title,
                Flags = Comdlg32.OFN_OVERWRITEPROMPT | Comdlg32.OFN_PATHMUSTEXIST | Comdlg32.OFN_NOCHANGEDIR,
            };
            if (Comdlg32.GetSaveFileNameW(&ofn) == 0) return null;
        }
        return new string(file);
    }
}

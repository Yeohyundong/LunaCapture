using System.Runtime.InteropServices;

namespace LunaCapture.Services;

/// <summary>
/// 윈도우 기본 "폴더 선택" 대화상자 (IFileOpenDialog + FOS_PICKFOLDERS).
/// COM 인터페이스는 vtable 슬롯 번호로 직접 호출한다.
/// </summary>
internal static unsafe partial class FolderPicker
{
    static readonly Guid CLSID_FileOpenDialog = new("dc1c5a9c-e88a-4dde-a5a1-60f82a20aef7");
    static readonly Guid IID_IFileOpenDialog = new("d57c7288-d4ad-4768-be02-9d969532d960");
    static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    const uint CLSCTX_INPROC_SERVER = 1;
    const uint FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40;
    const uint SIGDN_FILESYSPATH = 0x80058000;

    [LibraryImport("ole32.dll")] private static partial int CoInitializeEx(nint reserved, uint coinit);
    [LibraryImport("ole32.dll")] private static partial int CoCreateInstance(Guid* clsid, nint outer, uint ctx, Guid* iid, nint* obj);
    [LibraryImport("ole32.dll")] private static partial void CoTaskMemFree(nint p);
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string path, nint bindCtx, Guid* iid, nint* item);

    static void* Slot(nint obj, int i) => (*(void***)obj)[i];
    static void Release(nint obj) { if (obj != 0) ((delegate* unmanaged<nint, uint>)Slot(obj, 2))(obj); }

    /// <summary>폴더를 고르게 한다. 취소하면 null.</summary>
    public static string? Pick(nint owner, string title, string? initialFolder)
    {
        CoInitializeEx(0, 2 /* COINIT_APARTMENTTHREADED */);

        nint dialog = 0, folderItem = 0, result = 0;
        try
        {
            Guid clsid = CLSID_FileOpenDialog, iid = IID_IFileOpenDialog;
            if (CoCreateInstance(&clsid, 0, CLSCTX_INPROC_SERVER, &iid, &dialog) < 0) return null;

            // IFileDialog: 9 SetOptions, 12 SetFolder, 17 SetTitle, 20 GetResult / IModalWindow: 3 Show
            ((delegate* unmanaged<nint, uint, int>)Slot(dialog, 9))(dialog, FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM);
            fixed (char* t = title)
                ((delegate* unmanaged<nint, char*, int>)Slot(dialog, 17))(dialog, t);

            if (initialFolder != null && Directory.Exists(initialFolder))
            {
                Guid itemIid = IID_IShellItem;
                if (SHCreateItemFromParsingName(initialFolder, 0, &itemIid, &folderItem) >= 0)
                    ((delegate* unmanaged<nint, nint, int>)Slot(dialog, 12))(dialog, folderItem);
            }

            int hr = ((delegate* unmanaged<nint, nint, int>)Slot(dialog, 3))(dialog, owner);
            if (hr < 0) return null; // 취소 포함

            if (((delegate* unmanaged<nint, nint*, int>)Slot(dialog, 20))(dialog, &result) < 0) return null;

            // IShellItem: 5 GetDisplayName
            nint name;
            if (((delegate* unmanaged<nint, uint, nint*, int>)Slot(result, 5))(result, SIGDN_FILESYSPATH, &name) < 0) return null;
            string path = new((char*)name);
            CoTaskMemFree(name);
            return path;
        }
        finally
        {
            Release(result);
            Release(folderItem);
            Release(dialog);
        }
    }
}

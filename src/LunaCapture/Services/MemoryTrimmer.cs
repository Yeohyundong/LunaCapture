using LunaCapture.Interop;

namespace LunaCapture.Services;

internal static class MemoryTrimmer
{
    /// <summary>캡처가 끝난 뒤 큰 버퍼를 회수하고 작업 집합을 OS에 돌려준다.</summary>
    public static void Trim()
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        Kernel32.SetProcessWorkingSetSize(Kernel32.GetCurrentProcess(), -1, -1);
    }
}

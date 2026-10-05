using System.Runtime.InteropServices;

namespace LunaCapture.Interop;

/// <summary>
/// Media Foundation 최소 바인딩. COM 인터페이스는 vtable 슬롯 번호로 직접 호출한다
/// (Native AOT에서 무거운 COM 런타임 없이 쓰기 위해).
/// </summary>
internal static unsafe partial class MF
{
    public const uint MF_VERSION = 0x00020070;
    public const uint COINIT_MULTITHREADED = 0x0;

    // ── 속성 키 ──
    public static readonly Guid MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid MT_AVG_BITRATE = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    public static readonly Guid MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static readonly Guid MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid MT_PIXEL_ASPECT_RATIO = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    public static readonly Guid MT_DEFAULT_STRIDE = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    public static readonly Guid MT_MPEG2_PROFILE = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
    public static readonly Guid MT_YUV_MATRIX = new("3e23d450-2c75-4d25-a00e-b91670d12327");
    public static readonly Guid MT_VIDEO_NOMINAL_RANGE = new("c21b8ee5-b956-4071-8daf-325edf5cab11");
    public static readonly Guid READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("a634a91c-822b-41b9-a494-4de4643612b0");
    public static readonly Guid SINK_WRITER_DISABLE_THROTTLING = new("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");
    public static readonly Guid CODECAPI_AVEncCommonRateControlMode = new("1c0608e9-370c-4710-8a58-cb6181c42423");
    public static readonly Guid CODECAPI_AVEncCommonMeanBitRate = new("f7222374-2144-4815-b550-a37f8e12ee52");

    // ── 값 GUID ──
    public static readonly Guid MediaType_Video = new("73646976-0000-0010-8000-00aa00389b71");
    public static readonly Guid VideoFormat_H264 = new("34363248-0000-0010-8000-00aa00389b71");
    public static readonly Guid VideoFormat_NV12 = new("3231564e-0000-0010-8000-00aa00389b71");

    [LibraryImport("mfplat.dll")] public static partial int MFStartup(uint version, uint flags);
    [LibraryImport("mfplat.dll")] public static partial int MFShutdown();
    [LibraryImport("mfplat.dll")] public static partial int MFCreateMediaType(nint* type);
    [LibraryImport("mfplat.dll")] public static partial int MFCreateAttributes(nint* attrs, uint initialSize);
    [LibraryImport("mfplat.dll")] public static partial int MFCreateMemoryBuffer(uint maxLength, nint* buffer);
    [LibraryImport("mfplat.dll")] public static partial int MFCreateSample(nint* sample);
    [LibraryImport("mfreadwrite.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MFCreateSinkWriterFromURL(string url, nint byteStream, nint attributes, nint* writer);
    [LibraryImport("ole32.dll")] public static partial int CoInitializeEx(nint reserved, uint coinit);
    [LibraryImport("ole32.dll")] public static partial void CoUninitialize();

    public static void Check(int hr, string what)
    {
        if (hr < 0) throw new InvalidOperationException($"{what} 실패 (0x{hr:X8})");
    }

    static void* Slot(nint obj, int index) => (*(void***)obj)[index];

    // IUnknown
    public static void Release(ref nint obj)
    {
        if (obj == 0) return;
        ((delegate* unmanaged<nint, uint>)Slot(obj, 2))(obj);
        obj = 0;
    }

    // IMFAttributes (IMFMediaType, IMFSample도 상속)
    public static int SetUInt32(nint attrs, Guid key, uint value) =>
        ((delegate* unmanaged<nint, Guid*, uint, int>)Slot(attrs, 21))(attrs, &key, value);
    public static int SetUInt64(nint attrs, Guid key, ulong value) =>
        ((delegate* unmanaged<nint, Guid*, ulong, int>)Slot(attrs, 22))(attrs, &key, value);
    public static int SetGuid(nint attrs, Guid key, Guid value) =>
        ((delegate* unmanaged<nint, Guid*, Guid*, int>)Slot(attrs, 24))(attrs, &key, &value);

    // IMFSinkWriter
    public static int AddStream(nint writer, nint mediaType, uint* streamIndex) =>
        ((delegate* unmanaged<nint, nint, uint*, int>)Slot(writer, 3))(writer, mediaType, streamIndex);
    public static int SetInputMediaType(nint writer, uint stream, nint mediaType, nint encodingParams) =>
        ((delegate* unmanaged<nint, uint, nint, nint, int>)Slot(writer, 4))(writer, stream, mediaType, encodingParams);
    public static int BeginWriting(nint writer) =>
        ((delegate* unmanaged<nint, int>)Slot(writer, 5))(writer);
    public static int WriteSample(nint writer, uint stream, nint sample) =>
        ((delegate* unmanaged<nint, uint, nint, int>)Slot(writer, 6))(writer, stream, sample);
    public static int FinalizeWriter(nint writer) =>
        ((delegate* unmanaged<nint, int>)Slot(writer, 11))(writer);

    // IMFMediaBuffer
    public static int Lock(nint buffer, byte** data, uint* maxLen, uint* curLen) =>
        ((delegate* unmanaged<nint, byte**, uint*, uint*, int>)Slot(buffer, 3))(buffer, data, maxLen, curLen);
    public static int Unlock(nint buffer) =>
        ((delegate* unmanaged<nint, int>)Slot(buffer, 4))(buffer);
    public static int SetCurrentLength(nint buffer, uint len) =>
        ((delegate* unmanaged<nint, uint, int>)Slot(buffer, 6))(buffer, len);

    // IMFSample
    public static int SetSampleTime(nint sample, long time100ns) =>
        ((delegate* unmanaged<nint, long, int>)Slot(sample, 36))(sample, time100ns);
    public static int SetSampleDuration(nint sample, long duration100ns) =>
        ((delegate* unmanaged<nint, long, int>)Slot(sample, 38))(sample, duration100ns);
    public static int AddBuffer(nint sample, nint buffer) =>
        ((delegate* unmanaged<nint, nint, int>)Slot(sample, 42))(sample, buffer);
}

using LunaCapture.Gif;
using LunaCapture.Graphics;
using LunaCapture.Interop;

namespace LunaCapture.Recording;

/// <summary>
/// MP4(H.264) 파일 싱크. Media Foundation Sink Writer가 그래픽카드 인코더를 자동으로 고른다.
/// 프레임은 직접 NV12(인코더가 바로 먹는 형식)로 바꿔 넘긴다.
/// 용량 한도가 있으면 실제 파일 크기를 보면서 한도 직전에 멈춘다.
/// </summary>
internal sealed unsafe class Mp4Sink : IFrameSink
{
    const int CheckEvery = 10; // 파일 크기 확인 주기(프레임)

    readonly string _path;
    readonly int _w, _h, _fps, _bitrate;
    readonly long _limit;
    readonly long _frameDuration; // 100ns 단위
    nint _writer;
    uint _stream;
    bool _comInit, _mfStarted, _began, _finished;
    long _firstTs = -1;
    long _fileBytes;
    int _frames;

    public Mp4Sink(string path, int width, int height, int fps, int bitrateBps, long limitBytes)
    {
        _path = path;
        _w = width;
        _h = height;
        _fps = fps;
        _bitrate = bitrateBps;
        _limit = limitBytes;
        _frameDuration = 10_000_000L / fps;

        _comInit = MF.CoInitializeEx(0, MF.COINIT_MULTITHREADED) >= 0;
        MF.Check(MF.MFStartup(MF.MF_VERSION, 0), "Media Foundation 시작");
        _mfStarted = true;
        CreateWriter();
    }

    public int FrameCount => _frames;

    /// <summary>
    /// 디스크에 쓰인 크기 + 인코더 안에 남은 분량(약 0.5초) + 마지막에 붙는 목차(moov) 추정치.
    /// </summary>
    public long CurrentSize => _fileBytes + Reserve;

    long Reserve => _bitrate / 8 / 2 + _frames * 24L + 32 * 1024;

    void CreateWriter()
    {
        nint attrs = 0, outType = 0, inType = 0, enc = 0;
        try
        {
            MF.Check(MF.MFCreateAttributes(&attrs, 2), "속성 생성");
            MF.SetUInt32(attrs, MF.READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
            MF.SetUInt32(attrs, MF.SINK_WRITER_DISABLE_THROTTLING, 1);

            nint writer;
            MF.Check(MF.MFCreateSinkWriterFromURL(_path, 0, attrs, &writer), "MP4 파일 생성");
            _writer = writer;

            // 출력: H.264
            MF.Check(MF.MFCreateMediaType(&outType), "출력 형식");
            MF.SetGuid(outType, MF.MT_MAJOR_TYPE, MF.MediaType_Video);
            MF.SetGuid(outType, MF.MT_SUBTYPE, MF.VideoFormat_H264);
            MF.SetUInt32(outType, MF.MT_AVG_BITRATE, (uint)_bitrate);
            MF.SetUInt32(outType, MF.MT_INTERLACE_MODE, 2); // progressive
            MF.SetUInt64(outType, MF.MT_FRAME_SIZE, (ulong)_w << 32 | (uint)_h);
            MF.SetUInt64(outType, MF.MT_FRAME_RATE, (ulong)_fps << 32 | 1);
            MF.SetUInt64(outType, MF.MT_PIXEL_ASPECT_RATIO, 1UL << 32 | 1);
            MF.SetUInt32(outType, MF.MT_MPEG2_PROFILE, 100); // High
            MF.SetUInt32(outType, MF.MT_YUV_MATRIX, 1);       // BT.709
            MF.SetUInt32(outType, MF.MT_VIDEO_NOMINAL_RANGE, 2); // 16-235
            uint stream;
            MF.Check(MF.AddStream(_writer, outType, &stream), "비디오 스트림 추가");
            _stream = stream;

            // 입력: NV12 (우리가 직접 변환)
            MF.Check(MF.MFCreateMediaType(&inType), "입력 형식");
            MF.SetGuid(inType, MF.MT_MAJOR_TYPE, MF.MediaType_Video);
            MF.SetGuid(inType, MF.MT_SUBTYPE, MF.VideoFormat_NV12);
            MF.SetUInt32(inType, MF.MT_INTERLACE_MODE, 2);
            MF.SetUInt64(inType, MF.MT_FRAME_SIZE, (ulong)_w << 32 | (uint)_h);
            MF.SetUInt64(inType, MF.MT_FRAME_RATE, (ulong)_fps << 32 | 1);
            MF.SetUInt64(inType, MF.MT_PIXEL_ASPECT_RATIO, 1UL << 32 | 1);
            MF.SetUInt32(inType, MF.MT_DEFAULT_STRIDE, (uint)_w);
            MF.SetUInt32(inType, MF.MT_YUV_MATRIX, 1);
            MF.SetUInt32(inType, MF.MT_VIDEO_NOMINAL_RANGE, 2);

            // 용량을 예측 가능하게: 고정 비트레이트(CBR). 지원 안 하는 인코더면 기본값으로 재시도.
            MF.Check(MF.MFCreateAttributes(&enc, 2), "인코더 설정");
            MF.SetUInt32(enc, MF.CODECAPI_AVEncCommonRateControlMode, 0); // CBR
            MF.SetUInt32(enc, MF.CODECAPI_AVEncCommonMeanBitRate, (uint)_bitrate);
            int hr = MF.SetInputMediaType(_writer, _stream, inType, enc);
            if (hr < 0)
            {
                Log.Info($"CBR 설정 실패(0x{hr:X8}), 기본 설정으로 재시도");
                hr = MF.SetInputMediaType(_writer, _stream, inType, 0);
            }
            MF.Check(hr, "입력 형식 설정");

            MF.Check(MF.BeginWriting(_writer), "녹화 시작");
            _began = true;
        }
        finally
        {
            MF.Release(ref attrs);
            MF.Release(ref outType);
            MF.Release(ref inType);
            MF.Release(ref enc);
        }
    }

    public FrameResult AddFrame(GdiSurface frame, long timestampMs)
    {
        if (_firstTs < 0) _firstTs = timestampMs;

        if (_limit > 0)
        {
            if (_frames % CheckEvery == 0) RefreshFileSize();
            if (CurrentSize + _bitrate / 8 / _fps > _limit) return FrameResult.LimitReached;
        }

        uint nv12Size = (uint)(_w * _h * 3 / 2);
        nint buffer = 0, sample = 0;
        try
        {
            MF.Check(MF.MFCreateMemoryBuffer(nv12Size, &buffer), "버퍼 생성");
            byte* dst;
            uint max, cur;
            MF.Check(MF.Lock(buffer, &dst, &max, &cur), "버퍼 잠금");
            ConvertToNv12(frame, dst);
            MF.Unlock(buffer);
            MF.SetCurrentLength(buffer, nv12Size);

            MF.Check(MF.MFCreateSample(&sample), "샘플 생성");
            MF.AddBuffer(sample, buffer);
            MF.SetSampleTime(sample, (timestampMs - _firstTs) * 10_000);
            MF.SetSampleDuration(sample, _frameDuration);
            MF.Check(MF.WriteSample(_writer, _stream, sample), "프레임 쓰기");
            _frames++;
        }
        finally
        {
            MF.Release(ref sample);
            MF.Release(ref buffer);
        }
        return FrameResult.Added;
    }

    void RefreshFileSize()
    {
        try { _fileBytes = new FileInfo(_path).Length; } catch { }
    }

    /// <summary>BGRA → NV12 (BT.709, 16-235). 세로·가로 2픽셀씩 묶어 색(UV)을 평균낸다.</summary>
    void ConvertToNv12(GdiSurface frame, byte* dst)
    {
        int w = _w, h = _h;
        byte* yPlane = dst;
        byte* uvPlane = dst + w * h;
        byte* src = frame.Bits;
        int stride = frame.Stride;

        for (int y = 0; y < h; y += 2)
        {
            byte* r0 = src + y * stride;
            byte* r1 = r0 + stride;
            byte* y0 = yPlane + y * w;
            byte* y1 = y0 + w;
            byte* uv = uvPlane + (y / 2) * w;

            for (int x = 0; x < w; x += 2)
            {
                int o = x * 4;
                int b00 = r0[o], g00 = r0[o + 1], rr00 = r0[o + 2];
                int b01 = r0[o + 4], g01 = r0[o + 5], rr01 = r0[o + 6];
                int b10 = r1[o], g10 = r1[o + 1], rr10 = r1[o + 2];
                int b11 = r1[o + 4], g11 = r1[o + 5], rr11 = r1[o + 6];

                y0[x] = (byte)(16 + ((47 * rr00 + 157 * g00 + 16 * b00 + 128) >> 8));
                y0[x + 1] = (byte)(16 + ((47 * rr01 + 157 * g01 + 16 * b01 + 128) >> 8));
                y1[x] = (byte)(16 + ((47 * rr10 + 157 * g10 + 16 * b10 + 128) >> 8));
                y1[x + 1] = (byte)(16 + ((47 * rr11 + 157 * g11 + 16 * b11 + 128) >> 8));

                int r = rr00 + rr01 + rr10 + rr11, g = g00 + g01 + g10 + g11, b = b00 + b01 + b10 + b11;
                uv[x] = (byte)(128 + ((-26 * r - 87 * g + 112 * b + 512) >> 10));
                uv[x + 1] = (byte)(128 + ((112 * r - 102 * g - 10 * b + 512) >> 10));
            }
        }
    }

    public void Finish(long endTimestampMs)
    {
        if (_finished || !_began) return;
        _finished = true;
        if (_frames > 0) MF.Check(MF.FinalizeWriter(_writer), "MP4 마무리");
        RefreshFileSize();
    }

    public void Dispose()
    {
        MF.Release(ref _writer);
        if (_mfStarted) { MF.MFShutdown(); _mfStarted = false; }
        if (_comInit) { MF.CoUninitialize(); _comInit = false; }
    }
}

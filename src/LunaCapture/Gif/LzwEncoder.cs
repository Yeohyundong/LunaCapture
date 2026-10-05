namespace LunaCapture.Gif;

/// <summary>
/// GIF용 가변 길이 LZW 압축기 (고전적인 compress 해시 방식, 최대 12비트 코드).
/// </summary>
internal sealed class LzwEncoder
{
    const int MaxBits = 12;
    const int MaxMaxCode = 1 << MaxBits;
    const int HSize = 5003;
    const int HShift = 4; // HSize(5003) 기준으로 계산된 값

    readonly int[] _htab = new int[HSize];
    readonly int[] _codetab = new int[HSize];
    readonly byte[] _packet = new byte[256];

    Stream _out = null!;
    int _initBits, _nBits, _maxCode, _clearCode, _eofCode, _freeEnt;
    bool _clearFlag;
    int _curAccum, _curBits, _packetCount;

    /// <summary>인덱스 배열을 압축해 GIF 이미지 데이터(최소 코드 크기 + 서브블록 + 종료 0)로 쓴다.</summary>
    public void Encode(Stream output, ReadOnlySpan<byte> pixels, int minCodeSize = 8)
    {
        _out = output;
        output.WriteByte((byte)minCodeSize);

        _initBits = minCodeSize + 1;
        _nBits = _initBits;
        _maxCode = (1 << _nBits) - 1;
        _clearCode = 1 << minCodeSize;
        _eofCode = _clearCode + 1;
        _freeEnt = _clearCode + 2;
        _clearFlag = false;
        _curAccum = 0;
        _curBits = 0;
        _packetCount = 0;

        Array.Fill(_htab, -1);
        Output(_clearCode);

        int ent = pixels[0];
        for (int p = 1; p < pixels.Length; p++)
        {
            int c = pixels[p];
            int fcode = (c << MaxBits) + ent;
            int i = (c << HShift) ^ ent;

            if (_htab[i] == fcode)
            {
                ent = _codetab[i];
                continue;
            }
            if (_htab[i] >= 0)
            {
                int disp = i == 0 ? 1 : HSize - i;
                bool found = false;
                do
                {
                    if ((i -= disp) < 0) i += HSize;
                    if (_htab[i] == fcode)
                    {
                        ent = _codetab[i];
                        found = true;
                        break;
                    }
                } while (_htab[i] >= 0);
                if (found) continue;
            }

            Output(ent);
            ent = c;
            if (_freeEnt < MaxMaxCode)
            {
                _codetab[i] = _freeEnt++;
                _htab[i] = fcode;
            }
            else
            {
                Array.Fill(_htab, -1);
                _freeEnt = _clearCode + 2;
                _clearFlag = true;
                Output(_clearCode);
            }
        }

        Output(ent);
        Output(_eofCode);
        output.WriteByte(0); // 블록 종료
    }

    void Output(int code)
    {
        _curAccum &= (1 << _curBits) - 1;
        _curAccum = _curBits > 0 ? _curAccum | (code << _curBits) : code;
        _curBits += _nBits;

        while (_curBits >= 8)
        {
            CharOut((byte)_curAccum);
            _curAccum >>= 8;
            _curBits -= 8;
        }

        if (_freeEnt > _maxCode || _clearFlag)
        {
            if (_clearFlag)
            {
                _nBits = _initBits;
                _maxCode = (1 << _nBits) - 1;
                _clearFlag = false;
            }
            else
            {
                _nBits++;
                _maxCode = _nBits == MaxBits ? MaxMaxCode : (1 << _nBits) - 1;
            }
        }

        if (code == _eofCode)
        {
            while (_curBits > 0)
            {
                CharOut((byte)_curAccum);
                _curAccum >>= 8;
                _curBits -= 8;
            }
            FlushPacket();
        }
    }

    void CharOut(byte b)
    {
        _packet[_packetCount++] = b;
        if (_packetCount >= 255) FlushPacket();
    }

    void FlushPacket()
    {
        if (_packetCount == 0) return;
        _out.WriteByte((byte)_packetCount);
        _out.Write(_packet, 0, _packetCount);
        _packetCount = 0;
    }
}

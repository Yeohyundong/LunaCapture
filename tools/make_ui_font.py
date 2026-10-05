"""
LunaCapture UI 글꼴 만들기: Pretendard(SIL OFL 1.1)를 앱에 필요한 글자만 남기고 줄인 뒤 "Luna Sans"로 이름을 바꾼다.

- 남기는 글자: 영문·숫자·기호, 자주 쓰는 한글 2,350자(KS X 1001), 한글 자모(입력 중 조합 표시용)
- 없는 글자(드문 한글 등)는 Windows가 자동으로 맑은 고딕으로 대신 그린다.
- OFL 규칙상 수정한 글꼴은 원래 이름(Reserved Font Name "Pretendard")을 쓸 수 없어 이름을 바꾼다.

사용법: python make_ui_font.py <Pretendard-Regular.ttf> <Pretendard-Bold.ttf> <출력 폴더>
필요: pip install fonttools
"""
import sys
from pathlib import Path

from fontTools import subset
from fontTools.ttLib import TTFont

NEW_FAMILY = "Luna Sans"


def codepoints():
    cps = set(range(0x20, 0x7F))                 # ASCII
    cps |= set(range(0xA0, 0x100))               # Latin-1 (· × 등)
    ranges = [
        (0x2010, 0x206F),  # 일반 구두점 (– — … ‹ › 등)
        (0x20A0, 0x20CF),  # 통화 (₩)
        (0x2100, 0x218F),  # 문자형 기호, 숫자 형태
        (0x2190, 0x21FF),  # 화살표
        (0x2200, 0x22FF),  # 수학 기호
        (0x25A0, 0x25FF),  # 도형 (● ■ ▶)
        (0x2600, 0x27BF),  # 기타 기호, 딩뱃 (✓ ✕ ❚)
        (0x3000, 0x303F),  # CJK 기호
        (0x3131, 0x318F),  # 한글 호환 자모 (ㄱ ㅏ …)
    ]
    for lo, hi in ranges:
        cps |= set(range(lo, hi + 1))
    # KS X 1001 완성형 한글 2,350자 = EUC-KR로 표현 가능한 음절
    for cp in range(0xAC00, 0xD7A4):
        try:
            # 주의: 파이썬 euc-kr은 드문 한글도 8바이트 "조합 표현"으로 인코딩해 준다 → 정확히 2바이트인 것만 2,350자
            if len(chr(cp).encode("euc-kr")) == 2:
                cps.add(cp)
        except UnicodeEncodeError:
            pass
    return cps


def rename(font: TTFont, style: str):
    full = NEW_FAMILY if style == "Regular" else f"{NEW_FAMILY} {style}"
    ps = f"{NEW_FAMILY.replace(' ', '')}-{style}"
    # 굵은 글꼴은 별도 패밀리("Luna Sans Bold")의 '보통' 스타일로 등록한다.
    # GDI+는 메모리로 불러온 같은 패밀리의 여러 굵기를 제대로 구분하지 못해, 보통 글자도 굵게 그리는 문제가 있다.
    family = full
    for rec in font["name"].names:
        if rec.nameID == 1:
            rec.string = family
        elif rec.nameID == 2:
            rec.string = "Regular"
        elif rec.nameID == 16:
            rec.string = NEW_FAMILY
        elif rec.nameID == 17:
            rec.string = style
        elif rec.nameID == 3:
            rec.string = f"{ps};LunaCapture"
        elif rec.nameID == 4:
            rec.string = full
        elif rec.nameID == 6:
            rec.string = ps
    if style != "Regular":
        os2 = font["OS/2"]
        os2.fsSelection = (os2.fsSelection & ~0b100001) | 0b1000000  # bold/italic 해제, regular 설정
        font["head"].macStyle &= ~1
    # 원본 저작권(nameID 0)과 라이선스(13, 14)는 그대로 둔다.


def build(src: Path, style: str, out_dir: Path):
    options = subset.Options()
    # GDI+는 OpenType 조합 규칙(GSUB)을 쓰지 않는다. 규칙을 남기면 자모 조합 때문에 모든 한글이 딸려 와서 커지므로 자간(kern)만 남긴다.
    options.layout_features = ["kern"]
    options.name_IDs = ["*"]
    options.name_languages = ["*"]
    options.hinting = True          # 작은 글자에서 ClearType 선명도 유지
    options.notdef_outline = True
    options.glyph_names = False

    font = TTFont(str(src))
    sub = subset.Subsetter(options)
    sub.populate(unicodes=codepoints())
    sub.subset(font)
    rename(font, style)

    out = out_dir / f"LunaSans-{style}.ttf"
    font.save(str(out))
    print(f"{out.name}: {src.stat().st_size // 1024} KB -> {out.stat().st_size // 1024} KB, glyphs {len(font.getGlyphOrder())}")


if __name__ == "__main__":
    regular, bold, out_dir = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
    out_dir.mkdir(parents=True, exist_ok=True)
    build(regular, "Regular", out_dir)
    build(bold, "Bold", out_dir)

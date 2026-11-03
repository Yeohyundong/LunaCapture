using System.Runtime.InteropServices;
using LunaCapture;
using LunaCapture.Interop;

unsafe
{
    if (args.Length == 2 && args[0] == "--feedback-selftest") { LunaCapture.Services.FeedbackSelfTest.Run(args[1]); return; }
    if (args.Length > 0 && args[0] == "--apply-update") { LunaCapture.Services.UpdateInstaller.Apply(args); return; }
    if (args.Length == 2 && args[0] == "--update-selftest") { LunaCapture.Services.UpdateSelfTest.Run(args[1]); return; }
    if (args.Length == 2 && args[0] == "--update-download-selftest")
    {
        string cache = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, "download-test");
        var result = LunaCapture.Services.UpdateService.CheckAsync(new Version(0, 0, 0), cache).GetAwaiter().GetResult();
        if (result.Update == null) throw new Exception(result.Message);
        File.WriteAllText(args[1], $"GitHub Release 실제 다운로드·ZIP SHA-256·실행 파일 버전 검증 통과: {result.Update.Version}\n");
        return;
    }
    if (args.Length == 2 && args[0] == "--gif-selftest")
    {
        LunaCapture.Gif.GifSelfTest.Run(args[1]);
        return;
    }
    if (args.Length == 2 && args[0] == "--make-icon")
    {
        GdiPlus.Startup();
        UiFonts.LoadEmbedded();
        LunaCapture.Services.AppIcon.WriteIco(args[1]);
        GdiPlus.Shutdown();
        return;
    }
    if (args.Length is 3 or 4 && args[0] == "--font-sample")
    {
        // 개발용: 후보 글꼴들을 앱과 같은 방식(GDI+, ClearType)으로 그려 비교 이미지를 만든다.
        // 4번째 인자: 설치하지 않고 불러올 글꼴 파일들 ("a.ttf|b.ttf")
        GdiPlus.Startup();
        UiFonts.LoadEmbedded();
        if (args.Length == 4)
            foreach (var file in args[3].Split('|')) UiFonts.AddFontFile(file);
        string[] families = args[2].Split('|');
        int rowH = 150, w = 560;
        using var surf = new LunaCapture.Graphics.GdiSurface(w, rowH * families.Length);
        using (var g = new Gfx(surf.Dc))
        {
            g.FillRect(0xFF232329, 0, 0, w, rowH * families.Length);
            for (int i = 0; i < families.Length; i++)
            {
                float y = i * rowH + 14;
                if (i > 0) g.FillRect(0xFF3A3A44, 0, i * rowH, w, 1);
                using var tag = new GdiFont("Consolas", 12, false);
                using var title = new GdiFont(families[i], 16, true);
                using var body = new GdiFont(families[i], 14, false);
                using var small = new GdiFont(families[i], 13, false);
                g.Text($"{i + 1}. {families[i]}", tag, 0xFFFFC53D, 20, y);
                g.Text("PrtSc 키 설정 · GIF 녹화 1234", title, 0xFFECECF1, 20, y + 24);
                g.Text("PrtSc 키를 다른 프로그램(PicPick)이 쓰고 있어요.", body, 0xFFECECF1, 20, y + 54);
                g.Text("LunaCapture가 PrtSc를 우선 사용하도록 할까요?", body, 0xFFECECF1, 20, y + 78);
                g.Text("· 10초 녹화 시 예상 약 2.2MB · 0.6 Mbps · 화질 보통", small, 0xFF9A9AA6, 20, y + 106);
            }
        }
        LunaCapture.Graphics.PngEncoder.Save(surf.Crop(new RECT(0, 0, surf.Width, surf.Height)), args[1]);
        GdiPlus.Shutdown();
        return;
    }
    if (args.Length == 2 && args[0] == "--preview-record-setup")
    {
        // 개발용: 녹화 설정 창만 띄워 본다. 형식은 인자(gif/mp4)로, 설정은 저장하지 않음.
        User32.SetProcessDpiAwarenessContext(Win.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        GdiPlus.Startup();
        UiFonts.LoadEmbedded();
        LunaCapture.Services.AppSettings.Load();
        LunaCapture.Services.AppSettings.Current.RecordFormat = args[1] == "mp4" ? 1 : 0;
        var rect = new RECT(300, 200, 1000, 600);
        _ = new LunaCapture.Recording.RecordSetupWindow(LunaCapture.Recording.GifEstimator.Measure(rect),
            (_, _) => User32.PostQuitMessage(0), () => User32.PostQuitMessage(0));
        MSG m;
        while (User32.GetMessageW(&m, 0, 0, 0) > 0) { User32.TranslateMessage(&m); User32.DispatchMessageW(&m); }
        GdiPlus.Shutdown();
        return;
    }
    if (args.Length == 2 && args[0] == "--number-style-sample")
    {
        GdiPlus.Startup(); UiFonts.LoadEmbedded();
        using var surface = new LunaCapture.Graphics.GdiSurface(900, 360);
        using (var g = new Gfx(surface.Dc))
        {
            for (int y = 0; y < 360; y += 20) for (int x = 0; x < 900; x += 20)
            {
                uint[] palette = [0xFF434349, 0xFFF3E8D5, 0xFFACAFB8, 0xFF7E9F8A, 0xFF8F6D85, 0xFFD39568];
                g.FillRect(palette[(x / 20 * 3 + y / 20) % palette.Length], x, y, 20, 20);
            }
            for (int style = 0; style < 3; style++)
            {
                var n = new LunaCapture.Editor.NumberAnnotation { Number = style + 1, Center = new(30 + style * 300, 55), End = new(275 + style * 300, 320), Size = 34, Width = 4, Color = 0xFFFF2020, Square = style > 0, Glow = style == 2, HasBox = true };
                n.Draw(surface, g);
            }
        }
        Gdi32.GdiFlush();
        LunaCapture.Graphics.PngEncoder.Save(surface.Crop(new RECT(0, 0, 900, 360)), args[1]);
        GdiPlus.Shutdown(); return;
    }
    if (args.Length == 2 && args[0] == "--editor-sample")
    {
        // 개발용: 편집기 툴바와 스티커·번호·말풍선을 화면에 띄우지 않고 이미지로 그려 본다.
        GdiPlus.Startup();
        UiFonts.LoadEmbedded();
        int w = 980, h = 400;
        using var surf = new LunaCapture.Graphics.GdiSurface(w, h);
        using (var g = new Gfx(surf.Dc))
        {
            g.FillRect(0xFF1C1C20, 0, 0, w, h);
            var tb = new LunaCapture.Editor.Toolbar();
            tb.SetScale(1f);
            tb.Layout(w);
            tb.CurrentTool = LunaCapture.Editor.Tool.Rect;
            tb.CurrentColor = 0xFFE5484D;
            tb.ThicknessIndex = 1;
            tb.Draw(g, w);
            // 캡처 이미지처럼 밝은 바탕
            g.FillRect(0xFFF4F5F7, 16, 64, w - 32, h - 80);
            g.FillRect(0xFF2F3A4A, 16, 64, (w - 32) / 2, h - 80); // 어두운 바탕 절반 (흰 풍선 경계 확인용)
        }
        using (var g = new Gfx(surf.Dc, forImage: true))
        {
            // 꼬리 방향별 흰 말풍선 (빨간 점 = 클릭한 곳)
            var dirs = LunaCapture.Editor.Stickers.Tails;
            for (int i = 0; i < dirs.Length; i++)
            {
                var tail = new PointF(110 + (i % 4) * 230, i < 4 ? 190 : 330);
                new LunaCapture.Editor.BubbleAnnotation { Color = LunaCapture.Editor.Toolbar.Palette[7], FontSize = 16, Tail = tail, Dir = dirs[i], Text = dirs[i].ToString() }.Draw(surf, g);
                g.FillEllipse(0xFFE5484D, tail.X - 3, tail.Y - 3, 6, 6);
            }
        }
        LunaCapture.Graphics.PngEncoder.Save(surf.Crop(new RECT(0, 0, w, h)), args[1]);
        GdiPlus.Shutdown();
        return;
    }
    if (args.Length == 2 && args[0] == "--shape-sample")
    {
        // 개발용: 도형 6가지 + 안에 글자 (화면에 띄우지 않고 이미지로)
        GdiPlus.Startup();
        UiFonts.LoadEmbedded();
        int w = 900, h = 320;
        using var surf = new LunaCapture.Graphics.GdiSurface(w, h);
        using (var g0 = new Gfx(surf.Dc)) { g0.FillRect(0xFFF4F5F7, 0, 0, w, h); g0.FillRect(0xFF2F3A4A, 0, h / 2, w, h / 2); }
        using (var g = new Gfx(surf.Dc, forImage: true))
        {
            var styles = new (LunaCapture.Editor.ShapeKind, LunaCapture.Editor.FillStyle, uint, string)[]
            {
                (LunaCapture.Editor.ShapeKind.Rect, LunaCapture.Editor.FillStyle.Outline, LunaCapture.Editor.Toolbar.Palette[0], "테두리"),
                (LunaCapture.Editor.ShapeKind.Rect, LunaCapture.Editor.FillStyle.Solid, LunaCapture.Editor.Toolbar.Palette[4], "채움\n두 줄"),
                (LunaCapture.Editor.ShapeKind.Rect, LunaCapture.Editor.FillStyle.Tint, LunaCapture.Editor.Toolbar.Palette[5], "반투명"),
                (LunaCapture.Editor.ShapeKind.Ellipse, LunaCapture.Editor.FillStyle.Outline, LunaCapture.Editor.Toolbar.Palette[3], "원 테두리"),
                (LunaCapture.Editor.ShapeKind.Ellipse, LunaCapture.Editor.FillStyle.Solid, LunaCapture.Editor.Toolbar.Palette[2], "노랑 원"),
                (LunaCapture.Editor.ShapeKind.Ellipse, LunaCapture.Editor.FillStyle.Tint, LunaCapture.Editor.Toolbar.Palette[1], "반투명 원"),
            };
            for (int i = 0; i < styles.Length; i++)
            {
                float x = 20 + (i % 3) * 290, y = i < 3 ? 25 : 185;
                var (shape, fill, color, text) = styles[i];
                new LunaCapture.Editor.RectAnnotation { A = new(x, y), B = new(x + 250, y + 110), Shape = shape, Fill = fill, Color = color, Width = 4, FontSize = 22, Text = text }.Draw(surf, g);
            }
        }
        LunaCapture.Graphics.PngEncoder.Save(surf.Crop(new RECT(0, 0, w, h)), args[1]);
        GdiPlus.Shutdown();
        return;
    }
    if (args.Length == 2 && args[0] == "--history-selftest")
    {
        // 개발용: 캡처 기록 저장·불러오기·밀어내기 시험 (LUNA_HISTORY_DIR 임시 폴더에서). 결과는 args[1] 텍스트 파일로.
        GdiPlus.Startup();
        UiFonts.LoadEmbedded();
        LunaCapture.Services.AppSettings.Load();
        var log = new System.Text.StringBuilder();
        var orig = new LunaCapture.Graphics.PixelImage(640, 400);
        for (int i = 0; i < orig.Bgra.Length; i += 4) { orig.Bgra[i] = (byte)(i / 4 % 640 * 255 / 640); orig.Bgra[i + 1] = 180; orig.Bgra[i + 2] = 90; orig.Bgra[i + 3] = 255; }
        var piece = new LunaCapture.Graphics.PixelImage(30, 20); // 반투명 그라데이션 조각 (붙여넣기 그림 시험)
        for (int i = 0; i < piece.Bgra.Length; i += 4) { piece.Bgra[i] = 240; piece.Bgra[i + 1] = (byte)(i / 4 % 30 * 8); piece.Bgra[i + 2] = 40; piece.Bgra[i + 3] = (byte)(i / 4 / 30 * 12 + 20); }
        var items = new List<LunaCapture.Editor.Annotation>
        {
            new LunaCapture.Editor.ArrowAnnotation { Color = 0xFFE5484D, Width = 4, From = new(30, 300), To = new(200, 120) },
            new LunaCapture.Editor.RectAnnotation { Color = 0xFF0090FF, Width = 4, A = new(250, 40), B = new(450, 140), Shape = LunaCapture.Editor.ShapeKind.Rect, Fill = LunaCapture.Editor.FillStyle.Solid, Text = "도형 글자", FontSize = 22 },
            new LunaCapture.Editor.PenAnnotation { Color = 0xFF30A46C, Width = 5, Points = [new(40, 40), new(80, 60), new(120, 40)] },
            new LunaCapture.Editor.PenAnnotation { Color = 0x70FFC53D, Width = 22, Highlighter = true, Points = [new(40, 200), new(300, 200)] },
            new LunaCapture.Editor.TextAnnotation { Color = 0xFFFFFFFF, Position = new(480, 40), Text = "텍스트", FontSize = 24 },
            new LunaCapture.Editor.BubbleAnnotation { Color = 0xFF8B7CF6, Tail = new(500, 330), Dir = LunaCapture.Editor.TailDir.BottomRight, Text = "말풍선", FontSize = 20 },
            new LunaCapture.Editor.StickerAnnotation { Kind = LunaCapture.Editor.StickerKind.Star, Center = new(560, 200), Size = 52 },
            new LunaCapture.Editor.NumberAnnotation { Number = 3, Color = 0xFFF76B15, Center = new(380, 250), Size = 34, Square = true, Glow = true, HasBox = true, End = new(450, 290), Width = 3 },
            new LunaCapture.Editor.MosaicAnnotation { A = new(250, 300), B = new(360, 380), BlockSize = 12 },
            new LunaCapture.Editor.PatchAnnotation { Color = 0xFF202024, A = new(470, 250), B = new(540, 300) },
            new LunaCapture.Editor.ImageAnnotation { Image = piece, A = new(470.5f, 250), B = new(530.5f, 290) },
            new LunaCapture.Editor.SpotlightAnnotation { A = new(20, 20), B = new(460, 390) },
            new LunaCapture.Editor.SpotlightAnnotation { A = new(480, 20), B = new(620, 120) },
        };
        foreach (var shape in Enum.GetValues<LunaCapture.Editor.ShapeKind>())
            foreach (var fill in Enum.GetValues<LunaCapture.Editor.FillStyle>())
                items.Add(new LunaCapture.Editor.RectAnnotation { A = new(30 + (int)fill * 65, 40 + (int)shape * 65), B = new(85 + (int)fill * 65, 90 + (int)shape * 65), Shape = shape, Fill = fill, Color = 0x9F0090FF, Width = 3 });
        var expected = LunaCapture.Editor.Composer.Compose(orig, items);

        LunaCapture.Services.AppSettings.Current.HistoryMax = 3;
        string? id = LunaCapture.Services.HistoryStore.AddImage(orig, "C:\\nowhere\\a.png");
        LunaCapture.Services.HistoryStore.UpdateImage(id!, expected, items, null);
        var entry = LunaCapture.Services.HistoryStore.LoadAll().First(e => e.Id == id);
        var loaded = LunaCapture.Services.HistoryStore.LoadItems(entry);
        var reOrig = LunaCapture.Services.HistoryStore.LoadOriginal(id!)!;
        var actual = LunaCapture.Editor.Composer.Compose(reOrig, loaded);
        int diff = 0;
        for (int i = 0; i < expected.Bgra.Length; i++) if (expected.Bgra[i] != actual.Bgra[i]) diff++;
        log.AppendLine($"주석 {items.Count}개 저장 → 다시 읽음 {loaded.Count}개, 종류: {string.Join(",", loaded.Select(a => a.GetType().Name.Replace("Annotation", "")))}");
        log.AppendLine($"원본+주석 다시 합성: 다른 바이트 {diff}개 / {expected.Bgra.Length}");
        if (diff != 0 || loaded.Count != items.Count) throw new Exception("주석 저장·복원 불일치");
        var badge = loaded.OfType<LunaCapture.Editor.NumberAnnotation>().Single();
        if (!badge.Glow || !badge.Square || !badge.HasBox || badge.End.X != 450 || badge.Width != 3) throw new Exception("번호 영역 복원 실패");
        badge.Offset(10, 20);
        if (badge.End.X != 460 || badge.Center.X != 390 || !badge.HitBadge(badge.BadgeCenter, 0)) throw new Exception("번호 이동 실패");
        foreach (int dx in new[] { -100, 100 }) foreach (int dy in new[] { -100, 100 })
        {
            badge.Center = new(200, 200); badge.End = new(200 + dx, 200 + dy);
            foreach (bool square in new[] { false, true })
            {
                badge.Square = square;
                var box = LunaCapture.Editor.Geo.FromPoints(badge.Center, badge.End);
                var c = badge.BadgeCenter;
                if (!LunaCapture.Editor.Geo.Inside(box, new(c.X - badge.BadgeSize / 2, c.Y - badge.BadgeSize / 2)) || !LunaCapture.Editor.Geo.Inside(box, new(c.X + badge.BadgeSize / 2, c.Y + badge.BadgeSize / 2))) throw new Exception("번호 배지 안쪽 배치 실패");
            }
        }
        if (LunaCapture.Editor.ColorPaletteWindow.Hsv(0, 1, 1, 128) != 0x80FF0000 || LunaCapture.Editor.ColorPaletteWindow.Hsv(120, 1, 1) != 0xFF00FF00) throw new Exception("HSV 변환 실패");
        var oldBadge = LunaCapture.Editor.AnnotationJson.FromDto(new LunaCapture.Editor.AnnotationDto { Type = "number", Number = 1, Size = 32, Pts = [10, 20] }) as LunaCapture.Editor.NumberAnnotation;
        var resizeBadge = new LunaCapture.Editor.NumberAnnotation { Center = new(100, 100), End = new(240, 220), Size = 44, Width = 4, HasBox = true, Square = true, Outside = true };
        for (int handle = 0; handle < 4; handle++)
        {
            foreach (bool reversed in new[] { false, true })
            {
                resizeBadge.Center = reversed ? new(240, 220) : new(100, 100);
                resizeBadge.End = reversed ? new(100, 100) : new(240, 220);
                var opposite = handle switch { 0 => new PointF(240, 220), 1 => new PointF(100, 220), 2 => new PointF(240, 100), _ => new PointF(100, 100) };
                resizeBadge.ResizeBox(handle, new(handle is 0 or 2 ? 80 : 270, handle is 0 or 1 ? 70 : 250));
                var box = LunaCapture.Editor.Geo.FromPoints(resizeBadge.Center, resizeBadge.End);
                if (resizeBadge.Size != 44 || !LunaCapture.Editor.Geo.Inside(box, opposite)
                    || (reversed ? resizeBadge.Center.X <= resizeBadge.End.X : resizeBadge.Center.X >= resizeBadge.End.X)) throw new Exception("네 모서리 크기 조절 실패");
                if (LunaCapture.Editor.Geo.Inside(box, resizeBadge.BadgeCenter)) throw new Exception("바깥쪽 배치 실패");
            }
        }
        var restoredOutside = (LunaCapture.Editor.NumberAnnotation)LunaCapture.Editor.AnnotationJson.FromDto(LunaCapture.Editor.AnnotationJson.ToDto(resizeBadge))!;
        if (!restoredOutside.Outside || restoredOutside.Size != 44) throw new Exception("위치·크기 기록 복원 실패");
        log.AppendLine("안쪽/바깥쪽 배치 · 네 모서리 크기 조절 · 배지 크기 유지 통과");
        var fontBadge = new LunaCapture.Editor.NumberAnnotation { Number = 1, FontSize = 24, Size = 32, Center = new(100, 100), End = new(600, 500), HasBox = true, Square = true };
        float oneDigitSize = fontBadge.BadgeSize;
        fontBadge.Number = 88888;
        if (fontBadge.BadgeSize <= oneDigitSize || fontBadge.EffectiveFontSize != 24) throw new Exception("글자 크기 기반 배지 자동 조절 실패");
        foreach (bool outside in new[] { false, true }) for (int corner = 0; corner < 4; corner++)
        {
            fontBadge.Corner = corner; fontBadge.Outside = outside;
            var c = fontBadge.BadgeCenter;
            if (((corner & 1) == 0 ? c.X >= 350 : c.X <= 350) || ((corner & 2) == 0 ? c.Y >= 300 : c.Y <= 300)) throw new Exception("4개 모서리 배치 실패");
            if (LunaCapture.Editor.Geo.Inside(LunaCapture.Editor.Geo.FromPoints(fontBadge.Center, fontBadge.End), c) == outside) throw new Exception("모서리 안쪽/바깥쪽 실패");
        }
        var fontRestored = (LunaCapture.Editor.NumberAnnotation)LunaCapture.Editor.AnnotationJson.FromDto(LunaCapture.Editor.AnnotationJson.ToDto(fontBadge))!;
        if (fontRestored.Corner != 3 || fontRestored.FontSize != 24 || fontRestored.BadgeSize != fontBadge.BadgeSize) throw new Exception("번호 폰트·모서리 복원 실패");
        log.AppendLine("4개 모서리 × 안/밖 · 폰트 고정 및 여러 자리 배지 자동 확장 통과");
        for (int q = 0; q < 4; q++) foreach (int sx in new[] { -1, 1 }) foreach (int sy in new[] { -1, 1 })
        {
            fontBadge.Center = new(300, 300); fontBadge.End = new(300 + sx * 250, 300 + sy * 200); fontBadge.Quadrant = q;
            var c = fontBadge.BadgeCenter;
            int actualX = Math.Sign((c.X - 300) * sx), actualY = Math.Sign((c.Y - 300) * sy);
            if (actualX != (q is 0 or 3 ? -1 : 1) || actualY != (q is 0 or 1 ? -1 : 1)) throw new Exception("드래그 방향별 사분면 반전 실패");
            var restoredQ = (LunaCapture.Editor.NumberAnnotation)LunaCapture.Editor.AnnotationJson.FromDto(LunaCapture.Editor.AnnotationJson.ToDto(fontBadge))!;
            if (restoredQ.Quadrant != q || restoredQ.BadgeCenter.X != c.X || restoredQ.BadgeCenter.Y != c.Y) throw new Exception("사분면 복원 실패");
        }
        log.AppendLine("시작 꼭짓점 기준 4사분면 × 드래그 4방향 반전 및 기록 복원 통과");
        if (oldBadge == null || oldBadge.Square || oldBadge.HasBox) throw new Exception("이전 기록 호환 실패");
        if (LunaCapture.Editor.EditorWindow.NextNumber([new LunaCapture.Editor.NumberAnnotation { Number = 7 }, new LunaCapture.Editor.NumberAnnotation { Number = 7 }, new LunaCapture.Editor.NumberAnnotation { Number = 2 }]) != 8) throw new Exception("중복 번호 후 다음 번호 실패");
        var numberingWithHidden = new List<LunaCapture.Editor.Annotation>
        {
            new LunaCapture.Editor.NumberAnnotation { Number = 1, Center = new(50, 50), Size = 32 },
            new LunaCapture.Editor.NumberAnnotation { Number = 2, Center = new(100, 50), Size = 32 },
            new LunaCapture.Editor.NumberAnnotation { Number = 4, Center = new(-100, 50), Size = 32 },
            new LunaCapture.Editor.NumberAnnotation { Number = 5, Center = new(500, 50), Size = 32 }
        };
        if (LunaCapture.Editor.EditorWindow.NextNumber(numberingWithHidden, new RectF(0, 0, 300, 200)) != 3) throw new Exception("화면 밖 숨은 번호 배제 실패");
        numberingWithHidden.Add(new LunaCapture.Editor.NumberAnnotation { Number = 7, Center = new(295, 150), Size = 32 });
        if (LunaCapture.Editor.EditorWindow.NextNumber(numberingWithHidden, new RectF(0, 0, 300, 200)) != 8) throw new Exception("일부 보이는 번호 유지 실패");
        log.AppendLine("화면 밖 숨은 번호 배제 및 가장자리 번호 유지 통과");
        if (!LunaCapture.Editor.ColorPaletteWindow.TryParseHex("#12AB3480", out uint rgba) || rgba != 0x8012AB34
            || !LunaCapture.Editor.ColorPaletteWindow.TryParseHex("112233", out uint rgb) || rgb != 0xFF112233
            || LunaCapture.Editor.ColorPaletteWindow.TryParseHex("#oops", out _)) throw new Exception("HEX 변환 실패");
        if (Environment.GetEnvironmentVariable("LUNA_SETTINGS_DIR") != null)
        {
            LunaCapture.Services.AppSettings.Current.SelectedColor = rgba;
            LunaCapture.Services.AppSettings.Current.CustomColors = [rgba, rgb];
            LunaCapture.Services.AppSettings.Current.NumberSquare = true;
            LunaCapture.Services.AppSettings.Current.NumberOutside = true;
            LunaCapture.Services.AppSettings.Current.NumberSize = 2;
            LunaCapture.Services.AppSettings.Current.NumberCorner = 3;
            LunaCapture.Services.AppSettings.Current.NumberQuadrant = 1;
            LunaCapture.Services.AppSettings.Current.NumberFontSize = 28;
            LunaCapture.Services.AppSettings.Save();
            LunaCapture.Services.AppSettings.Load();
            if (!LunaCapture.Services.AppSettings.Current.NumberOutside || LunaCapture.Services.AppSettings.Current.NumberSize != 2) throw new Exception("번호 기본 위치·크기 설정 저장 실패");
            if (LunaCapture.Services.AppSettings.Current.NumberCorner != 3 || LunaCapture.Services.AppSettings.Current.NumberFontSize != 28) throw new Exception("번호 폰트·모서리 설정 저장 실패");
            if (LunaCapture.Services.AppSettings.Current.NumberQuadrant != 1) throw new Exception("사분면 설정 저장 실패");
            if (LunaCapture.Services.AppSettings.Current.SelectedColor != rgba || LunaCapture.Services.AppSettings.Current.CustomColors.Count != 2 || !LunaCapture.Services.AppSettings.Current.NumberSquare) throw new Exception("팔레트 설정 저장 실패");
            log.AppendLine("선택 색상·맞춤 팔레트·번호 스타일 설정 저장/복원 통과");
        }
        log.AppendLine("9가지 도형 · 번호 영역/이동 · 이전 기록 호환 · HSV 변환 통과");
        LunaCapture.Graphics.PngEncoder.Save(actual, Path.Combine(LunaCapture.Services.HistoryStore.Dir, "_recomposed.png"));

        for (int i = 0; i < 4; i++) { Thread.Sleep(5); LunaCapture.Services.HistoryStore.AddRecording(i % 2 == 0 ? "gif" : "mp4", $"C:\\nowhere\\r{i}.gif", expected, 640, 400); }
        var all = LunaCapture.Services.HistoryStore.LoadAll();
        log.AppendLine($"최대 3개 설정 후 5건 추가 → 남은 기록 {all.Count}개 (최신: {string.Join(",", all.Select(e => e.Kind))}), 처음 사진 기록 남아 있음: {all.Any(e => e.Id == id)}");
        File.WriteAllText(args[1], log.ToString());
        GdiPlus.Shutdown();
        return;
    }
    if (args.Length == 2 && args[0] == "--clipboard-read")
    {
        // 개발용: 지금 클립보드의 그림을 읽어 PNG로 저장 (붙여넣기 시험)
        GdiPlus.Startup();
        var img = LunaCapture.Services.ClipboardService.GetImage(0);
        File.WriteAllText(args[1] + ".txt", img == null ? "없음" : $"{img.Width}x{img.Height} alpha0={img.Bgra.Where((b, i) => i % 4 == 3 && b == 0).Count()}");
        if (img != null) LunaCapture.Graphics.PngEncoder.Save(img, args[1]);
        GdiPlus.Shutdown();
        return;
    }
    if (args.Length == 1 && args[0] == "--preview-editor")
    {
        // 개발용: 기록 패널이 붙은 편집 창 (LUNA_HISTORY_DIR 임시 폴더에 예시 사진 3장을 만들어서)
        User32.SetProcessDpiAwarenessContext(Win.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        GdiPlus.Startup();
        UiFonts.LoadEmbedded();
        LunaCapture.Services.AppSettings.Load();
        LunaCapture.Services.AppSettings.Current.EditorHistoryPanel = true;
        uint[] colors = [0xFF4F8EF7, 0xFF35B37E, 0xFFF0A23A];
        for (int n = 0; n < 3; n++)
        {
            var img = new LunaCapture.Graphics.PixelImage(800, 500);
            uint c = colors[n];
            for (int i = 0; i < img.Bgra.Length; i += 4) { img.Bgra[i] = (byte)c; img.Bgra[i + 1] = (byte)(c >> 8); img.Bgra[i + 2] = (byte)(c >> 16); img.Bgra[i + 3] = 255; }
            var id = LunaCapture.Services.HistoryStore.AddImage(img, null)!;
            var items = new List<LunaCapture.Editor.Annotation>
            {
                new LunaCapture.Editor.RectAnnotation { Color = 0xFFFFFFFF, Width = 4, A = new(80, 80), B = new(420, 220), Fill = LunaCapture.Editor.FillStyle.Solid, Text = $"사진 {3 - n}", FontSize = 40 },
                new LunaCapture.Editor.ArrowAnnotation { Color = 0xFFE5484D, Width = 6, From = new(600, 420), To = new(440, 240) },
            };
            LunaCapture.Services.HistoryStore.UpdateImage(id, LunaCapture.Editor.Composer.Compose(img, items), items, null);
            Thread.Sleep(5);
        }
        LunaCapture.Editor.EditorDocument? Open(LunaCapture.Services.HistoryEntry e)
        {
            var o = LunaCapture.Services.HistoryStore.LoadOriginal(e.Id);
            return o == null ? null : new LunaCapture.Editor.EditorDocument
            {
                Original = o, Items = LunaCapture.Services.HistoryStore.LoadItems(e), HistoryId = e.Id,
                Save = (img, orig, its) => LunaCapture.Services.HistoryStore.UpdateImage(e.Id, img, its, null, orig),
            };
        }
        var first = Open(LunaCapture.Services.HistoryStore.LoadAll()[0])!;
        _ = new LunaCapture.Editor.EditorWindow(first, "미리보기", () => User32.PostQuitMessage(0), Open);
        MSG m;
        while (User32.GetMessageW(&m, 0, 0, 0) > 0) { User32.TranslateMessage(&m); User32.DispatchMessageW(&m); }
        GdiPlus.Shutdown();
        return;
    }
    if (args.Length == 1 && args[0] == "--preview-history")
    {
        // 개발용: 기록 창만 띄워 본다 (LUNA_HISTORY_DIR 임시 폴더와 함께)
        User32.SetProcessDpiAwarenessContext(Win.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        GdiPlus.Startup();
        UiFonts.LoadEmbedded();
        LunaCapture.Services.AppSettings.Load();
        _ = new LunaCapture.Editor.HistoryWindow(_ => { }, _ => { }, () => User32.PostQuitMessage(0));
        MSG m;
        while (User32.GetMessageW(&m, 0, 0, 0) > 0) { User32.TranslateMessage(&m); User32.DispatchMessageW(&m); }
        GdiPlus.Shutdown();
        return;
    }
    if (args.Length == 2 && args[0] == "--test-shortcut")
    {
        LunaCapture.Services.Installer.TestShortcut(args[1]);
        return;
    }
    if (args.Length == 2 && args[0] == "--preview-install")
    {
        // 개발용: 설치/업데이트 창만 띄워 본다 (설치하지 않음)
        User32.SetProcessDpiAwarenessContext(Win.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        GdiPlus.Startup();
        UiFonts.LoadEmbedded();
        _ = new LunaCapture.Capture.InstallPrompt(args[1] == "update", "0.9.0", (_, _) => User32.PostQuitMessage(0));
        MSG m;
        while (User32.GetMessageW(&m, 0, 0, 0) > 0) { User32.TranslateMessage(&m); User32.DispatchMessageW(&m); }
        GdiPlus.Shutdown();
        return;
    }
    if (args.Length == 1 && args[0] == "--preview-prompt")
    {
        // 개발용: PrtSc 설정 팝업만 띄워 본다 (답은 저장하지 않음)
        User32.SetProcessDpiAwarenessContext(Win.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        GdiPlus.Startup();
        UiFonts.LoadEmbedded();
        _ = new LunaCapture.Capture.TakeoverPrompt("PicPick", _ => User32.PostQuitMessage(0));
        MSG m;
        while (User32.GetMessageW(&m, 0, 0, 0) > 0) { User32.TranslateMessage(&m); User32.DispatchMessageW(&m); }
        GdiPlus.Shutdown();
        return;
    }
    if (args.Length == 2 && args[0] == "--gif-bench")
    {
        User32.SetProcessDpiAwarenessContext(Win.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        LunaCapture.Gif.GifSelfTest.Bench(args[1]);
        return;
    }

    // "설치된 앱"에서 제거를 누르면 이 인자로 실행된다
    if (args.Length == 1 && args[0] == "--uninstall")
    {
        const int IDYES = 6;
        if (User32.MessageBoxW(0, "LunaCapture를 제거할까요?\n\n찍어 둔 캡처 파일과 설정은 지워지지 않아요.", "LunaCapture 제거", 0x4 | 0x20) == IDYES)
        {
            LunaCapture.Services.Installer.CloseRunningInstance();
            LunaCapture.Services.Installer.Uninstall();
            User32.MessageBoxW(0, "LunaCapture를 제거했어요.", "LunaCapture", 0x40);
        }
        return;
    }

    User32.SetProcessDpiAwarenessContext(Win.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    GdiPlus.Startup();
    UiFonts.LoadEmbedded();
    LunaCapture.Services.AppSettings.Load();
    NativeWindow.DefaultIcon = LunaCapture.Services.AppIcon.Load(User32.GetSystemMetrics(11 /* SM_CXICON */));
    NativeWindow.DefaultSmallIcon = LunaCapture.Services.AppIcon.Load(User32.GetSystemMetrics(49 /* SM_CXSMICON */));

    // 처음 실행(또는 새 버전 실행)이면 설치/업데이트를 권한다
    var offer = LunaCapture.Services.Installer.ShouldOffer();
    if (offer != LunaCapture.Services.Installer.Offer.None)
    {
        bool install = false, autoStart = false;
        _ = new LunaCapture.Capture.InstallPrompt(
            offer == LunaCapture.Services.Installer.Offer.Update,
            LunaCapture.Services.Installer.InstalledVersion?.ToString(3) ?? "",
            (ok, auto) => { install = ok; autoStart = auto; User32.PostQuitMessage(0); });
        MSG pm;
        while (User32.GetMessageW(&pm, 0, 0, 0) > 0) { User32.TranslateMessage(&pm); User32.DispatchMessageW(&pm); }

        if (install)
        {
            try
            {
                LunaCapture.Services.Installer.Install(autoStart);
                LunaCapture.Services.Installer.LaunchInstalled(); // 설치된 위치의 exe로 다시 시작
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                User32.MessageBoxW(0, "설치하지 못했어요:\n" + ex.Message, "LunaCapture", 0x10);
            }
            GdiPlus.Shutdown();
            return;
        }
        if (offer == LunaCapture.Services.Installer.Offer.Install)
        {
            LunaCapture.Services.AppSettings.Current.InstallDeclinedVersion = LunaCapture.Services.Installer.Version;
            LunaCapture.Services.AppSettings.Save();
        }
    }

    // 중복 실행 방지
    nint mutex = Kernel32.CreateMutexW(0, 1, "LunaCapture.SingleInstance");
    const int ERROR_ALREADY_EXISTS = 183;
    if (mutex == 0 || Marshal.GetLastPInvokeError() == ERROR_ALREADY_EXISTS)
    {
        User32.MessageBoxW(0, "LunaCapture가 이미 실행 중입니다.\n트레이 아이콘을 확인하세요.", "LunaCapture", 0x40);
        GdiPlus.Shutdown();
        return;
    }

    try
    {
        var app = new TrayApp(args.Contains("--skip-update-once"));
        LunaCapture.Services.Installer.RefreshInstalledVersion();
        LunaCapture.Services.UpdateInstaller.SignalHealthy(args);
        GC.KeepAlive(app);
        LunaCapture.Services.MemoryTrimmer.Trim(); // 시작 준비에 쓴 메모리를 돌려주고 트레이에서 가볍게 대기

        MSG msg;
        while (User32.GetMessageW(&msg, 0, 0, 0) > 0)
        {
            User32.TranslateMessage(&msg);
            User32.DispatchMessageW(&msg);
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex);
        User32.MessageBoxW(0, "오류가 발생했습니다:\n" + ex.Message, "LunaCapture", 0x10);
    }

    GdiPlus.Shutdown();
    GC.KeepAlive(mutex);
}

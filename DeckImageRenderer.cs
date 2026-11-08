using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Text;

namespace EdoProDeckImage
{
    // ──────────────────────────────────────────────────────────────
    //  덱(.ydk) 파싱
    // ──────────────────────────────────────────────────────────────
    public sealed class DeckList
    {
        public string FilePath = "";
        public readonly List<int> Main = new List<int>();
        public readonly List<int> Extra = new List<int>();
        public readonly List<int> Side = new List<int>();

        public string FileName
        {
            get { return string.IsNullOrEmpty(FilePath) ? "(없음)" : Path.GetFileName(FilePath); }
        }

        public int Total { get { return Main.Count + Extra.Count + Side.Count; } }
    }

    // ──────────────────────────────────────────────────────────────
    //  렌더 옵션
    // ──────────────────────────────────────────────────────────────
    public sealed class DeckImageOptions
    {
        public string Title { get; set; } = "";
        public string PicsFolder { get; set; } = "";
        public int Columns { get; set; } = 10;
        public int CardWidth { get; set; } = 110;
        public int Gap { get; set; } = 5;
        public int Padding { get; set; } = 18;
        public string Theme { get; set; } = "dark";
        public bool ShowCounts { get; set; } = false;
        public bool ShowTitle { get; set; } = true;
        public bool ShowFooter { get; set; } = true;
        public bool ShowSide { get; set; } = true;
        public string FooterNote { get; set; } = "";

        /// <summary>추가 이미지 폴더(자동 탐색 결과 등). PicsFolder 와 함께 사용한다.</summary>
        public List<string> ExtraPicsFolders = new List<string>();

        public DeckImageOptions Clone()
        {
            var c = (DeckImageOptions)this.MemberwiseClone();
            c.ExtraPicsFolders = new List<string>(ExtraPicsFolders);
            return c;
        }

        /// <summary>
        /// 실제로 사용할 pics 폴더 목록 — PicsFolder 는 `;` 로 여러 개를 적을 수 있고,
        /// ExtraPicsFolders(자동 탐색)가 뒤에 붙는다. 존재하는 폴더만, 중복 제거.
        /// </summary>
        public List<string> ResolvePicsFolders()
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in CardImageStore.SplitFolders(PicsFolder))
            {
                string full;
                try { full = Path.GetFullPath(part); } catch { continue; }
                if (!Directory.Exists(full)) continue;
                if (seen.Add(full)) list.Add(full);
            }
            foreach (string part in ExtraPicsFolders)
            {
                if (string.IsNullOrWhiteSpace(part)) continue;
                string full;
                try { full = Path.GetFullPath(part); } catch { continue; }
                if (!Directory.Exists(full)) continue;
                if (seen.Add(full)) list.Add(full);
            }
            return list;
        }
    }

    public sealed class RenderStats
    {
        public int Width;
        public int Height;
        public int MainCount;
        public int ExtraCount;
        public int SideCount;
        public int Placed;
        public readonly List<int> Missing = new List<int>();
        public string Summary
        {
            get
            {
                string s = string.Format("{0}x{1}px · 메인 {2} / 엑스트라 {3} / 사이드 {4}",
                    Width, Height, MainCount, ExtraCount, SideCount);
                if (Missing.Count > 0) s += string.Format(" · 이미지 없음 {0}장", Missing.Count);
                return s;
            }
        }
    }

    internal sealed class ThemePalette
    {
        public string Name = "dark";
        public Color Background;
        public Color Band;
        public Color Header;
        public Color Accent;
        public Color Text;
        public Color SubText;
        public Color Missing;
        public Color CardBorder;

        public static ThemePalette Get(string theme)
        {
            if (string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase))
            {
                return new ThemePalette
                {
                    Name = "light",
                    Background = Color.FromArgb(243, 246, 251),
                    Band = Color.FromArgb(224, 233, 246),
                    Header = Color.FromArgb(206, 221, 242),
                    Accent = Color.FromArgb(42, 116, 205),
                    Text = Color.FromArgb(22, 36, 56),
                    SubText = Color.FromArgb(92, 108, 132),
                    Missing = Color.FromArgb(219, 226, 236),
                    CardBorder = Color.FromArgb(58, 70, 88)
                };
            }
            return new ThemePalette
            {
                Name = "dark",
                Background = Color.FromArgb(11, 28, 54),
                Band = Color.FromArgb(16, 39, 71),
                Header = Color.FromArgb(26, 60, 108),
                Accent = Color.FromArgb(120, 190, 255),
                Text = Color.FromArgb(238, 246, 255),
                SubText = Color.FromArgb(170, 190, 210),
                Missing = Color.FromArgb(40, 50, 66),
                CardBorder = Color.FromArgb(6, 16, 30)
            };
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  렌더러 (WinForms 의존 없음 → CLI/검증에서 그대로 사용)
    // ──────────────────────────────────────────────────────────────
    public static class DeckImageRenderer
    {
        private const double CardAspect = 254.0 / 177.0;   // EDOPro pics 실제 비율

        // ── .ydk 파싱 ────────────────────────────────────────────
        public static DeckList ParseYdk(string path)
        {
            var deck = new DeckList { FilePath = path };
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new FileNotFoundException(".ydk 파일을 찾을 수 없습니다: " + path, path);

            string section = "main";
            bool sawMarker = false;
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string s = raw.Trim().TrimStart('\uFEFF');
                if (s.Length == 0) continue;

                if (s[0] == '#' || s[0] == '!')
                {
                    string marker = s.TrimStart('#', '!').Trim().ToLowerInvariant();
                    if (marker == "main") { section = "main"; sawMarker = true; continue; }
                    if (marker == "extra") { section = "extra"; sawMarker = true; continue; }
                    if (marker == "side") { section = "side"; sawMarker = true; continue; }
                    continue;   // 주석 줄
                }

                int id;
                if (!int.TryParse(s, out id)) continue;     // 숫자 아닌 줄은 무시
                if (id <= 0) continue;

                if (!sawMarker) { deck.Main.Add(id); continue; }
                if (section == "extra") deck.Extra.Add(id);
                else if (section == "side") deck.Side.Add(id);
                else deck.Main.Add(id);
            }
            return deck;
        }

        // ── 이미지 폴더 자동 탐색 ────────────────────────────────
        public static bool LooksLikePicsFolder(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            try
            {
                int n = 0;
                foreach (string f in Directory.EnumerateFiles(dir, "*.jpg")) { if (++n >= 10) return true; }
                foreach (string f in Directory.EnumerateFiles(dir, "*.png")) { if (++n >= 10) return true; }
            }
            catch { }
            return false;
        }

        public static string GuessPicsFolder(string ydkPath)
        {
            List<string> all = GuessPicsFolders(ydkPath);
            return all.Count > 0 ? all[0] : "";
        }

        /// <summary>
        /// 이미지 폴더 자동 탐색 — **여러 개**를 찾는다(앞이 우선).
        ///   ① .ydk 위로 5단계의 pics
        ///   ② exe 옆/위로 3단계의 pics
        ///   ③ 흔한 EDOPro 설치 경로
        ///   ④ EDOPro 의 repositories\*\pics (커스텀 카드 배포본)
        /// </summary>
        public static List<string> GuessPicsFolders(string ydkPath)
        {
            var candidates = new List<string>();
            Action<string> add = delegate (string dir)
            {
                if (string.IsNullOrEmpty(dir)) return;
                foreach (string c in candidates)
                    if (string.Equals(c, dir, StringComparison.OrdinalIgnoreCase)) return;
                candidates.Add(dir);
            };
            Action<string, int> addChain = delegate (string start, int depth)
            {
                string dir = start;
                for (int i = 0; i < depth && !string.IsNullOrEmpty(dir); i++)
                {
                    add(Path.Combine(dir, "pics"));
                    try
                    {
                        string parent = Path.GetDirectoryName(dir);
                        if (string.IsNullOrEmpty(parent) || parent == dir) break;
                        dir = parent;
                    }
                    catch { break; }
                }
            };

            if (!string.IsNullOrEmpty(ydkPath))
            {
                string dir = null;
                try { dir = Path.GetDirectoryName(Path.GetFullPath(ydkPath)); } catch { }
                addChain(dir, 5);
            }
            try { addChain(AppDomain.CurrentDomain.BaseDirectory, 4); } catch { }

            string[] known = { @"D:\edopro", @"C:\ProjectIgnis", @"D:\ProjectIgnis", @"C:\edopro", @"D:\EDOPro" };
            foreach (string k in known) add(Path.Combine(k, "pics"));

            // repositories\<repo>\pics (예: 커스텀 카드 배포본)
            foreach (string k in known)
            {
                string repos = Path.Combine(k, "repositories");
                if (!Directory.Exists(repos)) continue;
                try
                {
                    foreach (string sub in Directory.GetDirectories(repos))
                        add(Path.Combine(sub, "pics"));
                }
                catch { }
            }

            var result = new List<string>();
            foreach (string c in candidates)
                if (LooksLikePicsFolder(c)) result.Add(c);
            return result;
        }

        // ── 렌더 ─────────────────────────────────────────────────
        public static Bitmap Render(DeckList deck, DeckImageOptions o, out RenderStats stats)
        {
            return Render(deck, o, null, out stats);
        }

        /// <summary>이미지 캐시(store)를 넘기면 렌더 사이에 디코드 결과를 재사용한다.</summary>
        public static Bitmap Render(DeckList deck, DeckImageOptions o, CardImageStore store, out RenderStats stats)
        {
            if (deck == null) throw new ArgumentNullException("deck");
            if (o == null) o = new DeckImageOptions();

            stats = new RenderStats();
            stats.MainCount = deck.Main.Count;
            stats.ExtraCount = deck.Extra.Count;
            stats.SideCount = deck.Side.Count;

            int cols = Math.Max(1, Math.Min(40, o.Columns));
            int cardW = Math.Max(40, Math.Min(400, o.CardWidth));
            int cardH = (int)Math.Round(cardW * CardAspect);
            int gap = Math.Max(0, Math.Min(60, o.Gap));
            int pad = Math.Max(0, Math.Min(120, o.Padding));

            ThemePalette th = ThemePalette.Get(o.Theme);
            List<string> picsDirs = o.ResolvePicsFolders();

            float titlePt = Math.Max(9f, cardW / 5.0f);
            float hdrPt = Math.Max(8.5f, cardW / 6.0f);
            float footPt = Math.Max(6.5f, cardW / 12.0f);
            float missPt = Math.Max(6f, cardW / 13.0f);

            using (var probe = new Bitmap(1, 1))
            using (var pg = Graphics.FromImage(probe))
            using (var fT = new Font("Malgun Gothic", titlePt, FontStyle.Bold))
            using (var fH = new Font("Malgun Gothic", hdrPt, FontStyle.Bold))
            using (var fF = new Font("Malgun Gothic", footPt))
            {
                SizeF szT = pg.MeasureString(o.Title ?? "", fT);
                SizeF szH = pg.MeasureString("메인 덱 : 43장", fH);
                SizeF szF = pg.MeasureString("M", fF);

                float titleTextH = (o.ShowTitle ? szT.Height : 0f);
                int bandH = o.ShowTitle ? (int)Math.Round(titleTextH) + 22 : 0;
                int hdrH = (int)Math.Round(szH.Height) + 16;
                int barH = hdrH - 8;
                int footH = (int)Math.Round(szF.Height) + 22;

                // ── 섹션 구성 ──
                var sections = new List<Section>();
                sections.Add(BuildSection("메인 덱", deck.Main, o.ShowCounts));
                sections.Add(BuildSection("엑스트라 덱", deck.Extra, o.ShowCounts));
                if (o.ShowSide && deck.Side.Count > 0)
                    sections.Add(BuildSection("사이드 덱", deck.Side, o.ShowCounts));

                var rowsOf = new List<int>();
                foreach (Section s in sections)
                    rowsOf.Add(s.Placeables == 0 ? 0 : (int)Math.Ceiling(s.Placeables / (double)cols));

                int gridW = cols * cardW + (cols - 1) * gap;
                int width = gridW + pad * 2;

                int height = bandH;
                for (int i = 0; i < sections.Count; i++)
                {
                    int rows = rowsOf[i];
                    height += hdrH;
                    if (rows > 0) height += rows * cardH + (rows - 1) * gap;
                    height += pad;
                }
                if (o.ShowFooter) height += footH;
                height += pad / 2;
                if (height < 120) height = 120;

                var bmp = new Bitmap(width, height);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                    using (var bg = new SolidBrush(th.Background)) g.FillRectangle(bg, 0, 0, width, height);
                    using (var bandBrush = new SolidBrush(th.Band))
                    using (var hdrBrush = new SolidBrush(th.Header))
                    using (var accBrush = new SolidBrush(th.Accent))
                    using (var textBrush = new SolidBrush(th.Text))
                    using (var subBrush = new SolidBrush(th.SubText))
                    using (var missBrush = new SolidBrush(th.Missing))
                    using (var borderPen = new Pen(th.CardBorder, 1f))
                    using (var fTitle = new Font("Malgun Gothic", titlePt, FontStyle.Bold))
                    using (var fHdr = new Font("Malgun Gothic", hdrPt, FontStyle.Bold))
                    using (var fFoot = new Font("Malgun Gothic", footPt))
                    using (var fMiss = new Font("Malgun Gothic", missPt, FontStyle.Bold))
                    {
                        // ── 제목 밴드 ──
                        if (o.ShowTitle)
                        {
                            g.FillRectangle(bandBrush, 0, 0, width, bandH - 3);
                            g.FillRectangle(accBrush, 0, bandH - 3, width, 3);
                            string title = o.Title;
                            if (string.IsNullOrEmpty(title)) title = Path.GetFileNameWithoutExtension(deck.FilePath);
                            if (string.IsNullOrEmpty(title)) title = "Deck";
                            g.DrawString(title, fTitle, textBrush, pad, (bandH - 3 - titleTextH) / 2);
                        }

                        int y = bandH;
                        var imgCache = new Dictionary<int, Image>();
                        try
                        {
                            for (int si = 0; si < sections.Count; si++)
                            {
                                Section sec = sections[si];

                                // ── 섹션 헤더 ──
                                g.FillRectangle(hdrBrush, pad - 6, y, gridW + 12, barH);
                                g.FillRectangle(accBrush, pad - 6, y + barH - 3, gridW + 12, 3);
                                int marker = Math.Max(10, (int)Math.Round(szH.Height * 0.85));
                                g.FillRectangle(accBrush, pad, y + (barH - marker) / 2, marker, marker);

                                string label = string.Format("{0} : {1}장", sec.Name, sec.Total);
                                g.DrawString(label, fHdr, textBrush,
                                    pad + marker + 10, y + (barH - szH.Height) / 2);
                                y += hdrH;

                                // ── 카드 ──
                                int x = pad, col = 0, row = 0;
                                foreach (CardSlot slot in sec.Slots)
                                {
                                    int cx = pad + col * (cardW + gap);
                                    int cy = y + row * (cardH + gap);
                                    Image img = store != null ? store.Get(slot.Id) : LoadCardImage(slot.Id, picsDirs, imgCache);
                                    if (img != null)
                                    {
                                        g.DrawImage(img, cx, cy, cardW, cardH);
                                    }
                                    else
                                    {
                                        g.FillRectangle(missBrush, cx, cy, cardW, cardH);
                                        string missLabel = "?" + slot.Id;
                                        string name = store != null ? store.NameOf(slot.Id) : "";
                                        if (!string.IsNullOrEmpty(name))
                                        {
                                            // 이름을 알면 ID 대신 이름을(2줄까지) 쓴다
                                            var rect = new RectangleF(cx + 4, cy + 4, cardW - 8, cardH - 8);
                                            using (var sf = new StringFormat
                                            {
                                                Alignment = StringAlignment.Center,
                                                LineAlignment = StringAlignment.Center,
                                                Trimming = StringTrimming.EllipsisCharacter
                                            })
                                                g.DrawString(name, fMiss, subBrush, rect, sf);
                                        }
                                        else
                                        {
                                            SizeF ms = g.MeasureString(missLabel, fMiss);
                                            g.DrawString(missLabel, fMiss, subBrush,
                                                cx + (cardW - ms.Width) / 2, cy + (cardH - ms.Height) / 2);
                                        }
                                        if (!stats.Missing.Contains(slot.Id)) stats.Missing.Add(slot.Id);
                                    }
                                    g.DrawRectangle(borderPen, cx, cy, cardW - 1, cardH - 1);
                                    stats.Placed++;

                                    if (o.ShowCounts && slot.Count > 1)
                                        DrawCountBadge(g, cx + cardW, cy + cardH, slot.Count, cardW, fMiss, accBrush, th);

                                    col++;
                                    if (col >= cols) { col = 0; row++; }
                                }

                                int usedRows = rowsOf[si];
                                if (usedRows > 0) y += usedRows * cardH + (usedRows - 1) * gap;
                                y += pad;
                            }

                            // ── 푸터 ──
                            if (o.ShowFooter)
                            {
                                int fy = height - footH + 6;
                                using (var linePen = new Pen(th.SubText))
                                {
                                    linePen.Color = Color.FromArgb(90, th.SubText);
                                    g.DrawLine(linePen, pad, fy - 6, width - pad, fy - 6);
                                }
                                string foot = string.Format("{0}  ·  메인 {1} / 엑스트라 {2} / 사이드 {3}  ·  {4}",
                                    deck.FileName, deck.Main.Count, deck.Extra.Count, deck.Side.Count,
                                    DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                                if (!string.IsNullOrEmpty(o.FooterNote)) foot += "  ·  " + o.FooterNote;
                                if (stats.Missing.Count > 0) foot += string.Format("  ·  이미지 없음 {0}장", stats.Missing.Count);
                                g.DrawString(foot, fFoot, subBrush, pad, fy + 4);
                            }
                        }
                        finally
                        {
                            // store 를 쓰는 경우 imgCache 는 비어 있다(소유권은 store 에 있음)
                            foreach (KeyValuePair<int, Image> kv in imgCache)
                                if (kv.Value != null) kv.Value.Dispose();
                        }
                    }
                }

                stats.Width = width;
                stats.Height = height;
                return bmp;
            }
        }

        private sealed class CardSlot
        {
            public int Id;
            public int Count;
        }

        private sealed class Section
        {
            public string Name;
            public int Total;
            public int Placeables;
            public readonly List<CardSlot> Slots = new List<CardSlot>();
        }

        private static Section BuildSection(string name, List<int> ids, bool showCounts)
        {
            var sec = new Section { Name = name, Total = ids.Count };
            var order = new List<CardSlot>();
            var map = new Dictionary<int, CardSlot>();
            foreach (int id in ids)
            {
                CardSlot slot;
                if (map.TryGetValue(id, out slot)) { slot.Count++; continue; }
                slot = new CardSlot { Id = id, Count = 1 };
                map[id] = slot;
                order.Add(slot);
            }
            if (!showCounts)
            {
                // 배지 없이 복사본마다 한 장씩
                foreach (CardSlot s in order)
                    for (int k = 0; k < s.Count; k++) sec.Slots.Add(new CardSlot { Id = s.Id, Count = 1 });
            }
            else
            {
                sec.Slots.AddRange(order);
            }
            sec.Placeables = sec.Slots.Count;
            return sec;
        }

        private static Image LoadCardImage(int id, List<string> picsFolders, Dictionary<int, Image> cache)
        {
            Image cached;
            if (cache.TryGetValue(id, out cached)) return cached;

            Image img = null;
            if (picsFolders != null)
            {
                foreach (string dir in picsFolders)
                {
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                    foreach (string ext in new[] { ".jpg", ".png", ".jpeg" })
                    {
                        string path = Path.Combine(dir, id + ext);
                        if (!File.Exists(path)) continue;
                        try { img = Image.FromFile(path); }
                        catch { img = null; }
                        if (img != null) break;
                    }
                    if (img != null) break;
                }
            }
            cache[id] = img;   // null 도 캐시(반복 조회 방지)
            return img;
        }

        private static void DrawCountBadge(Graphics g, int right, int bottom, int count,
            int cardW, Font font, SolidBrush accent, ThemePalette th)
        {
            string text = "x" + count;
            SizeF sz = g.MeasureString(text, font);
            int w = (int)Math.Round(sz.Width) + 12;
            int h = (int)Math.Round(sz.Height) + 4;
            int x = right - w - 3;
            int y = bottom - h - 3;

            // 배지 배경 = 액센트, 글자색 = 배경 대비(라이트 테마에서는 흰색)
            Color textColor = string.Equals(th.Name, "light", StringComparison.Ordinal)
                ? Color.White
                : Color.FromArgb(8, 20, 34);
            using (var bg = new SolidBrush(th.Accent))
                g.FillRectangle(bg, x, y, w, h);
            using (var pen = new Pen(Color.FromArgb(200, 0, 0, 0)))
                g.DrawRectangle(pen, x, y, w - 1, h - 1);
            using (var txt = new SolidBrush(textColor))
                g.DrawString(text, font, txt, x + 6, y + 2);
        }

        // ── 저장 ────────────────────────────────────────────────
        public static void Save(Bitmap bmp, string path, bool jpeg)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            if (jpeg)
            {
                ImageCodecInfo codec = null;
                foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders())
                    if (c.FormatID == ImageFormat.Jpeg.Guid) { codec = c; break; }
                using (var ps = new EncoderParameters(1))
                {
                    ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                    if (codec != null) bmp.Save(path, codec, ps);
                    else bmp.Save(path, ImageFormat.Jpeg);
                }
            }
            else bmp.Save(path, ImageFormat.Png);
        }
    }
}

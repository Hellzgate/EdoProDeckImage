using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace WindBotDeckImage
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args == null) args = new string[0];

            // 창 없이 렌더 (스크립트/자동화용)
            if (args.Length > 0 && (args[0] == "--ydk" || args[0] == "--batch"))
            {
                Environment.ExitCode = RunCli(args);
                return;
            }

            // 화면을 PNG 로 저장(창을 화면 밖에 띄웠다 바로 닫는다) — 디자인 확인용
            if (args.Length >= 2 && args[0] == "--screenshot")
            {
                Environment.ExitCode = Shot(args[1], args.Length >= 3 ? args[2] : null);
                return;
            }

            // 폼 생성만 검사(창 표시 없음)
            if (args.Length > 0 && args[0] == "--uicheck")
            {
                try
                {
                    using (var f = new MainForm())
                    {
                        Console.WriteLine("UICHECK OK");
                        Console.WriteLine("  " + f.UiCheckSummary());
                    }
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("UICHECK FAIL: " + ex.Message);
                    LogCrash(ex);
                    Environment.ExitCode = 1;
                }
                return;
            }

            if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h" || args[0] == "/?"))
            {
                Console.WriteLine(UsageText());
                return;
            }

            try
            {
                ApplicationConfiguration.Initialize();
                Application.ThreadException += delegate (object s, System.Threading.ThreadExceptionEventArgs e) { LogCrash(e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { LogCrash(e.ExceptionObject as Exception); };
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                MessageBox.Show("시작 중 오류가 발생했습니다.\r\n\r\n" + ex.Message +
                    "\r\n\r\n로그: " + Path.Combine(AppSettings.Folder, "startup-error.txt"),
                    "WindBot 덱 이미지 생성기", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ── CLI ──────────────────────────────────────────────────
        private static string UsageText()
        {
            return string.Join("\r\n", new[]
            {
                "WindBot 덱 이미지 생성기 v1.0",
                "",
                "사용법:",
                "  WindBotDeckImage.exe                       (창 열기)",
                "  WindBotDeckImage.exe --ydk <파일.ydk> --out <결과.png> [옵션]",
                "  WindBotDeckImage.exe --batch <폴더> --out <출력폴더> [옵션]",
                "  WindBotDeckImage.exe --uicheck             (창 없이 폼 생성 검사)",
                "",
                "옵션:",
                "  --pics <폴더>     카드 이미지 폴더 (기본 자동 탐색)",
                "  --cols <N>        한 줄 카드 수 (기본 10)",
                "  --cardw <N>       카드 폭 px (기본 110)",
                "  --gap <N>         카드 간격 px (기본 5)",
                "  --pad <N>         여백 px (기본 18)",
                "  --theme dark|light",
                "  --title <문자열>  상단 제목 (기본 덱 파일명)",
                "  --note <문자열>   푸터에 덧붙일 문구",
                "  --counts          같은 카드는 1장만 + x2/x3 배지",
                "  --no-title / --no-footer / --no-side",
                "  --jpg             JPG 로 저장 (기본 PNG)",
                "  --no-net          없는 카드를 인터넷에서 받지 않음",
                "",
                "종료 코드: 0 성공 / 1 실패"
            });
        }

        private static int RunCli(string[] args)
        {
            var opt = new DeckImageOptions();
            string ydk = null, batch = null, outPath = null;
            bool jpeg = false;
            bool noNet = false;
            string picsExplicit = null;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                string next = (i + 1 < args.Length) ? args[i + 1] : null;
                switch (a)
                {
                    case "--ydk": ydk = next; i++; break;
                    case "--batch": batch = next; i++; break;
                    case "--out": outPath = next; i++; break;
                    case "--pics": picsExplicit = next; i++; break;
                    case "--cols": opt.Columns = ParseInt(next, 10); i++; break;
                    case "--cardw": opt.CardWidth = ParseInt(next, 110); i++; break;
                    case "--gap": opt.Gap = ParseInt(next, 5); i++; break;
                    case "--pad": opt.Padding = ParseInt(next, 18); i++; break;
                    case "--theme": opt.Theme = (next ?? "dark"); i++; break;
                    case "--title": opt.Title = next ?? ""; i++; break;
                    case "--note": opt.FooterNote = next ?? ""; i++; break;
                    case "--counts": opt.ShowCounts = true; break;
                    case "--no-title": opt.ShowTitle = false; break;
                    case "--no-footer": opt.ShowFooter = false; break;
                    case "--no-side": opt.ShowSide = false; break;
                    case "--jpg": jpeg = true; break;
                    case "--no-net": noNet = true; break;
                }
            }

            try
            {
                if (string.IsNullOrEmpty(outPath))
                {
                    Console.WriteLine("ERROR: --out 이 필요합니다.");
                    return 1;
                }

                if (!string.IsNullOrEmpty(batch))
                {
                    return RunBatch(batch, outPath, opt, picsExplicit, jpeg);
                }

                if (string.IsNullOrEmpty(ydk) || !File.Exists(ydk))
                {
                    Console.WriteLine("ERROR: --ydk 파일을 찾을 수 없습니다: " + ydk);
                    return 1;
                }

                opt.PicsFolder = string.IsNullOrEmpty(picsExplicit)
                    ? DeckImageRenderer.GuessPicsFolder(ydk)
                    : picsExplicit;

                DeckList deck = DeckImageRenderer.ParseYdk(ydk);
                using (var store = new CardImageStore(opt.PicsFolder, !noNet, Path.Combine(AppSettings.Folder, "imgcache")))
                {
                    var ids = new List<int>();
                    var seen = new HashSet<int>();
                    foreach (int id in deck.Main) if (seen.Add(id)) ids.Add(id);
                    foreach (int id in deck.Extra) if (seen.Add(id)) ids.Add(id);
                    foreach (int id in deck.Side) if (seen.Add(id)) ids.Add(id);
                    store.Preload(ids, null);

                    RenderStats st;
                    using (Bitmap bmp = DeckImageRenderer.Render(deck, opt, store, out st))
                    {
                        DeckImageRenderer.Save(bmp, outPath, jpeg);
                        Console.WriteLine("OUT " + outPath);
                        Console.WriteLine("  " + st.Summary + " · 배치 " + st.Placed + "장"
                            + (store.FromNetwork > 0 ? string.Format(" · 인터넷에서 {0}장 받음", store.FromNetwork) : "")
                            + (store.FromCache > 0 ? string.Format(" · 캐시 {0}장", store.FromCache) : ""));
                    if (st.Missing.Count > 0)
                        Console.WriteLine("  이미지 없음: " + string.Join(",", st.Missing.ConvertAll(x => x.ToString()).ToArray()));
                }
                }
                if (string.IsNullOrEmpty(opt.PicsFolder))
                    Console.WriteLine("  ⚠ 카드 이미지 폴더를 찾지 못했습니다(--pics 로 지정).");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: " + ex.Message);
                LogCrash(ex);
                return 1;
            }
        }

        private static int RunBatch(string folder, string outFolder, DeckImageOptions opt, string picsExplicit, bool jpeg)
        {
            if (!Directory.Exists(folder))
            {
                Console.WriteLine("ERROR: 폴더가 없습니다: " + folder);
                return 1;
            }
            Directory.CreateDirectory(outFolder);

            string[] files;
            try { files = Directory.GetFiles(folder, "*.ydk", SearchOption.TopDirectoryOnly); }
            catch (Exception ex) { Console.WriteLine("ERROR: " + ex.Message); return 1; }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            if (files.Length == 0)
            {
                Console.WriteLine("ERROR: *.ydk 파일이 없습니다: " + folder);
                return 1;
            }

            string pics = string.IsNullOrEmpty(picsExplicit) ? DeckImageRenderer.GuessPicsFolder(files[0]) : picsExplicit;
            int ok = 0, fail = 0;
            foreach (string f in files)
            {
                try
                {
                    DeckImageOptions o = opt.Clone();
                    o.PicsFolder = pics;
                    DeckList deck = DeckImageRenderer.ParseYdk(f);
                    RenderStats st;
                    string outFile = Path.Combine(outFolder,
                        Path.GetFileNameWithoutExtension(f) + (jpeg ? ".jpg" : ".png"));
                    using (Bitmap bmp = DeckImageRenderer.Render(deck, o, out st))
                    {
                        DeckImageRenderer.Save(bmp, outFile, jpeg);
                    }
                    ok++;
                    Console.WriteLine("  OK   " + Path.GetFileName(outFile) + "  (" + deck.Total + "장)");
                }
                catch (Exception ex)
                {
                    fail++;
                    Console.WriteLine("  FAIL " + Path.GetFileName(f) + " : " + ex.Message);
                }
            }
            Console.WriteLine(string.Format("BATCH OK {0} / 실패 {1} · 이미지 폴더: {2}",
                ok, fail, string.IsNullOrEmpty(pics) ? "(없음)" : pics));
            return fail == 0 ? 0 : 1;
        }

        private static int ParseInt(string s, int fallback)
        {
            int v;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return fallback;
        }

        private static void LogCrash(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(AppSettings.Folder);
                var sb = new StringBuilder();
                sb.AppendLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]");
                if (ex != null) sb.AppendLine(ex.ToString());
                sb.AppendLine();
                File.AppendAllText(Path.Combine(AppSettings.Folder, "startup-error.txt"), sb.ToString(), Encoding.UTF8);
            }
            catch { }
            try { Console.WriteLine(ex != null ? ex.ToString() : "(unknown error)"); } catch { }
        }
        /// <summary>
        /// 화면을 PNG 로 저장한다(WinForms 는 Show() 없이는 자식이 안 그려짐 → 화면 밖에 잠깐 띄운다).
        ///   --screenshot out.png [덱.ydk]
        /// </summary>
        private static int Shot(string path, string ydk)
        {
            try
            {
                ApplicationConfiguration.Initialize();
                Application.EnableVisualStyles();
                using var f = new MainForm();
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-4000, -4000);
                f.Show();
                Application.DoEvents();
                if (!string.IsNullOrEmpty(ydk) && File.Exists(ydk))
                {
                    f.LoadDeckForShot(ydk);
                    Application.DoEvents();
                }
                System.Threading.Thread.Sleep(250);
                Application.DoEvents();
                string full = Path.GetFullPath(path);
                string dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                using (var bmp = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(full, System.Drawing.Imaging.ImageFormat.Png);
                }
                f.Hide();
                Console.WriteLine("SCREENSHOT OK " + full + " " + f.Width + "x" + f.Height);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("SCREENSHOT FAIL " + ex.Message);
                return 1;
            }
        }
    }

}

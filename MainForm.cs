using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EdoProDeckImage
{
    public sealed class MainForm : Form
    {
        // ── 컨트롤 ──
        private TextBox TxtYdk;
        private TextBox TxtPics;
        private TextBox TxtTitle;
        private NumericUpDown NumCols;
        private NumericUpDown NumCardW;
        private NumericUpDown NumGap;
        private ChoiceBox CmbTheme;
        private ChoiceBox CmbFormat;
        private CheckBox ChkCounts;
        private CheckBox ChkTitle;
        private CheckBox ChkFooter;
        private CheckBox ChkSide;
        private CheckBox ChkNet;
        private Button BtnOpen;
        private Button BtnSave;
        private Button BtnPics;
        private Button BtnPicsAuto;
        private Button BtnRefresh;
        private Button BtnBatch;
        private PictureBox Pic;
        private Label LblHint;
        private StatusStrip Status;
        private ToolStripStatusLabel StLeft;
        private ToolStripStatusLabel StMid;
        private ToolStripStatusLabel StRight;
        private Timer Debounce;

        private readonly AppSettings _settings;
        private DeckList _deck;
        private Bitmap _preview;
        private CardImageStore _store;
        private string _storePics;
        private bool _storeNet;
        private string _storeDeck;
        private bool _loadingUi;
        private bool _syncPreload;

        private const int StackWidth = 320;

        // 2026-11-07: 화면 토큰(다크) — 미리보기 배경과 같은 계열로 통일
        private static readonly Color UiBg = Color.FromArgb(30, 31, 34);
        private static readonly Color UiPanel = Color.FromArgb(38, 40, 44);
        private static readonly Color UiBox = Color.FromArgb(45, 47, 52);
        private static readonly Color UiBorder = Color.FromArgb(70, 74, 82);
        private static readonly Color UiFg = Color.FromArgb(232, 234, 238);
        private static readonly Color UiSub = Color.FromArgb(158, 164, 174);
        private static readonly Color UiAccent = Color.FromArgb(86, 156, 214);

        public MainForm()
        {
            _settings = AppSettings.Load();
            BuildUi();
            LoadSettingsIntoUi();
            TryLoadInitialDeck();
        }

        //  UI 구성
        // ══════════════════════════════════════════════════════
        private void BuildUi()
        {
            Text = "EDOPro 덱 이미지 생성기  v" + Program.VersionText;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1240, 890);
            MinimumSize = new Size(1010, 700);
            Font = new Font("Malgun Gothic", 9f);
            BackColor = UiBg;
            ForeColor = UiFg;
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            AllowDrop = true;
            DragEnter += OnFormDragEnter;
            DragDrop += OnFormDragDrop;

            // ── 컨트롤 생성 ──
            TxtYdk = new TextBox { ReadOnly = true, BackColor = UiPanel, ForeColor = UiSub, BorderStyle = BorderStyle.FixedSingle };
            TxtPics = new TextBox { BackColor = UiBox, ForeColor = UiFg, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "EDOPro\\pics   (여러 폴더는 ; 로 구분)" };
            TxtTitle = new TextBox { PlaceholderText = "(비우면 덱 파일명)", BackColor = UiBox, ForeColor = UiFg, BorderStyle = BorderStyle.FixedSingle };

            NumCols = Num(4, 20, 10);
            NumCardW = Num(60, 220, 110);
            NumGap = Num(0, 30, 5);

            CmbTheme = new ChoiceBox(new[] { "다크", "라이트" });
            CmbTheme.SelectedIndex = 0;

            CmbFormat = new ChoiceBox(new[] { "PNG", "JPG" });
            CmbFormat.SelectedIndex = 0;

            ChkCounts = Chk("같은 카드는 1장 + x2/x3 배지", false);
            ChkTitle = Chk("상단 제목 표시", true);
            ChkFooter = Chk("하단 정보 표시", true);
            ChkSide = Chk("사이드 덱 포함", true);
            ChkNet = Chk("없는 카드는 인터넷에서 받기", true);

            BtnOpen = Btn("덱 파일 열기…");
            BtnOpen.Click += delegate { OnOpenDeck(); };
            BtnSave = Btn("이미지 저장", true);
            BtnSave.Click += delegate { OnSaveImage(); };
            BtnPics = Btn("폴더 찾기…");
            BtnPics.Click += delegate { OnPickPicsFolder(); };
            BtnPicsAuto = Btn("자동 탐색");
            BtnPicsAuto.Click += delegate { OnAutoPicsFolder(); };
            BtnRefresh = Btn("미리보기 새로고침");
            BtnRefresh.Click += delegate { RefreshPreview(); };
            BtnBatch = Btn("폴더 일괄 변환 (모든 .ydk)");
            BtnBatch.Click += delegate { OnBatch(); };

            // ── 왼쪽 옵션 스택 ──
            var left = new Panel { Dock = DockStyle.Left, Width = 366, Padding = new Padding(12, 10, 8, 8), AutoScroll = true, BackColor = UiBg };
            var stack = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Width = StackWidth + 20
            };
            stack.Controls.Add(Header("덱"));
            stack.Controls.Add(Field("덱 파일 (.ydk)", TxtYdk));
            stack.Controls.Add(Row(BtnOpen, BtnSave));
            stack.Controls.Add(Header("카드 이미지"));
            stack.Controls.Add(Field("이미지 폴더 (; 로 여러 개)", TxtPics));
            stack.Controls.Add(Row(BtnPics, BtnPicsAuto));
            stack.Controls.Add(Header("이미지 설정"));
            stack.Controls.Add(Field("제목", TxtTitle));
            stack.Controls.Add(Field("한 줄 카드 수", NumCols));
            stack.Controls.Add(Field("카드 폭 (px)", NumCardW));
            stack.Controls.Add(Field("카드 간격 (px)", NumGap));
            stack.Controls.Add(Field("배경", CmbTheme));
            stack.Controls.Add(Field("출력 형식", CmbFormat));
            stack.Controls.Add(Checks());
            stack.Controls.Add(Row(BtnRefresh));
            stack.Controls.Add(Row(BtnBatch));
            stack.Controls.Add(HelpLabel());
            left.Controls.Add(stack);

            // ── 오른쪽 미리보기 ──
            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 12, 12, 4), BackColor = Color.FromArgb(26, 30, 38) };
            Pic = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(26, 30, 38)
            };
            LblHint = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(150, 165, 190),
                Text = "덱 파일(.ydk)을 열거나 이 창으로 끌어다 놓으세요.",
                Font = new Font("Malgun Gothic", 11f)
            };
            right.Controls.Add(Pic);
            right.Controls.Add(LblHint);
            LblHint.BringToFront();

            // ── 상태 표시줄 ──
            Status = new StatusStrip { BackColor = UiPanel, ForeColor = UiFg, SizingGrip = false };
            StLeft = new ToolStripStatusLabel("준비") { Spring = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiSub };
            StMid = new ToolStripStatusLabel("") { ForeColor = UiFg };
            StRight = new ToolStripStatusLabel("") { ForeColor = UiFg };
            Status.Items.AddRange(new ToolStripItem[] { StLeft, StMid, StRight });

            Controls.Add(right);
            Controls.Add(left);
            Controls.Add(Status);

            Debounce = new Timer { Interval = 280 };
            Debounce.Tick += delegate { Debounce.Stop(); StartPreload(); };

            EventHandler onChange = delegate
            {
                if (_loadingUi) return;
                Debounce.Stop();
                Debounce.Start();
            };
            NumCols.ValueChanged += onChange;
            NumCardW.ValueChanged += onChange;
            NumGap.ValueChanged += onChange;
            TxtPics.TextChanged += onChange;
            TxtTitle.TextChanged += onChange;
            CmbTheme.SelectedIndexChanged += onChange;
            ChkCounts.CheckedChanged += onChange;
            ChkTitle.CheckedChanged += onChange;
            ChkFooter.CheckedChanged += onChange;
            ChkSide.CheckedChanged += onChange;
            ChkNet.CheckedChanged += onChange;
            CmbFormat.SelectedIndexChanged += delegate { UpdateStatus(); };
        }

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        private static NumericUpDown Num(int min, int max, int val)
        {
            var n = new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = val,
                Width = 90,
                TextAlign = HorizontalAlignment.Right,
                BackColor = UiBox,
                ForeColor = UiFg,
                BorderStyle = BorderStyle.FixedSingle   // ⚠ NumericUpDown 은 BorderStyle 이 없으면 흰 배경
            };
            n.HandleCreated += delegate
            {
                try { n.BackColor = UiBox; n.ForeColor = UiFg; } catch { }
            };
            return n;
        }

        /// <summary>
        /// WinForms ComboBox(DropDownList)는 BackColor 를 무시하고 **흰 필드**로 그린다 →
        /// 테마를 끄면(SetWindowTheme "") 지정한 배경색이 적용된다.
        /// </summary>


        private static CheckBox Chk(string text, bool on)
        {
            return new CheckBox
            {
                Text = text,
                AutoSize = true,
                Checked = on,
                ForeColor = UiFg,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
        }

        private static Button Btn(string text, bool primary = false)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(0, 30),
                Margin = new Padding(0, 0, 6, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? UiAccent : UiBox,
                ForeColor = primary ? Color.White : UiFg,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = primary ? UiAccent : UiBorder;
            b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(108, 178, 236) : Color.FromArgb(62, 66, 73);
            b.FlatAppearance.MouseDownBackColor = primary ? UiAccent : UiBox;
            return b;
        }

        /// <summary>왼쪽 옵션 구역 제목(강조 막대 + 옅은 띠).</summary>
        private static Control Header(string text)
        {
            var p = new Panel { Width = StackWidth, Height = 26, Margin = new Padding(0, 6, 0, 6), BackColor = UiPanel };
            var bar = new Panel { Width = 3, Dock = DockStyle.Left, BackColor = UiAccent };
            var l = new Label
            {
                Text = "  " + text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiSub,
                BackColor = Color.Transparent,
                Font = new Font("Malgun Gothic", 9f, FontStyle.Bold)
            };
            p.Controls.Add(l);
            p.Controls.Add(bar);
            return p;
        }

        // 라벨 + 컨트롤 한 덩어리
        private static Panel Field(string label, Control ctrl)
        {
            var p = new Panel { Width = StackWidth, Margin = new Padding(0, 0, 0, 8) };
            var l = new Label { Text = label, AutoSize = true, Location = new Point(0, 0), ForeColor = UiSub };
            p.Controls.Add(l);
            ctrl.Location = new Point(0, l.PreferredHeight + 2);
            ctrl.Width = StackWidth - 4;
            p.Controls.Add(ctrl);
            p.Height = l.PreferredHeight + ctrl.Height + 6;
            return p;
        }

        private static FlowLayoutPanel Row(params Control[] items)
        {
            var f = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Width = StackWidth,
                Margin = new Padding(0, 0, 0, 8)
            };
            foreach (Control c in items) f.Controls.Add(c);
            return f;
        }

        private FlowLayoutPanel Checks()
        {
            var f = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Width = StackWidth,
                Margin = new Padding(0, 0, 0, 8)
            };
            f.Controls.Add(ChkCounts);
            f.Controls.Add(ChkTitle);
            f.Controls.Add(ChkFooter);
            f.Controls.Add(ChkSide);
            f.Controls.Add(ChkNet);
            return f;
        }

        private static Label HelpLabel()
        {
            return new Label
            {
                AutoSize = true,
                MaximumSize = new Size(StackWidth, 0),
                ForeColor = Color.FromArgb(110, 118, 130),
                Margin = new Padding(0, 6, 0, 0),
                Text = "· 카드 이미지는 EDOPro 의 pics 폴더(파일명 = 카드 ID)를 씁니다.\r\n" +
                       "· 카드 폭을 키우면 섹션 헤더 글씨도 함께 커집니다.\r\n" +
                       "· 덱 파일을 창으로 끌어다 놓아도 됩니다."
            };
        }

        // ══════════════════════════════════════════════════════
        //  설정 <-> UI
        // ══════════════════════════════════════════════════════
        private void LoadSettingsIntoUi()
        {
            _loadingUi = true;
            try
            {
                TxtPics.Text = _settings.PicsFolder ?? "";
                TxtTitle.Text = _settings.LastTitle ?? "";
                NumCols.Value = Clamp(NumCols, _settings.Columns);
                NumCardW.Value = Clamp(NumCardW, _settings.CardWidth);
                NumGap.Value = Clamp(NumGap, _settings.Gap);
                CmbTheme.SelectedIndex = string.Equals(_settings.Theme, "light", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                CmbFormat.SelectedIndex = string.Equals(_settings.Format, "JPG", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                ChkCounts.Checked = _settings.ShowCounts;
                ChkTitle.Checked = _settings.ShowTitle;
                ChkFooter.Checked = _settings.ShowFooter;
                ChkSide.Checked = _settings.ShowSide;
                ChkNet.Checked = _settings.NetImages;

                // 창 크기(있으면) — 최소 크기보다 작으면 무시
                try
                {
                    if (!string.IsNullOrEmpty(_settings.WindowSize))
                    {
                        string[] p = _settings.WindowSize.Split('x');
                        int w, h;
                        if (p.Length == 2 && int.TryParse(p[0], out w) && int.TryParse(p[1], out h))
                        {
                            if (w >= MinimumSize.Width && h >= MinimumSize.Height && w <= 8000 && h <= 8000)
                                ClientSize = new Size(w, h);
                        }
                    }
                }
                catch { }
            }
            finally { _loadingUi = false; }
        }

        private static decimal Clamp(NumericUpDown n, string s)
        {
            decimal v;
            if (!decimal.TryParse(s, out v)) return n.Value;
            if (v < n.Minimum) v = n.Minimum;
            if (v > n.Maximum) v = n.Maximum;
            return v;
        }

        private void SaveSettingsFromUi()
        {
            try
            {
                if (_deck != null) _settings.LastDeckPath = _deck.FilePath;
                _settings.PicsFolder = TxtPics.Text.Trim();
                _settings.LastTitle = TxtTitle.Text;
                _settings.Columns = ((int)NumCols.Value).ToString();
                _settings.CardWidth = ((int)NumCardW.Value).ToString();
                _settings.Gap = ((int)NumGap.Value).ToString();
                _settings.Theme = CmbTheme.SelectedIndex == 1 ? "light" : "dark";
                _settings.Format = CmbFormat.SelectedIndex == 1 ? "JPG" : "PNG";
                _settings.ShowCounts = ChkCounts.Checked;
                _settings.ShowTitle = ChkTitle.Checked;
                _settings.ShowFooter = ChkFooter.Checked;
                _settings.ShowSide = ChkSide.Checked;
                _settings.NetImages = ChkNet.Checked;
                if (WindowState == FormWindowState.Normal)
                    _settings.WindowSize = ClientSize.Width + "x" + ClientSize.Height;
                _settings.Save();
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSettingsFromUi();
            if (_preview != null) { _preview.Dispose(); _preview = null; }
            if (_store != null) { try { _store.Dispose(); _store = null; } catch { } }
            base.OnFormClosing(e);
        }

        // ══════════════════════════════════════════════════════
        //  덱 불러오기
        // ══════════════════════════════════════════════════════
        private void TryLoadInitialDeck()
        {
            string p = _settings.LastDeckPath;
            if (!string.IsNullOrEmpty(p) && File.Exists(p)) { LoadDeck(p, true); return; }

            string[] guess =
            {
                @"D:\edopro\WindBot\Decks\AI_SunGodTimeWizard.ydk",
                @"C:\ProjectIgnis\WindBot\Decks\AI_raisemoon.ydk"
            };
            foreach (string g in guess)
                if (File.Exists(g)) { LoadDeck(g, true); return; }

            UpdateStatus();
        }

        private void LoadDeck(string path, bool silent)
        {
            try
            {
                DeckList deck = DeckImageRenderer.ParseYdk(path);
                _deck = deck;
                TxtYdk.Text = path;

                var curPics = CardImageStore.SplitFolders(TxtPics.Text.Trim());
                if (curPics.Count == 0 || !DeckImageRenderer.LooksLikePicsFolder(curPics[0]))
                {
                    List<string> pics = DeckImageRenderer.GuessPicsFolders(path);
                    if (pics.Count > 0) TxtPics.Text = string.Join(";", pics.ToArray());
                }

                Text = "EDOPro 덱 이미지 생성기  v" + Program.VersionText + "  —  " + Path.GetFileName(path);
                RefreshPreview();
                StartPreload();
                StLeft.Text = silent
                    ? string.Format("덱: {0}  (메인 {1} / 엑스트라 {2} / 사이드 {3})",
                        Path.GetFileName(path), deck.Main.Count, deck.Extra.Count, deck.Side.Count)
                    : "덱 불러옴: " + Path.GetFileName(path);
            }
            catch (Exception ex)
            {
                StLeft.Text = "덱을 읽지 못했습니다: " + ex.Message;
                if (!silent)
                    MessageBox.Show(this, ex.Message, "덱 파일 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ══════════════════════════════════════════════════════
        //  미리보기
        // ══════════════════════════════════════════════════════
        private DeckImageOptions OptionsFromUi()
        {
            var o = new DeckImageOptions
            {
                Title = TxtTitle.Text.Trim(),
                PicsFolder = TxtPics.Text.Trim(),
                Columns = (int)NumCols.Value,
                CardWidth = (int)NumCardW.Value,
                Gap = (int)NumGap.Value,
                Padding = 18,
                Theme = CmbTheme.SelectedIndex == 1 ? "light" : "dark",
                ShowCounts = ChkCounts.Checked,
                ShowTitle = ChkTitle.Checked,
                ShowFooter = ChkFooter.Checked,
                ShowSide = ChkSide.Checked
            };
            // 사용자가 고른 폴더가 우선, 자동 탐색 폴더는 뒤에 붙인다(없는 카드를 다른 설치에서 찾는다)
            try
            {
                foreach (string p in DeckImageRenderer.GuessPicsFolders(_deck != null ? _deck.FilePath : null))
                    o.ExtraPicsFolders.Add(p);
            }
            catch { }
            return o;
        }

        // 카드 이미지 캐시(폴더/인터넷/덱 설정이 바뀌면 새로 만든다)
        private CardImageStore Store()
        {
            string pics = TxtPics.Text.Trim();
            bool net = ChkNet.Checked;
            string deckPath = _deck != null ? _deck.FilePath : "";
            if (_store == null || _storePics != pics || _storeNet != net || _storeDeck != deckPath)
            {
                CardImageStore old = _store;
                var folders = new List<string>();
                folders.AddRange(CardImageStore.SplitFolders(pics));
                try { folders.AddRange(DeckImageRenderer.GuessPicsFolders(deckPath)); } catch { }

                // cdb(alias·이름) 조회 — 커스텀/재록 카드 이미지 폴백에 필요
                CdbLookup cdb = null;
                try { cdb = CdbLookup.Create(folders, deckPath, AppSettings.Folder); } catch { }

                _store = new CardImageStore(folders, net, System.IO.Path.Combine(AppSettings.Folder, "imgcache"), cdb);
                _storePics = pics;
                _storeNet = net;
                _storeDeck = deckPath;
                if (old != null) { try { old.Dispose(); } catch { } }
            }
            return _store;
        }

        // 덱/폴더가 정해지면: 미리 읽기 + (없는 카드는) 인터넷에서 받기 + 진행률 창
        private void StartPreload()
        {
            if (_deck == null) return;
            CardImageStore store = Store();

            var ids = new List<int>();
            var seen = new HashSet<int>();
            foreach (int id in _deck.Main) if (seen.Add(id)) ids.Add(id);
            foreach (int id in _deck.Extra) if (seen.Add(id)) ids.Add(id);
            foreach (int id in _deck.Side) if (seen.Add(id)) ids.Add(id);

            // --screenshot 처럼 창 없이 그릴 때는 동기로 다 받아 두고 그린다
            if (_syncPreload)
            {
                store.Preload(ids, null);
                RefreshPreview();
                return;
            }

            int missing = 0;
            foreach (int id in ids) if (store.Get(id) == null) missing++;

            if (missing > 0 && !store.HasNetwork)
            {
                // 인터넷은 꺼져 있지만 이름/alias 는 cdb 에서 읽어 둔다(빈 칸에 이름 표시)
                if (store.HasCdb)
                {
                    Task.Run(delegate
                    {
                        store.Prefetch(ids);
                        try { BeginInvoke((Action)RefreshPreview); } catch { }
                    });
                }
                RefreshPreview();
                return;
            }

            if (missing == 0)
            {
                RefreshPreview();
                return;
            }

            var busy = new BusyForm("카드 이미지 준비",
                string.Format("이미지 내려받는 중…  0 / {0}", missing), missing);
            busy.Show(this);
            StLeft.Text = string.Format("카드 이미지 {0}장을 인터넷에서 받아옵니다…", missing);

            Task.Run(delegate { store.Preload(ids, busy.Update); })
                .ContinueWith(delegate
                {
                    try
                    {
                        BeginInvoke((Action)delegate
                        {
                            try { busy.Close(); busy.Dispose(); } catch { }
                            StLeft.Text = string.Format("카드 이미지 준비 완료 (인터넷 {0}장{1}{2})",
                                store.FromNetwork,
                                store.FromAlias > 0 ? string.Format(" · 원본ID {0}", store.FromAlias) : "",
                                store.FromName > 0 ? string.Format(" · 이름검색 {0}", store.FromName) : "");
                            RefreshPreview();
                        });
                    }
                    catch { }
                });
        }
        private void RefreshPreview()
        {
            if (_deck == null) { UpdateStatus(); return; }

            RenderStats st;
            try
            {
                Cursor prev = Cursor;
                Cursor = Cursors.WaitCursor;
                try
                {
                    Bitmap bmp = DeckImageRenderer.Render(_deck, OptionsFromUi(), Store(), out st);
                    Bitmap old = _preview;
                    _preview = bmp;
                    Pic.Image = bmp;
                    LblHint.Visible = false;
                    if (old != null) old.Dispose();
                }
                finally { Cursor = prev; }
            }
            catch (Exception ex)
            {
                StLeft.Text = "미리보기 실패: " + ex.Message;
                return;
            }
            UpdateStatus(st);
        }

        private void UpdateStatus(RenderStats st = null)
        {
            if (_deck == null)
            {
                StLeft.Text = "덱 파일을 열어주세요.";
                StMid.Text = "";
                StRight.Text = "";
                return;
            }
            if (st == null)
            {
                StMid.Text = string.Format("메인 {0} / 엑스트라 {1} / 사이드 {2}",
                    _deck.Main.Count, _deck.Extra.Count, _deck.Side.Count);
                StRight.Text = "";
                return;
            }
            StMid.Text = st.Summary;
            CardImageStore s = _store;
            int folders = s != null ? s.FolderCount : 0;
            StRight.Text = string.Format("이미지 폴더 {0}개 · cdb {1}개{2}",
                folders,
                s != null ? s.CdbCount : 0,
                st.Missing.Count > 0 ? "  ⚠ 이미지 없음 " + st.Missing.Count : "");
        }

        // ══════════════════════════════════════════════════════
        //  동작
        // ══════════════════════════════════════════════════════
        private void OnOpenDeck()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "덱 파일(.ydk) 열기";
                dlg.Filter = "덱 파일 (*.ydk)|*.ydk|모든 파일 (*.*)|*.*";
                string dir = null;
                try { if (_deck != null) dir = Path.GetDirectoryName(_deck.FilePath); } catch { }
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = @"D:\edopro\WindBot\Decks";
                if (Directory.Exists(dir)) dlg.InitialDirectory = dir;
                if (dlg.ShowDialog(this) == DialogResult.OK) LoadDeck(dlg.FileName, false);
            }
        }

        private void OnPickPicsFolder()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "EDOPro 의 pics 폴더를 선택하세요 (파일명 = 카드 ID)";
                string cur = TxtPics.Text.Trim();
                if (Directory.Exists(cur)) dlg.SelectedPath = cur;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    TxtPics.Text = dlg.SelectedPath;
                    RefreshPreview();
                }
            }
        }

        private void OnAutoPicsFolder()
        {
            List<string> found = DeckImageRenderer.GuessPicsFolders(_deck != null ? _deck.FilePath : null);
            if (found.Count == 0)
            {
                MessageBox.Show(this, "카드 이미지 폴더를 찾지 못했습니다.\r\nEDOPro 의 pics 폴더를 직접 선택해 주세요.",
                    "자동 탐색", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            TxtPics.Text = string.Join(";", found.ToArray());
            RefreshPreview();
        }

        private void OnSaveImage()
        {
            if (_deck == null) { MessageBox.Show(this, "먼저 덱 파일을 열어주세요."); return; }

            bool jpeg = CmbFormat.SelectedIndex == 1;
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "덱 이미지 저장";
                dlg.Filter = jpeg ? "JPEG 이미지 (*.jpg)|*.jpg" : "PNG 이미지 (*.png)|*.png";
                dlg.FileName = Path.GetFileNameWithoutExtension(_deck.FilePath) + (jpeg ? ".jpg" : ".png");
                string dir = null;
                try
                {
                    dir = (!string.IsNullOrEmpty(_settings.OutputFolder) && Directory.Exists(_settings.OutputFolder))
                        ? _settings.OutputFolder : Path.GetDirectoryName(_deck.FilePath);
                }
                catch { }
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    RenderStats st;
                    using (Bitmap bmp = DeckImageRenderer.Render(_deck, OptionsFromUi(), out st))
                        DeckImageRenderer.Save(bmp, dlg.FileName, jpeg);
                    _settings.OutputFolder = Path.GetDirectoryName(dlg.FileName);
                    SaveSettingsFromUi();
                    StLeft.Text = "저장 완료: " + dlg.FileName;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "저장하지 못했습니다.\r\n\r\n" + ex.Message, "오류",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnBatch()
        {
            using (var src = new FolderBrowserDialog())
            {
                src.Description = "변환할 .ydk 파일들이 있는 폴더 (예: WindBot\\Decks)";
                string cur = null;
                try { if (_deck != null) cur = Path.GetDirectoryName(_deck.FilePath); } catch { }
                if (string.IsNullOrEmpty(cur) || !Directory.Exists(cur)) cur = @"D:\edopro\WindBot\Decks";
                if (Directory.Exists(cur)) src.SelectedPath = cur;
                if (src.ShowDialog(this) != DialogResult.OK) return;

                string[] files;
                try { files = Directory.GetFiles(src.SelectedPath, "*.ydk"); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "폴더 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (files.Length == 0)
                {
                    MessageBox.Show(this, "선택한 폴더에 .ydk 파일이 없습니다.");
                    return;
                }
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);

                using (var dst = new FolderBrowserDialog())
                {
                    dst.Description = "이미지를 저장할 폴더";
                    string def = Path.Combine(src.SelectedPath, "deck-images");
                    try { Directory.CreateDirectory(def); } catch { }
                    dst.SelectedPath = def;
                    if (dst.ShowDialog(this) != DialogResult.OK) return;

                    bool jpeg = CmbFormat.SelectedIndex == 1;
                    string pics = TxtPics.Text.Trim();
                    // ';' 로 여러 폴더를 적을 수 있으므로 첫 폴더가 유효한지만 본다
                    var picsList = CardImageStore.SplitFolders(pics);
                    if (picsList.Count == 0 || !DeckImageRenderer.LooksLikePicsFolder(picsList[0]))
                        pics = DeckImageRenderer.GuessPicsFolder(files[0]);

                    int ok = 0, fail = 0;
                    Cursor prev = Cursor;
                    Cursor = Cursors.WaitCursor;
                    CardImageStore store = Store();
                    try
                    {
                        foreach (string f in files)
                        {
                            try
                            {
                                DeckImageOptions o = OptionsFromUi();
                                o.PicsFolder = pics;
                                DeckList deck = DeckImageRenderer.ParseYdk(f);
                                var ids = new List<int>();
                                var seen = new HashSet<int>();
                                foreach (int id in deck.Main) if (seen.Add(id)) ids.Add(id);
                                foreach (int id in deck.Extra) if (seen.Add(id)) ids.Add(id);
                                foreach (int id in deck.Side) if (seen.Add(id)) ids.Add(id);
                                store.Preload(ids, null);

                                RenderStats st;
                                string outFile = Path.Combine(dst.SelectedPath,
                                    Path.GetFileNameWithoutExtension(f) + (jpeg ? ".jpg" : ".png"));
                                using (Bitmap bmp = DeckImageRenderer.Render(deck, o, store, out st))
                                    DeckImageRenderer.Save(bmp, outFile, jpeg);
                                ok++;
                                StLeft.Text = string.Format("일괄 변환… {0} / {1}", ok + fail, files.Length);
                                Application.DoEvents();
                            }
                            catch { fail++; }
                        }
                    }
                    finally { Cursor = prev; }

                    _settings.OutputFolder = dst.SelectedPath;
                    SaveSettingsFromUi();
                    StLeft.Text = string.Format("일괄 변환: 성공 {0} / 실패 {1} → {2}", ok, fail, dst.SelectedPath);
                    MessageBox.Show(this, string.Format("완료했습니다.\r\n\r\n성공 {0}건 / 실패 {1}건\r\n{2}",
                        ok, fail, dst.SelectedPath), "일괄 변환", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void OnFormDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;
            foreach (string f in files)
            {
                if (Directory.Exists(f) || f.EndsWith(".ydk", StringComparison.OrdinalIgnoreCase))
                {
                    e.Effect = DragDropEffects.Copy;
                    return;
                }
            }
        }

        private void OnFormDragDrop(object sender, DragEventArgs e)
        {
            if (e.Data == null) return;
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;

            var decks = new List<string>();
            string dir = null;
            foreach (string f in files)
            {
                if (Directory.Exists(f)) { if (dir == null) dir = f; }
                else if (f.EndsWith(".ydk", StringComparison.OrdinalIgnoreCase)) decks.Add(f);
            }

            // 여러 덱 파일 → 그 폴더의 deck-images 로 일괄 변환
            if (decks.Count >= 2)
            {
                StartBatchFromFiles(decks, Path.GetDirectoryName(decks[0]));
                return;
            }
            if (decks.Count == 1) { LoadDeck(decks[0], false); return; }

            if (dir != null)
            {
                string pics = Path.Combine(dir, "pics");
                if (DeckImageRenderer.LooksLikePicsFolder(pics)) { TxtPics.Text = pics; RefreshPreview(); return; }
                string[] ydk;
                try { ydk = Directory.GetFiles(dir, "*.ydk"); } catch { return; }
                if (ydk.Length > 0) { Array.Sort(ydk, StringComparer.OrdinalIgnoreCase); LoadDeck(ydk[0], false); }
            }
        }

        /// <summary>끌어놓은 여러 .ydk 를 &lt;폴더&gt;\deck-images 로 일괄 변환(진행률 창).</summary>
        private void StartBatchFromFiles(List<string> files, string baseDir)
        {
            if (files == null || files.Count == 0) return;
            if (string.IsNullOrEmpty(baseDir)) baseDir = AppSettings.Folder;
            string outDir = Path.Combine(baseDir, "deck-images");
            try { Directory.CreateDirectory(outDir); } catch { }

            bool jpeg = CmbFormat.SelectedIndex == 1;
            DeckImageOptions opt = OptionsFromUi();
            CardImageStore store = Store();     // UI 스레드에서 준비(설정 반영)

            var busy = new BusyForm("일괄 변환", string.Format("0 / {0}", files.Count), files.Count);
            busy.Show(this);

            Task.Run(delegate
            {
                int ok = 0, fail = 0, done = 0;
                foreach (string f in files)
                {
                    try
                    {
                        DeckList deck = DeckImageRenderer.ParseYdk(f);
                        var ids = new List<int>();
                        var seen = new HashSet<int>();
                        foreach (int id in deck.Main) if (seen.Add(id)) ids.Add(id);
                        foreach (int id in deck.Extra) if (seen.Add(id)) ids.Add(id);
                        foreach (int id in deck.Side) if (seen.Add(id)) ids.Add(id);
                        store.Preload(ids, null);

                        DeckImageOptions o = opt.Clone();
                        RenderStats st;
                        string outFile = Path.Combine(outDir,
                            Path.GetFileNameWithoutExtension(f) + (jpeg ? ".jpg" : ".png"));
                        using (Bitmap bmp = DeckImageRenderer.Render(deck, o, store, out st))
                            DeckImageRenderer.Save(bmp, outFile, jpeg);
                        ok++;
                    }
                    catch { fail++; }
                    done++;
                    busy.SetMessage(string.Format("일괄 변환…  {0} / {1}", done, files.Count));
                    busy.Update(done, files.Count);
                }

                try
                {
                    BeginInvoke((Action)delegate
                    {
                        try { busy.Close(); busy.Dispose(); } catch { }
                        StLeft.Text = string.Format("끌어놓기 일괄 변환: 성공 {0} / 실패 {1} → {2}", ok, fail, outDir);
                        MessageBox.Show(this,
                            string.Format("완료했습니다.\r\n\r\n성공 {0}건 / 실패 {1}건\r\n{2}", ok, fail, outDir),
                            "일괄 변환", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    });
                }
                catch { }
            });
        }

        // ══════════════════════════════════════════════════════
        //  검증(--uicheck)
        // ══════════════════════════════════════════════════════
        /// <summary>--screenshot: 덱을 읽어 미리보기까지 그린다(저장/설정 변경 없음).</summary>
        public void LoadDeckForShot(string path)
        {
            _syncPreload = true;
            try { LoadDeck(path, silent: true); }
            finally { _syncPreload = false; }
            RefreshPreview();
        }

        public string UiCheckSummary()
        {
            int n = 0;
            CountControls(this, ref n);

            var sb = new StringBuilder();
            sb.AppendFormat("version={0}", Program.VersionText);
            sb.AppendFormat(" controls={0} size={1}x{2}", n, ClientSize.Width, ClientSize.Height);
            sb.AppendFormat(" theme={0}", CmbTheme.Count);
            sb.AppendFormat(" format={0}", CmbFormat.Count);
            sb.AppendFormat(" cols={0}", NumCols.Value);
            sb.AppendFormat(" cardw={0}", NumCardW.Value);
            sb.AppendFormat(" gap={0}", NumGap.Value);

            try
            {
                var d = new DeckList();
                for (int i = 0; i < 13; i++) d.Main.Add(10000000 + i);
                d.Extra.Add(10000010);
                d.Side.Add(10000010);
                var o = new DeckImageOptions { CardWidth = 90, Columns = 10, ShowCounts = true };
                RenderStats st;
                using (Bitmap bmp = DeckImageRenderer.Render(d, o, out st))
                    sb.AppendFormat(" render={0}x{1} placed={2}", bmp.Width, bmp.Height, st.Placed);

                o.ShowCounts = false;
                o.ShowSide = false;
                o.ShowTitle = false;
                o.ShowFooter = false;
                using (Bitmap bmp2 = DeckImageRenderer.Render(d, o, out st))
                    sb.AppendFormat(" render2={0}x{1}", bmp2.Width, bmp2.Height);
            }
            catch (Exception ex)
            {
                sb.Append(" render=FAIL(" + ex.GetType().Name + ":" + ex.Message + ")");
                Console.WriteLine(ex.ToString());
            }

            sb.AppendFormat(" deck={0}", _deck != null ? "OK" : "none");
            sb.AppendFormat(" preview={0}", _preview != null ? "OK" : "none");
            var shownPics = CardImageStore.SplitFolders(TxtPics.Text.Trim());
            sb.AppendFormat(" pics={0}", (shownPics.Count > 0 && DeckImageRenderer.LooksLikePicsFolder(shownPics[0])) ? "OK" : "none");
            sb.AppendFormat(" net={0}", ChkNet.Checked ? "on" : "off");

            // 여러 폴더(pics) · cdb 조회 · 이름 폴백
            try
            {
                var dirs = OptionsFromUi().ResolvePicsFolders();
                sb.AppendFormat(" picsDirs={0}", dirs.Count);
                using (var cdb = CdbLookup.Create(dirs, _deck != null ? _deck.FilePath : null, AppSettings.Folder))
                {
                    sb.AppendFormat(" cdb={0}", cdb.CdbCount);
                    // Deattach 대상 예시 카드의 이름이 읽히면 이름 폴백 OK
                    cdb.Prefetch(new[] { 14558127, 101403001 });
                    string nm = cdb.DisplayName(14558127);
                    sb.AppendFormat(" cdbName={0}", string.IsNullOrEmpty(nm) ? "none" : "OK");
                }
            }
            catch (Exception ex)
            {
                sb.Append(" cdb=FAIL(" + ex.GetType().Name + ")");
            }
            return sb.ToString();
        }

        private static void CountControls(Control parent, ref int n)
        {
            foreach (Control c in parent.Controls)
            {
                n++;
                if (c.HasChildren) CountControls(c, ref n);
            }
        }
    }

    /// <summary>
    /// 단추 + 메뉴로 만든 어두운 선택 상자.
    /// WinForms ComboBox(DropDownList)는 BackColor 를 무시하고 **흰 필드/어두운 글자**로 그려져
    /// 다크 테마에서 읽기 어렵다 → 테마를 끄거나 색을 덮는 대신 단추+메뉴로 대체한다.
    /// </summary>
    internal sealed class ChoiceBox : Button
    {
        private readonly string[] _items;
        private int _index;
        private readonly ContextMenuStrip _menu;

        public ChoiceBox(string[] items)
        {
            _items = items ?? new string[0];
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 1;
            FlatAppearance.BorderColor = Color.FromArgb(70, 74, 82);
            FlatAppearance.MouseOverBackColor = Color.FromArgb(62, 66, 73);
            BackColor = Color.FromArgb(45, 47, 52);
            ForeColor = Color.FromArgb(232, 234, 238);
            Cursor = Cursors.Hand;
            TextAlign = ContentAlignment.MiddleLeft;
            Padding = new Padding(8, 0, 0, 0);
            UseVisualStyleBackColor = false;
            Height = 26;

            _menu = new ContextMenuStrip { BackColor = Color.FromArgb(38, 40, 44), ForeColor = Color.FromArgb(232, 234, 238), ShowImageMargin = false };
            for (int i = 0; i < _items.Length; i++)
            {
                int idx = i;
                var it = new ToolStripMenuItem(_items[i]);
                it.Click += delegate { SelectedIndex = idx; };
                _menu.Items.Add(it);
            }
            Click += delegate { _menu.Show(this, new Point(0, Height)); };
            SelectedIndex = 0;
        }

        public int Count { get { return _items.Length; } }

        public int SelectedIndex
        {
            get { return _index; }
            set
            {
                if (_items.Length == 0) return;
                _index = Math.Max(0, Math.Min(_items.Length - 1, value));
                Text = _items[_index] + "  ▾";
                for (int i = 0; i < _menu.Items.Count; i++)
                    ((ToolStripMenuItem)_menu.Items[i]).Checked = (i == _index);
                var h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public string SelectedText { get { return _items.Length == 0 ? "" : _items[_index]; } }

        public event EventHandler SelectedIndexChanged;
    }
}

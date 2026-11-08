using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace EdoProDeckImage
{
    /// <summary>다운로드/압축 해제 진행률을 보여주는 작은 창(모달 아님, 가운데 표시).</summary>
    public sealed class BusyForm : Form
    {
        private readonly Label _label;
        private readonly ProgressBar _bar;
        private int _lastDone = -1;

        public BusyForm(string title, string message, int total)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ControlBox = false;
            TopMost = true;
            ClientSize = new Size(380, 105);
            Font = new Font("Malgun Gothic", 9f);

            _label = new Label
            {
                Text = message,
                AutoSize = false,
                Location = new Point(16, 18),
                Size = new Size(348, 24),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _bar = new ProgressBar
            {
                Location = new Point(16, 52),
                Size = new Size(348, 22),
                Style = total > 0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Maximum = total > 0 ? total : 100,
                Value = 0
            };
            Controls.Add(_label);
            Controls.Add(_bar);
        }

        /// <summary>다른 스레드에서 안전하게 진행률 갱신.</summary>
        public void Update(int done, int total)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)delegate
                {
                    if (IsDisposed) return;
                    if (total > 0)
                    {
                        if (_bar.Maximum != total) _bar.Maximum = total;
                        int v = Math.Min(done, total);
                        if (v != _lastDone)
                        {
                            _bar.Value = v;
                            _lastDone = v;
                        }
                        _label.Text = string.Format("이미지 내려받는 중…  {0} / {1}", done, total);
                    }
                });
            }
            catch { }
        }

        public void SetMessage(string text)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)delegate { if (!IsDisposed) _label.Text = text; }); } catch { }
        }
    }
}

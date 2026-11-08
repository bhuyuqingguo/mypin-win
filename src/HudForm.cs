using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MyPin
{
    /// <summary>Small confirmation toast in the top-right corner of the screen.</summary>
    internal sealed class HudForm : Form
    {
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;

        private readonly Timer _animation;
        private readonly Font _titleFont;
        private readonly Font _nameFont;

        private string _name = string.Empty;
        private float _opacity;
        private float _scale = 1f;
        private int _phase;
        private int _holdTicks;

        public HudForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(20, 22, 26);
            DoubleBuffered = true;
            Opacity = 0;
            _titleFont = Ui.Font(8.5f, FontStyle.Regular);
            _nameFont = Ui.Font(10.5f, FontStyle.Bold);

            _animation = new Timer();
            _animation.Interval = 16;
            _animation.Tick += OnAnimationTick;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
                return parameters;
            }
        }

        public void ShowMessage(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                return;
            }

            _name = deviceName;
            _scale = Ui.ScaleOf(this);
            ApplyLayout();
            _opacity = 0f;
            _phase = 0;
            _holdTicks = 0;
            Opacity = 0;

            if (!Visible)
            {
                Show();
            }
            BringToFront();
            _animation.Start();
        }

        private void ApplyLayout()
        {
            int padding = (int)Math.Round(12 * _scale);
            int iconSize = (int)Math.Round(26 * _scale);
            int gap = (int)Math.Round(10 * _scale);
            int maxTextWidth = (int)Math.Round(360 * _scale);

            Size titleSize = TextRenderer.MeasureText(
                L.HudTitle, _titleFont, new Size(10000, 200),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            Size nameSize = TextRenderer.MeasureText(
                _name, _nameFont, new Size(10000, 200),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            int textWidth = Math.Min(Math.Max(titleSize.Width, nameSize.Width), maxTextWidth);
            int textHeight = titleSize.Height + nameSize.Height + (int)Math.Round(2 * _scale);
            int width = padding * 2 + iconSize + gap + textWidth + (int)Math.Round(4 * _scale);
            int height = padding * 2 + Math.Max(iconSize, textHeight);

            ClientSize = new Size(width, height);
            UpdateRegion();

            Rectangle working = Screen.PrimaryScreen.WorkingArea;
            Left = working.Right - Width - (int)Math.Round(24 * _scale);
            Top = working.Top + (int)Math.Round(24 * _scale);
        }

        private void UpdateRegion()
        {
            using (GraphicsPath path = Ui.RoundedRect(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), 12 * _scale))
            {
                Region previous = Region;
                Region = new Region(path);
                if (previous != null)
                {
                    previous.Dispose();
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (GraphicsPath path = Ui.RoundedRect(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), 12 * _scale))
            {
                using (SolidBrush background = new SolidBrush(Color.FromArgb(242, 20, 22, 26)))
                {
                    graphics.FillPath(background, path);
                }
                using (Pen border = new Pen(Color.FromArgb(28, 255, 255, 255), 1f))
                {
                    graphics.DrawPath(border, path);
                }
            }

            int padding = (int)Math.Round(12 * _scale);
            int iconSize = (int)Math.Round(26 * _scale);
            int gap = (int)Math.Round(10 * _scale);

            using (Bitmap mic = Glyphs.RenderMic(iconSize, MicState.Locked, Glyphs.Accent))
            {
                graphics.DrawImage(mic, padding, (ClientSize.Height - iconSize) / 2, iconSize, iconSize);
            }

            Size titleSize = TextRenderer.MeasureText(
                L.HudTitle, _titleFont, new Size(10000, 200),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            Size nameSize = TextRenderer.MeasureText(
                _name, _nameFont, new Size(10000, 200),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            int textLeft = padding + iconSize + gap;
            int textWidth = ClientSize.Width - textLeft - padding;
            int blockHeight = titleSize.Height + nameSize.Height + (int)Math.Round(2 * _scale);
            int textTop = (ClientSize.Height - blockHeight) / 2;

            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

            TextRenderer.DrawText(
                graphics, L.HudTitle, _titleFont,
                new Rectangle(textLeft, textTop, textWidth, titleSize.Height),
                Color.FromArgb(150, 160, 175), flags);

            TextRenderer.DrawText(
                graphics, _name, _nameFont,
                new Rectangle(textLeft, textTop + titleSize.Height + (int)Math.Round(2 * _scale), textWidth, nameSize.Height),
                Color.White, flags);
        }

        private void OnAnimationTick(object sender, EventArgs e)
        {
            if (_phase == 0)
            {
                _opacity += 0.16f;
                if (_opacity >= 0.96f)
                {
                    _opacity = 0.96f;
                    _phase = 1;
                }
            }
            else if (_phase == 1)
            {
                _holdTicks++;
                if (_holdTicks >= 105)
                {
                    _phase = 2;
                }
            }
            else
            {
                _opacity -= 0.09f;
                if (_opacity <= 0f)
                {
                    _opacity = 0f;
                    _animation.Stop();
                    Opacity = 0;
                    Hide();
                    return;
                }
            }

            Opacity = _opacity;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animation.Stop();
                _animation.Dispose();
                _titleFont.Dispose();
                _nameFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

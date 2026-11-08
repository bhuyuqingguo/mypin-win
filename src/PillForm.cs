using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MyPin
{
    /// <summary>
    /// Windows has no persistent text next to tray icons, so the optional
    /// "device name" is shown as a click-through label tucked above the
    /// taskbar corner.
    /// </summary>
    internal sealed class PillForm : Form
    {
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;
        private const int WsExTransparent = 0x00000020;

        private Font _nameFont;
        private string _name = string.Empty;
        private float _scale = 1f;

        public PillForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(20, 22, 26);
            DoubleBuffered = true;
            ApplyFonts();
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
                parameters.ExStyle |= WsExToolWindow | WsExNoActivate | WsExTransparent;
                return parameters;
            }
        }

        public void UpdateContent(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                if (Visible)
                {
                    Hide();
                }
                return;
            }

            _name = deviceName;
            _scale = Ui.ScaleOf(this);
            ApplyLayout();
            if (!Visible)
            {
                Show();
            }
            BringToFront();
            Invalidate();
        }

        private void ApplyLayout()
        {
            ApplyFonts();

            int padding = (int)Math.Round(9 * _scale);
            int iconSize = (int)Math.Round(14 * _scale);
            int gap = (int)Math.Round(6 * _scale);

            Size textSize = TextRenderer.MeasureText(
                _name, _nameFont, new Size(360, 200),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

            int width = padding * 2 + iconSize + gap + Math.Min(textSize.Width, (int)Math.Round(260 * _scale));
            int height = padding * 2 + Math.Max(iconSize, textSize.Height);

            ClientSize = new Size(width, height);
            UpdateRegion();

            Rectangle working = Screen.PrimaryScreen.WorkingArea;
            Left = working.Right - Width - (int)Math.Round(6 * _scale);
            Top = working.Bottom - Height - (int)Math.Round(6 * _scale);
        }

        private void ApplyFonts()
        {
            if (_nameFont != null)
            {
                _nameFont.Dispose();
            }
            _nameFont = Ui.Font(9f, FontStyle.Regular, _scale);
        }

        private void UpdateRegion()
        {
            using (GraphicsPath path = Ui.RoundedRect(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), 9 * _scale))
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

            using (GraphicsPath path = Ui.RoundedRect(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), 9 * _scale))
            {
                using (SolidBrush background = new SolidBrush(Color.FromArgb(235, 20, 22, 26)))
                {
                    graphics.FillPath(background, path);
                }
                using (Pen border = new Pen(Color.FromArgb(26, 255, 255, 255), 1f))
                {
                    graphics.DrawPath(border, path);
                }
            }

            int padding = (int)Math.Round(9 * _scale);
            int iconSize = (int)Math.Round(14 * _scale);
            int gap = (int)Math.Round(6 * _scale);

            using (Bitmap mic = Glyphs.RenderMic(iconSize, MicState.Locked, Glyphs.Accent))
            {
                graphics.DrawImage(mic, padding, (ClientSize.Height - iconSize) / 2, iconSize, iconSize);
            }

            int textLeft = padding + iconSize + gap;
            int textWidth = ClientSize.Width - textLeft - padding;
            Size textSize = TextRenderer.MeasureText(
                _name, _nameFont, new Size(10000, 200),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            TextRenderer.DrawText(
                graphics, _name, _nameFont,
                new Rectangle(textLeft, (ClientSize.Height - textSize.Height) / 2, textWidth, textSize.Height),
                Color.FromArgb(232, 236, 244), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_nameFont != null)
                {
                    _nameFont.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}

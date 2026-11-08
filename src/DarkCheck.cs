using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MyPin
{
    /// <summary>
    /// Check box drawn by hand: the system glyphs are light themed and would stand
    /// out badly on a dark window.
    /// </summary>
    internal sealed class DarkCheck : Control
    {
        private bool _checked;
        private bool _hover;
        private float _scale = 1f;

        public event EventHandler CheckedChanged;

        public DarkCheck()
        {
            SetStyle(
                ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Window;
            ForeColor = Theme.TextPrimary;
            Font = Ui.Font(9.5f, FontStyle.Regular);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value)
                {
                    return;
                }
                _checked = value;
                Invalidate();
                EventHandler handler = CheckedChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        public void SetScale(float scale)
        {
            _scale = scale <= 0f ? 1f : scale;
            Invalidate();
        }

        private int Px(float value)
        {
            return (int)Math.Round(value * _scale);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Focus();
                Checked = !Checked;
                return;
            }
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Space || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                Checked = !Checked;
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (SolidBrush background = new SolidBrush(BackColor))
            {
                graphics.FillRectangle(background, ClientRectangle);
            }

            int size = Px(18);
            RectangleF box = new RectangleF(Px(1), (Height - size) / 2f, size, size);
            float radius = 4f * _scale;

            if (_checked)
            {
                using (GraphicsPath path = Ui.RoundedRect(box, radius))
                using (SolidBrush fill = new SolidBrush(_hover ? Theme.AccentHover : Theme.Accent))
                {
                    graphics.FillPath(fill, path);
                }

                using (Pen pen = new Pen(Color.White, Math.Max(1.5f, 2f * _scale)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    float x = box.Left;
                    float y = box.Top;
                    graphics.DrawLines(pen, new PointF[]
                    {
                        new PointF(x + size * 0.25f, y + size * 0.52f),
                        new PointF(x + size * 0.43f, y + size * 0.70f),
                        new PointF(x + size * 0.76f, y + size * 0.31f)
                    });
                }
            }
            else
            {
                using (GraphicsPath path = Ui.RoundedRect(box, radius))
                using (SolidBrush fill = new SolidBrush(Theme.SurfaceAlt))
                using (Pen border = new Pen(
                    _hover ? Theme.TextMuted : Color.FromArgb(74, 83, 97),
                    Math.Max(1f, 1.3f * _scale)))
                {
                    border.Alignment = PenAlignment.Inset;
                    graphics.FillPath(fill, path);
                    graphics.DrawPath(border, path);
                }
            }

            Color textColor = !Enabled
                ? Theme.TextMuted
                : (_hover ? Theme.TextPrimary : Color.FromArgb(214, 219, 228));

            int textLeft = (int)Math.Round(box.Right) + Px(10);
            Rectangle textBounds = new Rectangle(
                textLeft, 0, Math.Max(Width - textLeft, Px(4)), Height);

            TextRenderer.DrawText(
                graphics, Text, Font, textBounds, textColor,
                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}

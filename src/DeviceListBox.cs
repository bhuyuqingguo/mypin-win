using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MicPin
{
    /// <summary>One line in the microphone priority list.</summary>
    internal sealed class DeviceRow
    {
        public string Id;
        public string Text;
        public string Tag;
        public string Note;
        public bool IsAuto;
        public bool Blocked;
        public bool CanMoveUp;
        public bool CanMoveDown;
    }

    /// <summary>delta: -1 = move up, +1 = move down, 0 = toggle the mute state.</summary>
    internal sealed class DeviceActionEventArgs : EventArgs
    {
        public readonly string Id;
        public readonly int Delta;

        public DeviceActionEventArgs(string id, int delta)
        {
            Id = id;
            Delta = delta;
        }
    }

    /// <summary>
    /// Owner-drawn list where every row carries its own reorder arrows and a
    /// per-session mute switch. Clicking anywhere else still selects the device.
    /// </summary>
    internal sealed class DeviceListBox : ListBox
    {
        private const int RowHeight = 40;
        private const int BlockWidth = 54;
        private const int ArrowWidth = 20;

        private readonly Font _nameFont;
        private readonly Font _smallFont;

        private float _scale = 1f;
        private int _hover = -1;

        public event EventHandler<DeviceActionEventArgs> DeviceAction;

        public DeviceListBox()
        {
            BorderStyle = BorderStyle.None;
            DrawMode = DrawMode.OwnerDrawFixed;
            IntegralHeight = false;
            BackColor = Theme.SurfaceAlt;
            _nameFont = Ui.Font(9.5f, FontStyle.Regular);
            _smallFont = Ui.Font(8f, FontStyle.Regular);
            Font = _nameFont;
        }

        /// <summary>Re-applies the DPI scale; call again whenever the window DPI changes.</summary>
        public void SetScale(float scale)
        {
            _scale = scale <= 0f ? 1f : scale;
            ItemHeight = Px(RowHeight);
            Invalidate();
        }

        private int Px(float value)
        {
            return (int)Math.Round(value * _scale);
        }

        #region Hit areas

        private Rectangle BlockRect(Rectangle bounds)
        {
            int height = Px(21);
            return new Rectangle(
                bounds.Right - Px(8) - Px(BlockWidth),
                bounds.Top + (bounds.Height - height) / 2,
                Px(BlockWidth),
                height);
        }

        private Rectangle DownRect(Rectangle bounds)
        {
            Rectangle block = BlockRect(bounds);
            int width = Px(ArrowWidth);
            return new Rectangle(
                block.Left - Px(4) - width,
                bounds.Top + (bounds.Height - Px(20)) / 2,
                width,
                Px(20));
        }

        private Rectangle UpRect(Rectangle bounds)
        {
            Rectangle down = DownRect(bounds);
            return new Rectangle(down.Left - Px(ArrowWidth), down.Top, down.Width, down.Height);
        }

        /// <summary>Extends a small glyph box to the full row height so it is easy to hit.</summary>
        private static Rectangle RowHit(Rectangle rect, Rectangle bounds)
        {
            return new Rectangle(rect.Left, bounds.Top, rect.Width, bounds.Height);
        }

        private bool IsActionable(DeviceRow row, Rectangle bounds, Point point)
        {
            if (row == null || row.IsAuto)
            {
                return false;
            }
            if (RowHit(BlockRect(bounds), bounds).Contains(point))
            {
                return true;
            }
            if (row.CanMoveUp && RowHit(UpRect(bounds), bounds).Contains(point))
            {
                return true;
            }
            return row.CanMoveDown && RowHit(DownRect(bounds), bounds).Contains(point);
        }

        #endregion

        #region Interaction

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                int index = IndexFromPoint(e.Location);
                if (index >= 0 && index < Items.Count)
                {
                    DeviceRow row = Items[index] as DeviceRow;
                    Rectangle bounds = GetItemRectangle(index);
                    if (row != null && !row.IsAuto)
                    {
                        if (RowHit(BlockRect(bounds), bounds).Contains(e.Location))
                        {
                            Raise(row, 0);
                            return;
                        }
                        if (row.CanMoveUp && RowHit(UpRect(bounds), bounds).Contains(e.Location))
                        {
                            Raise(row, -1);
                            return;
                        }
                        if (row.CanMoveDown && RowHit(DownRect(bounds), bounds).Contains(e.Location))
                        {
                            Raise(row, 1);
                            return;
                        }
                    }
                }
            }
            base.OnMouseDown(e);
        }

        private void Raise(DeviceRow row, int delta)
        {
            EventHandler<DeviceActionEventArgs> handler = DeviceAction;
            if (handler != null)
            {
                handler(this, new DeviceActionEventArgs(row.Id, delta));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int index = IndexFromPoint(e.Location);
            bool actionable = false;
            if (index >= 0 && index < Items.Count)
            {
                actionable = IsActionable(Items[index] as DeviceRow, GetItemRectangle(index), e.Location);
            }

            Cursor target = actionable ? Cursors.Hand : Cursors.Default;
            if (Cursor != target)
            {
                Cursor = target;
            }

            if (_hover != index)
            {
                _hover = index;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1)
            {
                _hover = -1;
                Invalidate();
            }
        }

        #endregion

        #region Painting

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count)
            {
                return;
            }

            DeviceRow row = Items[e.Index] as DeviceRow;
            if (row == null)
            {
                return;
            }

            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle bounds = e.Bounds;
            bool selected = e.Index == SelectedIndex;
            bool hovered = !selected && e.Index == _hover;

            Color background = selected
                ? Theme.RowSelected
                : (hovered ? Theme.RowHover : Theme.SurfaceAlt);
            using (SolidBrush brush = new SolidBrush(background))
            {
                graphics.FillRectangle(brush, bounds);
            }

            if (e.Index > 0)
            {
                using (Pen line = new Pen(Theme.Separator, 1f))
                {
                    float y = bounds.Top + 0.5f;
                    graphics.DrawLine(line, bounds.Left + Px(14), y, bounds.Right - Px(10), y);
                }
            }

            int centerY = bounds.Top + bounds.Height / 2;
            DrawRadio(graphics, bounds, centerY, selected);

            int textLeft = bounds.Left + Px(38);
            int textRight = bounds.Right - Px(10);

            if (row.IsAuto)
            {
                textRight = DrawNote(graphics, bounds, row.Note, textRight);
                DrawName(graphics, row, textLeft, textRight, bounds, selected);
                return;
            }

            Rectangle block = BlockRect(bounds);
            Rectangle down = DownRect(bounds);
            Rectangle up = UpRect(bounds);

            DrawArrow(graphics, up, true, row.CanMoveUp);
            DrawArrow(graphics, down, false, row.CanMoveDown);
            DrawBlock(graphics, block, row.Blocked);

            int chipRight = up.Left - Px(6);
            if (!string.IsNullOrEmpty(row.Tag))
            {
                Size tagSize = TextRenderer.MeasureText(
                    row.Tag, _smallFont, new Size(1000, 100),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                int chipWidth = tagSize.Width + Px(14);
                int chipHeight = Px(19);
                RectangleF chip = new RectangleF(chipRight - chipWidth, centerY - chipHeight / 2f, chipWidth, chipHeight);

                using (GraphicsPath path = Ui.RoundedRect(chip, chipHeight / 2f))
                using (SolidBrush fill = new SolidBrush(Theme.ChipBack))
                {
                    graphics.FillPath(fill, path);
                }

                TextRenderer.DrawText(
                    graphics, row.Tag, _smallFont,
                    new Rectangle((int)Math.Round(chip.X), (int)Math.Round(chip.Y), chipWidth, chipHeight),
                    Theme.TextSecondary,
                    TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                chipRight -= chipWidth + Px(6);
            }

            textRight = Math.Min(textRight, chipRight);
            DrawName(graphics, row, textLeft, textRight, bounds, selected);
        }

        private void DrawName(Graphics graphics, DeviceRow row, int left, int right, Rectangle bounds, bool selected)
        {
            Color color;
            if (row.Blocked)
            {
                color = Theme.TextMuted;
            }
            else if (selected)
            {
                color = Theme.TextPrimary;
            }
            else
            {
                color = Color.FromArgb(216, 221, 230);
            }

            TextRenderer.DrawText(
                graphics, row.Text, _nameFont,
                new Rectangle(left, bounds.Top, Math.Max(right - left, Px(20)), bounds.Height),
                color,
                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        /// <summary>Right-aligned hint on the automatic row; returns the new text limit.</summary>
        private int DrawNote(Graphics graphics, Rectangle bounds, string note, int textRight)
        {
            if (string.IsNullOrEmpty(note))
            {
                return textRight;
            }

            Size size = TextRenderer.MeasureText(
                note, _smallFont, new Size(10000, 100),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            int noteLeft = Math.Max(textRight - size.Width, bounds.Left + Px(120));

            TextRenderer.DrawText(
                graphics, note, _smallFont,
                new Rectangle(noteLeft, bounds.Top, textRight - noteLeft, bounds.Height),
                Theme.TextMuted,
                TextFormatFlags.NoPadding | TextFormatFlags.Right |
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            return noteLeft - Px(10);
        }

        private void DrawRadio(Graphics graphics, Rectangle bounds, int centerY, bool selected)
        {
            int radius = Px(7);
            int centerX = bounds.Left + Px(15);
            Rectangle ring = new Rectangle(centerX - radius, centerY - radius, radius * 2, radius * 2);

            using (Pen pen = new Pen(
                selected ? Glyphs.Accent : Color.FromArgb(76, 85, 99),
                Math.Max(1f, 1.4f * _scale)))
            {
                graphics.DrawEllipse(pen, ring);
            }

            if (selected)
            {
                int inner = Px(8);
                using (SolidBrush dot = new SolidBrush(Glyphs.Accent))
                {
                    graphics.FillEllipse(dot, centerX - inner / 2f, centerY - inner / 2f, inner, inner);
                }
            }
        }

        private void DrawArrow(Graphics graphics, Rectangle area, bool up, bool enabled)
        {
            int width = Px(9);
            int height = Px(5);
            float centerX = area.Left + area.Width / 2f;
            float centerY = area.Top + area.Height / 2f;

            PointF[] points = up
                ? new PointF[]
                {
                    new PointF(centerX, centerY - height / 2f),
                    new PointF(centerX - width / 2f, centerY + height / 2f),
                    new PointF(centerX + width / 2f, centerY + height / 2f)
                }
                : new PointF[]
                {
                    new PointF(centerX, centerY + height / 2f),
                    new PointF(centerX - width / 2f, centerY - height / 2f),
                    new PointF(centerX + width / 2f, centerY - height / 2f)
                };

            using (SolidBrush brush = new SolidBrush(
                enabled ? Theme.Arrow : Theme.ArrowDisabled))
            {
                graphics.FillPolygon(brush, points);
            }
        }

        private void DrawBlock(Graphics graphics, Rectangle rect, bool blocked)
        {
            using (GraphicsPath path = Ui.RoundedRect(rect, rect.Height / 2f))
            {
                if (blocked)
                {
                    using (SolidBrush fill = new SolidBrush(Theme.WarnBack))
                    {
                        graphics.FillPath(fill, path);
                    }
                    using (Pen border = new Pen(Theme.WarnBorder, 1f))
                    {
                        border.Alignment = PenAlignment.Inset;
                        graphics.DrawPath(border, path);
                    }
                }
                else
                {
                    using (Pen border = new Pen(Color.FromArgb(58, 66, 78), 1f))
                    {
                        border.Alignment = PenAlignment.Inset;
                        graphics.DrawPath(border, path);
                    }
                }
            }

            TextRenderer.DrawText(
                graphics, blocked ? L.Blocked : L.Block, _smallFont, rect,
                blocked ? Theme.WarnText : Theme.TextSecondary,
                TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _nameFont.Dispose();
                _smallFont.Dispose();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MicPin
{
    /// <summary>Immutable view of everything the window needs to render.</summary>
    internal sealed class AppSnapshot
    {
        public List<AudioInput> Devices = new List<AudioInput>();
        public List<string> BlockedIds = new List<string>();
        public string CurrentId;
        public string CurrentName = string.Empty;
        public string StatusText = string.Empty;
        public MicState State = MicState.Idle;
        public bool AutoLock = true;
        public bool ShowHud = true;
        public bool ShowName;
        public bool LaunchAtLogin;
        public string PinnedDeviceId;
    }

    /// <summary>The settings window: current input, priority list and switches.</summary>
    internal sealed class MainForm : Form
    {
        private const int WmDpiChanged = 0x02E0;
        private const int DwmUseImmersiveDarkMode = 20;
        private const int DwmUseImmersiveDarkModeLegacy = 19;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        private readonly TrayApp _app;
        private readonly List<Font> _fonts = new List<Font>();

        private readonly Label _caption;
        private readonly Label _name;
        private readonly Label _status;
        private readonly Label _hint;
        private readonly DeviceListBox _list;
        private readonly Button _restore;
        private readonly DarkCheck _autoLock;
        private readonly DarkCheck _showHud;
        private readonly DarkCheck _showName;
        private readonly DarkCheck _launch;
        private readonly Button _recheck;
        private readonly Button _close;

        private readonly Font _titleFont;
        private readonly Font _subtitleFont;
        private readonly Font _sectionFont;

        private float _scale = 1f;
        private MicState _state = MicState.Idle;
        private bool _updating;
        private string _signature = "\u0000";

        // Rows currently shown in the priority list, used to size the window.
        private int _rows = 3;
        private int _listHeight = 120;
        private int _optionsTitleY = 364;

        public MainForm(TrayApp app)
        {
            _app = app;

            Text = L.AppName;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Window;
            ForeColor = Theme.TextPrimary;
            DoubleBuffered = true;
            Icon = Glyphs.CreateIcon(32, MicState.Idle);

            IntPtr handle = Handle; // create the window first so the real DPI is known
            if (handle == IntPtr.Zero)
            {
                // Fall through with 100% scaling.
            }
            _scale = Ui.ScaleOf(this);

            _titleFont = NewFont(15f, FontStyle.Bold);
            _subtitleFont = NewFont(8.5f, FontStyle.Regular);
            _sectionFont = NewFont(9f, FontStyle.Bold);

            _caption = MakeLabel(L.CardCurrentInput, NewFont(8.5f, FontStyle.Regular), Theme.TextSecondary, Theme.Surface);
            _name = MakeLabel(string.Empty, NewFont(12f, FontStyle.Bold), Theme.TextPrimary, Theme.Surface);
            _status = MakeLabel(string.Empty, NewFont(8.5f, FontStyle.Regular), Theme.TextSecondary, Theme.Surface);
            _hint = MakeLabel(L.SectionDevicesHint, NewFont(8f, FontStyle.Regular), Theme.TextMuted, Theme.Window);

            _list = new DeviceListBox();
            _list.DeviceAction += OnDeviceAction;
            _list.SelectedIndexChanged += OnSelectionChanged;

            _restore = MakeButton(L.UnblockAll, false, 8.5f);
            _restore.Enabled = false;
            _restore.Click += delegate { _app.UnblockAll(); };
            Controls.Add(_restore);

            _autoLock = MakeToggle(L.MenuAutoLock);
            _showHud = MakeToggle(L.MenuShowHud);
            _showName = MakeToggle(L.MenuShowName);
            _launch = MakeToggle(L.MenuLaunchAtLogin);

            _autoLock.CheckedChanged += delegate { if (!_updating) _app.SetAutoLock(_autoLock.Checked); };
            _showHud.CheckedChanged += delegate { if (!_updating) _app.SetShowHud(_showHud.Checked); };
            _showName.CheckedChanged += delegate { if (!_updating) _app.SetShowName(_showName.Checked); };
            _launch.CheckedChanged += delegate { if (!_updating) _app.SetLaunchAtLogin(_launch.Checked); };

            _recheck = MakeButton(L.ButtonRecheck, true, 9.5f);
            _recheck.Click += delegate { _app.RecheckNow(); };
            Controls.Add(_recheck);

            _close = MakeButton(L.ButtonClose, false, 9.5f);
            _close.Click += delegate { Hide(); };
            Controls.Add(_close);

            Controls.Add(_caption);
            Controls.Add(_name);
            Controls.Add(_status);
            Controls.Add(_hint);
            Controls.Add(_list);
            Controls.Add(_autoLock);
            Controls.Add(_showHud);
            Controls.Add(_showName);
            Controls.Add(_launch);

            CancelButton = _close;

            ApplyLayout();
        }

        private Font NewFont(float points, FontStyle style)
        {
            Font font = Ui.Font(points, style);
            _fonts.Add(font);
            return font;
        }

        private int Px(float value)
        {
            return (int)Math.Round(value * _scale);
        }

        /// <summary>All control geometry lives here so a DPI change is a single re-run.</summary>
        private void ApplyLayout()
        {
            SuspendLayout();
            try
            {
                _listHeight = _rows * 40;
                _optionsTitleY = 220 + _listHeight + 24;
                int checkTop = _optionsTitleY + 20;
                int buttonTop = checkTop + 4 * 30 + 14;

                ClientSize = new Size(Px(460), Px(buttonTop + 34 + 18));

                _caption.SetBounds(Px(32), Px(88), Px(320), Px(15));
                _name.SetBounds(Px(32), Px(104), Px(336), Px(24));
                _status.SetBounds(Px(32), Px(130), Px(352), Px(18));

                _hint.SetBounds(Px(18), Px(200), Px(400), Px(16));
                _restore.SetBounds(Px(352), Px(172), Px(92), Px(24));

                _list.SetScale(_scale);
                _list.SetBounds(Px(18), Px(222), Px(424), Px(_listHeight));
                using (GraphicsPath clip = Ui.RoundedRect(
                    new RectangleF(0, 0, Px(424), Px(_listHeight)), 8f * _scale))
                {
                    Region previous = _list.Region;
                    _list.Region = new Region(clip);
                    if (previous != null)
                    {
                        previous.Dispose();
                    }
                }

                _autoLock.SetScale(_scale);
                _showHud.SetScale(_scale);
                _showName.SetScale(_scale);
                _launch.SetScale(_scale);

                _autoLock.SetBounds(Px(16), Px(checkTop), Px(428), Px(26));
                _showHud.SetBounds(Px(16), Px(checkTop + 30), Px(428), Px(26));
                _showName.SetBounds(Px(16), Px(checkTop + 60), Px(428), Px(26));
                _launch.SetBounds(Px(16), Px(checkTop + 90), Px(428), Px(26));

                _recheck.SetBounds(Px(16), Px(buttonTop), Px(160), Px(34));
                _close.SetBounds(Px(352), Px(buttonTop), Px(92), Px(34));
            }
            finally
            {
                ResumeLayout();
            }

            Invalidate();
        }

        private Label MakeLabel(string text, Font font, Color color, Color backColor)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = backColor;
            label.AutoSize = false;
            label.AutoEllipsis = true;
            label.UseMnemonic = false;
            return label;
        }

        private DarkCheck MakeToggle(string text)
        {
            DarkCheck box = new DarkCheck();
            box.Text = text;
            box.Font = NewFont(9.5f, FontStyle.Regular);
            box.BackColor = Theme.Window;
            return box;
        }

        private Button MakeButton(string text, bool primary, float fontSize)
        {
            Button button = new Button();
            button.Text = text;
            button.Font = NewFont(fontSize, FontStyle.Regular);
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.BackColor = primary ? Theme.Accent : Theme.ButtonBack;
            button.ForeColor = primary ? Color.White : Theme.TextPrimary;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Theme.Border;
            button.FlatAppearance.MouseOverBackColor = primary ? Theme.AccentHover : Theme.ButtonHover;
            button.FlatAppearance.MouseDownBackColor = primary ? Theme.AccentPressed : Theme.ButtonBack;
            return button;
        }

        public void UpdateSnapshot(AppSnapshot snapshot)
        {
            _updating = true;
            try
            {
                _state = snapshot.State;
                _name.Text = snapshot.CurrentName;
                _status.Text = snapshot.StatusText;

                _autoLock.Checked = snapshot.AutoLock;
                _showHud.Checked = snapshot.ShowHud;
                _showName.Checked = snapshot.ShowName;
                _launch.Checked = snapshot.LaunchAtLogin;

                _restore.Enabled = snapshot.BlockedIds.Count > 0;

                string signature = (snapshot.PinnedDeviceId ?? "-") + "|";
                for (int i = 0; i < snapshot.Devices.Count; i++)
                {
                    AudioInput device = snapshot.Devices[i];
                    signature += device.Id + (snapshot.BlockedIds.Contains(device.Id) ? "*" : string.Empty) + ",";
                }

                if (signature != _signature)
                {
                    _signature = signature;

                    int rows = Math.Min(Math.Max(snapshot.Devices.Count + 1, 1), 5);
                    if (rows != _rows)
                    {
                        _rows = rows;
                        ApplyLayout();
                    }

                    RebuildRows(snapshot);
                }

                int index = 0;
                if (!string.IsNullOrEmpty(snapshot.PinnedDeviceId))
                {
                    for (int i = 1; i < _list.Items.Count; i++)
                    {
                        DeviceRow row = _list.Items[i] as DeviceRow;
                        if (row != null && row.Id == snapshot.PinnedDeviceId)
                        {
                            index = i;
                            break;
                        }
                    }
                }
                if (_list.SelectedIndex != index)
                {
                    _list.SelectedIndex = index;
                }
            }
            finally
            {
                _updating = false;
            }

            Invalidate();
        }

        private void RebuildRows(AppSnapshot snapshot)
        {
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();

                DeviceRow automatic = new DeviceRow();
                automatic.IsAuto = true;
                automatic.Text = L.AutoRow;
                automatic.Note = L.AutoRowHint;
                _list.Items.Add(automatic);

                int count = snapshot.Devices.Count;
                for (int i = 0; i < count; i++)
                {
                    AudioInput device = snapshot.Devices[i];
                    DeviceRow row = new DeviceRow();
                    row.Id = device.Id;
                    row.Text = device.Name;
                    row.Tag = device.Tag;
                    row.Blocked = snapshot.BlockedIds.Contains(device.Id);
                    row.CanMoveUp = i > 0;
                    row.CanMoveDown = i < count - 1;
                    _list.Items.Add(row);
                }
            }
            finally
            {
                _list.EndUpdate();
            }
        }

        private void OnDeviceAction(object sender, DeviceActionEventArgs e)
        {
            if (e.Delta == 0)
            {
                _app.ToggleBlock(e.Id);
            }
            else
            {
                _app.MoveDevice(e.Id, e.Delta);
            }
        }

        private void OnSelectionChanged(object sender, EventArgs e)
        {
            if (_updating || _list.SelectedIndex < 0)
            {
                return;
            }
            DeviceRow row = _list.Items[_list.SelectedIndex] as DeviceRow;
            if (row == null)
            {
                return;
            }
            _app.PinDevice(row.Id, row.Id == null ? null : row.Text);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            TryEnableDarkTitleBar();
        }

        /// <summary>Asks DWM to paint the non-client area dark so the frame matches.</summary>
        private void TryEnableDarkTitleBar()
        {
            int enabled = 1;
            try
            {
                if (DwmSetWindowAttribute(Handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(Handle, DwmUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
                }
            }
            catch (Exception)
            {
                // Older Windows builds simply keep the default frame.
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WmDpiChanged)
            {
                ApplyScale();
            }
        }

        private void ApplyScale()
        {
            float scale = Ui.ScaleOf(this);
            if (Math.Abs(scale - _scale) < 0.01f)
            {
                return;
            }
            _scale = scale;
            ApplyLayout();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (SolidBrush background = new SolidBrush(Theme.Window))
            {
                graphics.FillRectangle(background, ClientRectangle);
            }

            int iconSize = Px(34);
            using (Bitmap mic = Glyphs.RenderMic(iconSize, MicState.Locked, Glyphs.Accent))
            {
                graphics.DrawImage(mic, Px(18), Px(16), iconSize, iconSize);
            }

            TextRenderer.DrawText(graphics, L.AppName, _titleFont, new Point(Px(62), Px(14)), Theme.TextPrimary, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(graphics, L.WindowSubtitle, _subtitleFont, new Point(Px(63), Px(44)), Theme.TextMuted, TextFormatFlags.NoPadding);

            DrawCard(graphics, new RectangleF(Px(16), Px(78), Px(428), Px(84)), 10f);

            int dot = Px(10);
            using (SolidBrush dotBrush = new SolidBrush(Theme.State(_state)))
            {
                graphics.FillEllipse(dotBrush, Px(414), Px(91), dot, dot);
            }

            TextRenderer.DrawText(graphics, L.SectionDevices, _sectionFont, new Point(Px(18), Px(180)), Theme.TextSecondary, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(graphics, L.SectionOptions, _sectionFont, new Point(Px(18), Px(_optionsTitleY)), Theme.TextSecondary, TextFormatFlags.NoPadding);

            DrawListFrame(graphics);
        }

        private void DrawCard(Graphics graphics, RectangleF bounds, float radius)
        {
            using (GraphicsPath path = Ui.RoundedRect(bounds, radius * _scale))
            using (SolidBrush fill = new SolidBrush(Theme.Surface))
            using (Pen border = new Pen(Theme.Border, 1f))
            {
                border.Alignment = PenAlignment.Inset;
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);
            }
        }

        private void DrawListFrame(Graphics graphics)
        {
            using (GraphicsPath path = Ui.RoundedRect(
                new RectangleF(Px(16), Px(220), Px(428), Px(_listHeight + 4)), 10f * _scale))
            using (Pen border = new Pen(Theme.Border, 1f))
            {
                border.Alignment = PenAlignment.Inset;
                graphics.DrawPath(border, path);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                for (int i = 0; i < _fonts.Count; i++)
                {
                    _fonts[i].Dispose();
                }
                _fonts.Clear();
                if (Icon != null)
                {
                    Icon.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MyPin
{
    internal sealed class TrayApp : ApplicationContext
    {
        private readonly Settings _settings;
        private readonly AudioSystem _audio;
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _menu;
        private readonly Control _dispatcher;
        private readonly Timer _debounce;
        private readonly Timer _safety;
        private readonly HudForm _hud;
        private readonly PillForm _pill;
        private readonly Font _headerFont;
        private readonly Font _statusFont;

        private readonly List<DateTime> _recentSwitches = new List<DateTime>();

        // Devices the user muted for this session only; never persisted on purpose.
        private readonly HashSet<string> _blocked = new HashSet<string>();

        private MicState _iconState = (MicState)(-1);
        private string _statusText = string.Empty;
        private string _currentName = string.Empty;
        private DateTime _backoffUntil = DateTime.MinValue;
        private bool _disposed;
        private MainForm _window;
        private AppSnapshot _snapshot;

        public TrayApp()
        {
            _settings = Settings.Load();

            try
            {
                _audio = new AudioSystem();
                _audio.StartMonitoring();
                _audio.DevicesChanged += OnAudioServiceChanged;
            }
            catch (Exception)
            {
                _audio = null;
            }

            Font menuFont = SystemFonts.MenuFont;
            _headerFont = new Font(menuFont, FontStyle.Bold);
            _statusFont = new Font(menuFont.FontFamily, Math.Max(7f, menuFont.Size - 1f), FontStyle.Regular);

            _menu = new ContextMenuStrip();
            _menu.Opening += delegate { RebuildMenu(); };

            _tray = new NotifyIcon();
            _tray.ContextMenuStrip = _menu;
            _tray.Visible = true;
            _tray.Text = L.AppName;
            _tray.MouseClick += OnTrayClick;
            _tray.DoubleClick += delegate { ShowWindow(); };
            ApplyTrayIcon(MicState.Idle);

            _hud = new HudForm();
            _pill = new PillForm();

            _dispatcher = new Control();
            IntPtr handle = _dispatcher.Handle; // forces handle creation on the UI thread
            if (handle == IntPtr.Zero)
            {
                // Nothing to do; polling still keeps the app functional.
            }

            _debounce = new Timer();
            _debounce.Interval = 350;
            _debounce.Tick += delegate
            {
                _debounce.Stop();
                Evaluate(false);
            };

            _safety = new Timer();
            _safety.Interval = 3000;
            _safety.Tick += delegate { Evaluate(false); };
            _safety.Start();

            Startup.SetEnabled(_settings.LaunchAtLogin, Application.ExecutablePath);

            Evaluate(false);
        }

        private void OnTrayClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowWindow();
            }
        }

        private void OnAudioServiceChanged(object sender, EventArgs e)
        {
            try
            {
                _dispatcher.BeginInvoke((MethodInvoker)delegate
                {
                    _debounce.Stop();
                    _debounce.Start();
                });
            }
            catch (Exception)
            {
                // The dispatcher may already be gone during shutdown.
            }
        }

        /// <summary>Resolves the target device, enforces it, and refreshes the UI.</summary>
        private void Evaluate(bool forceHud)
        {
            if (_disposed)
            {
                return;
            }

            if (_audio == null)
            {
                _statusText = L.StatusNoAudio;
                _currentName = L.NoDevice;
                ApplyTrayIcon(MicState.Idle);
                UpdateTrayText();
                PublishSnapshot(new List<AudioInput>(), null, _currentName, _statusText, MicState.Idle);
                return;
            }

            List<AudioInput> devices = OrderDevices(_audio.GetInputs());
            AudioInput desired = null;
            MicState state;
            string status;

            if (!_settings.AutoLock)
            {
                state = MicState.Paused;
                status = L.StatusPaused;
            }
            else if (!string.IsNullOrEmpty(_settings.PinnedDeviceId))
            {
                desired = FindById(devices, _settings.PinnedDeviceId);
                if (desired == null)
                {
                    state = MicState.Idle;
                    status = L.StatusPinnedOffline(PinnedDisplayName());
                }
                else
                {
                    state = MicState.Locked;
                    status = L.StatusPinned(desired.Name);
                }
            }
            else
            {
                desired = AudioSystem.PickAutomatic(devices, _blocked);
                if (desired == null)
                {
                    state = MicState.Idle;
                    status = L.StatusAutoNoUsb;
                }
                else
                {
                    state = MicState.Locked;
                    status = L.StatusAuto;
                }
            }

            string currentId = _audio.GetDefaultInputId(AudioConstants.RoleConsole);
            string currentName = NameOf(devices, currentId);

            bool switched = false;
            if (desired != null && desired.Id != currentId && DateTime.UtcNow >= _backoffUntil)
            {
                if (_audio.SetDefaultInput(desired.Id))
                {
                    switched = true;
                    currentId = desired.Id;
                    currentName = desired.Name;
                    RecordSwitch(desired.Id);
                }
            }

            _statusText = status;
            _currentName = currentName;

            ApplyTrayIcon(state);
            UpdateTrayText();
            UpdatePill();
            PublishSnapshot(devices, currentId, currentName, status, state);

            if (switched && _settings.ShowHud)
            {
                _hud.ShowMessage(currentName);
            }
            else if (forceHud)
            {
                _hud.ShowMessage(currentName);
            }
        }

        private void PublishSnapshot(List<AudioInput> devices, string currentId, string currentName, string status, MicState state)
        {
            AppSnapshot snapshot = new AppSnapshot();
            snapshot.Devices = devices;
            snapshot.BlockedIds = new List<string>(_blocked);
            snapshot.CurrentId = currentId;
            snapshot.CurrentName = currentName;
            snapshot.StatusText = status;
            snapshot.State = state;
            snapshot.AutoLock = _settings.AutoLock;
            snapshot.ShowHud = _settings.ShowHud;
            snapshot.ShowName = _settings.ShowName;
            snapshot.LaunchAtLogin = _settings.LaunchAtLogin;
            snapshot.PinnedDeviceId = _settings.PinnedDeviceId;
            _snapshot = snapshot;

            if (_window != null && !_window.IsDisposed)
            {
                _window.UpdateSnapshot(snapshot);
            }
        }

        #region Window commands

        internal void ShowWindow()
        {
            if (_window == null || _window.IsDisposed)
            {
                _window = new MainForm(this);
            }
            if (_snapshot != null)
            {
                _window.UpdateSnapshot(_snapshot);
            }
            if (!_window.Visible)
            {
                _window.Show();
            }
            if (_window.WindowState == FormWindowState.Minimized)
            {
                _window.WindowState = FormWindowState.Normal;
            }
            _window.Activate();
            _window.BringToFront();
        }

        internal void PinDevice(string deviceId, string displayName)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                _settings.PinnedDeviceId = null;
                _settings.PinnedDeviceName = null;
            }
            else if (_settings.PinnedDeviceId == deviceId)
            {
                _settings.PinnedDeviceId = null;
                _settings.PinnedDeviceName = null;
            }
            else
            {
                _blocked.Remove(deviceId);
                _settings.PinnedDeviceId = deviceId;
                _settings.PinnedDeviceName = displayName;
            }

            _settings.Save();
            Evaluate(false);
        }

        internal void SetAutoLock(bool value)
        {
            _settings.AutoLock = value;
            _settings.Save();
            Evaluate(false);
        }

        internal void SetShowHud(bool value)
        {
            _settings.ShowHud = value;
            _settings.Save();
        }

        internal void SetShowName(bool value)
        {
            _settings.ShowName = value;
            _settings.Save();
            UpdateTrayText();
            UpdatePill();
        }

        internal void SetLaunchAtLogin(bool value)
        {
            _settings.LaunchAtLogin = value;
            _settings.Save();
            Startup.SetEnabled(value, Application.ExecutablePath);
        }

        internal void RecheckNow()
        {
            Evaluate(true);
        }

        /// <summary>Moves a device one slot up (-1) or down (+1) in the priority order.</summary>
        internal void MoveDevice(string deviceId, int delta)
        {
            if (string.IsNullOrEmpty(deviceId) || delta == 0 || _audio == null)
            {
                return;
            }

            List<AudioInput> ordered = OrderDevices(_audio.GetInputs());
            int index = IndexOf(ordered, deviceId);
            int target = index + delta;
            if (index < 0 || target < 0 || target >= ordered.Count)
            {
                return;
            }

            AudioInput moving = ordered[index];
            ordered.RemoveAt(index);
            ordered.Insert(target, moving);

            List<string> next = new List<string>();
            for (int i = 0; i < ordered.Count; i++)
            {
                next.Add(ordered[i].Id);
            }

            // Keep the slots of devices that are currently unplugged.
            List<string> previous = _settings.DeviceOrder ?? new List<string>();
            for (int i = 0; i < previous.Count; i++)
            {
                if (!next.Contains(previous[i]))
                {
                    next.Add(previous[i]);
                }
            }

            _settings.DeviceOrder = next;
            _settings.Save();
            Evaluate(false);
        }

        /// <summary>Temporarily ignores a device for this session, or un-ignores it.</summary>
        internal void ToggleBlock(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                return;
            }

            if (_blocked.Contains(deviceId))
            {
                _blocked.Remove(deviceId);
            }
            else
            {
                _blocked.Add(deviceId);
                if (_settings.PinnedDeviceId == deviceId)
                {
                    _settings.PinnedDeviceId = null;
                    _settings.PinnedDeviceName = null;
                    _settings.Save();
                }
            }

            Evaluate(false);
        }

        internal void UnblockAll()
        {
            if (_blocked.Count == 0)
            {
                return;
            }
            _blocked.Clear();
            Evaluate(false);
        }

        #endregion

        /// <summary>Sorts live devices by the saved priority, appending unknown ones last.</summary>
        private List<AudioInput> OrderDevices(List<AudioInput> devices)
        {
            List<AudioInput> ordered = new List<AudioInput>();
            List<string> order = _settings.DeviceOrder ?? new List<string>();

            for (int i = 0; i < order.Count; i++)
            {
                if (IndexOf(ordered, order[i]) >= 0)
                {
                    continue;
                }
                AudioInput match = FindById(devices, order[i]);
                if (match != null)
                {
                    ordered.Add(match);
                }
            }

            for (int i = 0; i < devices.Count; i++)
            {
                if (IndexOf(ordered, devices[i].Id) < 0)
                {
                    ordered.Add(devices[i]);
                }
            }
            return ordered;
        }

        private static int IndexOf(List<AudioInput> devices, string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return -1;
            }
            for (int i = 0; i < devices.Count; i++)
            {
                if (devices[i].Id == id)
                {
                    return i;
                }
            }
            return -1;
        }

        private void RecordSwitch(string deviceId)
        {
            DateTime now = DateTime.UtcNow;
            _recentSwitches.Add(now);
            for (int i = _recentSwitches.Count - 1; i >= 0; i--)
            {
                if ((now - _recentSwitches[i]).TotalSeconds > 15)
                {
                    _recentSwitches.RemoveAt(i);
                }
            }

            // Guard against a tug-of-war with another audio application.
            if (_recentSwitches.Count > 6)
            {
                _recentSwitches.Clear();
                _backoffUntil = now.AddSeconds(20);
            }
        }

        #region Tray presentation

        private void ApplyTrayIcon(MicState state)
        {
            if (_iconState == state && _tray.Icon != null)
            {
                return;
            }

            int size = SystemInformation.SmallIconSize.Width;
            if (size <= 0)
            {
                size = 16;
            }

            Icon next = Glyphs.CreateIcon(size, state);
            Icon previous = _tray.Icon;
            _tray.Icon = next;
            if (previous != null)
            {
                previous.Dispose();
            }
            _iconState = state;
        }

        private void UpdateTrayText()
        {
            string text = L.AppName;
            if (_settings.ShowName && !string.IsNullOrEmpty(_currentName))
            {
                text = L.AppName + " - " + _currentName;
            }
            if (text.Length > 63)
            {
                text = text.Substring(0, 62) + "\u2026";
            }

            try
            {
                if (_tray.Text != text)
                {
                    _tray.Text = text;
                }
            }
            catch (Exception)
            {
            }
        }

        private void UpdatePill()
        {
            string name = _settings.ShowName ? _currentName : null;
            try
            {
                _pill.UpdateContent(name);
            }
            catch (Exception)
            {
            }
        }

        #endregion

        #region Menu

        private void RebuildMenu()
        {
            _menu.Items.Clear();
            _menu.Font = SystemFonts.MenuFont;

            ToolStripMenuItem header = new ToolStripMenuItem(L.CurrentInput(_currentName));
            header.Font = _headerFont;
            header.Enabled = false;
            _menu.Items.Add(header);

            ToolStripMenuItem status = new ToolStripMenuItem(_statusText);
            status.Font = _statusFont;
            status.Enabled = false;
            _menu.Items.Add(status);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem open = new ToolStripMenuItem(L.MenuOpenWindow);
            open.Font = _headerFont;
            open.Click += delegate { ShowWindow(); };
            _menu.Items.Add(open);

            _menu.Items.Add(new ToolStripSeparator());

            List<AudioInput> devices = _audio != null ? OrderDevices(_audio.GetInputs()) : new List<AudioInput>();

            ToolStripMenuItem deviceRoot = new ToolStripMenuItem(L.MenuChooseDevice);
            ToolStripMenuItem automatic = new ToolStripMenuItem(L.MenuAutomatic);
            automatic.Checked = string.IsNullOrEmpty(_settings.PinnedDeviceId);
            automatic.Click += delegate { Pin(null); };
            deviceRoot.DropDownItems.Add(automatic);

            if (!string.IsNullOrEmpty(_settings.PinnedDeviceId) && FindById(devices, _settings.PinnedDeviceId) == null)
            {
                ToolStripMenuItem offline = new ToolStripMenuItem(PinnedDisplayName() + "  (" + L.Offline + ")");
                offline.Enabled = false;
                deviceRoot.DropDownItems.Add(offline);
            }

            deviceRoot.DropDownItems.Add(new ToolStripSeparator());

            if (devices.Count == 0)
            {
                ToolStripMenuItem none = new ToolStripMenuItem(L.NoDevices);
                none.Enabled = false;
                deviceRoot.DropDownItems.Add(none);
            }
            else
            {
                foreach (AudioInput device in devices)
                {
                    AudioInput captured = device;
                    ToolStripMenuItem item = new ToolStripMenuItem(DeviceLabel(device));
                    item.Checked = _settings.PinnedDeviceId == device.Id;
                    if (item.Checked)
                    {
                        item.Font = _headerFont;
                    }
                    item.Click += delegate { Pin(captured); };
                    deviceRoot.DropDownItems.Add(item);
                }
            }

            _menu.Items.Add(deviceRoot);
            _menu.Items.Add(new ToolStripSeparator());

            _menu.Items.Add(CreateToggle(L.MenuAutoLock, _settings.AutoLock, delegate
            {
                _settings.AutoLock = !_settings.AutoLock;
                _settings.Save();
                Evaluate(false);
            }));

            _menu.Items.Add(CreateToggle(L.MenuShowHud, _settings.ShowHud, delegate
            {
                _settings.ShowHud = !_settings.ShowHud;
                _settings.Save();
            }));

            _menu.Items.Add(CreateToggle(L.MenuShowName, _settings.ShowName, delegate
            {
                _settings.ShowName = !_settings.ShowName;
                _settings.Save();
                UpdateTrayText();
                UpdatePill();
            }));

            _menu.Items.Add(CreateToggle(L.MenuLaunchAtLogin, _settings.LaunchAtLogin, delegate
            {
                _settings.LaunchAtLogin = !_settings.LaunchAtLogin;
                _settings.Save();
                Startup.SetEnabled(_settings.LaunchAtLogin, Application.ExecutablePath);
            }));

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem recheck = new ToolStripMenuItem(L.MenuRecheck);
            recheck.Click += delegate { Evaluate(true); };
            _menu.Items.Add(recheck);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem quit = new ToolStripMenuItem(L.MenuQuit);
            quit.Click += delegate { Shutdown(); };
            _menu.Items.Add(quit);
        }

        private ToolStripMenuItem CreateToggle(string text, bool isChecked, EventHandler onClick)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Checked = isChecked;
            item.Click += onClick;
            return item;
        }

        private static string DeviceLabel(AudioInput device)
        {
            string name = device.Name;
            if (name.Length > 46)
            {
                name = name.Substring(0, 45) + "\u2026";
            }
            string tag = device.Tag;
            return string.IsNullOrEmpty(tag) ? name : name + "   " + tag;
        }

        private void Pin(AudioInput device)
        {
            if (device == null)
            {
                PinDevice(null, null);
            }
            else
            {
                PinDevice(device.Id, device.Name);
            }
        }

        private string PinnedDisplayName()
        {
            if (!string.IsNullOrEmpty(_settings.PinnedDeviceName))
            {
                return _settings.PinnedDeviceName;
            }
            return _audio != null ? (_audio.GetKnownName(_settings.PinnedDeviceId) ?? L.NoDevice) : L.NoDevice;
        }

        #endregion

        private static AudioInput FindById(List<AudioInput> devices, string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }
            foreach (AudioInput device in devices)
            {
                if (device.Id == id)
                {
                    return device;
                }
            }
            return null;
        }

        private string NameOf(List<AudioInput> devices, string id)
        {
            AudioInput device = FindById(devices, id);
            if (device != null)
            {
                return device.Name;
            }
            if (_audio != null)
            {
                string known = _audio.GetKnownName(id);
                if (!string.IsNullOrEmpty(known))
                {
                    return known;
                }
            }
            return L.NoDevice;
        }

        private void Shutdown()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            _safety.Stop();
            _debounce.Stop();
            _settings.Save();

            try
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
            catch (Exception)
            {
            }

            if (_audio != null)
            {
                try { _audio.Dispose(); }
                catch (Exception) { }
            }

            _menu.Dispose();
            if (_window != null && !_window.IsDisposed)
            {
                _window.Dispose();
                _window = null;
            }
            _hud.Dispose();
            _pill.Dispose();
            _dispatcher.Dispose();
            _headerFont.Dispose();
            _statusFont.Dispose();

            ExitThread();
        }
    }
}

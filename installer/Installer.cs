using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MicPinSetup
{
    /// <summary>
    /// Per-user installer: no admin rights, no MSI, no external tooling.
    /// MicPin.exe is embedded as a managed resource, so this single file is the whole package.
    /// </summary>
    internal static class Program
    {
        internal const string Version = "1.0.0";
        internal const string PayloadResource = "MicPinPayload.exe";

        internal static readonly string InstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Programs\MicPin");

        internal static string AppPath
        {
            get { return Path.Combine(InstallDir, "MicPin.exe"); }
        }

        internal static string UninstallerPath
        {
            get { return Path.Combine(InstallDir, "Uninstall.exe"); }
        }

        private static string StartMenuShortcut
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    "MicPin.lnk");
            }
        }

        private static string DesktopShortcut
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "MicPin.lnk");
            }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;

            if (mode == "--uninstall")
            {
                Uninstall();
                return;
            }

            if (mode == "--install")
            {
                try
                {
                    Install(false, true);
                    Process.Start(AppPath);
                }
                catch (Exception)
                {
                    Environment.ExitCode = 1;
                }
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }

        internal static void Install(bool launchAtLogin, bool desktopShortcut)
        {
            Directory.CreateDirectory(InstallDir);

            string payload = Path.Combine(Path.GetTempPath(), "micpin-payload.exe");
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource))
            {
                if (resource == null)
                {
                    throw new InvalidOperationException("安装包内容缺失。");
                }
                using (FileStream file = File.Create(payload))
                {
                    resource.CopyTo(file);
                }
            }

            File.Copy(payload, AppPath, true);
            try
            {
                File.Delete(payload);
            }
            catch (IOException)
            {
            }

            // Keep a copy of this installer inside the target folder to act as the uninstaller.
            if (!string.Equals(
                Path.GetFullPath(Application.ExecutablePath),
                Path.GetFullPath(UninstallerPath),
                StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(Application.ExecutablePath, UninstallerPath, true);
            }

            CreateShortcut(StartMenuShortcut);
            if (desktopShortcut)
            {
                CreateShortcut(DesktopShortcut);
            }

            if (launchAtLogin)
            {
                EnableLaunchAtLogin();
            }

            WriteUninstallEntry();
        }

        private static void Uninstall()
        {
            try
            {
                foreach (Process process in Process.GetProcessesByName("MicPin"))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null && key.GetValue("MicPin") != null)
                    {
                        key.DeleteValue("MicPin", false);
                    }
                }
            }
            catch (Exception)
            {
            }

            DeleteFile(StartMenuShortcut);
            DeleteFile(DesktopShortcut);

            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MicPin", false);
            }
            catch (Exception)
            {
            }

            // The running uninstaller cannot delete itself; let cmd finish the cleanup.
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + InstallDir + "\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }

        private static void DeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Creates a .lnk through the WScript.Shell COM object (no interop assembly needed).</summary>
        private static void CreateShortcut(string shortcutPath)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                return;
            }

            object shell = Activator.CreateInstance(shellType);
            object shortcut = shellType.InvokeMember(
                "CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });

            Type shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { AppPath });
            shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { InstallDir });
            shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { AppPath + ",0" });
            shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "MicPin" });
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }

        private static void EnableLaunchAtLogin()
        {
            // The app rewrites the Run key from its own settings on every start,
            // so the preference has to be stored there as well.
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicPin");
            string path = Path.Combine(folder, "settings.json");

            string json;
            if (File.Exists(path))
            {
                json = File.ReadAllText(path);
            }
            else
            {
                json = "{\"pinnedDeviceId\":null,\"pinnedDeviceName\":null,\"autoLock\":true," +
                       "\"showHud\":true,\"showName\":false,\"launchAtLogin\":false,\"deviceOrder\":[]}";
            }

            json = Regex.Replace(json, "\"launchAtLogin\"\\s*:\\s*(true|false)", "\"launchAtLogin\":true");
            if (json.IndexOf("\"launchAtLogin\"", StringComparison.Ordinal) < 0)
            {
                int brace = json.LastIndexOf('}');
                if (brace >= 0)
                {
                    json = json.Substring(0, brace).TrimEnd() + ",\"launchAtLogin\":true}";
                }
            }

            Directory.CreateDirectory(folder);
            File.WriteAllText(path, json, new UTF8Encoding(false));

            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (key != null)
                    {
                        key.SetValue("MicPin", "\"" + AppPath + "\"");
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private static void WriteUninstallEntry()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MicPin"))
                {
                    if (key == null)
                    {
                        return;
                    }
                    key.SetValue("DisplayName", "MicPin");
                    key.SetValue("DisplayVersion", Version);
                    key.SetValue("DisplayIcon", AppPath);
                    key.SetValue("InstallLocation", InstallDir);
                    key.SetValue("UninstallString", "\"" + UninstallerPath + "\" --uninstall");
                    key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    try
                    {
                        key.SetValue("EstimatedSize", (int)(new FileInfo(AppPath).Length / 1024), RegistryValueKind.DWord);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly CheckBox _launchAtLogin;
        private readonly CheckBox _desktopShortcut;
        private readonly Label _status;
        private readonly Button _install;
        private readonly Button _cancel;

        public SetupForm()
        {
            Text = "MicPin 安装程序";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(468, 306);
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9f);
            Icon = null;

            Label title = new Label();
            title.Text = "安装 MicPin";
            title.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(23, 26, 31);
            title.SetBounds(24, 18, 420, 30);
            title.AutoSize = false;
            Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "让 Windows 的默认麦克风始终停在你选择的设备上。";
            subtitle.ForeColor = Color.FromArgb(120, 129, 143);
            subtitle.SetBounds(26, 52, 420, 20);
            subtitle.AutoSize = false;
            Controls.Add(subtitle);

            Label locationCaption = new Label();
            locationCaption.Text = "安装位置";
            locationCaption.ForeColor = Color.FromArgb(120, 129, 143);
            locationCaption.SetBounds(26, 86, 420, 18);
            locationCaption.AutoSize = false;
            Controls.Add(locationCaption);

            Label location = new Label();
            location.Text = Program.InstallDir;
            location.ForeColor = Color.FromArgb(40, 46, 54);
            location.SetBounds(26, 105, 416, 18);
            location.AutoSize = false;
            location.AutoEllipsis = true;
            Controls.Add(location);

            _launchAtLogin = new CheckBox();
            _launchAtLogin.Text = "登录时启动 MicPin";
            _launchAtLogin.SetBounds(24, 142, 300, 22);
            Controls.Add(_launchAtLogin);

            _desktopShortcut = new CheckBox();
            _desktopShortcut.Text = "在桌面创建快捷方式";
            _desktopShortcut.Checked = true;
            _desktopShortcut.SetBounds(24, 170, 300, 22);
            Controls.Add(_desktopShortcut);

            _status = new Label();
            _status.ForeColor = Color.FromArgb(120, 129, 143);
            _status.SetBounds(26, 206, 416, 20);
            _status.AutoSize = false;
            Controls.Add(_status);

            _install = new Button();
            _install.Text = "安装";
            _install.Font = new Font("Segoe UI", 9.5f);
            _install.FlatStyle = FlatStyle.Flat;
            _install.FlatAppearance.BorderSize = 0;
            _install.BackColor = Color.FromArgb(84, 150, 255);
            _install.ForeColor = Color.White;
            _install.UseVisualStyleBackColor = false;
            _install.SetBounds(24, 244, 158, 34);
            _install.Click += OnInstall;
            Controls.Add(_install);

            _cancel = new Button();
            _cancel.Text = "取消";
            _cancel.Font = new Font("Segoe UI", 9.5f);
            _cancel.FlatStyle = FlatStyle.System;
            _cancel.SetBounds(346, 244, 96, 34);
            _cancel.Click += delegate { Close(); };
            Controls.Add(_cancel);

            AcceptButton = _install;
            CancelButton = _cancel;
        }

        private void OnInstall(object sender, EventArgs e)
        {
            _install.Enabled = false;
            _cancel.Enabled = false;
            _status.Text = "正在安装…";
            Application.DoEvents();

            try
            {
                Program.Install(_launchAtLogin.Checked, _desktopShortcut.Checked);
                _status.Text = "安装完成，正在启动 MicPin…";
                Application.DoEvents();

                try
                {
                    Process.Start(Program.AppPath);
                }
                catch (Exception)
                {
                }

                Close();
            }
            catch (Exception error)
            {
                _install.Enabled = true;
                _cancel.Enabled = true;
                _status.Text = string.Empty;
                MessageBox.Show(
                    this, "安装失败：" + error.Message, "MicPin",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

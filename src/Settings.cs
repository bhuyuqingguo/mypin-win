using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MyPin
{
    [DataContract]
    public sealed class Settings
    {
        [DataMember(Name = "pinnedDeviceId", Order = 1)]
        public string PinnedDeviceId;

        [DataMember(Name = "pinnedDeviceName", Order = 2)]
        public string PinnedDeviceName;

        [DataMember(Name = "autoLock", Order = 3)]
        public bool AutoLock;

        [DataMember(Name = "showHud", Order = 4)]
        public bool ShowHud;

        [DataMember(Name = "showName", Order = 5)]
        public bool ShowName;

        [DataMember(Name = "launchAtLogin", Order = 6)]
        public bool LaunchAtLogin;

        /// <summary>Device ids in the user's preferred order, highest priority first.</summary>
        [DataMember(Name = "deviceOrder", Order = 7)]
        public List<string> DeviceOrder;

        [OnDeserializing]
        private void ApplyDefaults(StreamingContext context)
        {
            PinnedDeviceId = null;
            PinnedDeviceName = null;
            AutoLock = true;
            ShowHud = true;
            ShowName = false;
            LaunchAtLogin = false;
            DeviceOrder = new List<string>();
        }

        public static string FilePath
        {
            get
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MyPin");
                return Path.Combine(folder, "settings.json");
            }
        }

        /// <summary>Moves the settings left by the old MicPin build into the MyPin folder.</summary>
        private static void MigrateLegacyFolder()
        {
            try
            {
                string target = FilePath;
                if (File.Exists(target))
                {
                    return;
                }

                string legacyFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MicPin");
                string legacy = Path.Combine(legacyFolder, "settings.json");
                if (!File.Exists(legacy))
                {
                    return;
                }

                string folder = Path.GetDirectoryName(target);
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                File.Move(legacy, target);

                if (Directory.GetFileSystemEntries(legacyFolder).Length == 0)
                {
                    Directory.Delete(legacyFolder);
                }
            }
            catch (Exception)
            {
                // A failed migration only costs the user their preferences.
            }
        }

        public static Settings Load()
        {
            MigrateLegacyFolder();

            try
            {
                string path = FilePath;
                if (File.Exists(path))
                {
                    using (FileStream stream = File.OpenRead(path))
                    {
                        DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(Settings));
                        Settings loaded = serializer.ReadObject(stream) as Settings;
                        if (loaded != null)
                        {
                            return loaded;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Corrupt or unreadable settings fall back to defaults.
            }

            Settings fresh = new Settings();
            fresh.ApplyDefaults(new StreamingContext());
            return fresh;
        }

        public void Save()
        {
            try
            {
                string path = FilePath;
                string folder = Path.GetDirectoryName(path);
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                byte[] bytes;
                using (MemoryStream buffer = new MemoryStream())
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(Settings));
                    serializer.WriteObject(buffer, this);
                    bytes = buffer.ToArray();
                }

                File.WriteAllText(path, Encoding.UTF8.GetString(bytes), new UTF8Encoding(false));
            }
            catch (Exception)
            {
            }
        }
    }

    internal static class Startup
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "MyPin";
        private const string LegacyValueName = "MicPin";

        public static bool IsEnabled()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    return key != null && key.GetValue(ValueName) != null;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void SetEnabled(bool enabled, string executablePath)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null)
                    {
                        return;
                    }

                    // Drop the entry written by the old MicPin build so it cannot start a second copy.
                    if (key.GetValue(LegacyValueName) != null)
                    {
                        key.DeleteValue(LegacyValueName, false);
                    }

                    if (enabled)
                    {
                        key.SetValue(ValueName, "\"" + executablePath + "\"");
                    }
                    else if (key.GetValue(ValueName) != null)
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }
            }
            catch (Exception)
            {
            }
        }
    }
}

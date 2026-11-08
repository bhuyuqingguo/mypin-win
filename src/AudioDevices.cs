using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MyPin
{
    internal enum DeviceKind
    {
        Usb,
        BuiltIn,
        Bluetooth,
        Virtual,
        Other
    }

    internal sealed class AudioInput
    {
        public string Id;
        public string Name;
        public string Enumerator;
        public DeviceKind Kind;

        public string Tag
        {
            get
            {
                switch (Kind)
                {
                    case DeviceKind.Usb: return L.TagUsb;
                    case DeviceKind.BuiltIn: return L.TagBuiltIn;
                    case DeviceKind.Bluetooth: return L.TagBluetooth;
                    case DeviceKind.Virtual: return L.TagVirtual;
                    default: return L.TagOther;
                }
            }
        }

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>COM callbacks from the audio service arrive on a background thread.</summary>
    [ComVisible(true)]
    public sealed class EndpointNotificationClient : IMMNotificationClient
    {
        public event EventHandler Changed;

        private void Raise()
        {
            EventHandler handler = Changed;
            if (handler == null)
            {
                return;
            }
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception)
            {
                // Never let a managed exception travel back into the audio service.
            }
        }

        public void OnDeviceStateChanged(string deviceId, int newState) { Raise(); }
        public void OnDeviceAdded(string deviceId) { Raise(); }
        public void OnDeviceRemoved(string deviceId) { Raise(); }
        public void OnDefaultDeviceChanged(int flow, int role, string defaultDeviceId) { Raise(); }
        public void OnPropertyValueChanged(string deviceId, PropertyKey key) { }
    }

    /// <summary>Enumerates capture endpoints and switches the system default input.</summary>
    internal sealed class AudioSystem : IDisposable
    {
        private static readonly string[] VirtualKeywords = new string[]
        {
            "virtual", "vb-audio", "voicemeeter", "cable output", "blackhole", "krisp",
            "teams audio", "discord", "obs", "stereo mix", "what u hear", "loopback",
            "nvidia broadcast", "sound mapper", "primary sound", "虚拟"
        };

        private readonly IMMDeviceEnumerator _enumerator;
        private readonly PolicyConfig _policy;
        private readonly Dictionary<string, string> _names = new Dictionary<string, string>();
        private EndpointNotificationClient _notification;

        public AudioSystem()
        {
            Type type = Type.GetTypeFromCLSID(AudioConstants.ClsidMMDeviceEnumerator, true);
            object instance = Activator.CreateInstance(type);
            _enumerator = (IMMDeviceEnumerator)instance;
            _policy = new PolicyConfig();
        }

        public bool CanSwitchDefault
        {
            get { return _policy.Available; }
        }

        public event EventHandler DevicesChanged;

        public bool StartMonitoring()
        {
            if (_notification == null)
            {
                _notification = new EndpointNotificationClient();
                _notification.Changed += OnNotification;
            }
            try
            {
                return _enumerator.RegisterEndpointNotificationCallback(_notification) == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void OnNotification(object sender, EventArgs e)
        {
            EventHandler handler = DevicesChanged;
            if (handler != null)
            {
                try { handler(this, EventArgs.Empty); }
                catch (Exception) { }
            }
        }

        /// <summary>All active capture endpoints, in the order Windows reports them.</summary>
        public List<AudioInput> GetInputs()
        {
            List<AudioInput> result = new List<AudioInput>();
            IMMDeviceCollection collection = null;
            try
            {
                int hr = _enumerator.EnumAudioEndpoints(
                    AudioConstants.DataFlowCapture,
                    AudioConstants.DeviceStateActive,
                    out collection);
                if (hr != 0 || collection == null)
                {
                    return result;
                }

                int count;
                if (collection.GetCount(out count) != 0)
                {
                    return result;
                }

                for (int i = 0; i < count; i++)
                {
                    IMMDevice device = null;
                    try
                    {
                        if (collection.Item(i, out device) != 0 || device == null)
                        {
                            continue;
                        }
                        AudioInput input = Describe(device);
                        if (input != null && !string.IsNullOrEmpty(input.Id))
                        {
                            _names[input.Id] = input.Name;
                            result.Add(input);
                        }
                    }
                    catch (Exception)
                    {
                    }
                    finally
                    {
                        Release(device);
                    }
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                Release(collection);
            }
            return result;
        }

        public string GetDefaultInputId(int role)
        {
            IMMDevice device = null;
            try
            {
                if (_enumerator.GetDefaultAudioEndpoint(AudioConstants.DataFlowCapture, role, out device) != 0 || device == null)
                {
                    return null;
                }
                return GetId(device);
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                Release(device);
            }
        }

        public string GetKnownName(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                return null;
            }
            string name;
            return _names.TryGetValue(deviceId, out name) ? name : null;
        }

        public bool SetDefaultInput(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId) || !_policy.Available)
            {
                return false;
            }

            int console = _policy.SetDefaultEndpoint(deviceId, AudioConstants.RoleConsole);
            int multimedia = _policy.SetDefaultEndpoint(deviceId, AudioConstants.RoleMultimedia);
            int communications = _policy.SetDefaultEndpoint(deviceId, AudioConstants.RoleCommunications);
            return console == 0 || multimedia == 0 || communications == 0;
        }

        /// <summary>
        /// Automatic mode: the first USB endpoint in the user's priority order that is
        /// online and not temporarily muted. Built-in, Bluetooth and virtual endpoints
        /// are never returned, so nothing else can win the fallback.
        /// </summary>
        public static AudioInput PickAutomatic(List<AudioInput> devices, ICollection<string> blocked)
        {
            if (devices == null)
            {
                return null;
            }

            for (int i = 0; i < devices.Count; i++)
            {
                AudioInput device = devices[i];
                if (device.Kind != DeviceKind.Usb)
                {
                    continue;
                }
                if (blocked != null && blocked.Contains(device.Id))
                {
                    continue;
                }
                return device;
            }
            return null;
        }

        private AudioInput Describe(IMMDevice device)
        {
            string id = GetId(device);
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            string name = GetProperty(device, AudioConstants.PkeyFriendlyName);
            if (string.IsNullOrEmpty(name))
            {
                name = GetProperty(device, AudioConstants.PkeyDeviceDesc);
            }
            if (string.IsNullOrEmpty(name))
            {
                name = L.UnnamedDevice;
            }

            string enumerator = GetProperty(device, AudioConstants.PkeyEnumeratorName) ?? string.Empty;

            AudioInput input = new AudioInput();
            input.Id = id;
            input.Name = name.Trim();
            input.Enumerator = enumerator.Trim();
            input.Kind = Classify(input.Name, input.Enumerator);
            return input;
        }

        internal static DeviceKind Classify(string name, string enumerator)
        {
            string en = (enumerator ?? string.Empty).ToLowerInvariant();
            string n = (name ?? string.Empty).ToLowerInvariant();

            if (en.Contains("bthenum") || en.Contains("bthle") || en.Contains("bluetooth") ||
                n.Contains("bluetooth") || n.Contains("蓝牙") ||
                n.Contains("airpods") || n.Contains("hands-free"))
            {
                return DeviceKind.Bluetooth;
            }

            if (en == "swd" || en == "root")
            {
                return DeviceKind.Virtual;
            }

            foreach (string keyword in VirtualKeywords)
            {
                if (n.Contains(keyword))
                {
                    return DeviceKind.Virtual;
                }
            }

            if (en.Contains("usb"))
            {
                return DeviceKind.Usb;
            }

            if (en.Contains("hdaudio") || en.Contains("hdaudbus") || en.Contains("intelaudio") ||
                en.Contains("soundwire") || en.Contains("acpi") || en.Contains("pci"))
            {
                return DeviceKind.BuiltIn;
            }

            return DeviceKind.Other;
        }

        private static string GetId(IMMDevice device)
        {
            string id;
            if (device.GetId(out id) == 0)
            {
                return id;
            }
            return null;
        }

        private static string GetProperty(IMMDevice device, PropertyKey key)
        {
            IPropertyStore store = null;
            try
            {
                if (device.OpenPropertyStore(AudioConstants.StgmRead, out store) != 0 || store == null)
                {
                    return null;
                }

                PropVariant value;
                if (store.GetValue(ref key, out value) != 0)
                {
                    return null;
                }
                return AudioInterop.ReadString(ref value);
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                Release(store);
            }
        }

        private static void Release(object comObject)
        {
            if (comObject == null)
            {
                return;
            }
            try
            {
                if (Marshal.IsComObject(comObject))
                {
                    Marshal.ReleaseComObject(comObject);
                }
            }
            catch (Exception)
            {
            }
        }

        public void Dispose()
        {
            if (_notification != null)
            {
                try { _enumerator.UnregisterEndpointNotificationCallback(_notification); }
                catch (Exception) { }
                _notification = null;
            }
            Release(_enumerator);
        }
    }
}

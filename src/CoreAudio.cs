using System;
using System.Runtime.InteropServices;

namespace MicPin
{
    internal static class AudioConstants
    {
        public const int DeviceStateActive = 0x00000001;
        public const int DeviceStateDisabled = 0x00000002;
        public const int DeviceStateNotPresent = 0x00000004;
        public const int DeviceStateUnplugged = 0x00000008;

        public const int DataFlowRender = 0;
        public const int DataFlowCapture = 1;

        public const int RoleConsole = 0;
        public const int RoleMultimedia = 1;
        public const int RoleCommunications = 2;

        public const int StgmRead = 0;

        /// <summary>MMDeviceEnumerator class id.</summary>
        public static readonly Guid ClsidMMDeviceEnumerator = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");

        /// <summary>Undocumented PolicyConfigClient class id, present since Windows 7.</summary>
        public static readonly Guid ClsidPolicyConfigClient = new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");

        // PKEY_Device_FriendlyName  {a45c254e-df1c-4efd-8020-67d146a850e0}, 14
        // PKEY_Device_DeviceDesc    {a45c254e-df1c-4efd-8020-67d146a850e0}, 2
        // PKEY_Device_EnumeratorName{a45c254e-df1c-4efd-8020-67d146a850e0}, 24  ("USB", "HDAUDIO", "BTHENUM", "SWD" ...)
        private static readonly Guid DevicePropertyFormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0");

        public static readonly PropertyKey PkeyFriendlyName = new PropertyKey(DevicePropertyFormatId, 14);
        public static readonly PropertyKey PkeyDeviceDesc = new PropertyKey(DevicePropertyFormatId, 2);
        public static readonly PropertyKey PkeyEnumeratorName = new PropertyKey(DevicePropertyFormatId, 24);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey
    {
        public Guid FormatId;
        public int PropertyId;

        public PropertyKey(Guid formatId, int propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    /// <summary>
    /// PROPVARIANT is 16 bytes on x86 and 24 bytes on x64; the two trailing
    /// pointers keep the managed struct large enough on both architectures.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PropVariant
    {
        public short ValueType;
        public short Reserved1;
        public short Reserved2;
        public short Reserved3;
        public IntPtr Pointer;
        public IntPtr Pointer2;
    }

    internal static class AudioInterop
    {
        public const short VtLpwstr = 31;
        public const short VtBstr = 8;

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant value);

        public static string ReadString(ref PropVariant value)
        {
            string text = null;
            try
            {
                if (value.ValueType == VtLpwstr)
                {
                    text = Marshal.PtrToStringUni(value.Pointer);
                }
                else if (value.ValueType == VtBstr)
                {
                    text = Marshal.PtrToStringBSTR(value.Pointer);
                }
            }
            finally
            {
                try { PropVariantClear(ref value); }
                catch (Exception) { }
            }
            return text;
        }
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int classContext, IntPtr activationParams, out IntPtr instance);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport]
    [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMNotificationClient
    {
        void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int newState);
        void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        void OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string defaultDeviceId);
        void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
    }

    /// <summary>
    /// Undocumented IPolicyConfig from audioses.dll. Only SetDefaultEndpoint is
    /// used, but every preceding slot must be declared so the vtable indices line up.
    /// </summary>
    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool isDefault, out IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool isDefault, out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref long period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool visible);
    }

    /// <summary>Thin wrapper around the PolicyConfig COM object.</summary>
    internal sealed class PolicyConfig
    {
        private IPolicyConfig _config;

        public PolicyConfig()
        {
            try
            {
                Type type = Type.GetTypeFromCLSID(AudioConstants.ClsidPolicyConfigClient, true);
                object instance = Activator.CreateInstance(type);
                _config = instance as IPolicyConfig;
            }
            catch (Exception)
            {
                _config = null;
            }
        }

        public bool Available
        {
            get { return _config != null; }
        }

        /// <summary>Switches the default capture device for one role. Returns the HRESULT.</summary>
        public int SetDefaultEndpoint(string deviceId, int role)
        {
            if (_config == null)
            {
                return unchecked((int)0x80004005); // E_FAIL
            }
            try
            {
                return _config.SetDefaultEndpoint(deviceId, role);
            }
            catch (Exception)
            {
                return unchecked((int)0x80004005);
            }
        }
    }
}

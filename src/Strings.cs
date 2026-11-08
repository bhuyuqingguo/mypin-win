using System;
using System.Globalization;

namespace MicPin
{
    /// <summary>All user-facing text, following the Windows display language.</summary>
    internal static class L
    {
        public static readonly bool Zh = IsChinese();

        private static bool IsChinese()
        {
            CultureInfo culture = CultureInfo.CurrentUICulture;
            while (culture != null && !string.IsNullOrEmpty(culture.Name))
            {
                if (culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                culture = culture.Parent;
            }
            return false;
        }

        private static string Pick(string english, string chinese)
        {
            return Zh ? chinese : english;
        }

        public static string AppName { get { return "MicPin"; } }

        public static string CurrentInput(string name)
        {
            return Zh ? "当前输入：" + name : "Current input: " + name;
        }

        public static string StatusAuto { get { return Pick("Automatic - preferring the USB mic", "自动 - 优先使用 USB 麦克风"); } }
        public static string StatusAutoNoUsb { get { return Pick("Automatic - no USB mic connected, system default left alone", "自动 - 未检测到 USB 麦克风，系统默认保持不变"); } }
        public static string StatusPinned(string name)
        {
            return Zh ? "已锁定 - " + name : "Pinned - " + name;
        }
        public static string StatusPinnedOffline(string name)
        {
            return Zh
                ? "已锁定 - " + name + " 未连接，系统默认保持不变"
                : "Pinned - " + name + " is not connected, system default left alone";
        }
        public static string StatusPaused { get { return Pick("Paused - system default untouched", "已暂停 - 不干预系统默认"); } }
        public static string StatusNoAudio { get { return Pick("Audio devices unavailable", "音频设备不可用"); } }

        public static string MenuChooseDevice { get { return Pick("Which microphone to lock", "锁定哪个麦克风"); } }
        public static string MenuOpenWindow { get { return Pick("Open MicPin window", "打开 MicPin 窗口"); } }

        public static string WindowSubtitle
        {
            get { return Pick("Keeps the system input on the mic you chose.", "让系统输入始终停在你选择的麦克风上"); }
        }
        public static string CardCurrentInput { get { return Pick("Current input", "当前输入"); } }
        public static string SectionDevices { get { return Pick("Microphone priority", "麦克风优先级"); } }
        public static string SectionDevicesHint
        {
            get { return Pick("Automatic uses the first available USB mic that is not muted.", "自动模式从上往下取第一个可用且未屏蔽的 USB 麦克风"); }
        }
        public static string AutoRow { get { return Pick("Automatic (use order below)", "自动（按下面顺序使用）"); } }
        public static string Block { get { return Pick("Mute", "屏蔽"); } }
        public static string Blocked { get { return Pick("Muted", "已屏蔽"); } }
        public static string UnblockAll { get { return Pick("Restore all", "全部恢复"); } }
        public static string MoveUpTip { get { return Pick("Move up", "上移"); } }
        public static string MoveDownTip { get { return Pick("Move down", "下移"); } }
        public static string BlockTip { get { return Pick("Temporarily ignore this mic", "临时不使用这个麦克风"); } }
        public static string SectionOptions { get { return Pick("Options", "选项"); } }
        public static string ButtonRecheck { get { return Pick("Re-check now", "立即重新检测"); } }
        public static string ButtonClose { get { return Pick("Close", "关闭"); } }
        public static string AutoRowHint { get { return Pick("Prefer any USB mic", "优先任意 USB 麦克风"); } }

        public static string MenuAutomatic { get { return Pick("Automatic (prefer USB mic)", "自动（优先使用 USB 麦克风）"); } }
        public static string MenuAutoLock { get { return Pick("Enable auto-lock", "启用自动锁定"); } }
        public static string MenuShowHud { get { return Pick("Show HUD when switching", "切换时显示提示"); } }
        public static string MenuShowName { get { return Pick("Show device name in the tray corner", "在任务栏角落显示设备名称"); } }
        public static string MenuLaunchAtLogin { get { return Pick("Launch at login", "登录时启动"); } }
        public static string MenuRecheck { get { return Pick("Re-check now", "立即重新检测"); } }
        public static string MenuQuit { get { return Pick("Quit MicPin", "退出 MicPin"); } }

        public static string TagUsb { get { return "USB"; } }
        public static string TagBuiltIn { get { return Pick("Built-in", "内置"); } }
        public static string TagBluetooth { get { return Pick("Bluetooth", "蓝牙"); } }
        public static string TagVirtual { get { return Pick("Virtual", "虚拟"); } }
        public static string TagOther { get { return string.Empty; } }

        public static string NoDevices { get { return Pick("No input devices found", "未找到输入设备"); } }
        public static string NoDevice { get { return Pick("No input device", "无输入设备"); } }
        public static string UnnamedDevice { get { return Pick("Unnamed device", "未命名设备"); } }
        public static string Offline { get { return Pick("not connected", "未连接"); } }

        public static string HudTitle { get { return Pick("Microphone", "麦克风"); } }
    }
}

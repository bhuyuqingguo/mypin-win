using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace MyPin
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length >= 2 && string.Equals(args[0], "--write-icon", StringComparison.OrdinalIgnoreCase))
            {
                Glyphs.WriteIconFile(args[1]);
                return 0;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Local\\MyPin.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                string path = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(path))
                {
                    path = Application.ExecutablePath;
                }

                TrayApp app = new TrayApp();
                if (args.Length >= 1 && string.Equals(args[0], "--show", StringComparison.OrdinalIgnoreCase))
                {
                    app.ShowWindow();
                }
                Application.Run(app);
                GC.KeepAlive(mutex);
            }

            return 0;
        }
    }
}

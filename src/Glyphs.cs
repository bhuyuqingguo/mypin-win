using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MyPin
{
    internal enum MicState
    {
        /// <summary>Locked on the target device.</summary>
        Locked,
        /// <summary>Enabled, but no usable USB microphone is present.</summary>
        Idle,
        /// <summary>Auto-lock turned off.</summary>
        Paused
    }

    internal static class Glyphs
    {
        public static readonly Color Accent = Color.FromArgb(84, 150, 255);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        /// <summary>Draws the microphone on a 16x16 design grid, scaled to any pixel size.</summary>
        public static Bitmap RenderMic(int pixelSize, MicState state, Color color)
        {
            int size = Math.Max(8, pixelSize);
            Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                float scale = size / 16f;
                g.ScaleTransform(scale, scale);

                bool filled = state == MicState.Locked;
                using (Pen pen = new Pen(color, 1.45f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;

                    using (GraphicsPath capsule = CreateCapsule())
                    {
                        if (filled)
                        {
                            using (SolidBrush brush = new SolidBrush(color))
                            {
                                g.FillPath(brush, capsule);
                            }
                        }
                        else
                        {
                            g.DrawPath(pen, capsule);
                        }
                    }

                    // Holder: the "U" under the capsule.
                    g.DrawArc(pen, 3.6f, 5.0f, 8.8f, 8.8f, 0f, 180f);

                    // Stem and base.
                    g.DrawLine(pen, 8f, 13.8f, 8f, 15.1f);
                    g.DrawLine(pen, 5.9f, 15.2f, 10.1f, 15.2f);
                }

                if (state == MicState.Paused)
                {
                    using (Pen slash = new Pen(color, 1.8f))
                    {
                        slash.StartCap = LineCap.Round;
                        slash.EndCap = LineCap.Round;
                        g.DrawLine(slash, 2.4f, 2.1f, 13.6f, 14.3f);
                    }
                }
            }
            return bitmap;
        }

        private static GraphicsPath CreateCapsule()
        {
            GraphicsPath path = new GraphicsPath();
            path.AddArc(6.0f, 1.4f, 4.0f, 4.0f, 180f, 180f);
            path.AddArc(6.0f, 3.6f, 4.0f, 4.0f, 0f, 180f);
            path.CloseFigure();
            return path;
        }

        public static Icon CreateIcon(int size, MicState state)
        {
            using (Bitmap bitmap = RenderMic(size, state, Accent))
            {
                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon temporary = Icon.FromHandle(handle))
                    {
                        return (Icon)temporary.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        /// <summary>Writes a multi-resolution .ico used as the executable icon.</summary>
        public static void WriteIconFile(string path)
        {
            int[] sizes = new int[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            List<byte[]> images = new List<byte[]>();

            foreach (int size in sizes)
            {
                using (Bitmap bitmap = RenderMic(size, MicState.Locked, Accent))
                using (MemoryStream buffer = new MemoryStream())
                {
                    bitmap.Save(buffer, ImageFormat.Png);
                    images.Add(buffer.ToArray());
                }
            }

            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write((short)0);
                writer.Write((short)1);
                writer.Write((short)sizes.Length);

                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte dimension = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    writer.Write(dimension);
                    writer.Write(dimension);
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((short)1);
                    writer.Write((short)32);
                    writer.Write(images[i].Length);
                    writer.Write(offset);
                    offset += images[i].Length;
                }

                for (int i = 0; i < images.Count; i++)
                {
                    writer.Write(images[i]);
                }
            }
        }
    }

    internal static class Ui
    {
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr window);

        /// <summary>
        /// Current DPI scale of the monitor the control lives on. Reading the window DPI
        /// (instead of the process DPI) keeps text crisp on mixed-DPI setups.
        /// </summary>
        public static float ScaleOf(Control control)
        {
            try
            {
                if (control.IsHandleCreated)
                {
                    uint dpi = GetDpiForWindow(control.Handle);
                    if (dpi >= 72 && dpi <= 600)
                    {
                        return dpi / 96f;
                    }
                }
            }
            catch (Exception)
            {
                // GetDpiForWindow is missing before Windows 10 1607.
            }

            try
            {
                using (Graphics graphics = control.CreateGraphics())
                {
                    return graphics.DpiX / 96f;
                }
            }
            catch (Exception)
            {
                return 1f;
            }
        }

        public static Font Font(float points, FontStyle style)
        {
            string family = L.Zh ? "Microsoft YaHei UI" : "Segoe UI";
            try
            {
                return new Font(family, points, style);
            }
            catch (Exception)
            {
                return new Font(FontFamily.GenericSansSerif, points, style);
            }
        }

        public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0f)
            {
                path.AddRectangle(bounds);
                return path;
            }

            float diameter = radius * 2f;
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180f, 90f);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270f, 90f);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
            path.CloseFigure();
            return path;
        }
    }
}

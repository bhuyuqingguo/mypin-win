using System.Drawing;

namespace MicPin
{
    /// <summary>One dark palette so every surface, chip and button stays in sync.</summary>
    internal static class Theme
    {
        public static readonly Color Window = Color.FromArgb(21, 24, 29);
        public static readonly Color Surface = Color.FromArgb(29, 34, 42);
        public static readonly Color SurfaceAlt = Color.FromArgb(25, 30, 38);
        public static readonly Color Border = Color.FromArgb(44, 51, 62);

        public static readonly Color TextPrimary = Color.FromArgb(231, 235, 242);
        public static readonly Color TextSecondary = Color.FromArgb(154, 164, 178);
        public static readonly Color TextMuted = Color.FromArgb(122, 132, 149);

        public static readonly Color Accent = Color.FromArgb(84, 150, 255);
        public static readonly Color AccentHover = Color.FromArgb(106, 166, 255);
        public static readonly Color AccentPressed = Color.FromArgb(66, 130, 235);

        public static readonly Color RowSelected = Color.FromArgb(34, 51, 73);
        public static readonly Color RowHover = Color.FromArgb(32, 38, 47);
        public static readonly Color Separator = Color.FromArgb(35, 42, 51);

        public static readonly Color ChipBack = Color.FromArgb(42, 49, 60);
        public static readonly Color Arrow = Color.FromArgb(127, 136, 153);
        public static readonly Color ArrowDisabled = Color.FromArgb(56, 64, 76);

        public static readonly Color WarnBack = Color.FromArgb(58, 46, 30);
        public static readonly Color WarnBorder = Color.FromArgb(110, 84, 41);
        public static readonly Color WarnText = Color.FromArgb(232, 179, 107);

        public static readonly Color ButtonBack = Color.FromArgb(38, 44, 54);
        public static readonly Color ButtonHover = Color.FromArgb(47, 55, 67);

        public static Color State(MicState state)
        {
            switch (state)
            {
                case MicState.Locked: return Color.FromArgb(61, 214, 140);
                case MicState.Paused: return Color.FromArgb(122, 132, 149);
                default: return Color.FromArgb(240, 180, 90);
            }
        }
    }
}

using System.Runtime.InteropServices;

namespace ModularAudience.Forms
{
    internal static class WindowsScreenHelper
    {
        private const int ENUM_CURRENT_SETTINGS = -1;

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern int EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DEVMODE
        {
            private const int CCHDEVICENAME = 32;
            private const int CCHFORMNAME = 32;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;

            public short dmOrientation;
            public short dmPaperSize;
            public short dmPaperLength;
            public short dmPaperWidth;
            public short dmScale;
            public short dmCopies;
            public short dmDefaultSource;
            public short dmPrintQuality;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;

            public int dmDisplayFlags;
            public int dmDisplayFrequency;


            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
        }

        /// <summary>
        /// Ruft die aktuelle Bildwiederholfrequenz (Refresh Rate) des Monitors ab.
        /// </summary>
        /// <param name="screenId">Optionale ID des zu prüfenden Bildschirms. Standardmäßig wird der primäre Bildschirm verwendet.</param>
        /// <returns>Die Bildwiederholfrequenz in Hz als Float. Gibt 60.0f als Fallback zurück.</returns>
        internal static float GetScreenRefreshRate(int? screenId = null)
        {
            Screen screen;
            try
            {
                Screen[] allScreens = Screen.AllScreens;
                if (screenId.HasValue && screenId.Value >= 0 && screenId.Value < allScreens.Length)
                {
                    screen = allScreens[screenId.Value];
                }
                else
                {
                    screen = Screen.PrimaryScreen ?? allScreens.FirstOrDefault() ?? throw new InvalidOperationException("Kein Bildschirm gefunden.");
                }
            }
            catch (Exception)
            {
                return 60.0f;
            }

            DEVMODE dm = new()
            {
                dmSize = (short) Marshal.SizeOf(typeof(DEVMODE))
            };

            int result = EnumDisplaySettings(screen.DeviceName, ENUM_CURRENT_SETTINGS, ref dm);

            if (result != 0 && dm.dmDisplayFrequency > 1)
            {
                return (float) dm.dmDisplayFrequency;
            }

            return 60.0f;
        }

        internal static Point GetCenterStartingPoint(Form? form = null, int? screenId = null)
        {
            if (form != null)
            {
                screenId = Array.IndexOf(Screen.AllScreens, Screen.FromControl(form));
            }

            screenId ??= Screen.PrimaryScreen != null
                ? Array.IndexOf(Screen.AllScreens, Screen.PrimaryScreen)
                : 0;

            Screen screen;
            if (screenId.HasValue)
            {
                Screen[] allScreens = Screen.AllScreens;
                if (screenId.Value >= 0 && screenId.Value < allScreens.Length)
                {
                    screen = allScreens[screenId.Value];
                }
                else
                {
                    screen = Screen.PrimaryScreen ?? allScreens.FirstOrDefault() ?? throw new InvalidOperationException("Kein Bildschirm gefunden.");
                }
            }
            else
            {
                screen = Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault() ?? throw new InvalidOperationException("Kein Bildschirm gefunden.");
            }

            int x = screen.WorkingArea.X + (screen.WorkingArea.Width - (form?.Width ?? 0)) / 2;
            int y = screen.WorkingArea.Y + (screen.WorkingArea.Height - (form?.Height ?? 0)) / 2;
            return new Point(x, y);
        }

        internal static Point GetCornerPosition(Form? form = null, bool left = true, bool top = true, int? screenId = null)
        {
            // Calculate starting position based on screen working area and formif given
            if (form != null)
            {
                screenId = Array.IndexOf(Screen.AllScreens, Screen.FromControl(form));
            }

            screenId ??= Screen.PrimaryScreen != null
                ? Array.IndexOf(Screen.AllScreens, Screen.PrimaryScreen)
                : 0;
            Screen screen;
            if (screenId.HasValue)
            {
                Screen[] allScreens = Screen.AllScreens;
                if (screenId.Value >= 0 && screenId.Value < allScreens.Length)
                {
                    screen = allScreens[screenId.Value];
                }
                else
                {
                    screen = Screen.PrimaryScreen ?? allScreens.FirstOrDefault() ?? throw new InvalidOperationException("Kein Bildschirm gefunden.");
                }
            }
            else
            {
                screen = Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault() ?? throw new InvalidOperationException("Kein Bildschirm gefunden.");
            }
            int x = left
                ? screen.WorkingArea.X
                : screen.WorkingArea.X + screen.WorkingArea.Width - (form?.Width ?? 0);
            int y = top
                ? screen.WorkingArea.Y
                : screen.WorkingArea.Y + screen.WorkingArea.Height - (form?.Height ?? 0);
            return new Point(x, y);
        }


    }
}

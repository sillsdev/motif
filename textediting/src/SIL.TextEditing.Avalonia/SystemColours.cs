using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;

namespace SIL.TextEditing.Avalonia
{
    /// <summary>The operating system's own colours, which the field uses for its default text, page and selection while the system is in a high contrast
    /// theme: Avalonia's themes have no high contrast variant, so they would give black on white there.</summary>
    enum SystemColour
    {
        /// <summary>The colour of text in a window (the page text).</summary>
        WindowText,
        /// <summary>The colour of a window's page.</summary>
        Window,
        /// <summary>The fill of selected text.</summary>
        Highlight,
        /// <summary>The colour of selected text, on the highlight.</summary>
        HighlightText,
    }

    static class SystemColours
    {
        /// <summary>Whether the system is in a high contrast theme. Internal and settable so a test can say it is.</summary>
        internal static Func<bool> Probe
        {
            get => _probe;
            set { _probe = value; _cached = null; }
        }

        static Func<bool> _probe = DefaultIsHighContrast;
        static bool? _cached;

        /// <summary>Whether the system is in a high contrast theme, asked once and kept until <see cref="Refresh"/> (the system call is not free, and
        /// the colours are asked for several times in a paint).</summary>
        internal static bool IsHighContrast() => _cached ??= _probe();

        /// <summary>Forgets the answer, so the next one is asked of the system again.</summary>
        internal static void Refresh() => _cached = null;

        /// <summary>The system's colour of a kind, or null when this system has none to give (anything but Windows). Internal and settable like
        /// <see cref="IsHighContrast"/>.</summary>
        internal static Func<SystemColour, Color?> Get = DefaultGet;

        static bool DefaultIsHighContrast()
        {
            try
            {
                if (Application.Current?.PlatformSettings?.GetColorValues().ContrastPreference == ColorContrastPreference.High) return true;
            }
            catch (Exception) { }
            return IsWindows && WindowsHighContrast();
        }

        static Color? DefaultGet(SystemColour kind)
        {
            if (!IsWindows) return null;
            uint rgb = GetSysColor(kind == SystemColour.WindowText ? 8 : kind == SystemColour.Window ? 5 : kind == SystemColour.Highlight ? 13 : 14);   // COLOR_WINDOWTEXT, COLOR_WINDOW, COLOR_HIGHLIGHT, COLOR_HIGHLIGHTTEXT
            return Color.FromRgb((byte)(rgb & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)((rgb >> 16) & 0xFF));
        }

        static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        static bool WindowsHighContrast()
        {
            try
            {
                var hc = new HighContrast { Size = (uint)(IntPtr.Size == 8 ? 16 : 12) };
                return SystemParametersInfo(0x0042, hc.Size, ref hc, 0) && (hc.Flags & 1) != 0;   // SPI_GETHIGHCONTRAST, HCF_HIGHCONTRASTON
            }
            catch (Exception) { return false; }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HighContrast
        {
            public uint Size;
            public uint Flags;
            public IntPtr DefaultScheme;
        }

        [DllImport("user32.dll")]
        static extern uint GetSysColor(int index);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SystemParametersInfo(uint action, uint param, ref HighContrast value, uint winIni);
    }
}

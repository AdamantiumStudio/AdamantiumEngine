using System;

namespace Adamantium.Win32
{
    [Flags]
    public enum SPI
    {
        GetMouse = 0x0003,
        GetMouseSpeed = 0x0070,

        /// <summary>SPI_GETMOUSEHOVERTIME - how long the pointer must rest before the OS calls it a hover, in
        /// milliseconds. The user's own dwell preference; we reuse it for spring-loading and window raising.</summary>
        GetMouseHoverTime = 0x0066,

        /// <summary>SPI_GETKEYBOARDDELAY - how long a held key waits before it repeats, 0 to 3: 250 ms to 1 s.</summary>
        GetKeyboardDelay = 0x0016,

        /// <summary>SPI_GETKEYBOARDSPEED - how fast a held key repeats, 0 to 31: about 2.5 to 30 times a second.</summary>
        GetKeyboardSpeed = 0x000A,

        /// <summary>SPI_SETDESKWALLPAPER - arrives as the wParam of the WM_SETTINGCHANGE broadcast when the desktop
        /// picture changes, including the slideshow turning its page. Backdrop materials cache the picture, so this is
        /// the moment they re-read it.</summary>
        SetDeskWallpaper = 0x0014
    }
}
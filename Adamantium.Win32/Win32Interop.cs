using System;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.Win32
{
    public class Win32Interop
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern UInt16 RegisterClassW([In] ref WndClass lpWndClass);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CreateWindowExW(
            WindowStyleEx dwExStyle,
            [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
            [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName,
            WindowStyle dwStyle,
            Int32 x,
            Int32 y,
            Int32 nWidth,
            Int32 nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam
        );

        public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr CallWindowProc(
            IntPtr wndProc,
            IntPtr hWnd,
            WindowMessages Msg,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr ShowWindow(IntPtr hwnd, WindowShowStyle show);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "DefWindowProcW")]
        public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        // --- Clipboard (used by the text-input controls via a WindowsClipboard : IClipboard) ---
        public const uint CF_UNICODETEXT = 13;
        public const uint GMEM_MOVEABLE = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GlobalUnlock(IntPtr hMem);

        public static IntPtr DefWindowProc(IntPtr hWnd, WindowMessages msg, IntPtr wParam, IntPtr lParam)
        {
            return DefWindowProcW(hWnd, (uint)msg, wParam, lParam);
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        // --- Per-Monitor DPI (PMv2). ---
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr deviceContext, IntPtr clip, MonitorEnumProc callback, IntPtr data);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);

        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 is a pseudo-handle (-4), not a real pointer.
        public static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new IntPtr(-4);
        public const uint MonitorDefaultToNearest = 2;
        public const int MdtEffectiveDpi = 0;   // MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI

        // Mouse capture: while a window holds the capture the OS delivers ALL mouse messages (move + button-up) to it,
        // even when the cursor is outside the client area - so a drag keeps tracking and the release is caught off-window.
        [DllImport("user32.dll", EntryPoint = "SetCapture")]
        public static extern IntPtr SetCapture(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "ReleaseCapture")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        /// <summary>The active window OF THE CALLING THREAD - which is the UI thread, so it is our own window and never
        /// another application's. What a modal dialog is owned by, so it centers on the window the user pressed in and
        /// that window cannot be used behind it.</summary>
        [DllImport("user32.dll")]
        public static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern Boolean AdjustWindowRect(ref RECT lpRect, UInt32 dwStyle, bool bMenu);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(uint key);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(uint key);

        [DllImport("user32.dll")]
        public static extern IntPtr LoadCursor(IntPtr hInstance, NativeCursors cursorName);

        [DllImport("user32.dll", EntryPoint = "LoadImageW", SetLastError = true)]
        public static extern IntPtr LoadImage(IntPtr instance, IntPtr name, LoadImageType type, int width, int height, LoadImageFlags flags);

        [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string moduleName);

        // CharSet.Unicode is REQUIRED: the entry point is the W (wide) variant, so the path must be marshaled as UTF-16.
        // Without it the string goes as ANSI, the W function reads garbage, the file "isn't found" and it returns NULL.
        [DllImport("user32.dll", EntryPoint = "LoadCursorFromFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadCursorFromFile(string filePath);

        [DllImport("user32.dll")]
        public static extern IntPtr SetCursor(IntPtr hCursor);

        [DllImport("user32.dll")]
        public static extern IntPtr CreateCursor(IntPtr hInst, int xHotSpot, int yHotSpot, int nWidth, int nHeight, byte[] pvANDPlane, byte[] pvXORPlane);

        [DllImport("user32.dll")]
        public static extern bool SetSystemCursor(IntPtr hcur, NativeCursors cursorId);

        [DllImport("user32.dll")]
        public static extern int ShowCursor(bool show);

        /// <summary>
        /// Changes an attribute of the specified window. The function also sets the 32-bit (long) value at the specified offset into the extra window memory.
        /// </summary>
        /// <param name="hWnd">A handle to the window and, indirectly, the class to which the window belongs..</param>
        /// <param name="nIndex">The zero-based offset to the value to be set. Valid values are in the range zero through the number of bytes of extra window memory, minus the size of an integer. To set any other value, specify one of the following values: GWL_EXSTYLE, GWL_HINSTANCE, GWL_ID, GWL_STYLE, GWL_USERDATA, GWL_WNDPROC </param>
        /// <param name="dwNewLong">The replacement value.</param>
        /// <returns>If the function succeeds, the return value is the previous value of the specified 32-bit integer. 
        /// If the function fails, the return value is zero. To get extended error information, call GetLastError. </returns>
        public static IntPtr SetWindowLong(IntPtr hWnd, WindowLongType nIndex, IntPtr dwNewLong)
        {
            if (Environment.Is64BitProcess)
            {
                return SetWindowLong64(hWnd, nIndex, dwNewLong);
            }
            return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowText")]
        public static extern bool SetWindowText(IntPtr hwnd, string title);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", CharSet = CharSet.Unicode)]
        private static extern IntPtr SetWindowLong64(IntPtr hwnd, WindowLongType index, IntPtr wndProc);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", CharSet = CharSet.Unicode)]
        private static extern long SetWindowLong32(IntPtr hwnd, WindowLongType index, long newValue);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out NativePoint lpPoint);

        /// <summary>The window under a SCREEN point, honoring the real z-order. Hidden, disabled and click-through
        /// (WS_EX_TRANSPARENT) windows are skipped - which is why the drag ghost never shadows the window behind it.</summary>
        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(NativePoint point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            SetWindowPosFlags uFlags);

        [DllImport(
            "user32.dll",
            CharSet = CharSet.Auto,
            CallingConvention = CallingConvention.Winapi,
            SetLastError = true)]
        public static extern MessageBoxResult MessageBoxEx(
            IntPtr hwnd,
            String text,
            String caption,
            uint options,
            ushort languageId);

        [DllImport("user32.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        public static extern int GetMessageTime();

        [DllImport("user32.dll")]
        public static extern bool ScreenToClient(
            IntPtr hWnd,
            [MarshalAs(UnmanagedType.Struct)] ref NativePoint lpPoint);

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(
            IntPtr hWnd,
            [MarshalAs(UnmanagedType.Struct)] ref NativePoint lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);

        //Get value which defines time between two mouse down events, which will be translated as mouse double click 
        [DllImport("user32.dll")]
        public static extern uint GetDoubleClickTime();


        [DllImport("Kernel32.dll")]
        public static extern UInt64 GetTickCount64();

        [DllImport("user32.dll")]
        public static extern IntPtr BeginDeferWindowPos(int nNumWindows);

        [DllImport("user32.dll")]
        public static extern IntPtr DeferWindowPos(
            IntPtr hWinPosInfo,
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            SetWindowPosFlags flags);

        [DllImport("user32.dll")]
        public static extern bool EndDeferWindowPos(IntPtr hWinPosInfo);

        [DllImport("user32.dll")]
        public static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT lpPaint);

        [DllImport("user32.dll")]
        public static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

        [DllImport("user32.dll")]
        public static extern bool GetUpdateRect(IntPtr hwnd, out RECT lpRect, bool bErase);

        public static IntPtr GetWindowLong(IntPtr hWnd, WindowLongType nIndex)
        {
            return Environment.Is64BitProcess ? GetWindowLong64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
        }

        public static WindowStyle GetWindowStyle(IntPtr hWnd, WindowLongType nIndex)
        {
            var longPtr = GetWindowLong64(hWnd, nIndex);
            var style = IntPtr.Size == 4 ? (WindowStyle) longPtr.ToInt32() : (WindowStyle) longPtr.ToInt64();
            return style;
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetWindowLong64(IntPtr hwnd, WindowLongType index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetWindowLong32(IntPtr hwnd, WindowLongType index);

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        // When you don't want the ProcessId, use this overload and pass IntPtr.Zero for the second parameter
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        // True if the window is currently maximized (zoomed). Queried live from the OS so frame math never trusts a
        // cached state that can lag a native restore-drag.
        [DllImport("user32.dll")]
        public static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfo(SPI uiAction, uint uiParam, ref IntPtr pvParam, SPIF fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfo(SPI uiAction, uint uiParam, int[] pvParam, SPIF fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I4)]
        public static extern int GetSystemMetrics(SystemMetrics metrics);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr windowHandle, WindowMessages msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDCEx(IntPtr hWnd, IntPtr hrgnClip, DeviceContextValues flags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        public static extern bool RedrawWindow(IntPtr hWnd, [In] ref RECT lprcUpdate, IntPtr hrgnUpdate, RedrawWindowFlags flags);

        [DllImport("user32.dll")]
        public static extern bool InflateRect(ref RECT lprc, int dx, int dy);

        [DllImport("dwmapi.dll")]
        public static extern IntPtr DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

        // Windows 11 (build 22000+): set a DWM window attribute. For DWMWA_BORDER_COLOR (34) the value is a COLORREF
        // (0x00BBGGRR); DWMWA_COLOR_DEFAULT/NONE are sentinels. Returns S_OK (0) on success, an error HRESULT on older OS.
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

        // Whether Windows asks apps to look dark: HKCU AppsUseLightTheme, 0 = dark; absent (older Windows) means light.
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private static readonly IntPtr HkeyCurrentUser = new(unchecked((int)0x80000001));
        private const uint RrfRtRegDword = 0x00000010;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegGetValueW")]
        private static extern int RegGetValue(IntPtr hkey, string subKey, string value, uint flags,
            out uint type, out int data, ref uint dataSize);

        /// <summary>Reads the Windows "apps use light theme" setting. Returns false (light) when the value cannot be
        /// read at all, which is what a Windows without dark mode looks like.</summary>
        public static bool SystemPrefersDarkAppearance()
        {
            uint size = sizeof(int);
            var result = RegGetValue(HkeyCurrentUser, PersonalizeKey, "AppsUseLightTheme", RrfRtRegDword,
                out _, out var appsUseLightTheme, ref size);

            return result == 0 && appsUseLightTheme == 0;
        }

        /*
            Graphics
        */

        [DllImport("gdi32.dll")]
        public static extern bool Rectangle(IntPtr hDC, int left, int top, int right, int bottom);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateSolidBrush(uint crColor);

        /// <summary>
        /// The SelectObject function selects an object into the specified device context (DC). The new object replaces the previous object of the same type.
        /// </summary>
        /// <param name="hdc">Device Context</param>
        /// <param name="hgdiobj"></param>
        /// <returns></returns>
        [DllImport("gdi32.dll", EntryPoint = "SelectObject")]
        public static extern IntPtr SelectObject([In] IntPtr hdc, [In] IntPtr hgdiobj);

        [DllImport("gdi32.dll", EntryPoint = "CreatePen")]
        public static extern IntPtr CreatePen(PenStyle penStyle, int width, uint color);

        /*
            Layered window (drag ghost): per-pixel-alpha composite done by the OS.
        */

        public const int ULW_ALPHA = 0x00000002;

        // Uniform translucency for a window that renders ITSELF (no bitmap pushed at the OS): the DWM composites the
        // whole window at one alpha. Per-pixel transparency does not come from here - it comes from the swapchain
        // asking for pre-multiplied composition.
        public const uint LWA_ALPHA = 0x00000002;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        public const byte AC_SRC_OVER = 0x00;
        public const byte AC_SRC_ALPHA = 0x01;
        public const uint BI_RGB = 0;
        public const uint DIB_RGB_COLORS = 0;

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        // Push a 32-bit premultiplied-BGRA bitmap onto a WS_EX_LAYERED window; the DWM composites it with per-pixel alpha.
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateLayeredWindow(
            IntPtr hwnd, IntPtr hdcDst,
            ref NativePoint pptDst, ref NativeSize psize,
            IntPtr hdcSrc, ref NativePoint pptSrc,
            uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        // 32-bit top-down DIB section; ppvBits points at its pixel buffer (write premultiplied BGRA straight into it).
        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateDIBSection(
            IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        /*
            OLE drag-drop: the bridge to other applications.
        */

        /// <summary>Standard clipboard format ids (CF_UNICODETEXT sits with the clipboard block above). The drag payload
        /// speaks these on the wire; the neutral names a view-model sees live in <c>DataFormats</c>.</summary>
        public const uint CF_TEXT = 1;
        /// <summary>A packed device-independent bitmap: BITMAPINFOHEADER immediately followed by the pixels, with no
        /// file header. What the classic imaging applications (Paint, Word) accept - modern ones prefer the registered
        /// "PNG" format.</summary>
        public const uint CF_DIB = 8;
        public const uint CF_HDROP = 15;

        /// <summary>Bring OLE up on the CALLING thread (it must be an STA). Required before RegisterDragDrop /
        /// DoDragDrop; returns RPC_E_CHANGED_MODE when the thread is already an MTA.</summary>
        [DllImport("ole32.dll")]
        public static extern int OleInitialize(IntPtr reserved);

        [DllImport("ole32.dll")]
        public static extern void OleUninitialize();

        /// <summary>Make an HWND a drop target. The interface is kept alive by the OS until RevokeDragDrop; the window
        /// must belong to the calling thread.</summary>
        [DllImport("ole32.dll")]
        public static extern int RegisterDragDrop(IntPtr hwnd, [MarshalAs(UnmanagedType.Interface)] Ole.IDropTarget dropTarget);

        [DllImport("ole32.dll")]
        public static extern int RevokeDragDrop(IntPtr hwnd);

        /// <summary>Run the modal OLE drag loop. BLOCKS the calling thread for the whole gesture and returns
        /// DRAGDROP_S_DROP / DRAGDROP_S_CANCEL, with the effect the target applied in <paramref name="effect"/>.</summary>
        [DllImport("ole32.dll")]
        public static extern int DoDragDrop(
            [MarshalAs(UnmanagedType.Interface)] System.Runtime.InteropServices.ComTypes.IDataObject data,
            [MarshalAs(UnmanagedType.Interface)] Ole.IDropSource dropSource,
            Ole.DropEffect allowedEffects,
            out Ole.DropEffect effect);

        [DllImport("ole32.dll")]
        public static extern void ReleaseStgMedium(ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium);

        [DllImport("ole32.dll")]
        public static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out object instance);

        /// <summary>CLSCTX_INPROC_SERVER - the shell's drag-image helper lives in-process.</summary>
        public const uint ClsCtxInprocServer = 1;

        /// <summary>CLSCTX_ALL - let COM pick the server it is actually registered as. The wallpaper service is NOT
        /// in-process: its CLSID carries an AppId and no InProcServer32, so asking for in-process only comes back
        /// REGDB_E_CLASSNOTREG - the class is there, the context is not.</summary>
        public const uint ClsCtxAll = 23;

        /// <summary>CLSID_DragDropHelper: implements both IDragSourceHelper and IDropTargetHelper.</summary>
        public static readonly Guid ClsidDragDropHelper = new("4657278A-411B-11D2-839A-00C04FD918D0");

        /// <summary>CLSID_DesktopWallpaper: the shell's wallpaper service, which backdrop materials read (Mica shows
        /// the desktop picture behind the window, not the frame behind the element).</summary>
        public static readonly Guid ClsidDesktopWallpaper = new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");

        /// <summary>Register (or look up) a private clipboard format by name - how a non-standard payload crosses
        /// processes.</summary>
        [DllImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ushort RegisterClipboardFormat(string format);

        /// <summary>Read a CF_HDROP block: index 0xFFFFFFFF returns the file COUNT, any other index fills
        /// <paramref name="file"/> with that path.</summary>
        [DllImport("shell32.dll", EntryPoint = "DragQueryFileW", CharSet = CharSet.Unicode)]
        public static extern uint DragQueryFile(IntPtr drop, uint index, [Out] char[] file, uint charCount);

        [DllImport("kernel32.dll")]
        public static extern UIntPtr GlobalSize(IntPtr mem);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GlobalFree(IntPtr mem);

        // --- Pointer input (WM_POINTER*): touch and pen ---

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetPointerType(uint pointerId, out PointerInputType pointerType);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetPointerInfo(uint pointerId, out POINTER_INFO pointerInfo);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetPointerTouchInfo(uint pointerId, out POINTER_TOUCH_INFO touchInfo);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetPointerPenInfo(uint pointerId, out POINTER_PEN_INFO penInfo);

        /// <summary>Switches one of the system's touch or pen visuals off (or on) for a window. <paramref name="enabled"/>
        /// is a BOOL; its size goes in <paramref name="size"/>.</summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowFeedbackSetting(IntPtr hwnd, FeedbackType feedback, uint flags, uint size,
            ref int enabled);

        /// <summary>A touch or pen device that exists only to inject input through: real WM_POINTER messages without
        /// the hardware. <paramref name="maxCount"/> is how many contacts it carries at once (a pen carries one).</summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CreateSyntheticPointerDevice(PointerInputType pointerType, uint maxCount,
            PointerFeedbackMode mode);

        /// <summary>Hands the contacts of one frame to the system, which raises them as WM_POINTER at their screen
        /// points, in whatever window is there.</summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool InjectSyntheticPointerInput(IntPtr device, [In] POINTER_TYPE_INFO[] pointerInfo,
            uint count);

        [DllImport("user32.dll")]
        public static extern void DestroySyntheticPointerDevice(IntPtr device);
    }
}

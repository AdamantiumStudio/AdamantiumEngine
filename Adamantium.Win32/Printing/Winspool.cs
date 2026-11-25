using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Adamantium.Win32.Printing;

/// <summary>The spooler's printers (<c>winspool.drv</c>) and the PrintTicket provider (<c>prntvpt.dll</c>).</summary>
public static class Winspool
{
    public const uint EnumLocal = 0x2;
    public const uint EnumConnections = 0x4;

    /// <summary><c>DM_OUT_BUFFER</c>: the DEVMODE is written out.</summary>
    public const int DevModeOutBuffer = 2;

    /// <summary><c>DM_IN_PROMPT</c>: the driver's own window is shown.</summary>
    public const int DevModeInPrompt = 4;

    /// <summary><c>DM_IN_BUFFER</c>: the DEVMODE given is where the window starts.</summary>
    public const int DevModeInBuffer = 8;

    /// <summary><c>IDOK</c>, as the driver's window answers when it is left with OK.</summary>
    public const int Ok = 1;

    /// <summary><c>PRINTER_ATTRIBUTE_WORK_OFFLINE</c>.</summary>
    public const uint AttributeWorkOffline = 0x400;

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "EnumPrintersW")]
    public static extern bool EnumPrinters(uint flags, string name, uint level, IntPtr printers, uint size,
        out uint needed, out uint returned);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetDefaultPrinterW")]
    public static extern bool GetDefaultPrinter(char[] buffer, ref uint size);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "OpenPrinterW")]
    public static extern bool OpenPrinter(string name, out IntPtr printer, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool ClosePrinter(IntPtr printer);

    /// <summary>The printer's DEVMODE: its size with no buffers, or with <see cref="DevModeInPrompt"/> the driver's own
    /// settings window over <paramref name="owner"/>.</summary>
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "DocumentPropertiesW")]
    public static extern int DocumentProperties(IntPtr owner, IntPtr printer, string deviceName, IntPtr output,
        IntPtr input, int mode);

    // ---- PrintTicket provider ----

    [DllImport("prntvpt.dll", CharSet = CharSet.Unicode, EntryPoint = "PTOpenProvider")]
    public static extern int PTOpenProvider(string printerName, uint version, out IntPtr provider);

    [DllImport("prntvpt.dll")]
    public static extern int PTCloseProvider(IntPtr provider);

    [DllImport("prntvpt.dll")]
    public static extern int PTReleaseMemory(IntPtr buffer);

    [DllImport("prntvpt.dll")]
    public static extern int PTGetPrintCapabilities(IntPtr provider, IStream printTicket, IStream capabilities,
        [MarshalAs(UnmanagedType.BStr)] out string error);

    [DllImport("prntvpt.dll")]
    public static extern int PTMergeAndValidatePrintTicket(IntPtr provider, IStream baseTicket, IStream deltaTicket,
        PrintTicketScope scope, IStream result, [MarshalAs(UnmanagedType.BStr)] out string error);

    [DllImport("prntvpt.dll")]
    public static extern int PTConvertDevModeToPrintTicket(IntPtr provider, uint size, IntPtr devMode,
        PrintTicketScope scope, IStream printTicket);

    [DllImport("prntvpt.dll")]
    public static extern int PTConvertPrintTicketToDevMode(IntPtr provider, IStream printTicket,
        DefaultDevModeType baseType, PrintTicketScope scope, out uint size, out IntPtr devMode,
        [MarshalAs(UnmanagedType.BStr)] out string error);
}

/// <summary><c>EPrintTicketScope</c>.</summary>
public enum PrintTicketScope
{
    Page = 0,
    Document = 1,
    Job = 2,
}

/// <summary><c>EDefaultDevmodeType</c>.</summary>
public enum DefaultDevModeType
{
    User = 0,
    Printer = 1,
}

/// <summary>The fields of <c>PRINTER_INFO_2W</c> read from the spooler's answer.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct PrinterInfo2
{
    public IntPtr ServerName;
    public IntPtr PrinterName;
    public IntPtr ShareName;
    public IntPtr PortName;
    public IntPtr DriverName;
    public IntPtr Comment;
    public IntPtr Location;
    public IntPtr DevMode;
    public IntPtr SepFile;
    public IntPtr PrintProcessor;
    public IntPtr Datatype;
    public IntPtr Parameters;
    public IntPtr SecurityDescriptor;
    public uint Attributes;
    public uint Priority;
    public uint DefaultPriority;
    public uint StartTime;
    public uint UntilTime;
    public PrinterStatusFlags Status;
    public uint Jobs;
    public uint AveragePagesPerMinute;
}

/// <summary><c>PRINTER_STATUS_*</c>.</summary>
[Flags]
public enum PrinterStatusFlags : uint
{
    None = 0,
    Paused = 0x1,
    Error = 0x2,
    PendingDeletion = 0x4,
    PaperJam = 0x8,
    PaperOut = 0x10,
    ManualFeed = 0x20,
    PaperProblem = 0x40,
    Offline = 0x80,
    IoActive = 0x100,
    Busy = 0x200,
    Printing = 0x400,
    OutputBinFull = 0x800,
    NotAvailable = 0x1000,
    Waiting = 0x2000,
    Processing = 0x4000,
    Initializing = 0x8000,
    WarmingUp = 0x10000,
    TonerLow = 0x20000,
    NoToner = 0x40000,
    PagePunt = 0x80000,
    UserIntervention = 0x100000,
    OutOfMemory = 0x200000,
    DoorOpen = 0x400000,
    ServerUnknown = 0x800000,
    PowerSave = 0x1000000,
    ServerOffline = 0x2000000,
}

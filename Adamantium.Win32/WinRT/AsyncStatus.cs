namespace Adamantium.Win32.WinRT;

/// <summary>Where a Windows Runtime asynchronous operation stands (Windows.Foundation.AsyncStatus).</summary>
public enum AsyncStatus
{
    Started = 0,
    Completed = 1,
    Canceled = 2,
    Error = 3,
}

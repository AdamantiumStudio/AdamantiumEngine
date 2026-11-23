using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32.WinRT;

/// <summary>The Windows Runtime's own calls in combase.dll: its strings (HSTRING) and the factories its classes are made
/// with.</summary>
public static class WinRTInterop
{
    private const string Combase = "combase.dll";

    [DllImport(Combase, CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, uint length, out IntPtr hstring);

    [DllImport(Combase)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport(Combase)]
    private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

    [DllImport(Combase)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    /// <summary>A Windows Runtime string of <paramref name="text"/>, to be deleted with <see cref="DeleteString"/>;
    /// zero, the empty string, for an empty or null one.</summary>
    public static IntPtr CreateString(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return IntPtr.Zero;
        }

        Marshal.ThrowExceptionForHR(WindowsCreateString(text, (uint)text.Length, out var hstring));
        return hstring;
    }

    public static void DeleteString(IntPtr hstring)
    {
        if (hstring != IntPtr.Zero)
        {
            WindowsDeleteString(hstring);
        }
    }

    /// <summary>The text of a Windows Runtime string; zero is the empty string.</summary>
    public static string ReadString(IntPtr hstring)
    {
        if (hstring == IntPtr.Zero)
        {
            return string.Empty;
        }

        var buffer = WindowsGetStringRawBuffer(hstring, out var length);
        return Marshal.PtrToStringUni(buffer, (int)length);
    }

    /// <summary>The factory of the class named <paramref name="className"/> (as Windows.Data.Text.TextPredictionGenerator)
    /// as <typeparamref name="TFactory"/>; null where this Windows has no such class.</summary>
    public static TFactory GetActivationFactory<TFactory>(string className) where TFactory : class
    {
        var name = CreateString(className);
        try
        {
            var iid = typeof(TFactory).GUID;
            if (RoGetActivationFactory(name, ref iid, out var factory) != 0 || factory == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.GetObjectForIUnknown(factory) as TFactory;
            }
            finally
            {
                Marshal.Release(factory);
            }
        }
        finally
        {
            DeleteString(name);
        }
    }
}

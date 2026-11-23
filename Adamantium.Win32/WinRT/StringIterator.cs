using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Adamantium.Win32.WinRT;

/// <summary>Going through the strings of a <see cref="StringIterable"/> for the Windows Runtime: each string handed out
/// is a new HSTRING, which its taker deletes.</summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class StringIterator(IReadOnlyList<string> strings) : IStringIterator, IInspectable
{
    private const int BoundsError = unchecked((int)0x8000000B);

    private int _index;

    public int GetIids(out uint count, out IntPtr iids)
    {
        count = 0;
        iids = IntPtr.Zero;
        return 0;
    }

    public int GetRuntimeClassName(out IntPtr className)
    {
        className = IntPtr.Zero;
        return 0;
    }

    public int GetTrustLevel(out int trustLevel)
    {
        trustLevel = 0;
        return 0;
    }

    public int GetCurrent(out IntPtr current)
    {
        if (_index >= strings.Count)
        {
            current = IntPtr.Zero;
            return BoundsError;
        }

        current = WinRTInterop.CreateString(strings[_index]);
        return 0;
    }

    public int GetHasCurrent(out bool hasCurrent)
    {
        hasCurrent = _index < strings.Count;
        return 0;
    }

    public int MoveNext(out bool hasCurrent)
    {
        if (_index < strings.Count)
        {
            _index++;
        }

        hasCurrent = _index < strings.Count;
        return 0;
    }

    public int GetMany(uint capacity, IntPtr items, out uint count)
    {
        count = 0;
        while (count < capacity && _index < strings.Count)
        {
            Marshal.WriteIntPtr(items, (int)count * IntPtr.Size, WinRTInterop.CreateString(strings[_index]));
            _index++;
            count++;
        }

        return 0;
    }
}

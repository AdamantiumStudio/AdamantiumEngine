using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Adamantium.Win32.WinRT;

/// <summary>Strings of ours handed to a Windows Runtime call that takes an <see cref="IStringIterable"/>.</summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class StringIterable(IReadOnlyList<string> strings) : IStringIterable, IInspectable
{
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

    public int First(out IStringIterator iterator)
    {
        iterator = new StringIterator(strings);
        return 0;
    }
}

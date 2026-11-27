using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32.Shell;

/// <summary>Items of the shell namespace, as an Open dialog that lets several be chosen returns them. Every slot is
/// declared, in order: a missing one would put those after it at the wrong offset.</summary>
[ComImport]
[Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItemArray
{
    [PreserveSig]
    int BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr instance);

    [PreserveSig]
    int GetPropertyStore(int flags, ref Guid iid, out IntPtr store);

    [PreserveSig]
    int GetPropertyDescriptionList(IntPtr keyType, ref Guid iid, out IntPtr list);

    [PreserveSig]
    int GetAttributes(int how, uint mask, out uint attributes);

    [PreserveSig]
    int GetCount(out uint count);

    /// <summary>The item at <paramref name="index"/>; the caller releases it.</summary>
    [PreserveSig]
    int GetItemAt(uint index, out IShellItem item);

    [PreserveSig]
    int EnumItems(out IntPtr items);
}

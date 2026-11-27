using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Adamantium.Mathematics;

namespace Adamantium.Win32.Ole;

/// <summary>
/// OLE <c>IDropTarget</c> - the window-side half of a native drag. A managed implementation is handed to
/// <c>RegisterDragDrop</c>; the OS then calls it (on the window's own thread, inside the drag source's modal loop) as
/// the pointer travels over the window. A method's <c>effect</c> is in/out: in = what the source ALLOWS, out = what we
/// will do (which is what the user sees on the cursor).
/// </summary>
[ComImport]
[Guid("00000122-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDropTarget
{
    [PreserveSig]
    int DragEnter([MarshalAs(UnmanagedType.Interface)] IDataObject data, OleKeyState keyState, NativePoint point, ref DropEffect effect);

    [PreserveSig]
    int DragOver(OleKeyState keyState, NativePoint point, ref DropEffect effect);

    [PreserveSig]
    int DragLeave();

    [PreserveSig]
    int Drop([MarshalAs(UnmanagedType.Interface)] IDataObject data, OleKeyState keyState, NativePoint point, ref DropEffect effect);
}

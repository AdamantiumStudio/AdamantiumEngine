using System;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.Foundation;

/// <summary>Foundation data to and from bytes.</summary>
public static unsafe class NSData
{
    private static readonly IntPtr Class = ObjCFrameworks.GetClass("NSData");
    private static readonly IntPtr DataWithBytesSelector = ObjCRuntime.RegisterSelector("dataWithBytes:length:");
    private static readonly IntPtr BytesSelector = ObjCRuntime.RegisterSelector("bytes");
    private static readonly IntPtr LengthSelector = ObjCRuntime.RegisterSelector("length");

    /// <summary>A copy of <paramref name="bytes"/>, autoreleased.</summary>
    public static IntPtr Create(ReadOnlySpan<byte> bytes)
    {
        fixed (byte* first = bytes)
        {
            return ((delegate* unmanaged<IntPtr, IntPtr, byte*, nuint, IntPtr>)ObjCRuntime.MessageSend)(Class,
                DataWithBytesSelector, first, (nuint)bytes.Length);
        }
    }

    /// <summary>A copy of the bytes of an NSData; null for a null one.</summary>
    public static byte[] ToManaged(IntPtr data) => data == IntPtr.Zero
        ? null
        : new ReadOnlySpan<byte>((void*)NSObject.Send(data, BytesSelector), (int)NSObject.SendForInteger(data, LengthSelector))
            .ToArray();
}

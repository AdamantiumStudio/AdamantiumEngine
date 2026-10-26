using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Adamantium.Core;

/// <summary>How <typeparamref name="T"/> lies in native memory: whether it is copied as it is in managed memory, and
/// its size there. A char alone is its UTF-16 unit; a bool, and a struct holding a bool or a char, goes through
/// marshaling - a struct's chars as its <see cref="StructLayoutAttribute.CharSet"/> says; a struct of neither that
/// marshaling cannot size, a generic one, is copied as it is.</summary>
internal static class NativeLayout<T> where T : struct
{
    private static readonly Lazy<int> MarshaledSize = new(Marshal.SizeOf<T>);

    /// <summary>Whether <typeparamref name="T"/> is copied byte for byte.</summary>
    public static readonly bool SameInMemory = Ask();

    /// <summary>The bytes <typeparamref name="T"/> takes in native memory.</summary>
    public static int Size => SameInMemory ? Unsafe.SizeOf<T>() : MarshaledSize.Value;

    private static bool Ask()
    {
        if (typeof(T) == typeof(char))
        {
            return true;
        }

#if NET6_0_OR_GREATER
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            return false;
        }

        try
        {
            return Unsafe.SizeOf<T>() == Marshal.SizeOf<T>() && !Marshaled(typeof(T));
        }
        catch (ArgumentException)
        {
            return !Marshaled(typeof(T));
        }
#else
        return false;
#endif
    }

    private static bool Marshaled(Type type) =>
        type == typeof(bool) || type == typeof(char)
        || !type.IsPrimitive && !type.IsEnum && type.IsValueType && type
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                       | System.Reflection.BindingFlags.NonPublic)
            .Any(field => Marshaled(field.FieldType));
}

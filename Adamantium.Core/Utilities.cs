using System;
using System.Collections;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Adamantium.Core;

/// <summary>Native memory - allocated, freed, filled, copied, read and written by value - and a few helpers the engine
/// shares.</summary>
public static unsafe class Utilities
{
    /// <summary><paramref name="sizeInBytes"/> bytes of native memory, not cleared, for <see cref="FreeMemory"/> to
    /// free. <paramref name="align"/> is at most 16, which every block already meets.</summary>
    public static IntPtr AllocateMemory(int sizeInBytes, int align = 1)
    {
        if (align is < 1 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(align), align, "Blocks are aligned to 16 bytes, no further.");
        }

        return AllocateMemory((nuint)sizeInBytes);
    }

    /// <summary><paramref name="sizeInBytes"/> bytes of native memory, not cleared, for <see cref="FreeMemory"/> to
    /// free.</summary>
    public static IntPtr AllocateMemory(nuint sizeInBytes)
    {
#if NET6_0_OR_GREATER
        return (IntPtr)NativeMemory.Alloc(sizeInBytes);
#else
        return Marshal.AllocHGlobal((IntPtr)(long)sizeInBytes);
#endif
    }

    /// <summary>Frees a block of <see cref="AllocateMemory(nuint)"/>; nothing for a null pointer.</summary>
    public static void FreeMemory(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
        {
            return;
        }

#if NET6_0_OR_GREATER
        NativeMemory.Free((void*)pointer);
#else
        Marshal.FreeHGlobal(pointer);
#endif
    }

    /// <summary>Sets <paramref name="sizeInBytes"/> bytes at <paramref name="destination"/> to
    /// <paramref name="value"/>.</summary>
    public static void ClearMemory(IntPtr destination, byte value, long sizeInBytes)
    {
        for (long done = 0; done < sizeInBytes; done += int.MaxValue)
        {
            new Span<byte>((byte*)destination + done, (int)Math.Min(int.MaxValue, sizeInBytes - done)).Fill(value);
        }
    }

    /// <summary>Copies <paramref name="sizeInBytes"/> bytes from <paramref name="source"/> to
    /// <paramref name="destination"/>; the two may overlap.</summary>
    public static void CopyMemory(IntPtr destination, IntPtr source, long sizeInBytes) =>
        Buffer.MemoryCopy((void*)source, (void*)destination, sizeInBytes, sizeInBytes);

    /// <summary>The bytes a <typeparamref name="T"/> takes in native memory.</summary>
    public static int SizeOf<T>() where T : struct => Marshal.SizeOf<T>();

    /// <summary>The bytes <paramref name="array"/> takes in native memory; 0 for none.</summary>
    public static int SizeOf<T>(T[] array) where T : struct => array == null ? 0 : array.Length * SizeOf<T>();

    /// <summary>Writes <paramref name="value"/> at <paramref name="destination"/>.</summary>
    public static void Write<T>(IntPtr destination, ref T value) where T : struct
    {
        if (Layout<T>.SameInMemory)
        {
            Unsafe.WriteUnaligned((void*)destination, value);
        }
        else
        {
            Marshal.StructureToPtr(value, destination, false);
        }
    }

    /// <summary>Writes <paramref name="count"/> values of <paramref name="data"/> from <paramref name="offset"/> at
    /// <paramref name="destination"/>; gives the address after them.</summary>
    public static IntPtr Write<T>(IntPtr destination, T[] data, int offset, int count) where T : struct
    {
        var bytes = (long)count * SizeOf<T>();
        if (!Layout<T>.SameInMemory)
        {
            for (var i = 0; i < count; i++)
            {
                Marshal.StructureToPtr(data[offset + i], (IntPtr)((byte*)destination + (long)i * SizeOf<T>()), false);
            }

            return (IntPtr)((byte*)destination + bytes);
        }

        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            CopyMemory(destination, (IntPtr)((byte*)handle.AddrOfPinnedObject() + (long)offset * SizeOf<T>()), bytes);
        }
        finally
        {
            handle.Free();
        }

        return (IntPtr)((byte*)destination + bytes);
    }

    /// <summary>The <typeparamref name="T"/> at <paramref name="source"/>.</summary>
    public static T Read<T>(IntPtr source) where T : struct =>
        Layout<T>.SameInMemory ? Unsafe.ReadUnaligned<T>((void*)source) : Marshal.PtrToStructure<T>(source);

    /// <summary>The <typeparamref name="T"/> at <paramref name="source"/>, into <paramref name="data"/>.</summary>
    public static void Read<T>(IntPtr source, ref T data) where T : struct => data = Read<T>(source);

    /// <summary>Reads <paramref name="count"/> values at <paramref name="source"/> into <paramref name="data"/> from
    /// <paramref name="offset"/>; gives the address after them.</summary>
    public static IntPtr Read<T>(IntPtr source, T[] data, int offset, int count) where T : struct
    {
        var bytes = (long)count * SizeOf<T>();
        if (!Layout<T>.SameInMemory)
        {
            for (var i = 0; i < count; i++)
            {
                data[offset + i] = Marshal.PtrToStructure<T>((IntPtr)((byte*)source + (long)i * SizeOf<T>()));
            }

            return (IntPtr)((byte*)source + bytes);
        }

        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            CopyMemory((IntPtr)((byte*)handle.AddrOfPinnedObject() + (long)offset * SizeOf<T>()), source, bytes);
        }
        finally
        {
            handle.Free();
        }

        return (IntPtr)((byte*)source + bytes);
    }

    /// <summary>Whether <paramref name="left"/> and <paramref name="right"/> hold equal elements in the same order -
    /// both null too.</summary>
    public static bool Compare(IEnumerable left, IEnumerable right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left == null || right == null || left is ICollection a && right is ICollection b && a.Count != b.Count)
        {
            return false;
        }

        var leftItems = left.GetEnumerator();
        var rightItems = right.GetEnumerator();
        while (true)
        {
            var leftHas = leftItems.MoveNext();
            if (leftHas != rightItems.MoveNext())
            {
                return false;
            }

            if (!leftHas)
            {
                return true;
            }

            if (!Equals(leftItems.Current, rightItems.Current))
            {
                return false;
            }
        }
    }

    /// <summary>A hash of <paramref name="data"/>: 32-bit FNV-1a, its bits then mixed further so close inputs spread.
    /// </summary>
    public static int ComputeHashFNV1Modified(byte[] data)
    {
        var hash = 2166136261u;
        foreach (var value in data)
        {
            hash = (hash ^ value) * 16777619u;
        }

        hash += hash << 13;
        hash ^= hash >> 7;
        hash += hash << 3;
        hash ^= hash >> 17;
        hash += hash << 5;
        return unchecked((int)hash);
    }

    /// <summary>Puts <paramref name="first"/> where <paramref name="second"/> was and the other way round.</summary>
    public static void Swap<T>(ref T first, ref T second) => (first, second) = (second, first);

    /// <summary><paramref name="size"/> rounded up to a multiple of <paramref name="alignment"/>, a power of two.
    /// </summary>
    public static ulong AlignSize(ulong size, ulong alignment) => (size + alignment - 1) & ~(alignment - 1);

    private static class Layout<T> where T : struct
    {
        public static readonly bool SameInMemory = Ask();

        private static bool Ask()
        {
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
                return false;
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
}

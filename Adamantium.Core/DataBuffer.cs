using System;

namespace Adamantium.Core;

/// <summary>A block of native memory read and written by value at byte positions - of its own, zeroed and freed when
/// disposed, or one the caller lends. Every access is checked against its size.</summary>
public class DataBuffer : DisposableBase
{
    private IntPtr _pointer;
    private readonly bool _owned;

    /// <summary>A block of <paramref name="sizeInBytes"/> zeroed bytes of its own.</summary>
    public DataBuffer(ulong sizeInBytes)
    {
        if (sizeInBytes is 0 or > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeInBytes), sizeInBytes, "A buffer holds 1 byte to 2 GB.");
        }

        _pointer = Utilities.AllocateMemory((int)sizeInBytes);
        Utilities.ClearMemory(_pointer, 0, (long)sizeInBytes);
        Size = sizeInBytes;
        _owned = true;
    }

    /// <summary>The <paramref name="sizeInBytes"/> bytes at <paramref name="memory"/>, which the caller keeps
    /// alive and frees.</summary>
    public DataBuffer(IntPtr memory, ulong sizeInBytes)
    {
        _pointer = memory;
        Size = sizeInBytes;
    }

    /// <summary>Where the bytes are.</summary>
    public IntPtr DataPointer => _pointer;

    /// <summary>How many bytes there are.</summary>
    public ulong Size { get; }

    public static implicit operator DataPointer(DataBuffer buffer) => new(buffer.DataPointer, buffer.Size);

    /// <summary>Sets every byte to <paramref name="value"/>.</summary>
    public void Clear(byte value = 0)
    {
        Utilities.ClearMemory(_pointer, value, (long)Size);
    }

    /// <summary>The value at <paramref name="positionInBytes"/>.</summary>
    public T Get<T>(int positionInBytes) where T : struct
    {
        Get(positionInBytes, out T value);
        return value;
    }

    /// <summary>The value at <paramref name="positionInBytes"/>.</summary>
    public void Get<T>(int positionInBytes, out T value) where T : struct
    {
        value = default;
        Utilities.Read(At(positionInBytes, Utilities.SizeOf<T>()), ref value);
    }

    /// <summary>The <paramref name="count"/> values from <paramref name="positionInBytes"/> on.</summary>
    public T[] GetRange<T>(int positionInBytes, int count) where T : struct
    {
        var values = new T[count];
        Utilities.Read(At(positionInBytes, (long)count * Utilities.SizeOf<T>()), values, 0, count);
        return values;
    }

    /// <summary>Writes <paramref name="value"/> at <paramref name="positionInBytes"/>.</summary>
    public void Set<T>(int positionInBytes, ref T value) where T : struct =>
        Utilities.Write(At(positionInBytes, Utilities.SizeOf<T>()), ref value);

    /// <summary>Writes <paramref name="value"/> at <paramref name="positionInBytes"/>.</summary>
    public void Set<T>(int positionInBytes, T value) where T : struct => Set(positionInBytes, ref value);

    /// <summary>Writes <paramref name="value"/> at <paramref name="positionInBytes"/> as a shader reads a bool: an
    /// int of 1 or 0.</summary>
    public void Set(int positionInBytes, bool value) => Set(positionInBytes, value ? 1 : 0);

    /// <summary>Writes <paramref name="values"/> from <paramref name="positionInBytes"/> on.</summary>
    public void Set<T>(int positionInBytes, T[] values) where T : struct =>
        Utilities.Write(At(positionInBytes, (long)values.Length * Utilities.SizeOf<T>()), values, 0, values.Length);

    protected override void Dispose(bool disposeManaged)
    {
        if (_owned && _pointer != IntPtr.Zero)
        {
            Utilities.FreeMemory(_pointer);
            _pointer = IntPtr.Zero;
        }
    }

    private IntPtr At(int position, long length)
    {
        if (position < 0 || length < 0 || (ulong)position + (ulong)length > Size)
        {
            throw new ArgumentOutOfRangeException(nameof(position), position,
                $"{length} bytes at {position} lie outside a buffer of {Size}.");
        }

        return _pointer + position;
    }
}

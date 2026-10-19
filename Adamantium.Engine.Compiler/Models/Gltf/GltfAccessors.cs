using System;
using System.Buffers.Binary;
using System.IO;

namespace Adamantium.Engine.Compiler.Models.Gltf;

/// <summary>An accessor's elements as the specification reads them: any component type, normalized integers mapped
/// to their range, interleaved views by their stride, a matrix's columns each on a four-byte boundary, and a sparse
/// accessor's values laid over its base (zeros, when it has no view).</summary>
public static class GltfAccessors
{
    /// <summary>How many components one element of <paramref name="type"/> has: 1 for SCALAR up to 16 for MAT4.</summary>
    public static int ComponentsOf(string type) => type switch
    {
        "SCALAR" => 1,
        "VEC2" => 2,
        "VEC3" => 3,
        "VEC4" => 4,
        "MAT2" => 4,
        "MAT3" => 9,
        "MAT4" => 16,
        _ => throw new InvalidDataException($"an accessor of type {type ?? "none"}")
    };

    /// <summary>Every component of the accessor, element after element, as floats; a normalized integer as the
    /// fraction of its range it stands for, any other integer as it is.</summary>
    public static float[] ReadFloats(GltfFile file, int accessor, out int components)
    {
        var info = file.Root.Accessors[accessor];
        components = ComponentsOf(info.Type);
        var values = new float[info.Count * components];
        if (info.BufferView is { } view)
        {
            var bytes = file.View(view);
            var bufferView = file.Root.BufferViews[view];
            var element = ElementSize(info.Type, info.ComponentType);
            var stride = bufferView.ByteStride is > 0 ? bufferView.ByteStride.Value : element;
            for (int i = 0; i < info.Count; i++)
            {
                ReadElement(bytes, info.ByteOffset + i * stride, info, components, values.AsSpan(i * components));
            }
        }

        if (info.Sparse is { Count: > 0 } sparse)
        {
            var indices = file.View(sparse.Indices.BufferView);
            var replacements = file.View(sparse.Values.BufferView);
            var indexSize = SizeOf(sparse.Indices.ComponentType);
            var element = ElementSize(info.Type, info.ComponentType);
            for (int i = 0; i < sparse.Count; i++)
            {
                var target = (int)ReadInteger(indices, sparse.Indices.ByteOffset + i * indexSize, sparse.Indices.ComponentType);
                if ((uint)target >= (uint)info.Count)
                {
                    throw new InvalidDataException($"accessor {accessor}'s sparse index {target} is past its {info.Count}");
                }

                ReadElement(replacements, sparse.Values.ByteOffset + i * element, info, components,
                    values.AsSpan(target * components));
            }
        }

        return values;
    }

    /// <summary>An accessor of integer scalars - indices, a skin's joints - as ints.</summary>
    public static int[] ReadIntegers(GltfFile file, int accessor)
    {
        var info = file.Root.Accessors[accessor];
        if (info.ComponentType == GltfComponentType.Float || info.Normalized)
        {
            throw new InvalidDataException($"accessor {accessor} holds no plain integers");
        }

        var components = ComponentsOf(info.Type);
        var size = SizeOf(info.ComponentType);
        var integers = new int[info.Count * components];
        if (info.BufferView is { } view)
        {
            var bytes = file.View(view);
            var stride = file.Root.BufferViews[view].ByteStride is > 0 ? file.Root.BufferViews[view].ByteStride.Value : components * size;
            ReadIntegers(bytes, info.ByteOffset, stride, info.Count, components, info.ComponentType, integers, null);
        }

        if (info.Sparse is { Count: > 0 } sparse)
        {
            var targets = new int[sparse.Count];
            ReadIntegers(file.View(sparse.Indices.BufferView), sparse.Indices.ByteOffset, SizeOf(sparse.Indices.ComponentType),
                sparse.Count, 1, sparse.Indices.ComponentType, targets, null);
            foreach (var target in targets)
            {
                if ((uint)target >= (uint)info.Count)
                {
                    throw new InvalidDataException($"accessor {accessor}'s sparse index {target} is past its {info.Count}");
                }
            }

            ReadIntegers(file.View(sparse.Values.BufferView), sparse.Values.ByteOffset, components * size, sparse.Count,
                components, info.ComponentType, integers, targets);
        }

        return integers;
    }

    private static void ReadIntegers(ReadOnlySpan<byte> bytes, int offset, int stride, int count, int components,
        int componentType, int[] into, int[] targets)
    {
        var size = SizeOf(componentType);
        if (count > 0 && offset + (count - 1) * stride + components * size > bytes.Length)
        {
            throw new InvalidDataException("an accessor reads past the end of its buffer view");
        }

        for (int i = 0; i < count; i++)
        {
            var element = targets?[i] ?? i;
            for (int c = 0; c < components; c++)
            {
                into[element * components + c] = (int)ReadInteger(bytes, offset + i * stride + c * size, componentType);
            }
        }
    }

    private static int SizeOf(int componentType) => componentType switch
    {
        GltfComponentType.Byte or GltfComponentType.UnsignedByte => 1,
        GltfComponentType.Short or GltfComponentType.UnsignedShort => 2,
        GltfComponentType.UnsignedInt or GltfComponentType.Float => 4,
        _ => throw new InvalidDataException($"a component type of {componentType}")
    };

    private static int ElementSize(string type, int componentType)
    {
        var size = SizeOf(componentType);
        return type switch
        {
            "MAT2" => 2 * Align4(2 * size),
            "MAT3" => 3 * Align4(3 * size),
            "MAT4" => 4 * Align4(4 * size),
            _ => ComponentsOf(type) * size
        };
    }

    private static int Align4(int bytes) => (bytes + 3) & ~3;

    private static void ReadElement(ReadOnlySpan<byte> bytes, int at, GltfAccessor info, int components, Span<float> into)
    {
        var size = SizeOf(info.ComponentType);
        var rows = info.Type switch { "MAT2" => 2, "MAT3" => 3, "MAT4" => 4, _ => components };
        var columnStride = info.Type.StartsWith("MAT", StringComparison.Ordinal) ? Align4(rows * size) : rows * size;
        if (at + (components / rows - 1) * columnStride + rows * size > bytes.Length)
        {
            throw new InvalidDataException("an accessor reads past the end of its buffer view");
        }

        for (int c = 0; c < components; c++)
        {
            var offset = at + c / rows * columnStride + c % rows * size;
            into[c] = info.ComponentType == GltfComponentType.Float
                ? BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(offset, 4))
                : Integer(bytes, offset, info.ComponentType, info.Normalized);
        }
    }

    private static float Integer(ReadOnlySpan<byte> bytes, int at, int componentType, bool normalized)
    {
        var value = ReadInteger(bytes, at, componentType);
        if (!normalized) return value;

        return componentType switch
        {
            GltfComponentType.Byte => Math.Max(value / 127f, -1),
            GltfComponentType.UnsignedByte => value / 255f,
            GltfComponentType.Short => Math.Max(value / 32767f, -1),
            GltfComponentType.UnsignedShort => value / 65535f,
            _ => value / (float)uint.MaxValue
        };
    }

    private static long ReadInteger(ReadOnlySpan<byte> bytes, int at, int componentType) => componentType switch
    {
        GltfComponentType.Byte => (sbyte)bytes[at],
        GltfComponentType.UnsignedByte => bytes[at],
        GltfComponentType.Short => BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(at, 2)),
        GltfComponentType.UnsignedShort => BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(at, 2)),
        GltfComponentType.UnsignedInt => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(at, 4)),
        _ => throw new InvalidDataException($"a component type of {componentType} is no integer")
    };
}

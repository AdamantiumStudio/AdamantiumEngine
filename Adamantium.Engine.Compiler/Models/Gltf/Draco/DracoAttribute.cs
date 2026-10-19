// Ported from Draco (src/draco/attributes/point_attribute.cc).
// Copyright 2016 The Draco Authors. Under the Apache License 2.0; translated to C# and changed, details:
// THIRD-PARTY-NOTICES.md.

using System;
using System.IO;

namespace Adamantium.Engine.Compiler.Models.Gltf.Draco;

/// <summary>One attribute of a decoded Draco mesh: its values and which of them each point takes.</summary>
public sealed class DracoAttribute
{
    internal const byte Position = 0;

    internal DracoAttribute(byte kind, DracoDataType dataType, int components, bool normalized, uint uniqueId)
    {
        Kind = kind;
        DataType = dataType;
        Components = components;
        Normalized = normalized;
        UniqueId = uniqueId;
    }

    /// <summary>The id a glTF primitive's <c>KHR_draco_mesh_compression.attributes</c> names it by.</summary>
    public uint UniqueId { get; }

    /// <summary>The type of each component.</summary>
    public DracoDataType DataType { get; }

    /// <summary>How many components one value has.</summary>
    public int Components { get; }

    /// <summary>Whether integer components stand for fractions of their range.</summary>
    public bool Normalized { get; }

    /// <summary>The size of one component in bytes.</summary>
    public int ComponentSize => SizeOf(DataType);

    internal byte Kind { get; }

    internal byte[] Values { get; set; } = [];

    internal int[] Portable { get; set; }

    internal int[] PointToValue { get; set; }

    /// <summary>The value of each of <paramref name="pointCount"/> points in turn, little-endian; zeros for a point
    /// with no value.</summary>
    public byte[] PointValues(int pointCount)
    {
        var stride = Components * ComponentSize;
        if ((long)pointCount * stride > Array.MaxLength)
        {
            throw new InvalidDataException($"{pointCount} Draco points of {stride} bytes");
        }

        var bytes = new byte[pointCount * stride];
        for (var point = 0; point < pointCount; point++)
        {
            var value = PointToValue?[point] ?? point;
            if (value >= 0 && (value + 1L) * stride <= Values.Length)
            {
                Values.AsSpan(value * stride, stride).CopyTo(bytes.AsSpan(point * stride));
            }
        }

        return bytes;
    }

    internal static int SizeOf(DracoDataType type) => type switch
    {
        DracoDataType.Int8 or DracoDataType.UInt8 or DracoDataType.Bool => 1,
        DracoDataType.Int16 or DracoDataType.UInt16 => 2,
        DracoDataType.Int32 or DracoDataType.UInt32 or DracoDataType.Float32 => 4,
        _ => 8
    };
}

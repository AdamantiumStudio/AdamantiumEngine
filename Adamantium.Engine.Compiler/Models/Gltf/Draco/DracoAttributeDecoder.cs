// Ported from Draco (src/draco/compression/attributes/sequential_*_attribute_decoder.cc,
// src/draco/attributes/attribute_quantization_transform.cc, attribute_octahedron_transform.cc).
// Copyright 2016 The Draco Authors. Under the Apache License 2.0; translated to C# and changed, details:
// THIRD-PARTY-NOTICES.md.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Adamantium.Engine.Compiler.Models.Gltf.Draco;

internal sealed class DracoAttributeDecoder
{
    private const byte Generic = 0;
    private const byte Integer = 1;
    private const byte Quantization = 2;
    private const byte Normals = 3;
    private const sbyte MaxPrediction = 6;

    private readonly byte kind;
    private float[] minValues = [];
    private float range;
    private int quantizationBits;
    private int normalBits;

    public DracoAttributeDecoder(DracoAttribute attribute, byte kind)
    {
        var floats = attribute.DataType == DracoDataType.Float32;
        var valid = kind switch
        {
            Generic or Integer => true,
            Quantization => floats,
            Normals => floats && attribute.Components == 3,
            _ => false
        };
        if (!valid)
        {
            throw new InvalidDataException(
                $"a Draco attribute coding of kind {kind} for {attribute.Components} components of {attribute.DataType}");
        }

        Attribute = attribute;
        this.kind = kind;
    }

    public DracoAttribute Attribute { get; }

    public void DecodeValues(DracoReader reader, int[] points, DracoSequencer sequencer, IReadOnlyList<DracoAttribute> attributes)
    {
        if (kind == Generic)
        {
            Attribute.Values = reader.Bytes(Checked((long)points.Length * Attribute.Components * Attribute.ComponentSize)).ToArray();
            return;
        }

        var method = reader.SByte();
        if (method is < DracoPrediction.None or > MaxPrediction)
        {
            throw new InvalidDataException($"a Draco prediction of kind {method}");
        }

        DracoPrediction prediction = null;
        if (method != DracoPrediction.None)
        {
            var transform = reader.SByte();
            var expected = kind == Normals ? DracoTransform.Octahedron : DracoTransform.Wrap;
            if (transform != expected)
            {
                throw new InvalidDataException($"a Draco attribute of coding {kind} predicted through transform {transform}");
            }

            prediction = new DracoPrediction(method, new DracoTransform(transform), sequencer);
            if (prediction.NeedsPositions)
            {
                prediction.Positions = PositionsOf(attributes);
            }
        }

        var components = kind == Normals ? 2 : Attribute.Components;
        var count = Checked((long)points.Length * components);
        var values = new int[count];
        if (reader.Byte() > 0)
        {
            DracoSymbols.Decode(reader, count, components, MemoryMarshal.Cast<int, uint>(values.AsSpan()));
        }
        else
        {
            ReadPlain(reader, values);
        }

        if (count > 0 && (prediction == null || !prediction.CorrectionsPositive))
        {
            for (var i = 0; i < count; i++)
            {
                var symbol = (uint)values[i];
                values[i] = (symbol & 1) == 0 ? (int)(symbol >> 1) : -(int)(symbol >> 1) - 1;
            }
        }

        if (prediction != null)
        {
            prediction.DecodeData(reader, count);
            if (count > 0)
            {
                prediction.Compute(values, count, components, points);
            }
        }

        Attribute.Portable = values;
    }

    public void DecodeTransformData(DracoReader reader)
    {
        if (kind == Quantization)
        {
            minValues = new float[Attribute.Components];
            for (var c = 0; c < minValues.Length; c++)
            {
                minValues[c] = reader.Single();
            }

            range = reader.Single();
            quantizationBits = reader.Byte();
            if (quantizationBits is < 1 or > 30)
            {
                throw new InvalidDataException($"Draco values quantized to {quantizationBits} bits");
            }
        }
        else if (kind == Normals)
        {
            normalBits = reader.Byte();
        }
    }

    public void Transform()
    {
        switch (kind)
        {
            case Integer:
                StoreIntegers();
                break;
            case Quantization:
                Dequantize();
                break;
            case Normals:
                Unfold();
                break;
        }
    }

    private static void ReadPlain(DracoReader reader, int[] values)
    {
        var size = reader.Byte();
        if (size > 4)
        {
            throw new InvalidDataException($"Draco values of {size} bytes");
        }

        var bytes = reader.Bytes(Checked((long)size * values.Length));
        for (var i = 0; i < values.Length; i++)
        {
            var value = 0u;
            for (var b = 0; b < size; b++)
            {
                value |= (uint)bytes[i * size + b] << (8 * b);
            }

            values[i] = (int)value;
        }
    }

    private static DracoAttribute PositionsOf(IReadOnlyList<DracoAttribute> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.Kind != DracoAttribute.Position)
            {
                continue;
            }

            if (attribute.Portable == null || attribute.Components != 3)
            {
                throw new InvalidDataException("a Draco prediction from positions not decoded as integers of three components");
            }

            return attribute;
        }

        throw new InvalidDataException("a Draco prediction from positions the mesh does not have");
    }

    private void StoreIntegers()
    {
        var values = Attribute.Portable;
        var size = Attribute.ComponentSize;
        var bytes = new byte[Checked((long)values.Length * size)];
        for (var i = 0; i < values.Length; i++)
        {
            var at = bytes.AsSpan(i * size, size);
            switch (Attribute.DataType)
            {
                case DracoDataType.Int8 or DracoDataType.UInt8:
                    at[0] = (byte)values[i];
                    break;
                case DracoDataType.Int16 or DracoDataType.UInt16:
                    BinaryPrimitives.WriteInt16LittleEndian(at, (short)values[i]);
                    break;
                case DracoDataType.Int32 or DracoDataType.UInt32:
                    BinaryPrimitives.WriteInt32LittleEndian(at, values[i]);
                    break;
                default:
                    throw new InvalidDataException($"Draco integers stored as {Attribute.DataType}");
            }
        }

        Attribute.Values = bytes;
    }

    private void Dequantize()
    {
        var values = Attribute.Portable;
        var delta = range / (float)((1u << quantizationBits) - 1);
        var components = Attribute.Components;
        var bytes = new byte[Checked(values.Length * 4L)];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), values[i] * delta + minValues[i % components]);
        }

        Attribute.Values = bytes;
    }

    private void Unfold()
    {
        var normals = new DracoOctahedron(normalBits);
        var values = Attribute.Portable;
        var bytes = new byte[Checked(values.Length / 2 * 12L)];
        Span<float> normal = stackalloc float[3];
        for (var i = 0; i < values.Length / 2; i++)
        {
            normals.ToUnitVector(values[i * 2], values[i * 2 + 1], normal);
            MemoryMarshal.AsBytes(normal).CopyTo(bytes.AsSpan(i * 12));
        }

        Attribute.Values = bytes;
    }

    private static int Checked(long count)
    {
        if (count > Array.MaxLength)
        {
            throw new InvalidDataException($"{count} Draco values");
        }

        return (int)count;
    }
}

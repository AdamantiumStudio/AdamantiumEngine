// Ported from Draco (src/draco/compression/decode.cc, point_cloud/point_cloud_decoder.cc,
// mesh/mesh_sequential_decoder.cc, attributes/attributes_decoder.cc; src/draco/metadata/metadata_decoder.cc).
// Copyright 2016 The Draco Authors. Under the Apache License 2.0; translated to C# and changed, details:
// THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.IO;

namespace Adamantium.Engine.Compiler.Models.Gltf.Draco;

/// <summary>Decodes Draco meshes of bitstream 2.2, the one <c>KHR_draco_mesh_compression</c> names: sequential and
/// edgebreaker connectivity with standard or valence traversal, and every attribute coding and prediction that
/// bitstream writes.</summary>
public static class DracoDecoder
{
    private const byte TriangularMesh = 1;
    private const byte SequentialMethod = 0;
    private const byte EdgebreakerMethod = 1;
    private const ushort MetadataFlag = 0x8000;
    private const byte AttributeKinds = 9;
    private const int MaxMetadataDepth = 1000;

    /// <summary>The mesh <paramref name="data"/> holds, refused before anything is allocated for it if it says it has
    /// more than <paramref name="maxFaces"/> triangles or <paramref name="maxPoints"/> points.</summary>
    /// <exception cref="InvalidDataException">The data is no Draco 2.2 mesh, is broken, or is over the limits.</exception>
    public static DracoMesh Decode(byte[] data, int maxFaces = int.MaxValue, int maxPoints = int.MaxValue)
    {
        try
        {
            return DecodeMesh(new DracoReader(data), maxFaces, maxPoints);
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            throw new InvalidDataException("Draco data is broken", e);
        }
    }

    private static DracoMesh DecodeMesh(DracoReader reader, int maxFaces, int maxPoints)
    {
        if (!reader.Bytes(5).SequenceEqual("DRACO"u8))
        {
            throw new InvalidDataException("the data is no Draco mesh");
        }

        var major = reader.Byte();
        var minor = reader.Byte();
        if (major != 2 || minor != 2)
        {
            throw new InvalidDataException($"a Draco bitstream of version {major}.{minor}, not 2.2");
        }

        if (reader.Byte() != TriangularMesh)
        {
            throw new InvalidDataException("a Draco point cloud, not a mesh");
        }

        var method = reader.Byte();
        if ((reader.UInt16() & MetadataFlag) != 0)
        {
            SkipMetadata(reader);
        }

        int[] faces;
        int points;
        DracoEdgebreaker edgebreaker = null;
        switch (method)
        {
            case SequentialMethod:
                (faces, points) = DecodeSequential(reader, maxFaces, maxPoints);
                break;
            case EdgebreakerMethod:
                edgebreaker = DracoEdgebreaker.Decode(reader, maxFaces);
                faces = edgebreaker.Faces;
                points = edgebreaker.PointCount;
                break;
            default:
                throw new InvalidDataException($"a Draco mesh coding of kind {method}");
        }

        if (points > maxPoints)
        {
            throw new InvalidDataException($"a Draco mesh of {points} points, {maxPoints} at most are expected");
        }

        var sequencers = new DracoSequencer[reader.Byte()];
        for (var i = 0; i < sequencers.Length; i++)
        {
            sequencers[i] = edgebreaker?.CreateSequencer(i) ?? new DracoSequencer(null, null, false);
        }

        var attributes = new List<DracoAttribute>();
        var decoders = new List<DracoAttributeDecoder>[sequencers.Length];
        for (var i = 0; i < decoders.Length; i++)
        {
            decoders[i] = ReadDecoders(reader, attributes);
        }

        for (var i = 0; i < decoders.Length; i++)
        {
            var sequence = sequencers[i].Generate(faces, points);
            var map = sequencers[i].PointToValue(faces, points);
            foreach (var decoder in decoders[i])
            {
                decoder.Attribute.PointToValue = map;
            }

            foreach (var decoder in decoders[i])
            {
                decoder.DecodeValues(reader, sequence, sequencers[i], attributes);
            }

            foreach (var decoder in decoders[i])
            {
                decoder.DecodeTransformData(reader);
            }

            foreach (var decoder in decoders[i])
            {
                decoder.Transform();
            }
        }

        return new DracoMesh(faces, points, attributes);
    }

    private static (int[] Faces, int Points) DecodeSequential(DracoReader reader, int maxFaces, int maxPoints)
    {
        var faces = reader.VarUInt32();
        var points = reader.VarUInt32();
        if (faces > reader.Remaining / 3 || faces > maxFaces || points > maxPoints)
        {
            throw new InvalidDataException($"a Draco mesh of {faces} faces on {points} points");
        }

        var indices = new int[faces * 3];
        if (reader.Byte() == 0)
        {
            var symbols = new uint[indices.Length];
            DracoSymbols.Decode(reader, symbols.Length, 1, symbols);
            var last = 0;
            for (var i = 0; i < indices.Length; i++)
            {
                var difference = (int)(symbols[i] >> 1);
                if ((symbols[i] & 1) != 0)
                {
                    difference = -difference;
                }

                var index = (long)last + difference;
                if (index < 0 || index >= points)
                {
                    throw new InvalidDataException("a Draco index past the points");
                }

                last = (int)index;
                indices[i] = last;
            }

            return (indices, (int)points);
        }

        for (var i = 0; i < indices.Length; i++)
        {
            var index = points switch
            {
                < 256 => reader.Byte(),
                < 1 << 16 => reader.UInt16(),
                < 1 << 21 => reader.VarUInt32(),
                _ => reader.UInt32()
            };
            if (index >= points)
            {
                throw new InvalidDataException("a Draco index past the points");
            }

            indices[i] = (int)index;
        }

        return (indices, (int)points);
    }

    private static List<DracoAttributeDecoder> ReadDecoders(DracoReader reader, List<DracoAttribute> attributes)
    {
        var count = reader.VarUInt32();
        if (count == 0 || count > 5L * reader.Remaining)
        {
            throw new InvalidDataException($"a Draco attribute decoder of {count} attributes");
        }

        var read = new List<DracoAttribute>((int)count);
        for (var i = 0; i < count; i++)
        {
            var kind = reader.Byte();
            var dataType = reader.Byte();
            var components = reader.Byte();
            var normalized = reader.Byte() > 0;
            if (kind >= AttributeKinds || dataType is 0 or > (byte)DracoDataType.Bool || components == 0)
            {
                throw new InvalidDataException(
                    $"a Draco attribute of kind {kind}, {components} components of type {dataType}");
            }

            var attribute = new DracoAttribute(kind, (DracoDataType)dataType, components, normalized, reader.VarUInt32());
            attributes.Add(attribute);
            read.Add(attribute);
        }

        var decoders = new List<DracoAttributeDecoder>(read.Count);
        foreach (var attribute in read)
        {
            decoders.Add(new DracoAttributeDecoder(attribute, reader.Byte()));
        }

        return decoders;
    }

    private static void SkipMetadata(DracoReader reader)
    {
        var attributeMetadata = reader.VarUInt32();
        for (var i = 0; i < attributeMetadata; i++)
        {
            reader.VarUInt32();
            SkipMetadataElement(reader);
        }

        SkipMetadataElement(reader);
    }

    private static void SkipMetadataElement(DracoReader reader)
    {
        var pending = new Stack<(bool Named, int Depth)>();
        pending.Push((false, 0));
        while (pending.Count > 0)
        {
            var (named, depth) = pending.Pop();
            if (named)
            {
                if (depth > MaxMetadataDepth)
                {
                    throw new InvalidDataException("Draco metadata nests too deep");
                }

                reader.Skip(reader.Byte());
            }

            var entries = reader.VarUInt32();
            for (var i = 0; i < entries; i++)
            {
                reader.Skip(reader.Byte());
                var size = reader.VarUInt32();
                if (size == 0)
                {
                    throw new InvalidDataException("an empty Draco metadata entry");
                }

                reader.Skip(size);
            }

            var children = reader.VarUInt32();
            if (children > reader.Remaining)
            {
                throw DracoReader.Truncated();
            }

            for (var i = 0; i < children; i++)
            {
                pending.Push((true, named ? depth + 1 : depth));
            }
        }
    }
}

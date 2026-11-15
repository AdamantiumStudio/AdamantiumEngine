using System.Collections.Generic;
using System.IO;
using Adamantium.Mathematics;
using static Adamantium.Engine.Compiler.Models.U3d.U3dContexts;

namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>Reads the geometry blocks of ECMA-363 9.6: declarations, the base mesh, and point and line set
/// continuations.</summary>
internal static class U3dGeometryReader
{
    private const uint DuplicateFlag = 0x2;

    private static readonly U3dContext PointCount = U3dContext.CreateDynamic("cPointCnt");
    private static readonly U3dContext LineCount = U3dContext.CreateDynamic("cLineCnt");
    private static readonly U3dContext DiffuseDuplicate = U3dContext.CreateDynamic("cDiffDup");
    private static readonly U3dContext SpecularDuplicate = U3dContext.CreateDynamic("cSpecDup");

    /// <summary>A CLOD Mesh, Point Set or Line Set Declaration (9.6.1.1, 9.6.2.1, 9.6.3.1).</summary>
    internal static U3dGeometry ReadDeclaration(U3dBitReader reader, U3dGeometryKind kind)
    {
        var geometry = new U3dGeometry { Kind = kind, Name = reader.ReadString() };
        reader.ReadU32();
        geometry.MeshAttributes = reader.ReadU32();
        for (var count = 0; count < 6; count++)
        {
            reader.ReadU32();
        }

        var shadingCount = reader.ReadU32();
        reader.CheckCount(shadingCount, 96, "shading descriptions");
        for (var index = 0u; index < shadingCount; index++)
        {
            var attributes = reader.ReadU32();
            var layerCount = reader.ReadU32();
            reader.CheckCount(layerCount, 32, "texture layers");
            var dimensions = new uint[layerCount];
            for (var layer = 0; layer < dimensions.Length; layer++)
            {
                dimensions[layer] = reader.ReadU32();
            }

            reader.ReadU32();
            geometry.Shadings.Add(new U3dShadingDescription { Attributes = attributes, TextureCoordinateDimensions = dimensions });
        }

        if (kind == U3dGeometryKind.Mesh)
        {
            geometry.MinimumResolution = reader.ReadU32();
            geometry.FinalMaximumResolution = reader.ReadU32();
        }
        else
        {
            geometry.MeshAttributes = 0;
        }

        for (var factor = 0; factor < 3; factor++)
        {
            reader.ReadU32();
        }

        geometry.PositionInverseQuant = reader.ReadF32();
        geometry.NormalInverseQuant = reader.ReadF32();
        geometry.TextureCoordinateInverseQuant = reader.ReadF32();
        geometry.DiffuseColorInverseQuant = reader.ReadF32();
        geometry.SpecularColorInverseQuant = reader.ReadF32();
        for (var parameter = 0; parameter < 3; parameter++)
        {
            reader.ReadU32();
        }

        ReadSkeleton(reader, geometry);
        return geometry;
    }

    /// <summary>The rest of a CLOD Base Mesh Continuation (9.6.1.2), its name read: the base mesh, unquantized.</summary>
    internal static void ReadBaseMesh(U3dBitReader reader, U3dGeometry geometry)
    {
        reader.ReadU32();
        var faceCount = reader.ReadU32();
        var positionCount = reader.ReadU32();
        var normalCount = reader.ReadU32();
        var diffuseCount = reader.ReadU32();
        var specularCount = reader.ReadU32();
        var textureCount = reader.ReadU32();

        geometry.Positions.Clear();
        geometry.Normals.Clear();
        geometry.DiffuseColors.Clear();
        geometry.SpecularColors.Clear();
        geometry.TextureCoordinates.Clear();
        geometry.Primitives.Clear();

        reader.CheckCount(positionCount, 96, "positions");
        for (var index = 0u; index < positionCount; index++)
        {
            geometry.Positions.Add(ReadVector3(reader));
        }

        reader.CheckCount(normalCount, 96, "normals");
        for (var index = 0u; index < normalCount; index++)
        {
            geometry.Normals.Add(ReadVector3(reader));
        }

        reader.CheckCount(diffuseCount, 128, "diffuse colors");
        for (var index = 0u; index < diffuseCount; index++)
        {
            geometry.DiffuseColors.Add(ReadVector4(reader));
        }

        reader.CheckCount(specularCount, 128, "specular colors");
        for (var index = 0u; index < specularCount; index++)
        {
            geometry.SpecularColors.Add(ReadVector4(reader));
        }

        reader.CheckCount(textureCount, 128, "texture coordinates");
        for (var index = 0u; index < textureCount; index++)
        {
            geometry.TextureCoordinates.Add(ReadVector4(reader));
        }

        reader.CheckCount(faceCount, 1, "faces");
        var positions = U3dContext.CreateStatic(positionCount);
        var normals = U3dContext.CreateStatic(normalCount);
        var diffuse = U3dContext.CreateStatic(diffuseCount);
        var specular = U3dContext.CreateStatic(specularCount);
        var textures = U3dContext.CreateStatic(textureCount);
        for (var face = 0u; face < faceCount; face++)
        {
            var shadingId = ReadShadingId(reader, geometry);
            var description = geometry.Shadings[shadingId];
            var corners = new U3dCorner[3];
            for (var cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
            {
                var corner = new U3dCorner { Position = CheckIndex(reader.ReadCompressedU32(positions), positionCount, "position") };
                if (!geometry.ExcludesNormals)
                {
                    corner.Normal = CheckIndex(reader.ReadCompressedU32(normals), normalCount, "normal");
                }

                if (description.HasDiffuseColors)
                {
                    corner.Diffuse = CheckIndex(reader.ReadCompressedU32(diffuse), diffuseCount, "diffuse color");
                }

                if (description.HasSpecularColors)
                {
                    corner.Specular = CheckIndex(reader.ReadCompressedU32(specular), specularCount, "specular color");
                }

                corner.TextureCoordinates = new int[description.TextureLayerCount];
                for (var layer = 0; layer < corner.TextureCoordinates.Length; layer++)
                {
                    corner.TextureCoordinates[layer] =
                        CheckIndex(reader.ReadCompressedU32(textures), textureCount, "texture coordinate");
                }

                corners[cornerIndex] = corner;
            }

            geometry.Primitives.Add(new U3dPrimitive { ShadingId = shadingId, Corners = corners });
        }
    }

    /// <summary>The rest of a Point Set Continuation (9.6.2.2), its name read.</summary>
    internal static void ReadPointContinuation(U3dBitReader reader, U3dGeometry geometry)
    {
        var count = ReadResolutionRange(reader, geometry);
        for (var step = 0u; step < count; step++)
        {
            var splitCorners = ReadNewPosition(reader, geometry);
            var position = geometry.Positions.Count - 1;
            var normalStart = geometry.Normals.Count;
            var newNormals = ReadNewNormals(reader, geometry, splitCorners, true);

            var pointCount = reader.ReadCompressedU32(PointCount);
            reader.CheckCount(pointCount, 1, "points");
            for (var point = 0u; point < pointCount; point++)
            {
                var shadingId = ReadShadingId(reader, geometry);
                var corner = new U3dCorner { Position = position };
                ReadEnd(reader, geometry, geometry.Shadings[shadingId], corner, splitCorners, normalStart, newNormals);
                geometry.Primitives.Add(new U3dPrimitive { ShadingId = shadingId, Corners = [corner] });
                geometry.CornersAtPosition[position].Add(corner);
            }
        }
    }

    /// <summary>The rest of a Line Set Continuation (9.6.3.2), its name read.</summary>
    internal static void ReadLineContinuation(U3dBitReader reader, U3dGeometry geometry)
    {
        var count = ReadResolutionRange(reader, geometry);
        for (var step = 0u; step < count; step++)
        {
            var splitCorners = ReadNewPosition(reader, geometry);
            var position = geometry.Positions.Count - 1;
            var normalStart = geometry.Normals.Count;
            var newNormals = ReadNewNormals(reader, geometry, splitCorners, false);

            var lineCount = reader.ReadCompressedU32(LineCount);
            reader.CheckCount(lineCount, 1, "lines");
            for (var line = 0u; line < lineCount; line++)
            {
                var shadingId = ReadShadingId(reader, geometry);
                var description = geometry.Shadings[shadingId];
                var first = reader.ReadCompressedU32(U3dContext.CreateStatic(position == 0 ? 1u : (uint)position));
                if (first >= position)
                {
                    throw new InvalidDataException(
                        $"A line of line set {geometry.Name} starts at position {first}, and only {position} come before it");
                }

                var start = new U3dCorner { Position = (int)first };
                var end = new U3dCorner { Position = position };
                ReadEnd(reader, geometry, description, start, splitCorners, normalStart, newNormals);
                ReadEnd(reader, geometry, description, end, splitCorners, normalStart, newNormals);
                geometry.Primitives.Add(new U3dPrimitive { ShadingId = shadingId, Corners = [start, end] });
                geometry.CornersAtPosition[start.Position].Add(start);
                geometry.CornersAtPosition[position].Add(end);
            }
        }
    }

    private static void ReadSkeleton(U3dBitReader reader, U3dGeometry geometry)
    {
        var boneCount = reader.ReadU32();
        reader.CheckCount(boneCount, 512, "bones");
        geometry.BoneCount = boneCount;
        for (var bone = 0u; bone < boneCount; bone++)
        {
            reader.ReadString();
            reader.ReadString();
            var attributes = reader.ReadU32();
            var floats = 1 + 3 + 4 + 6;
            if ((attributes & 0x1) != 0)
            {
                reader.ReadU32();
                floats++;
            }

            if ((attributes & 0x2) != 0)
            {
                floats += 8;
            }

            for (var value = 0; value < floats; value++)
            {
                reader.ReadF32();
            }
        }
    }

    private static uint ReadResolutionRange(U3dBitReader reader, U3dGeometry geometry)
    {
        reader.ReadU32();
        var start = reader.ReadU32();
        var end = reader.ReadU32();
        if (end < start)
        {
            throw new InvalidDataException($"{geometry.Name} gives positions from {start} to {end}");
        }

        if (start != geometry.Positions.Count)
        {
            throw new InvalidDataException(
                $"{geometry.Name} goes on from position {start}, and it has {geometry.Positions.Count} positions");
        }

        reader.CheckCount(end - start, 1, "positions");
        return end - start;
    }

    private static List<U3dCorner> ReadNewPosition(U3dBitReader reader, U3dGeometry geometry)
    {
        var current = geometry.Positions.Count;
        var split = reader.ReadCompressedU32(U3dContext.CreateStatic(current == 0 ? 1u : (uint)current));
        if (current > 0 && split >= current)
        {
            throw new InvalidDataException(
                $"{geometry.Name} splits position {split} off, and it has {current} positions");
        }

        var predicted = current == 0 ? Vector3F.Zero : geometry.Positions[(int)split];
        var signs = reader.ReadCompressedU8(PositionSigns);
        var inverseQuant = geometry.PositionInverseQuant;
        var positionX = Dequantize(predicted.X, signs, 0x1, reader.ReadCompressedU32(PositionX), inverseQuant);
        var positionY = Dequantize(predicted.Y, signs, 0x2, reader.ReadCompressedU32(PositionY), inverseQuant);
        var positionZ = Dequantize(predicted.Z, signs, 0x4, reader.ReadCompressedU32(PositionZ), inverseQuant);
        geometry.Positions.Add(new Vector3F(positionX, positionY, positionZ));
        geometry.CornersAtPosition.Add([]);
        return current == 0 ? [] : geometry.CornersAtPosition[(int)split];
    }

    private static uint ReadNewNormals(U3dBitReader reader, U3dGeometry geometry, List<U3dCorner> splitCorners, bool spherical)
    {
        var count = reader.ReadCompressedU32(NormalCount);
        reader.CheckCount(count, 1, "normals");
        var predicted = PredictNormal(geometry, splitCorners, spherical);
        var inverseQuant = geometry.NormalInverseQuant;
        for (var normal = 0u; normal < count; normal++)
        {
            var signs = reader.ReadCompressedU8(NormalSigns);
            var normalX = Dequantize(predicted.X, signs, 0x1, reader.ReadCompressedU32(NormalX), inverseQuant);
            var normalY = Dequantize(predicted.Y, signs, 0x2, reader.ReadCompressedU32(NormalY), inverseQuant);
            var normalZ = Dequantize(predicted.Z, signs, 0x4, reader.ReadCompressedU32(NormalZ), inverseQuant);
            geometry.Normals.Add(new Vector3F(normalX, normalY, normalZ));
        }

        return count;
    }

    private static void ReadEnd(U3dBitReader reader, U3dGeometry geometry, U3dShadingDescription description,
        U3dCorner corner, List<U3dCorner> splitCorners, int normalStart, uint newNormals)
    {
        var local = reader.ReadCompressedU32(NormalIndex);
        corner.Normal = local < newNormals ? normalStart + (int)local : -1;
        if (description.HasDiffuseColors)
        {
            corner.Diffuse = ReadColor(reader, geometry, true, splitCorners);
        }

        if (description.HasSpecularColors)
        {
            corner.Specular = ReadColor(reader, geometry, false, splitCorners);
        }

        corner.TextureCoordinates = new int[description.TextureLayerCount];
        for (var layer = 0; layer < corner.TextureCoordinates.Length; layer++)
        {
            corner.TextureCoordinates[layer] = ReadTextureCoordinate(reader, geometry, layer, splitCorners);
        }
    }

    private static int ReadColor(U3dBitReader reader, U3dGeometry geometry, bool diffuse, List<U3dCorner> splitCorners)
    {
        var flag = reader.ReadCompressedU8(diffuse ? DiffuseDuplicate : SpecularDuplicate);
        if ((flag & DuplicateFlag) != 0)
        {
            return diffuse ? geometry.LastDiffuse : geometry.LastSpecular;
        }

        var pool = diffuse ? geometry.DiffuseColors : geometry.SpecularColors;
        var sum = Vector4F.Zero;
        var used = 0;
        foreach (var corner in splitCorners)
        {
            var index = diffuse ? corner.Diffuse : corner.Specular;
            if (index >= 0)
            {
                sum += pool[index];
                used++;
            }
        }

        var predicted = used == 0 ? Vector4F.Zero : sum / used;
        var signs = reader.ReadCompressedU8(diffuse ? DiffuseSigns : SpecularSigns);
        var inverseQuant = diffuse ? geometry.DiffuseColorInverseQuant : geometry.SpecularColorInverseQuant;
        var red = Dequantize(predicted.X, signs, 0x1, reader.ReadCompressedU32(ColorRed), inverseQuant);
        var green = Dequantize(predicted.Y, signs, 0x2, reader.ReadCompressedU32(ColorGreen), inverseQuant);
        var blue = Dequantize(predicted.Z, signs, 0x4, reader.ReadCompressedU32(ColorBlue), inverseQuant);
        var alpha = Dequantize(predicted.W, signs, 0x8, reader.ReadCompressedU32(ColorAlpha), inverseQuant);
        pool.Add(new Vector4F(red, green, blue, alpha));
        var added = pool.Count - 1;
        if (diffuse)
        {
            geometry.LastDiffuse = added;
        }
        else
        {
            geometry.LastSpecular = added;
        }

        return added;
    }

    private static int ReadTextureCoordinate(U3dBitReader reader, U3dGeometry geometry, int layer, List<U3dCorner> splitCorners)
    {
        var flag = reader.ReadCompressedU8(TextureDuplicate);
        if ((flag & DuplicateFlag) != 0)
        {
            return geometry.LastTextureCoordinate;
        }

        var sum = Vector4F.Zero;
        var used = 0;
        foreach (var corner in splitCorners)
        {
            if (layer < corner.TextureCoordinates.Length && corner.TextureCoordinates[layer] >= 0)
            {
                sum += geometry.TextureCoordinates[corner.TextureCoordinates[layer]];
                used++;
            }
        }

        var predicted = used == 0 ? Vector4F.Zero : sum / used;
        var signs = reader.ReadCompressedU8(TextureSigns);
        var inverseQuant = geometry.TextureCoordinateInverseQuant;
        var first = Dequantize(predicted.X, signs, 0x1, reader.ReadCompressedU32(TextureU), inverseQuant);
        var second = Dequantize(predicted.Y, signs, 0x2, reader.ReadCompressedU32(TextureV), inverseQuant);
        var third = Dequantize(predicted.Z, signs, 0x4, reader.ReadCompressedU32(TextureS), inverseQuant);
        var fourth = Dequantize(predicted.W, signs, 0x8, reader.ReadCompressedU32(TextureT), inverseQuant);
        geometry.TextureCoordinates.Add(new Vector4F(first, second, third, fourth));
        geometry.LastTextureCoordinate = geometry.TextureCoordinates.Count - 1;
        return geometry.LastTextureCoordinate;
    }

    private static Vector3F PredictNormal(U3dGeometry geometry, List<U3dCorner> splitCorners, bool spherical)
    {
        var sum = Vector3F.Zero;
        var used = 0;
        foreach (var corner in splitCorners)
        {
            if (corner.Normal >= 0)
            {
                sum += geometry.Normals[corner.Normal];
                used++;
            }
        }

        if (used == 0)
        {
            return Vector3F.Zero;
        }

        if (!spherical)
        {
            return sum / used;
        }

        var length = sum.Length();
        return length > 0 ? sum / length : Vector3F.Zero;
    }

    /// <summary>A shading ID, checked against those <paramref name="geometry"/> declares.</summary>
    internal static int ReadShadingId(U3dBitReader reader, U3dGeometry geometry)
    {
        var shadingId = reader.ReadCompressedU32(Shading);
        if (shadingId >= geometry.Shadings.Count)
        {
            throw new InvalidDataException(
                $"{geometry.Name} draws with shading ID {shadingId}, and it declares {geometry.Shadings.Count}");
        }

        return (int)shadingId;
    }

    private static int CheckIndex(uint value, uint count, string what)
    {
        if (value >= count)
        {
            throw new InvalidDataException($"A face names {what} {value}, and the base mesh has {count}");
        }

        return (int)value;
    }

    /// <summary>The value <paramref name="predicted"/> corrected by a quantized difference and its sign bit.</summary>
    internal static float Dequantize(float predicted, byte signs, int signBit, uint difference, float inverseQuant)
    {
        var sign = (signs & signBit) != 0 ? -1f : 1f;
        return predicted + sign * difference * inverseQuant;
    }

    /// <summary>Three F32 values.</summary>
    internal static Vector3F ReadVector3(U3dBitReader reader) => new(reader.ReadF32(), reader.ReadF32(), reader.ReadF32());

    private static Vector4F ReadVector4(U3dBitReader reader) =>
        new(reader.ReadF32(), reader.ReadF32(), reader.ReadF32(), reader.ReadF32());
}

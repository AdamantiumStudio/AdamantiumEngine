using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Mathematics;
using static Adamantium.Engine.Compiler.Models.U3d.U3dContexts;
using static Adamantium.Engine.Compiler.Models.U3d.U3dGeometryReader;

namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>Reads a CLOD Progressive Mesh Continuation (ECMA-363 9.6.1.3): resolution updates that split new positions
/// off the mesh, kept at the full resolution the block gives.</summary>
internal static class U3dProgressiveMeshReader
{
    private const byte LeftOrientation = 0x1;
    private const byte LocalThird = 0x1;
    private const byte Move = 0x1;
    private const byte Change = 0x1;
    private const byte ChangeToNew = 0x1;
    private const byte LocalIndex = 0x2;
    private const int NoPrediction = 0;
    private const int MoveFromThird = 1;
    private const int StayFromThird = 2;
    private const int MoveFromNeighbor = 3;
    private const int StayFromNeighbor = 4;
    private const int DiffuseSlot = 0;
    private const int SpecularSlot = 1;
    private const int FirstTextureSlot = 2;
    private const int SplitCorner = 0;
    private const int NewCorner = 1;
    private const int ThirdCorner = 2;

    private static readonly U3dContext Zero = U3dContext.CreateDynamic("cZero");
    private static readonly U3dContext DiffuseCount = U3dContext.CreateDynamic("cDiffuseCount");
    private static readonly U3dContext SpecularCount = U3dContext.CreateDynamic("cSpecularCount");
    private static readonly U3dContext TextureCount = U3dContext.CreateDynamic("cTexCoordCount");
    private static readonly U3dContext FaceCount = U3dContext.CreateDynamic("cFaceCnt");
    private static readonly U3dContext FaceOrientation = U3dContext.CreateDynamic("cFaceOrnt");
    private static readonly U3dContext ThirdPositionType = U3dContext.CreateDynamic("cThrdPosType");
    private static readonly U3dContext LocalThirdPosition = U3dContext.CreateDynamic("cLocal3rdPos");
    private static readonly U3dContext[] StayOrMove =
    [
        U3dContext.CreateDynamic("cStayMove0"), U3dContext.CreateDynamic("cStayMove1"), U3dContext.CreateDynamic("cStayMove2"),
        U3dContext.CreateDynamic("cStayMove3"), U3dContext.CreateDynamic("cStayMove4")
    ];
    private static readonly U3dContext ColorDuplicate = U3dContext.CreateDynamic("cColorDup");
    private static readonly U3dContext ColorIndexType = U3dContext.CreateDynamic("cColorIndexType");
    private static readonly U3dContext ColorIndexLocal = U3dContext.CreateDynamic("cColorIndexLocal");
    private static readonly U3dContext ColorIndexGlobal = U3dContext.CreateDynamic("cColorIndexGlobal");
    private static readonly U3dContext TextureIndexType = U3dContext.CreateDynamic("cTexCIndexType");
    private static readonly U3dContext TextureIndexLocal = U3dContext.CreateDynamic("cTextureIndexLocal");
    private static readonly U3dContext TextureIndexGlobal = U3dContext.CreateDynamic("cTextureIndexGlobal");
    private static readonly U3dChangeContexts DiffuseChange = new("cDiffuseKeepChange", "cDiffuseChangeType",
        "cDiffuseChangeIndexNew", "cDiffuseChangeIndexLocal", "cDiffuseChangeIndexGlobal");
    private static readonly U3dChangeContexts SpecularChange = new("cSpecularKeepChange", "cSpecularChangeType",
        "cSpecularChangeIndexNew", "cSpecularChangeIndexLocal", "cSpecularChangeIndexGlobal");
    private static readonly U3dChangeContexts TextureChange = new("cTCKeepChange", "cTCChangeType",
        "cTCChangeIndexNew", "cTCChangeIndexLocal", "cTCChangeIndexGlobal");

    /// <summary>The rest of the block, its name read.</summary>
    internal static void Read(U3dBitReader reader, U3dGeometry geometry)
    {
        reader.ReadU32();
        var start = reader.ReadU32();
        var end = reader.ReadU32();
        if (end < start || start != geometry.Positions.Count)
        {
            throw new InvalidDataException(
                $"Mesh {geometry.Name} updates positions {start} to {end}, and it has {geometry.Positions.Count}");
        }

        reader.CheckCount(end - start, 1, "resolution updates");
        while (geometry.FacesAtPosition.Count < geometry.Positions.Count)
        {
            geometry.FacesAtPosition.Add([]);
        }

        if (geometry.FacesAtPosition.All(faces => faces.Count == 0))
        {
            for (var face = 0; face < geometry.Primitives.Count; face++)
            {
                foreach (var position in geometry.Primitives[face].Corners.Select(corner => corner.Position).Distinct())
                {
                    geometry.FacesAtPosition[position].Add(face);
                }
            }
        }

        for (var update = start; update < end; update++)
        {
            ReadUpdate(reader, geometry);
        }
    }

    private static void ReadUpdate(U3dBitReader reader, U3dGeometry geometry)
    {
        var current = geometry.Positions.Count;
        var split = current == 0 ? reader.ReadCompressedU32(Zero) : reader.ReadCompressedU32(U3dContext.CreateStatic((uint)current));
        if (current > 0 && split >= current)
        {
            throw new InvalidDataException($"Mesh {geometry.Name} splits position {split} off, and it has {current}");
        }

        var splitPosition = current == 0 ? -1 : (int)split;
        var splitFaces = current == 0 ? [] : geometry.FacesAtPosition[splitPosition].OrderByDescending(face => face).ToList();
        var splitDiffuse = GetUsedAt(geometry, splitFaces, splitPosition, DiffuseSlot);
        var splitSpecular = GetUsedAt(geometry, splitFaces, splitPosition, SpecularSlot);
        var newDiffuse = ReadNewColors(reader, geometry, true, splitDiffuse);
        var newSpecular = ReadNewColors(reader, geometry, false, splitSpecular);
        var newTextures = ReadNewTextureCoordinates(reader, geometry, GetUsedAt(geometry, splitFaces, splitPosition, FirstTextureSlot));

        var newFaceCount = reader.ReadCompressedU32(FaceCount);
        reader.CheckCount(newFaceCount, 1, "new faces");
        var localPositions = splitFaces.SelectMany(face => geometry.Primitives[face].Corners)
            .Select(corner => corner.Position).Where(position => position != splitPosition).Distinct()
            .OrderByDescending(position => position).ToList();
        List<U3dNewFace> newFaces = [];
        for (var face = 0u; face < newFaceCount; face++)
        {
            var shadingId = ReadShadingId(reader, geometry);
            var orientation = reader.ReadCompressedU8(FaceOrientation);
            var type = reader.ReadCompressedU8(ThirdPositionType);
            int third;
            if (type == LocalThird)
            {
                var local = localPositions.Concat(newFaces.Select(added => added.Third)).Distinct()
                    .OrderByDescending(position => position).ToList();
                third = Pick(local, reader.ReadCompressedU32(LocalThirdPosition), geometry, "position");
            }
            else
            {
                var global = reader.ReadCompressedU32(U3dContext.CreateStatic(current == 0 ? 1u : (uint)current));
                if (global >= current)
                {
                    throw new InvalidDataException($"A new face of mesh {geometry.Name} uses position {global} of {current}");
                }

                third = (int)global;
            }

            newFaces.Add(new U3dNewFace { ShadingId = shadingId, IsLeft = orientation == LeftOrientation, Third = third });
        }

        var moved = ReadStayOrMove(reader, geometry, splitFaces, splitPosition, newFaces);
        List<int[]> movedAttributes = [];
        var splitTexturesOfLayer = new Dictionary<int, List<int>>();
        foreach (var face in moved)
        {
            var primitive = geometry.Primitives[face];
            var description = geometry.Shadings[primitive.ShadingId];
            var changes = new int[FirstTextureSlot + description.TextureLayerCount];
            Array.Fill(changes, -1);
            if (description.HasDiffuseColors)
            {
                changes[DiffuseSlot] = ReadChange(reader, DiffuseChange, newDiffuse, splitDiffuse, geometry);
            }

            if (description.HasSpecularColors)
            {
                changes[SpecularSlot] = ReadChange(reader, SpecularChange, newSpecular, splitSpecular, geometry);
            }

            for (var layer = 0; layer < description.TextureLayerCount; layer++)
            {
                if (!splitTexturesOfLayer.TryGetValue(layer, out var used))
                {
                    used = GetUsedAt(geometry, splitFaces, splitPosition, FirstTextureSlot + layer);
                    splitTexturesOfLayer.Add(layer, used);
                }

                changes[FirstTextureSlot + layer] = ReadChange(reader, TextureChange, newTextures, used, geometry);
            }

            movedAttributes.Add(changes);
        }

        List<U3dCorner[]> newCorners = [];
        foreach (var newFace in newFaces)
        {
            newCorners.Add(ReadNewFaceInfo(reader, geometry, newFace, splitFaces, splitPosition));
        }

        var predicted = splitPosition < 0 ? Vector3F.Zero : geometry.Positions[splitPosition];
        var signs = reader.ReadCompressedU8(PositionSigns);
        var inverseQuant = geometry.PositionInverseQuant;
        geometry.Positions.Add(new Vector3F(
            Dequantize(predicted.X, signs, 0x1, reader.ReadCompressedU32(PositionX), inverseQuant),
            Dequantize(predicted.Y, signs, 0x2, reader.ReadCompressedU32(PositionY), inverseQuant),
            Dequantize(predicted.Z, signs, 0x4, reader.ReadCompressedU32(PositionZ), inverseQuant)));
        geometry.FacesAtPosition.Add([]);

        Apply(geometry, current, splitPosition, moved, movedAttributes, newFaces, newCorners);
        if (!geometry.ExcludesNormals)
        {
            ReadNormals(reader, geometry, current);
        }
    }

    private static void Apply(U3dGeometry geometry, int newPosition, int splitPosition, List<int> moved,
        List<int[]> movedAttributes, List<U3dNewFace> newFaces, List<U3dCorner[]> newCorners)
    {
        for (var index = 0; index < moved.Count; index++)
        {
            var face = moved[index];
            var corner = geometry.Primitives[face].Corners.First(corner => corner.Position == splitPosition);
            corner.Position = newPosition;
            var changes = movedAttributes[index];
            if (changes[DiffuseSlot] >= 0)
            {
                corner.Diffuse = changes[DiffuseSlot];
            }

            if (changes[SpecularSlot] >= 0)
            {
                corner.Specular = changes[SpecularSlot];
            }

            for (var layer = 0; layer + FirstTextureSlot < changes.Length && layer < corner.TextureCoordinates.Length; layer++)
            {
                if (changes[FirstTextureSlot + layer] >= 0)
                {
                    corner.TextureCoordinates[layer] = changes[FirstTextureSlot + layer];
                }
            }

            geometry.FacesAtPosition[splitPosition].Remove(face);
            geometry.FacesAtPosition[newPosition].Add(face);
        }

        for (var index = 0; index < newFaces.Count; index++)
        {
            var newFace = newFaces[index];
            var corners = newCorners[index];
            corners[SplitCorner].Position = splitPosition < 0 ? newPosition : splitPosition;
            corners[NewCorner].Position = newPosition;
            corners[ThirdCorner].Position = newFace.Third;
            U3dCorner[] ordered = newFace.IsLeft
                ? [corners[SplitCorner], corners[NewCorner], corners[ThirdCorner]]
                : [corners[NewCorner], corners[SplitCorner], corners[ThirdCorner]];
            var faceIndex = geometry.Primitives.Count;
            geometry.Primitives.Add(new U3dPrimitive { ShadingId = newFace.ShadingId, Corners = ordered });
            foreach (var position in ordered.Select(corner => corner.Position).Distinct())
            {
                geometry.FacesAtPosition[position].Add(faceIndex);
            }
        }
    }

    private static int[] PredictFromThirds(U3dGeometry geometry, List<int> splitFaces, int splitPosition,
        List<U3dNewFace> newFaces)
    {
        var predictions = new int[splitFaces.Count];
        for (var index = 0; index < splitFaces.Count; index++)
        {
            var corners = geometry.Primitives[splitFaces[index]].Corners;
            var splitAt = Array.FindIndex(corners, corner => corner.Position == splitPosition);
            if (splitAt < 0)
            {
                continue;
            }

            var next = FindByThird(newFaces, corners[(splitAt + 1) % 3].Position);
            if (next != null)
            {
                predictions[index] = next.IsLeft ? StayFromThird : MoveFromThird;
                continue;
            }

            var previous = FindByThird(newFaces, corners[(splitAt + 2) % 3].Position);
            if (previous != null)
            {
                predictions[index] = previous.IsLeft ? MoveFromThird : StayFromThird;
            }
        }

        return predictions;
    }

    private static U3dNewFace FindByThird(List<U3dNewFace> newFaces, int position) =>
        newFaces.FirstOrDefault(newFace => newFace.Third == position && !newFace.IsLeft) ??
        newFaces.FirstOrDefault(newFace => newFace.Third == position);

    private static List<int> ReadStayOrMove(U3dBitReader reader, U3dGeometry geometry, List<int> splitFaces,
        int splitPosition, List<U3dNewFace> newFaces)
    {
        var predictions = PredictFromThirds(geometry, splitFaces, splitPosition, newFaces);
        var moves = new bool[splitFaces.Count];
        List<int> moved = [];
        for (var index = 0; index < splitFaces.Count; index++)
        {
            var prediction = predictions[index];
            if (prediction == NoPrediction)
            {
                var neighborStays = false;
                var neighborMoves = false;
                var corners = geometry.Primitives[splitFaces[index]].Corners;
                for (var earlier = 0; earlier < index; earlier++)
                {
                    if (HasSharedEdge(corners, geometry.Primitives[splitFaces[earlier]].Corners))
                    {
                        neighborMoves |= moves[earlier];
                        neighborStays |= !moves[earlier];
                    }
                }

                if (neighborMoves)
                {
                    prediction = MoveFromNeighbor;
                }
                else if (neighborStays)
                {
                    prediction = StayFromNeighbor;
                }
            }

            moves[index] = reader.ReadCompressedU8(StayOrMove[prediction]) == Move;
            if (moves[index])
            {
                moved.Add(splitFaces[index]);
            }
        }

        return moved;
    }

    private static bool HasSharedEdge(U3dCorner[] first, U3dCorner[] second)
    {
        var shared = 0;
        foreach (var corner in first)
        {
            if (second.Any(other => other.Position == corner.Position))
            {
                shared++;
            }
        }

        return shared >= 2;
    }

    private static U3dCorner[] ReadNewFaceInfo(U3dBitReader reader, U3dGeometry geometry, U3dNewFace newFace,
        List<int> splitFaces, int splitPosition)
    {
        var description = geometry.Shadings[newFace.ShadingId];
        U3dCorner[] corners = [new U3dCorner(), new U3dCorner(), new U3dCorner()];
        foreach (var corner in corners)
        {
            corner.TextureCoordinates = Enumerable.Repeat(-1, description.TextureLayerCount).ToArray();
        }

        var thirdFaces = geometry.FacesAtPosition[newFace.Third];
        if (description.HasDiffuseColors)
        {
            var picked = ReadCornerIndices(reader, geometry, DiffuseSlot, ColorDuplicate, ColorIndexType, ColorIndexLocal,
                ColorIndexGlobal, GetUsedAt(geometry, splitFaces, splitPosition, DiffuseSlot),
                GetUsedAt(geometry, thirdFaces, newFace.Third, DiffuseSlot));
            for (var corner = 0; corner < 3; corner++)
            {
                corners[corner].Diffuse = picked[corner];
            }
        }

        if (description.HasSpecularColors)
        {
            var picked = ReadCornerIndices(reader, geometry, SpecularSlot, ColorDuplicate, ColorIndexType, ColorIndexLocal,
                ColorIndexGlobal, GetUsedAt(geometry, splitFaces, splitPosition, SpecularSlot),
                GetUsedAt(geometry, thirdFaces, newFace.Third, SpecularSlot));
            for (var corner = 0; corner < 3; corner++)
            {
                corners[corner].Specular = picked[corner];
            }
        }

        for (var layer = 0; layer < description.TextureLayerCount; layer++)
        {
            var slot = FirstTextureSlot + layer;
            var picked = ReadCornerIndices(reader, geometry, slot, TextureDuplicate, TextureIndexType, TextureIndexLocal,
                TextureIndexGlobal, GetUsedAt(geometry, splitFaces, splitPosition, slot),
                GetUsedAt(geometry, thirdFaces, newFace.Third, slot));
            for (var corner = 0; corner < 3; corner++)
            {
                corners[corner].TextureCoordinates[layer] = picked[corner];
            }
        }

        return corners;
    }

    private static int[] ReadCornerIndices(U3dBitReader reader, U3dGeometry geometry, int slot, U3dContext duplicate,
        U3dContext type, U3dContext local, U3dContext global, List<int> atSplit, List<int> atThird)
    {
        if (!geometry.PreviousNewFaceAttributes.TryGetValue(slot, out var previous))
        {
            previous = [-1, -1, -1];
            geometry.PreviousNewFaceAttributes.Add(slot, previous);
        }

        var flags = reader.ReadCompressedU8(duplicate);
        var picked = new int[3];
        for (var corner = 0; corner < 3; corner++)
        {
            if ((flags & (1 << corner)) != 0)
            {
                picked[corner] = previous[corner];
                continue;
            }

            var indexType = reader.ReadCompressedU8(type);
            picked[corner] = indexType == LocalIndex
                ? Pick(corner == ThirdCorner ? atThird : atSplit, reader.ReadCompressedU32(local), geometry, "attribute")
                : (int)reader.ReadCompressedU32(global);
        }

        picked.CopyTo(previous, 0);
        return picked;
    }

    private static int ReadChange(U3dBitReader reader, U3dChangeContexts contexts, List<int> added, List<int> atSplit,
        U3dGeometry geometry)
    {
        if (reader.ReadCompressedU8(contexts.KeepChange) != Change)
        {
            return -1;
        }

        var type = reader.ReadCompressedU8(contexts.ChangeType);
        return type switch
        {
            ChangeToNew => Pick(added, reader.ReadCompressedU32(contexts.IndexNew), geometry, "new attribute"),
            LocalIndex => Pick(atSplit, reader.ReadCompressedU32(contexts.IndexLocal), geometry, "attribute"),
            _ => (int)reader.ReadCompressedU32(contexts.IndexGlobal)
        };
    }

    private static List<int> ReadNewColors(U3dBitReader reader, U3dGeometry geometry, bool diffuse, List<int> atSplit)
    {
        var count = reader.ReadCompressedU16(diffuse ? DiffuseCount : SpecularCount);
        reader.CheckCount(count, 1, "new colors");
        var pool = diffuse ? geometry.DiffuseColors : geometry.SpecularColors;
        var predicted = GetAverage(pool, atSplit);
        var inverseQuant = diffuse ? geometry.DiffuseColorInverseQuant : geometry.SpecularColorInverseQuant;
        List<int> added = [];
        for (var color = 0; color < count; color++)
        {
            var signs = reader.ReadCompressedU8(diffuse ? DiffuseSigns : SpecularSigns);
            pool.Add(new Vector4F(
                Dequantize(predicted.X, signs, 0x1, reader.ReadCompressedU32(ColorRed), inverseQuant),
                Dequantize(predicted.Y, signs, 0x2, reader.ReadCompressedU32(ColorGreen), inverseQuant),
                Dequantize(predicted.Z, signs, 0x4, reader.ReadCompressedU32(ColorBlue), inverseQuant),
                Dequantize(predicted.W, signs, 0x8, reader.ReadCompressedU32(ColorAlpha), inverseQuant)));
            added.Add(pool.Count - 1);
        }

        return added;
    }

    private static List<int> ReadNewTextureCoordinates(U3dBitReader reader, U3dGeometry geometry, List<int> atSplit)
    {
        var count = reader.ReadCompressedU16(TextureCount);
        reader.CheckCount(count, 1, "new texture coordinates");
        var predicted = GetAverage(geometry.TextureCoordinates, atSplit);
        var inverseQuant = geometry.TextureCoordinateInverseQuant;
        List<int> added = [];
        for (var coordinate = 0; coordinate < count; coordinate++)
        {
            var signs = reader.ReadCompressedU8(TextureSigns);
            geometry.TextureCoordinates.Add(new Vector4F(
                Dequantize(predicted.X, signs, 0x1, reader.ReadCompressedU32(TextureU), inverseQuant),
                Dequantize(predicted.Y, signs, 0x2, reader.ReadCompressedU32(TextureV), inverseQuant),
                Dequantize(predicted.Z, signs, 0x4, reader.ReadCompressedU32(TextureS), inverseQuant),
                Dequantize(predicted.W, signs, 0x8, reader.ReadCompressedU32(TextureT), inverseQuant)));
            added.Add(geometry.TextureCoordinates.Count - 1);
        }

        return added;
    }

    private static void ReadNormals(U3dBitReader reader, U3dGeometry geometry, int newPosition)
    {
        var neighborhood = geometry.FacesAtPosition[newPosition]
            .SelectMany(face => geometry.Primitives[face].Corners).Select(corner => corner.Position)
            .Append(newPosition).Distinct().OrderByDescending(position => position).ToList();
        var inverseQuant = geometry.NormalInverseQuant;
        foreach (var position in neighborhood)
        {
            var faces = geometry.FacesAtPosition[position];
            var count = reader.ReadCompressedU32(NormalCount);
            reader.CheckCount(count, 1, "normals");
            var predictions = PredictNormals(geometry, faces, (int)count);
            var first = geometry.Normals.Count;
            for (var normal = 0; normal < count; normal++)
            {
                var predicted = normal < predictions.Count ? predictions[normal] : Vector3F.Zero;
                var signs = reader.ReadCompressedU8(NormalSigns);
                geometry.Normals.Add(new Vector3F(
                    Dequantize(predicted.X, signs, 0x1, reader.ReadCompressedU32(NormalX), inverseQuant),
                    Dequantize(predicted.Y, signs, 0x2, reader.ReadCompressedU32(NormalY), inverseQuant),
                    Dequantize(predicted.Z, signs, 0x4, reader.ReadCompressedU32(NormalZ), inverseQuant)));
            }

            foreach (var face in faces)
            {
                var local = reader.ReadCompressedU32(NormalIndex);
                var corner = geometry.Primitives[face].Corners.First(corner => corner.Position == position);
                corner.Normal = local < count ? first + (int)local : -1;
            }
        }
    }

    private static List<Vector3F> PredictNormals(U3dGeometry geometry, List<int> faces, int count)
    {
        List<Vector3F> normals = [];
        List<int> weights = [];
        foreach (var face in faces)
        {
            var corners = geometry.Primitives[face].Corners;
            var first = geometry.Positions[corners[0].Position];
            var normal = Vector3F.Cross(geometry.Positions[corners[1].Position] - first,
                geometry.Positions[corners[2].Position] - first);
            var length = normal.Length();
            normals.Add(length > 0 ? normal / length : Vector3F.Zero);
            weights.Add(1);
        }

        while (normals.Count > Math.Max(count, 1))
        {
            var bestFirst = 0;
            var bestSecond = 1;
            var bestDot = float.MinValue;
            for (var first = 0; first < normals.Count; first++)
            {
                for (var second = first + 1; second < normals.Count; second++)
                {
                    var dot = Vector3F.Dot(normals[first], normals[second]);
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        bestFirst = first;
                        bestSecond = second;
                    }
                }
            }

            var merged = normals[bestFirst] * weights[bestFirst] + normals[bestSecond] * weights[bestSecond];
            var length = merged.Length();
            normals[bestFirst] = length > 0 ? merged / length : normals[bestFirst];
            weights[bestFirst] += weights[bestSecond];
            normals.RemoveAt(bestSecond);
            weights.RemoveAt(bestSecond);
        }

        return normals;
    }

    private static List<int> GetUsedAt(U3dGeometry geometry, IEnumerable<int> faces, int position, int slot)
    {
        var used = new SortedSet<int>(Comparer<int>.Create((left, right) => right.CompareTo(left)));
        foreach (var face in faces)
        {
            foreach (var corner in geometry.Primitives[face].Corners)
            {
                if (corner.Position != position)
                {
                    continue;
                }

                var index = slot switch
                {
                    DiffuseSlot => corner.Diffuse,
                    SpecularSlot => corner.Specular,
                    _ => slot - FirstTextureSlot < corner.TextureCoordinates.Length
                        ? corner.TextureCoordinates[slot - FirstTextureSlot]
                        : -1
                };
                if (index >= 0)
                {
                    used.Add(index);
                }
            }
        }

        return used.ToList();
    }

    private static Vector4F GetAverage(List<Vector4F> pool, List<int> indices)
    {
        if (indices.Count == 0)
        {
            return Vector4F.Zero;
        }

        var sum = Vector4F.Zero;
        foreach (var index in indices)
        {
            sum += pool[index];
        }

        return sum / indices.Count;
    }

    private static int Pick(List<int> list, uint index, U3dGeometry geometry, string what)
    {
        if (index >= list.Count)
        {
            throw new InvalidDataException(
                $"A resolution update of mesh {geometry.Name} picks {what} {index} of a local list of {list.Count}");
        }

        return list[(int)index];
    }
}

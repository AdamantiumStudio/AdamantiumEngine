using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>Reads a CLOD Progressive Mesh Continuation (ECMA-363 9.6.1.3): resolution updates, each adding one position
/// to the author mesh by splitting it off a position there is - new colours and texture coordinates, new faces between
/// the two, the faces that move from the split position to the new one, the new position, and the normals of every
/// position around it. The mesh is kept at the full resolution the block gives.
/// Which faces the stay-or-move flags are read for, in which order and in which context is what keeps the reading in
/// step with the writer, and the clause leaves part of it to be read between the lines. As read here: the faces at the
/// split position from the highest index down. A face whose corner after the split position is the third position of
/// a new face is predicted by that new face - the right-wound one when both windings have it - to stay when it winds
/// left, to move when it winds right; failing that, by the new face whose third position is the corner before the
/// split, the other way round. That is the reverse of the clause's wording, which would predict every such face wrong,
/// and it is what MeshLab's files - Intel's U3D library - hold, a torus's double-sided first updates among them. Any
/// other face is predicted from the faces before it that share an edge with it: to move when any of them moved, to
/// stay when they all stayed. The local list of third positions holds those of the faces at the split position and of
/// the new faces before, the split position left out, highest first. Predicted normals and colours only shift values,
/// never the reading.</summary>
internal static class U3dProgressiveMeshReader
{
    private static readonly U3dContext Zero = U3dContext.Dynamic("cZero");
    private static readonly U3dContext Shading = U3dContext.Dynamic("cShading");
    private static readonly U3dContext DiffuseCount = U3dContext.Dynamic("cDiffuseCount");
    private static readonly U3dContext SpecularCount = U3dContext.Dynamic("cSpecularCount");
    private static readonly U3dContext TextureCount = U3dContext.Dynamic("cTexCoordCount");
    private static readonly U3dContext DiffuseSigns = U3dContext.Dynamic("cDiffuseColorSign");
    private static readonly U3dContext SpecularSigns = U3dContext.Dynamic("cSpecularColorSign");
    private static readonly U3dContext TextureSigns = U3dContext.Dynamic("cTexCoordSign");
    private static readonly U3dContext ColorRed = U3dContext.Dynamic("cColorDiffR");
    private static readonly U3dContext ColorGreen = U3dContext.Dynamic("cColorDiffG");
    private static readonly U3dContext ColorBlue = U3dContext.Dynamic("cColorDiffB");
    private static readonly U3dContext ColorAlpha = U3dContext.Dynamic("cColorDiffA");
    private static readonly U3dContext TextureU = U3dContext.Dynamic("cTexCDiffU");
    private static readonly U3dContext TextureV = U3dContext.Dynamic("cTexCDiffV");
    private static readonly U3dContext TextureS = U3dContext.Dynamic("cTexCDiffS");
    private static readonly U3dContext TextureT = U3dContext.Dynamic("cTexCDiffT");
    private static readonly U3dContext FaceCount = U3dContext.Dynamic("cFaceCnt");
    private static readonly U3dContext FaceOrientation = U3dContext.Dynamic("cFaceOrnt");
    private static readonly U3dContext ThirdPositionType = U3dContext.Dynamic("cThrdPosType");
    private static readonly U3dContext LocalThirdPosition = U3dContext.Dynamic("cLocal3rdPos");
    private static readonly U3dContext[] StayOrMove =
    [
        U3dContext.Dynamic("cStayMove0"), U3dContext.Dynamic("cStayMove1"), U3dContext.Dynamic("cStayMove2"),
        U3dContext.Dynamic("cStayMove3"), U3dContext.Dynamic("cStayMove4")
    ];
    private static readonly U3dContext PositionSigns = U3dContext.Dynamic("cPosDiffSign");
    private static readonly U3dContext PositionX = U3dContext.Dynamic("cPosDiffX");
    private static readonly U3dContext PositionY = U3dContext.Dynamic("cPosDiffY");
    private static readonly U3dContext PositionZ = U3dContext.Dynamic("cPosDiffZ");
    private static readonly U3dContext NormalCount = U3dContext.Dynamic("cNormlCnt");
    private static readonly U3dContext NormalSigns = U3dContext.Dynamic("cDiffNormalSign");
    private static readonly U3dContext NormalX = U3dContext.Dynamic("cDiffNormalX");
    private static readonly U3dContext NormalY = U3dContext.Dynamic("cDiffNormalY");
    private static readonly U3dContext NormalZ = U3dContext.Dynamic("cDiffNormalZ");
    private static readonly U3dContext NormalIndex = U3dContext.Dynamic("cNormlIdx");
    private static readonly U3dContext ColorDuplicate = U3dContext.Dynamic("cColorDup");
    private static readonly U3dContext ColorIndexType = U3dContext.Dynamic("cColorIndexType");
    private static readonly U3dContext ColorIndexLocal = U3dContext.Dynamic("cColorIndexLocal");
    private static readonly U3dContext ColorIndexGlobal = U3dContext.Dynamic("cColorIndexGlobal");
    private static readonly U3dContext TextureDuplicate = U3dContext.Dynamic("cTexCDup");
    private static readonly U3dContext TextureIndexType = U3dContext.Dynamic("cTexCIndexType");
    private static readonly U3dContext TextureIndexLocal = U3dContext.Dynamic("cTextureIndexLocal");
    private static readonly U3dContext TextureIndexGlobal = U3dContext.Dynamic("cTextureIndexGlobal");
    private static readonly U3dChangeContexts DiffuseChange = new("cDiffuseKeepChange", "cDiffuseChangeType",
        "cDiffuseChangeIndexNew", "cDiffuseChangeIndexLocal", "cDiffuseChangeIndexGlobal");
    private static readonly U3dChangeContexts SpecularChange = new("cSpecularKeepChange", "cSpecularChangeType",
        "cSpecularChangeIndexNew", "cSpecularChangeIndexLocal", "cSpecularChangeIndexGlobal");
    private static readonly U3dChangeContexts TextureChange = new("cTCKeepChange", "cTCChangeType",
        "cTCChangeIndexNew", "cTCChangeIndexLocal", "cTCChangeIndexGlobal");

    private const byte LeftOrientation = 0x1;
    private const byte LocalThird = 0x1;
    private const byte Move = 0x1;
    private const byte Change = 0x1;
    private const byte ChangeToNew = 0x1;
    private const byte LocalIndex = 0x2;
    private const int NoPrediction = 0;
    private const int MoveFromThird = 1;
    private const int StayFromThird = 2;
    private const int MoveFromNeighbour = 3;
    private const int StayFromNeighbour = 4;
    private const int DiffuseSlot = 0;
    private const int SpecularSlot = 1;
    private const int FirstTextureSlot = 2;
    private const int SplitCorner = 0;
    private const int NewCorner = 1;
    private const int ThirdCorner = 2;

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
        var split = current == 0 ? reader.ReadCompressedU32(Zero) : reader.ReadCompressedU32(U3dContext.Static((uint)current));
        if (current > 0 && split >= current)
        {
            throw new InvalidDataException($"Mesh {geometry.Name} splits position {split} off, and it has {current}");
        }

        var splitPosition = current == 0 ? -1 : (int)split;
        var splitFaces = current == 0 ? [] : geometry.FacesAtPosition[splitPosition].OrderByDescending(face => face).ToList();
        var splitDiffuse = UsedAt(geometry, splitFaces, splitPosition, DiffuseSlot);
        var splitSpecular = UsedAt(geometry, splitFaces, splitPosition, SpecularSlot);
        var newDiffuse = ReadNewColors(reader, geometry, true, splitDiffuse);
        var newSpecular = ReadNewColors(reader, geometry, false, splitSpecular);
        var newTextures = ReadNewTextureCoordinates(reader, geometry, UsedAt(geometry, splitFaces, splitPosition, FirstTextureSlot));

        var newFaceCount = reader.ReadCompressedU32(FaceCount);
        reader.CheckCount(newFaceCount, 1, "new faces");
        var localPositions = splitFaces.SelectMany(face => geometry.Primitives[face].Corners)
            .Select(corner => corner.Position).Where(position => position != splitPosition).Distinct()
            .OrderByDescending(position => position).ToList();
        var newFaces = new List<U3dNewFace>();
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
                var global = reader.ReadCompressedU32(U3dContext.Static(current == 0 ? 1u : (uint)current));
                if (global >= current)
                {
                    throw new InvalidDataException($"A new face of mesh {geometry.Name} uses position {global} of {current}");
                }

                third = (int)global;
            }

            newFaces.Add(new U3dNewFace { ShadingId = shadingId, Left = orientation == LeftOrientation, Third = third });
        }

        var moved = ReadStayOrMove(reader, geometry, splitFaces, splitPosition, newFaces);
        var movedAttributes = new List<int[]>();
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
                var used = UsedAt(geometry, splitFaces, splitPosition, FirstTextureSlot + layer);
                changes[FirstTextureSlot + layer] = ReadChange(reader, TextureChange, newTextures, used, geometry);
            }

            movedAttributes.Add(changes);
        }

        var newCorners = new List<U3dCorner[]>();
        foreach (var newFace in newFaces)
        {
            newCorners.Add(ReadNewFaceInfo(reader, geometry, newFace, splitFaces, splitPosition));
        }

        var predicted = splitPosition < 0 ? Vector3F.Zero : geometry.Positions[splitPosition];
        var signs = reader.ReadCompressedU8(PositionSigns);
        var iq = geometry.PositionInverseQuant;
        geometry.Positions.Add(new Vector3F(
            InverseQuant(predicted.X, signs, 0x1, reader.ReadCompressedU32(PositionX), iq),
            InverseQuant(predicted.Y, signs, 0x2, reader.ReadCompressedU32(PositionY), iq),
            InverseQuant(predicted.Z, signs, 0x4, reader.ReadCompressedU32(PositionZ), iq)));
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
            var ordered = newFace.Left
                ? new[] { corners[SplitCorner], corners[NewCorner], corners[ThirdCorner] }
                : new[] { corners[NewCorner], corners[SplitCorner], corners[ThirdCorner] };
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

            var next = ThirdAt(newFaces, corners[(splitAt + 1) % 3].Position);
            if (next != null)
            {
                predictions[index] = next.Left ? StayFromThird : MoveFromThird;
                continue;
            }

            var previous = ThirdAt(newFaces, corners[(splitAt + 2) % 3].Position);
            if (previous != null)
            {
                predictions[index] = previous.Left ? MoveFromThird : StayFromThird;
            }
        }

        return predictions;
    }

    private static U3dNewFace ThirdAt(List<U3dNewFace> newFaces, int position) =>
        newFaces.FirstOrDefault(newFace => newFace.Third == position && !newFace.Left) ??
        newFaces.FirstOrDefault(newFace => newFace.Third == position);

    private static List<int> ReadStayOrMove(U3dBitReader reader, U3dGeometry geometry, List<int> splitFaces,
        int splitPosition, List<U3dNewFace> newFaces)
    {
        var predictions = PredictFromThirds(geometry, splitFaces, splitPosition, newFaces);
        var moves = new bool[splitFaces.Count];
        var moved = new List<int>();
        for (var index = 0; index < splitFaces.Count; index++)
        {
            var prediction = predictions[index];
            if (prediction == NoPrediction)
            {
                var neighbourStays = false;
                var neighbourMoves = false;
                var corners = geometry.Primitives[splitFaces[index]].Corners;
                for (var earlier = 0; earlier < index; earlier++)
                {
                    if (SharesEdge(corners, geometry.Primitives[splitFaces[earlier]].Corners))
                    {
                        neighbourMoves |= moves[earlier];
                        neighbourStays |= !moves[earlier];
                    }
                }

                if (neighbourMoves)
                {
                    prediction = MoveFromNeighbour;
                }
                else if (neighbourStays)
                {
                    prediction = StayFromNeighbour;
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

    private static bool SharesEdge(U3dCorner[] first, U3dCorner[] second)
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
        var corners = new[] { new U3dCorner(), new U3dCorner(), new U3dCorner() };
        foreach (var corner in corners)
        {
            corner.TextureCoordinates = Enumerable.Repeat(-1, description.TextureLayerCount).ToArray();
        }

        var thirdFaces = geometry.FacesAtPosition[newFace.Third];
        if (description.HasDiffuseColors)
        {
            var picked = ReadCornerIndices(reader, geometry, DiffuseSlot, ColorDuplicate, ColorIndexType, ColorIndexLocal,
                ColorIndexGlobal, UsedAt(geometry, splitFaces, splitPosition, DiffuseSlot),
                UsedAt(geometry, thirdFaces, newFace.Third, DiffuseSlot));
            for (var corner = 0; corner < 3; corner++)
            {
                corners[corner].Diffuse = picked[corner];
            }
        }

        if (description.HasSpecularColors)
        {
            var picked = ReadCornerIndices(reader, geometry, SpecularSlot, ColorDuplicate, ColorIndexType, ColorIndexLocal,
                ColorIndexGlobal, UsedAt(geometry, splitFaces, splitPosition, SpecularSlot),
                UsedAt(geometry, thirdFaces, newFace.Third, SpecularSlot));
            for (var corner = 0; corner < 3; corner++)
            {
                corners[corner].Specular = picked[corner];
            }
        }

        for (var layer = 0; layer < description.TextureLayerCount; layer++)
        {
            var slot = FirstTextureSlot + layer;
            var picked = ReadCornerIndices(reader, geometry, slot, TextureDuplicate, TextureIndexType, TextureIndexLocal,
                TextureIndexGlobal, UsedAt(geometry, splitFaces, splitPosition, slot),
                UsedAt(geometry, thirdFaces, newFace.Third, slot));
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
        reader.CheckCount(count, 1, "new colours");
        var pool = diffuse ? geometry.DiffuseColors : geometry.SpecularColors;
        var predicted = Average(pool, atSplit);
        var iq = diffuse ? geometry.DiffuseColorInverseQuant : geometry.SpecularColorInverseQuant;
        var added = new List<int>();
        for (var color = 0; color < count; color++)
        {
            var signs = reader.ReadCompressedU8(diffuse ? DiffuseSigns : SpecularSigns);
            pool.Add(new Vector4F(
                InverseQuant(predicted.X, signs, 0x1, reader.ReadCompressedU32(ColorRed), iq),
                InverseQuant(predicted.Y, signs, 0x2, reader.ReadCompressedU32(ColorGreen), iq),
                InverseQuant(predicted.Z, signs, 0x4, reader.ReadCompressedU32(ColorBlue), iq),
                InverseQuant(predicted.W, signs, 0x8, reader.ReadCompressedU32(ColorAlpha), iq)));
            added.Add(pool.Count - 1);
        }

        return added;
    }

    private static List<int> ReadNewTextureCoordinates(U3dBitReader reader, U3dGeometry geometry, List<int> atSplit)
    {
        var count = reader.ReadCompressedU16(TextureCount);
        reader.CheckCount(count, 1, "new texture coordinates");
        var predicted = Average(geometry.TextureCoordinates, atSplit);
        var iq = geometry.TextureCoordinateInverseQuant;
        var added = new List<int>();
        for (var coordinate = 0; coordinate < count; coordinate++)
        {
            var signs = reader.ReadCompressedU8(TextureSigns);
            geometry.TextureCoordinates.Add(new Vector4F(
                InverseQuant(predicted.X, signs, 0x1, reader.ReadCompressedU32(TextureU), iq),
                InverseQuant(predicted.Y, signs, 0x2, reader.ReadCompressedU32(TextureV), iq),
                InverseQuant(predicted.Z, signs, 0x4, reader.ReadCompressedU32(TextureS), iq),
                InverseQuant(predicted.W, signs, 0x8, reader.ReadCompressedU32(TextureT), iq)));
            added.Add(geometry.TextureCoordinates.Count - 1);
        }

        return added;
    }

    private static void ReadNormals(U3dBitReader reader, U3dGeometry geometry, int newPosition)
    {
        var neighbourhood = geometry.FacesAtPosition[newPosition]
            .SelectMany(face => geometry.Primitives[face].Corners).Select(corner => corner.Position)
            .Append(newPosition).Distinct().OrderByDescending(position => position).ToList();
        var iq = geometry.NormalInverseQuant;
        foreach (var position in neighbourhood)
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
                    InverseQuant(predicted.X, signs, 0x1, reader.ReadCompressedU32(NormalX), iq),
                    InverseQuant(predicted.Y, signs, 0x2, reader.ReadCompressedU32(NormalY), iq),
                    InverseQuant(predicted.Z, signs, 0x4, reader.ReadCompressedU32(NormalZ), iq)));
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
        var normals = new List<Vector3F>();
        var weights = new List<int>();
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

    private static List<int> UsedAt(U3dGeometry geometry, IEnumerable<int> faces, int position, int slot)
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

    private static Vector4F Average(List<Vector4F> pool, List<int> indices)
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

    private static int ReadShadingId(U3dBitReader reader, U3dGeometry geometry)
    {
        var shadingId = reader.ReadCompressedU32(Shading);
        if (shadingId >= geometry.Shadings.Count)
        {
            throw new InvalidDataException(
                $"Mesh {geometry.Name} draws with shading ID {shadingId}, and it declares {geometry.Shadings.Count}");
        }

        return (int)shadingId;
    }

    private static float InverseQuant(float predicted, byte signs, int signBit, uint difference, float inverseQuant)
    {
        var sign = (signs & signBit) != 0 ? -1f : 1f;
        return predicted + sign * difference * inverseQuant;
    }
}

/// <summary>A face a resolution update adds: its shading ID, its winding and its third position.</summary>
internal sealed class U3dNewFace
{
    internal int ShadingId { get; init; }

    /// <summary>Split, new, third when left; new, split, third when right.</summary>
    internal bool Left { get; init; }

    internal int Third { get; init; }
}

/// <summary>The contexts a moved face's attribute change is read in.</summary>
internal sealed class U3dChangeContexts
{
    internal U3dChangeContexts(string keepChange, string changeType, string indexNew, string indexLocal, string indexGlobal)
    {
        KeepChange = U3dContext.Dynamic(keepChange);
        ChangeType = U3dContext.Dynamic(changeType);
        IndexNew = U3dContext.Dynamic(indexNew);
        IndexLocal = U3dContext.Dynamic(indexLocal);
        IndexGlobal = U3dContext.Dynamic(indexGlobal);
    }

    internal U3dContext KeepChange { get; }

    internal U3dContext ChangeType { get; }

    internal U3dContext IndexNew { get; }

    internal U3dContext IndexLocal { get; }

    internal U3dContext IndexGlobal { get; }
}

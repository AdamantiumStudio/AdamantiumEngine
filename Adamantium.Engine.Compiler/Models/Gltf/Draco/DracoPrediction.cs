// Ported from Draco (src/draco/compression/attributes/prediction_schemes/: delta, parallelogram, constrained
// multi-parallelogram, tex coords portable and geometric normal decoders and predictors).
// Copyright 2016 The Draco Authors. Under the Apache License 2.0; translated to C# and changed, details:
// THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.IO;

namespace Adamantium.Engine.Compiler.Models.Gltf.Draco;

internal sealed class DracoPrediction
{
    public const sbyte None = -2;
    private const sbyte Undefined = -1;
    private const sbyte Difference = 0;
    private const sbyte Parallelogram = 1;
    private const sbyte ConstrainedMultiParallelogram = 4;
    private const sbyte TexCoordsPortable = 5;
    private const sbyte GeometricNormal = 6;
    private const int MaxParallelograms = 4;
    private const long NormalBound = 1 << 29;

    private readonly sbyte method;
    private readonly DracoTransform transform;
    private readonly DracoCorners corners;
    private readonly DracoEncodingData encoding;
    private readonly List<bool>[] creases = [[], [], [], []];
    private readonly List<bool> orientations = [];
    private readonly DracoBitDecoder flips = new();
    private int[] pointIds = [];

    public DracoPrediction(sbyte requested, DracoTransform transform, DracoSequencer sequencer)
    {
        this.transform = transform;
        corners = sequencer.Corners;
        encoding = sequencer.Encoding;
        method = Choose(requested, transform, corners != null);
    }

    public bool NeedsPositions => method is TexCoordsPortable or GeometricNormal;

    public bool CorrectionsPositive => transform.CorrectionsPositive;

    public DracoAttribute Positions { get; set; }

    public void DecodeData(DracoReader reader, int maxValues)
    {
        switch (method)
        {
            case ConstrainedMultiParallelogram:
                foreach (var flags in creases)
                {
                    var count = reader.VarUInt32();
                    if (count > corners.CornerCount)
                    {
                        throw new InvalidDataException("more Draco crease flags than corners");
                    }

                    if (count > 0)
                    {
                        var bits = new DracoBitDecoder();
                        bits.Start(reader);
                        for (var i = 0; i < count; i++)
                        {
                            flags.Add(bits.Next());
                        }
                    }
                }

                transform.DecodeData(reader);
                break;
            case TexCoordsPortable:
                var orientationCount = reader.Int32();
                if (orientationCount < 0 || orientationCount > maxValues)
                {
                    throw new InvalidDataException($"{orientationCount} Draco texture orientations");
                }

                var last = true;
                var orientationBits = new DracoBitDecoder();
                orientationBits.Start(reader);
                for (var i = 0; i < orientationCount; i++)
                {
                    if (!orientationBits.Next())
                    {
                        last = !last;
                    }

                    orientations.Add(last);
                }

                transform.DecodeData(reader);
                break;
            case GeometricNormal:
                transform.DecodeData(reader);
                flips.Start(reader);
                break;
            default:
                transform.DecodeData(reader);
                break;
        }
    }

    public void Compute(int[] data, int size, int components, int[] points)
    {
        pointIds = points;
        transform.Init(components);
        if (method != Difference && (long)encoding.ValueToCorner.Count * components > size)
        {
            throw new InvalidDataException("more Draco values predicted than decoded");
        }

        switch (method)
        {
            case Parallelogram:
                ComputeParallelogram(data, components);
                break;
            case ConstrainedMultiParallelogram:
                ComputeConstrained(data, components);
                break;
            case TexCoordsPortable:
                ComputeTexCoords(data, components);
                break;
            case GeometricNormal:
                ComputeNormals(data, components);
                break;
            default:
                ComputeDifference(data, size, components);
                break;
        }
    }

    private static sbyte Choose(sbyte requested, DracoTransform transform, bool onMesh)
    {
        if (!onMesh || requested is Undefined or Difference)
        {
            return Difference;
        }

        if (transform.IsOctahedron)
        {
            return requested == GeometricNormal ? GeometricNormal : Difference;
        }

        if (requested is Parallelogram or ConstrainedMultiParallelogram or TexCoordsPortable)
        {
            return requested;
        }

        throw new InvalidDataException($"a Draco prediction of kind {requested} is not read");
    }

    private void ComputeDifference(int[] data, int size, int components)
    {
        Span<int> zero = stackalloc int[components];
        zero.Clear();
        transform.Original(zero, data.AsSpan(0, components), data.AsSpan(0, components));
        for (var i = components; i + components <= size; i += components)
        {
            transform.Original(data.AsSpan(i - components, components), data.AsSpan(i, components), data.AsSpan(i, components));
        }
    }

    private void ComputeParallelogram(int[] data, int components)
    {
        var predicted = new int[components];
        transform.Original(predicted, data.AsSpan(0, components), data.AsSpan(0, components));
        for (var p = 1; p < encoding.ValueToCorner.Count; p++)
        {
            var at = data.AsSpan(p * components, components);
            if (PredictParallelogram(p, encoding.ValueToCorner[p], data, components, predicted))
            {
                transform.Original(predicted, at, at);
            }
            else
            {
                transform.Original(data.AsSpan((p - 1) * components, components), at, at);
            }
        }
    }

    private void ComputeConstrained(int[] data, int components)
    {
        var predictions = new int[MaxParallelograms][];
        for (var i = 0; i < MaxParallelograms; i++)
        {
            predictions[i] = new int[components];
        }

        transform.Original(predictions[0], data.AsSpan(0, components), data.AsSpan(0, components));
        var creaseAt = new int[MaxParallelograms];
        var multi = new int[components];
        for (var p = 1; p < encoding.ValueToCorner.Count; p++)
        {
            var count = Parallelograms(p, data, components, predictions);
            var used = 0;
            if (count > 0)
            {
                Array.Clear(multi);
                var flags = creases[count - 1];
                for (var i = 0; i < count; i++)
                {
                    var at = creaseAt[count - 1]++;
                    if (at >= flags.Count)
                    {
                        throw new InvalidDataException("Draco crease flags run out");
                    }

                    if (flags[at])
                    {
                        continue;
                    }

                    used++;
                    for (var c = 0; c < components; c++)
                    {
                        multi[c] = (int)((uint)multi[c] + (uint)predictions[i][c]);
                    }
                }
            }

            var target = data.AsSpan(p * components, components);
            if (used == 0)
            {
                transform.Original(data.AsSpan((p - 1) * components, components), target, target);
                continue;
            }

            for (var c = 0; c < components; c++)
            {
                multi[c] /= used;
            }

            transform.Original(multi, target, target);
        }
    }

    private int Parallelograms(int dataId, int[] data, int components, int[][] predictions)
    {
        var start = encoding.ValueToCorner[dataId];
        var corner = start;
        var count = 0;
        var firstPass = true;
        while (corner != DracoCorners.Invalid)
        {
            if (PredictParallelogram(dataId, corner, data, components, predictions[count]) && ++count == MaxParallelograms)
            {
                break;
            }

            corner = firstPass ? corners.SwingLeft(corner) : corners.SwingRight(corner);
            if (corner == start)
            {
                break;
            }

            if (corner == DracoCorners.Invalid && firstPass)
            {
                firstPass = false;
                corner = corners.SwingRight(start);
            }
        }

        return count;
    }

    private bool PredictParallelogram(int dataId, int corner, int[] data, int components, Span<int> predicted)
    {
        var opposite = corners.Opposite(corner);
        if (opposite == DracoCorners.Invalid)
        {
            return false;
        }

        var across = DataOf(opposite);
        var next = DataOf(DracoCorners.Next(opposite));
        var previous = DataOf(DracoCorners.Previous(opposite));
        if (across >= dataId || next >= dataId || previous >= dataId)
        {
            return false;
        }

        for (var c = 0; c < components; c++)
        {
            predicted[c] = (int)((long)data[next * components + c] + data[previous * components + c] - data[across * components + c]);
        }

        return true;
    }

    private void ComputeTexCoords(int[] data, int components)
    {
        if (components != 2)
        {
            throw new InvalidDataException($"Draco texture coordinates of {components} components");
        }

        Span<int> predicted = stackalloc int[2];
        for (var p = 0; p < encoding.ValueToCorner.Count; p++)
        {
            PredictTexCoord(encoding.ValueToCorner[p], data, p, predicted);
            var at = data.AsSpan(p * 2, 2);
            transform.Original(predicted, at, at);
        }
    }

    private void PredictTexCoord(int corner, int[] data, int dataId, Span<int> predicted)
    {
        var next = DataOf(DracoCorners.Next(corner));
        var previous = DataOf(DracoCorners.Previous(corner));
        if (previous < dataId && next < dataId)
        {
            long nu = data[next * 2];
            long nv = data[next * 2 + 1];
            long pu = data[previous * 2];
            long pv = data[previous * 2 + 1];
            if (pu == nu && pv == nv)
            {
                predicted[0] = (int)pu;
                predicted[1] = (int)pv;
                return;
            }

            var tip = Position(dataId);
            var nextPosition = Position(next);
            var pn = Subtract(Position(previous), nextPosition);
            var pnNorm = (ulong)Dot(pn, pn);
            if (pnNorm != 0)
            {
                var cnDotPn = Dot(pn, Subtract(tip, nextPosition));
                if (cnDotPn == long.MinValue)
                {
                    throw new InvalidDataException("Draco texture coordinates overflow their prediction");
                }

                var pnU = pu - nu;
                var pnV = pv - nv;
                var nMax = Math.Max(Math.Abs(nu), Math.Abs(nv));
                var pnUvMax = Math.Max(Math.Abs(pnU), Math.Abs(pnV));
                var pnMax = Math.Max(Math.Max(Math.Abs(pn.X), Math.Abs(pn.Y)), Math.Abs(pn.Z));
                if ((ulong)nMax > (ulong)long.MaxValue / pnNorm || Math.Abs(cnDotPn) > long.MaxValue / pnUvMax
                    || Math.Abs(cnDotPn) > long.MaxValue / pnMax)
                {
                    throw new InvalidDataException("Draco texture coordinates overflow their prediction");
                }

                var xU = (long)((ulong)nu * pnNorm + (ulong)pnU * (ulong)cnDotPn);
                var xV = (long)((ulong)nv * pnNorm + (ulong)pnV * (ulong)cnDotPn);
                var divisor = (long)pnNorm;
                var x = (X: nextPosition.X + cnDotPn * pn.X / divisor,
                    Y: nextPosition.Y + cnDotPn * pn.Y / divisor,
                    Z: nextPosition.Z + cnDotPn * pn.Z / divisor);
                var cx = Subtract(tip, x);
                var length = (long)IntSqrt((ulong)Dot(cx, cx) * pnNorm);
                var cxU = pnV * length;
                var cxV = -pnU * length;
                if (orientations.Count == 0)
                {
                    throw new InvalidDataException("Draco texture orientations run out");
                }

                var orientation = orientations[^1];
                orientations.RemoveAt(orientations.Count - 1);
                var u = orientation ? (long)((ulong)xU + (ulong)cxU) : (long)((ulong)xU - (ulong)cxU);
                var v = orientation ? (long)((ulong)xV + (ulong)cxV) : (long)((ulong)xV - (ulong)cxV);
                predicted[0] = (int)(u / divisor);
                predicted[1] = (int)(v / divisor);
                return;
            }
        }

        var from = 0;
        if (previous < dataId)
        {
            from = previous * 2;
        }

        if (next < dataId)
        {
            from = next * 2;
        }
        else if (dataId > 0)
        {
            from = (dataId - 1) * 2;
        }
        else
        {
            predicted.Clear();
            return;
        }

        predicted[0] = data[from];
        predicted[1] = data[from + 1];
    }

    private void ComputeNormals(int[] data, int components)
    {
        if (components != 2)
        {
            throw new InvalidDataException($"Draco octahedral normals of {components} components");
        }

        var normals = transform.Normals;
        Span<int> normal = stackalloc int[3];
        Span<int> predicted = stackalloc int[2];
        for (var dataId = 0; dataId < encoding.ValueToCorner.Count; dataId++)
        {
            PredictNormal(encoding.ValueToCorner[dataId], normal);
            normals.CanonicalizeVector(normal);
            if (flips.Next())
            {
                normal[0] = -normal[0];
                normal[1] = -normal[1];
                normal[2] = -normal[2];
            }

            (predicted[0], predicted[1]) = normals.ToQuantized(normal[0], normal[1], normal[2]);
            var at = data.AsSpan(dataId * 2, 2);
            transform.Original(predicted, at, at);
        }
    }

    private void PredictNormal(int corner, Span<int> normal)
    {
        var center = PositionOfCorner(corner);
        ulong x = 0;
        ulong y = 0;
        ulong z = 0;
        foreach (var around in corners.CornersAround(corner))
        {
            var next = Subtract(PositionOfCorner(DracoCorners.Next(around)), center);
            var previous = Subtract(PositionOfCorner(DracoCorners.Previous(around)), center);
            x += (ulong)next.Y * (ulong)previous.Z - (ulong)next.Z * (ulong)previous.Y;
            y += (ulong)next.Z * (ulong)previous.X - (ulong)next.X * (ulong)previous.Z;
            z += (ulong)next.X * (ulong)previous.Y - (ulong)next.Y * (ulong)previous.X;
        }

        long[] sum = [(long)x, (long)y, (long)z];
        var absolute = 0L;
        foreach (var component in sum)
        {
            var size = component == long.MinValue ? long.MaxValue : Math.Abs(component);
            if (absolute > long.MaxValue - size)
            {
                absolute = long.MaxValue;
                break;
            }

            absolute += size;
        }

        var quotient = absolute > NormalBound ? absolute / NormalBound : 1;
        for (var i = 0; i < 3; i++)
        {
            normal[i] = (int)(sum[i] / quotient);
        }
    }

    private int DataOf(int corner) => encoding.VertexToValue[corners.Vertex(corner)];

    private (long X, long Y, long Z) PositionOfCorner(int corner) => Position(DataOf(corner));

    private (long X, long Y, long Z) Position(int dataId)
    {
        var point = pointIds[dataId];
        var value = Positions.PointToValue?[point] ?? point;
        var values = Positions.Portable;
        return (values[value * 3], values[value * 3 + 1], values[value * 3 + 2]);
    }

    private static (long X, long Y, long Z) Subtract((long X, long Y, long Z) a, (long X, long Y, long Z) b) =>
        (a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static long Dot((long X, long Y, long Z) a, (long X, long Y, long Z) b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static ulong IntSqrt(ulong number)
    {
        if (number == 0)
        {
            return 0;
        }

        var root = 1ul;
        for (var rest = number; rest >= 2; rest /= 4)
        {
            root *= 2;
        }

        do
        {
            root = (root + number / root) / 2;
        }
        while (root * root > number);

        return root;
    }
}

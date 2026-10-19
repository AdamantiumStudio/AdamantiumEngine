// Ported from Draco (src/draco/compression/attributes/prediction_schemes/prediction_scheme_wrap_*.h,
// prediction_scheme_normal_octahedron_canonicalized_*.h).
// Copyright 2016 The Draco Authors. Under the Apache License 2.0; translated to C# and changed, details:
// THIRD-PARTY-NOTICES.md.

using System;
using System.IO;
using System.Numerics;

namespace Adamantium.Engine.Compiler.Models.Gltf.Draco;

internal sealed class DracoTransform
{
    public const sbyte Wrap = 1;
    public const sbyte Octahedron = 3;

    private readonly sbyte kind;
    private int components;
    private int min;
    private int max;
    private int range;

    public DracoTransform(sbyte kind)
    {
        if (kind is not (Wrap or Octahedron))
        {
            throw new InvalidDataException($"a Draco prediction transform of kind {kind}");
        }

        this.kind = kind;
    }

    public bool IsOctahedron => kind == Octahedron;

    public bool CorrectionsPositive => kind == Octahedron;

    public DracoOctahedron Normals { get; private set; }

    public void Init(int count) => components = count;

    public void DecodeData(DracoReader reader)
    {
        if (kind == Octahedron)
        {
            var maxQuantized = reader.Int32();
            reader.Int32();
            if (maxQuantized % 2 == 0)
            {
                throw new InvalidDataException($"Draco octahedral normals up to an even {maxQuantized}");
            }

            Normals = new DracoOctahedron(BitOperations.Log2((uint)maxQuantized) + 1);
            return;
        }

        min = reader.Int32();
        max = reader.Int32();
        var difference = (long)max - min;
        if (difference < 0 || difference >= int.MaxValue)
        {
            throw new InvalidDataException($"Draco values wrap between {min} and {max}");
        }

        range = (int)difference + 1;
    }

    public void Original(ReadOnlySpan<int> predicted, ReadOnlySpan<int> corrections, Span<int> output)
    {
        if (kind == Octahedron)
        {
            OriginalNormal(predicted, corrections, output);
            return;
        }

        for (var i = 0; i < components; i++)
        {
            var value = (long)Math.Clamp(predicted[i], min, max) + corrections[i];
            if (value > max)
            {
                value -= range;
            }
            else if (value < min)
            {
                value += range;
            }

            output[i] = (int)value;
        }
    }

    private void OriginalNormal(ReadOnlySpan<int> predicted, ReadOnlySpan<int> corrections, Span<int> output)
    {
        var center = Normals.Center;
        var s = predicted[0] - center;
        var t = predicted[1] - center;
        var inDiamond = Normals.IsInDiamond(s, t);
        if (!inDiamond)
        {
            Normals.InvertDiamond(ref s, ref t);
        }

        var bottomLeft = (s == 0 && t == 0) || (s < 0 && t <= 0);
        var rotation = Rotation(s, t);
        if (!bottomLeft)
        {
            (s, t) = Rotate(s, t, rotation);
        }

        var os = Normals.ModMax((int)((uint)s + (uint)corrections[0]));
        var ot = Normals.ModMax((int)((uint)t + (uint)corrections[1]));
        if (!bottomLeft)
        {
            (os, ot) = Rotate(os, ot, (4 - rotation) % 4);
        }

        if (!inDiamond)
        {
            Normals.InvertDiamond(ref os, ref ot);
        }

        output[0] = os + center;
        output[1] = ot + center;
    }

    private static int Rotation(int s, int t)
    {
        if (s == 0)
        {
            return t == 0 ? 0 : t > 0 ? 3 : 1;
        }

        if (s > 0)
        {
            return t >= 0 ? 2 : 1;
        }

        return t <= 0 ? 0 : 3;
    }

    private static (int S, int T) Rotate(int s, int t, int rotation) => rotation switch
    {
        1 => (t, -s),
        2 => (-s, -t),
        3 => (-t, s),
        _ => (s, t)
    };
}

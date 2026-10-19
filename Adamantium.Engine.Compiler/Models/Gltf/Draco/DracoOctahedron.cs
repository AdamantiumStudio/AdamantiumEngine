// Ported from Draco (src/draco/compression/attributes/normal_compression_utils.h).
// Copyright 2016 The Draco Authors. Under the Apache License 2.0; translated to C# and changed, details:
// THIRD-PARTY-NOTICES.md.

using System;
using System.IO;

namespace Adamantium.Engine.Compiler.Models.Gltf.Draco;

internal sealed class DracoOctahedron
{
    private readonly float scale;

    public DracoOctahedron(int bits)
    {
        if (bits is < 2 or > 30)
        {
            throw new InvalidDataException($"Draco octahedral normals of {bits} bits");
        }

        MaxQuantized = (1 << bits) - 1;
        MaxValue = MaxQuantized - 1;
        Center = MaxValue / 2;
        scale = 2f / MaxValue;
    }

    public int MaxQuantized { get; }

    public int MaxValue { get; }

    public int Center { get; }

    public (int S, int T) Canonicalize(int s, int t)
    {
        if ((s == 0 && t == 0) || (s == 0 && t == MaxValue) || (s == MaxValue && t == 0))
        {
            return (MaxValue, MaxValue);
        }

        if (s == 0 && t > Center)
        {
            return (s, Center - (t - Center));
        }

        if (s == MaxValue && t < Center)
        {
            return (s, Center + (Center - t));
        }

        if (t == MaxValue && s < Center)
        {
            return (Center + (Center - s), t);
        }

        if (t == 0 && s > Center)
        {
            return (Center - (s - Center), t);
        }

        return (s, t);
    }

    public (int S, int T) ToQuantized(int x, int y, int z)
    {
        int s;
        int t;
        if (x >= 0)
        {
            s = y + Center;
            t = z + Center;
        }
        else
        {
            s = y < 0 ? Math.Abs(z) : MaxValue - Math.Abs(z);
            t = z < 0 ? Math.Abs(y) : MaxValue - Math.Abs(y);
        }

        return Canonicalize(s, t);
    }

    public void CanonicalizeVector(Span<int> vector)
    {
        var sum = (long)Math.Abs(vector[0]) + Math.Abs(vector[1]) + Math.Abs(vector[2]);
        if (sum == 0)
        {
            vector[0] = Center;
            return;
        }

        vector[0] = (int)((long)vector[0] * Center / sum);
        vector[1] = (int)((long)vector[1] * Center / sum);
        var rest = Center - Math.Abs(vector[0]) - Math.Abs(vector[1]);
        vector[2] = vector[2] >= 0 ? rest : -rest;
    }

    public void ToUnitVector(int s, int t, Span<float> vector)
    {
        var y = s * scale - 1f;
        var z = t * scale - 1f;
        var x = 1f - MathF.Abs(y) - MathF.Abs(z);
        var offset = Math.Max(-x, 0f);
        y += y < 0 ? offset : -offset;
        z += z < 0 ? offset : -offset;
        var squared = x * x + y * y + z * z;
        if (squared < 1e-6f)
        {
            vector.Clear();
            return;
        }

        var d = 1f / MathF.Sqrt(squared);
        vector[0] = x * d;
        vector[1] = y * d;
        vector[2] = z * d;
    }

    public bool IsInDiamond(int s, int t) => (uint)Math.Abs(s) + (uint)Math.Abs(t) <= (uint)Center;

    public void InvertDiamond(ref int s, ref int t)
    {
        int signS;
        int signT;
        if (s >= 0 && t >= 0)
        {
            signS = 1;
            signT = 1;
        }
        else if (s <= 0 && t <= 0)
        {
            signS = -1;
            signT = -1;
        }
        else
        {
            signS = s > 0 ? 1 : -1;
            signT = t > 0 ? 1 : -1;
        }

        var cornerS = (uint)(signS * Center);
        var cornerT = (uint)(signT * Center);
        var us = (uint)s;
        var ut = (uint)t;
        us = us + us - cornerS;
        ut = ut + ut - cornerT;
        if (signS * signT >= 0)
        {
            var temp = us;
            us = 0u - ut;
            ut = 0u - temp;
        }
        else
        {
            (us, ut) = (ut, us);
        }

        us += cornerS;
        ut += cornerT;
        s = (int)us / 2;
        t = (int)ut / 2;
    }

    public int ModMax(int x)
    {
        if (x > Center)
        {
            return x - MaxQuantized;
        }

        return x < -Center ? x + MaxQuantized : x;
    }
}

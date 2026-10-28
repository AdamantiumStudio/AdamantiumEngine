using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;
using MessagePack;

namespace Adamantium.Graphics.Core.Models;

/// <summary>Reads and writes mesh vertex arrays as flat, lossless byte blobs; a narrower form is used only when it is exact
/// for the data. Byte order is the machine's (little-endian).</summary>
internal static class MeshBlobs
{
    public static void WriteSingles(ref MessagePackWriter writer, float[] values)
    {
        if (values == null || values.Length == 0)
        {
            writer.WriteNil();
            return;
        }

        writer.Write(MemoryMarshal.AsBytes<float>(values));
    }

    public static float[] ReadSingles(ref MessagePackReader reader)
    {
        if (reader.TryReadNil()) return null;

        var bytes = reader.ReadBytes();
        if (bytes == null) return null;

        return Reinterpret<float>(bytes.Value.ToArray());
    }

    /// <summary>Positions live in <see cref="Vector3"/>, which is double, but a model's coordinates come from a
    /// float source and stay exactly representable. Written as float when that holds for every one of them -
    /// half the bytes for the largest array in the file - and as double the moment it does not.</summary>
    public static void WritePoints(ref MessagePackWriter writer, Vector3<double>[] points)
    {
        if (points == null || points.Length == 0)
        {
            writer.Write((byte)0);
            writer.WriteNil();
            return;
        }

        if (FitInSingle(points))
        {
            var narrow = new float[points.Length * 3];
            for (var i = 0; i < points.Length; i++)
            {
                narrow[i * 3] = (float)points[i].X;
                narrow[i * 3 + 1] = (float)points[i].Y;
                narrow[i * 3 + 2] = (float)points[i].Z;
            }
            writer.Write((byte)sizeof(float));
            writer.Write(MemoryMarshal.AsBytes<float>(narrow));
            return;
        }

        var wide = new double[points.Length * 3];
        for (var i = 0; i < points.Length; i++)
        {
            wide[i * 3] = points[i].X;
            wide[i * 3 + 1] = points[i].Y;
            wide[i * 3 + 2] = points[i].Z;
        }
        writer.Write((byte)sizeof(double));
        writer.Write(MemoryMarshal.AsBytes<double>(wide));
    }

    public static Vector3<double>[] ReadPoints(ref MessagePackReader reader)
    {
        var width = reader.ReadByte();
        if (reader.TryReadNil() || width == 0) return null;

        var bytes = reader.ReadBytes();
        if (bytes == null) return null;

        var raw = bytes.Value.ToArray();
        if (width == sizeof(float))
        {
            var narrow = Reinterpret<float>(raw);
            var result = new Vector3<double>[narrow.Length / 3];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = new Vector3<double>(narrow[i * 3], narrow[i * 3 + 1], narrow[i * 3 + 2]);
            }
            return result;
        }

        var wide = Reinterpret<double>(raw);
        var points = new Vector3<double>[wide.Length / 3];
        for (var i = 0; i < points.Length; i++)
        {
            points[i] = new Vector3<double>(wide[i * 3], wide[i * 3 + 1], wide[i * 3 + 2]);
        }
        return points;
    }

    /// <summary>An index fits in a ushort for any mesh under 65536 vertices, which is most of them. Half the
    /// bytes, and not a value changed.</summary>
    public static void WriteIndices(ref MessagePackWriter writer, int[] indices)
    {
        if (indices == null || indices.Length == 0)
        {
            writer.Write((byte)0);
            writer.WriteNil();
            return;
        }

        if (FitInUInt16(indices))
        {
            var narrow = new ushort[indices.Length];
            for (var i = 0; i < indices.Length; i++) narrow[i] = (ushort)indices[i];
            writer.Write((byte)sizeof(ushort));
            writer.Write(MemoryMarshal.AsBytes<ushort>(narrow));
            return;
        }

        writer.Write((byte)sizeof(int));
        writer.Write(MemoryMarshal.AsBytes<int>(indices));
    }

    public static int[] ReadIndices(ref MessagePackReader reader)
    {
        var width = reader.ReadByte();
        if (reader.TryReadNil() || width == 0) return null;

        var bytes = reader.ReadBytes();
        if (bytes == null) return null;

        var raw = bytes.Value.ToArray();
        if (width == sizeof(ushort))
        {
            var narrow = Reinterpret<ushort>(raw);
            var result = new int[narrow.Length];
            for (var i = 0; i < narrow.Length; i++) result[i] = narrow[i];
            return result;
        }

        return Reinterpret<int>(raw);
    }

    public static void WriteColors(ref MessagePackWriter writer, Color[] colors)
    {
        if (colors == null || colors.Length == 0)
        {
            writer.WriteNil();
            return;
        }

        var raw = new byte[colors.Length * 4];
        for (var i = 0; i < colors.Length; i++)
        {
            raw[i * 4] = colors[i].R;
            raw[i * 4 + 1] = colors[i].G;
            raw[i * 4 + 2] = colors[i].B;
            raw[i * 4 + 3] = colors[i].A;
        }
        writer.Write(raw);
    }

    public static Color[] ReadColors(ref MessagePackReader reader)
    {
        if (reader.TryReadNil()) return null;

        var bytes = reader.ReadBytes();
        if (bytes == null) return null;

        var raw = bytes.Value.ToArray();
        var result = new Color[raw.Length / 4];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new Color(raw[i * 4], raw[i * 4 + 1], raw[i * 4 + 2], raw[i * 4 + 3]);
        }
        return result;
    }

    #region Flattening

    public static float[] Flatten(Vector2F[] values)
    {
        if (values == null) return null;

        var result = new float[values.Length * 2];
        for (var i = 0; i < values.Length; i++)
        {
            result[i * 2] = values[i].X;
            result[i * 2 + 1] = values[i].Y;
        }
        return result;
    }

    public static float[] Flatten(Vector3F[] values)
    {
        if (values == null) return null;

        var result = new float[values.Length * 3];
        for (var i = 0; i < values.Length; i++)
        {
            result[i * 3] = values[i].X;
            result[i * 3 + 1] = values[i].Y;
            result[i * 3 + 2] = values[i].Z;
        }
        return result;
    }

    public static float[] Flatten(Vector4F[] values)
    {
        if (values == null) return null;

        var result = new float[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
        {
            result[i * 4] = values[i].X;
            result[i * 4 + 1] = values[i].Y;
            result[i * 4 + 2] = values[i].Z;
            result[i * 4 + 3] = values[i].W;
        }
        return result;
    }

    public static Vector2F[] ToVector2(float[] values)
    {
        if (values == null) return null;

        var result = new Vector2F[values.Length / 2];
        for (var i = 0; i < result.Length; i++) result[i] = new Vector2F(values[i * 2], values[i * 2 + 1]);
        return result;
    }

    public static Vector3F[] ToVector3(float[] values)
    {
        if (values == null) return null;

        var result = new Vector3F[values.Length / 3];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new Vector3F(values[i * 3], values[i * 3 + 1], values[i * 3 + 2]);
        }
        return result;
    }

    public static Vector4F[] ToVector4(float[] values)
    {
        if (values == null) return null;

        var result = new Vector4F[values.Length / 4];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new Vector4F(values[i * 4], values[i * 4 + 1], values[i * 4 + 2], values[i * 4 + 3]);
        }
        return result;
    }

    #endregion

    /// <summary>A bitangent is made as <c>cross(normal, tangent) * tangent.W</c> and is worth storing only when
    /// it is not that. Asked of every vertex, so a mesh that carries bitangents from somewhere else keeps them.
    /// </summary>
    public static bool AreDerivable(Vector3F[] bitangents, Vector3F[] normals, Vector4F[] tangents)
    {
        if (bitangents == null || normals == null || tangents == null) return false;
        if (bitangents.Length != normals.Length || bitangents.Length != tangents.Length) return false;

        for (var i = 0; i < bitangents.Length; i++)
        {
            if (!Vector3F.NearEqual(Derive(normals[i], tangents[i]), bitangents[i])) return false;
        }
        return true;
    }

    public static Vector3F[] Derive(Vector3F[] normals, Vector4F[] tangents)
    {
        var result = new Vector3F[normals.Length];
        for (var i = 0; i < result.Length; i++) result[i] = Derive(normals[i], tangents[i]);
        return result;
    }

    private static Vector3F Derive(Vector3F normal, Vector4F tangent) =>
        Vector3F.Cross(normal, (Vector3F)tangent) * tangent.W;

    //The blobs are the machine's own bytes, so reading one back is a copy and not a conversion
    private static T[] Reinterpret<T>(byte[] raw) where T : struct
    {
        var result = new T[raw.Length / Unsafe.SizeOf<T>()];
        raw.AsSpan().CopyTo(MemoryMarshal.AsBytes(result.AsSpan()));
        return result;
    }

    private static bool FitInSingle(Vector3<double>[] points)
    {
        foreach (var point in points)
        {
            if ((float)point.X != point.X || (float)point.Y != point.Y || (float)point.Z != point.Z) return false;
        }
        return true;
    }

    private static bool FitInUInt16(int[] indices)
    {
        foreach (var index in indices)
        {
            if (index < 0 || index > ushort.MaxValue) return false;
        }
        return true;
    }
}

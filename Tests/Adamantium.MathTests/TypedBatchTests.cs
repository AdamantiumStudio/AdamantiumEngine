using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Adamantium.Mathematics.Typed;
using NUnit.Framework;
using Vector3 = Adamantium.Mathematics.Typed.Vector3;

namespace Adamantium.MathTests;

[TestFixture]
public class TypedBatchTests
{
    private static readonly int[] Counts = [0, 1, 2, 3, 4, 5, 7, 8, 9, 15, 16, 17, 23, 24, 25, 31, 33, 1000];

    private static readonly double[] Specials = [double.NaN, -double.NaN, double.PositiveInfinity, double.NegativeInfinity, -0.0, 0, 1e300, -1e-300, 5e-324, 3e38];

    [Test]
    public void PointsAsArrays_ComeOutAsOnePointAtATime_BitForBit() => EachType(new Points());

    [Test]
    public void APoint_ComesOutAsTheRowWithAWOfOne() => EachType(new Rows());

    [Test]
    public void ArraysMayBeWrittenInPlace_OrIntoOneAnothersPlace() => EachType(new Aliases());

    [Test]
    public void BadSpans_AreRefused()
    {
        var (x, y, z) = (new float[8], new float[8], new float[8]);
        var shorter = new float[7];
        var matrix = Matrix4x4<float>.Identity;

        Assert.Throws<ArgumentException>(() => Vector3<float>.TransformCoordinate(x, y, shorter, matrix, new float[8], new float[8], new float[8]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Vector3<float>.TransformCoordinate(x, y, z, matrix, new float[8], shorter, new float[8]));
        Assert.Throws<ArgumentException>(() =>
        {
            var shared = new float[9];
            Vector3<float>.TransformCoordinate(shared.AsSpan(0, 8), y, z, matrix, shared.AsSpan(1, 8), new float[8], new float[8]);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            var both = new float[8];
            Vector3<float>.TransformNormal(x, y, z, matrix, both, both, new float[8]);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            var points = new Vector3<double>[4];
            Vector3.Narrow(points, Vector3<double>.Zero, MemoryMarshal.Cast<Vector3<double>, Vector3<float>>(points.AsSpan()));
        });
    }

    [Test]
    public void AWorldInDouble_ComesNearTheCameraAsFloat_AsTheCastOneByOneGivesIt()
    {
        var random = new Random(42);
        foreach (var count in Counts)
        {
            var origin = new Vector3<double>(1e7 + random.NextDouble(), -3e6 + random.NextDouble(), 12345.678);
            var points = new Vector3<double>[count];
            var (x, y, z) = (new double[count], new double[count], new double[count]);
            for (var i = 0; i < count; i++)
            {
                points[i] = origin + new Vector3<double>(random.NextDouble() * 200 - 100, random.NextDouble() * 200 - 100, random.NextDouble() * 1e-3);
                if (random.Next(5) == 0)
                {
                    points[i].Y = Specials[random.Next(Specials.Length)];
                }

                (x[i], y[i], z[i]) = (points[i].X, points[i].Y, points[i].Z);
            }

            var near = new Vector3<float>[count];
            var (nx, ny, nz) = (new float[count], new float[count], new float[count]);
            Vector3.Narrow(points, origin, near);
            Vector3.Narrow(x, y, z, origin, nx, ny, nz);
            for (var i = 0; i < count; i++)
            {
                var expected = new Vector3<float>((float)(points[i].X - origin.X), (float)(points[i].Y - origin.Y), (float)(points[i].Z - origin.Z));
                Assert.That(Same(near[i], expected), Is.True, $"{count}: point {i}: {near[i]} / {expected}");
                Assert.That(Same(new Vector3<float>(nx[i], ny[i], nz[i]), expected), Is.True, $"{count}: arrays, point {i}");
            }
        }
    }

    private interface ICheck
    {
        void Run<T>() where T : unmanaged, IFloatingPointIeee754<T>;
    }

    private static void EachType(ICheck check)
    {
        check.Run<float>();
        check.Run<double>();
        check.Run<Half>();
    }

    private sealed class Points : ICheck
    {
        public void Run<T>() where T : unmanaged, IFloatingPointIeee754<T>
        {
            var random = new Random(40);
            foreach (var count in Counts)
            {
                for (var round = 0; round < 6; round++)
                {
                    var matrix = RandomMatrix<T>(random, round);
                    var (x, y, z) = (RandomArray<T>(random, count), RandomArray<T>(random, count), RandomArray<T>(random, count));
                    var (ox, oy, oz) = (new T[count], new T[count], new T[count]);
                    var (nx, ny, nz) = (new T[count], new T[count], new T[count]);
                    Vector3<T>.TransformCoordinate(x, y, z, matrix, ox, oy, oz);
                    Vector3<T>.TransformNormal(x, y, z, matrix, nx, ny, nz);
                    for (var i = 0; i < count; i++)
                    {
                        var point = new Vector3<T>(x[i], y[i], z[i]);
                        Assert.That(Same(new Vector3<T>(ox[i], oy[i], oz[i]), Vector3<T>.TransformCoordinate(point, matrix)), Is.True,
                            $"{typeof(T).Name} {count}: point {i}");
                        Assert.That(Same(new Vector3<T>(nx[i], ny[i], nz[i]), Vector3<T>.TransformNormal(point, matrix)), Is.True,
                            $"{typeof(T).Name} {count}: normal {i}");
                    }
                }
            }
        }
    }

    private sealed class Rows : ICheck
    {
        public void Run<T>() where T : unmanaged, IFloatingPointIeee754<T>
        {
            var random = new Random(43);
            for (var n = 0; n < 20000; n++)
            {
                var matrix = RandomMatrix<T>(random, n % 6);
                var point = new Vector3<T>(Random<T>(random), Random<T>(random), Random<T>(random));
                var row = Vector4<T>.Transform(new Vector4<T>(point, T.One), matrix);
                Assert.That(Same(Vector3<T>.Transform(point, matrix), row), Is.True, $"{typeof(T).Name}: {point} / {matrix}");
                var flat = new Vector2<T>(point.X, point.Y);
                Assert.That(Same(Vector2<T>.Transform(flat, matrix), Vector4<T>.Transform(new Vector4<T>(flat, T.Zero, T.One), matrix)), Is.True,
                    $"{typeof(T).Name}: {flat} / {matrix}");
            }
        }
    }

    private sealed class Aliases : ICheck
    {
        public void Run<T>() where T : unmanaged, IFloatingPointIeee754<T>
        {
            var random = new Random(41);
            int[][] orders = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];
            foreach (var normal in new[] { false, true })
            {
                foreach (var order in orders)
                {
                    var matrix = RandomMatrix<T>(random, 0);
                    T[][] arrays = [RandomArray<T>(random, 37), RandomArray<T>(random, 37), RandomArray<T>(random, 37)];
                    var expected = new Vector3<T>[37];
                    for (var i = 0; i < expected.Length; i++)
                    {
                        var point = new Vector3<T>(arrays[0][i], arrays[1][i], arrays[2][i]);
                        expected[i] = normal ? Vector3<T>.TransformNormal(point, matrix) : Vector3<T>.TransformCoordinate(point, matrix);
                    }

                    var (outX, outY, outZ) = (arrays[order[0]], arrays[order[1]], arrays[order[2]]);
                    if (normal)
                    {
                        Vector3<T>.TransformNormal(arrays[0], arrays[1], arrays[2], matrix, outX, outY, outZ);
                    }
                    else
                    {
                        Vector3<T>.TransformCoordinate(arrays[0], arrays[1], arrays[2], matrix, outX, outY, outZ);
                    }

                    for (var i = 0; i < expected.Length; i++)
                    {
                        Assert.That(Same(new Vector3<T>(outX[i], outY[i], outZ[i]), expected[i]), Is.True,
                            $"{typeof(T).Name} {(normal ? "normal" : "point")} [{string.Join(",", order)}]: {i}");
                    }
                }
            }
        }
    }

    private static bool Same<T>(Vector3<T> left, Vector3<T> right) where T : unmanaged, IFloatingPointIeee754<T> =>
        Same(left.X, right.X) && Same(left.Y, right.Y) && Same(left.Z, right.Z);

    private static bool Same<T>(Vector4<T> left, Vector4<T> right) where T : unmanaged, IFloatingPointIeee754<T> =>
        Same(left.X, right.X) && Same(left.Y, right.Y) && Same(left.Z, right.Z) && Same(left.W, right.W);

    private static bool Same<T>(T left, T right) where T : unmanaged, IFloatingPointIeee754<T> =>
        T.IsNaN(left) ? T.IsNaN(right) : MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in left)).SequenceEqual(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in right)));

    private static Matrix4x4<T> RandomMatrix<T>(Random random, int kind) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var matrix = new Matrix4x4<T>(RandomRow<T>(random), RandomRow<T>(random), RandomRow<T>(random), RandomRow<T>(random));
        switch (kind)
        {
            case 1:
                (matrix.M14, matrix.M24, matrix.M34, matrix.M44) = (T.Zero, T.Zero, T.Zero, T.Zero);
                break;
            case 2:
                matrix.M41 = T.CreateTruncating(Specials[random.Next(Specials.Length)]);
                matrix.M31 = T.CreateTruncating(Specials[random.Next(Specials.Length)]);
                break;
            case 3:
                (matrix.M14, matrix.M24, matrix.M34, matrix.M44) = (T.Zero, T.Zero, T.Zero, T.One);
                break;
        }

        return matrix;
    }

    private static Vector4<T> RandomRow<T>(Random random) where T : unmanaged, IFloatingPointIeee754<T> =>
        new(Random<T>(random), Random<T>(random), Random<T>(random), Random<T>(random));

    private static T[] RandomArray<T>(Random random, int count) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var values = new T[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = Random<T>(random);
        }

        return values;
    }

    private static T Random<T>(Random random) where T : unmanaged, IFloatingPointIeee754<T> =>
        T.CreateTruncating(random.Next(8) == 0 ? Specials[random.Next(Specials.Length)] : random.NextDouble() * 8 - 4);
}

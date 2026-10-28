using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using Adamantium.Mathematics;

namespace Adamantium.MathGolden;

/// <summary>
/// Records the answers of the mathematics being replaced: every public method of its value types that takes and
/// returns numbers, vectors, matrices, quaternions, colors and volumes by value, called on fixed random inputs. The new
/// types are checked against the file operation by operation.
/// </summary>
public static class Program
{
    /// <summary>How many calls on random inputs each operation is recorded for; one more per <see cref="ValueKind"/>
    /// follows them. Each call: the inputs, whether it threw, and the answer - the result, then every argument passed
    /// by reference as the call left it, then an instance as the call left it.</summary>
    public const int Cases = 24;

    private static readonly Type[] Recorded =
    [
        typeof(Vector2), typeof(Vector2F), typeof(Vector3), typeof(Vector3F), typeof(Vector4), typeof(Vector4F),
        typeof(Quaternion), typeof(QuaternionF), typeof(Matrix3x2), typeof(Matrix3x2F), typeof(Matrix3x3),
        typeof(Matrix3x3F), typeof(Matrix4x4), typeof(Matrix4x4F), typeof(Int3), typeof(Int4), typeof(Bool4),
        typeof(Rectangle), typeof(RectangleF), typeof(Size), typeof(SizeF), typeof(Plane), typeof(Ray),
        typeof(BoundingBox), typeof(BoundingSphere), typeof(Color), typeof(Color3F), typeof(Color4F),
        typeof(ColorBGRA), typeof(MathHelper), typeof(Collision), typeof(BoundingFrustum), typeof(OrientedBoundingBox)
    ];

    public static int Main(string[] args)
    {
        var path = args.Length > 0 ? args[0] : Path.Combine("Tests", "TestAssets", "MathGolden", "golden.bin.gz");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        var operations = 0;
        var skipped = new List<string>();
        using (var file = File.Create(path))
        using (var zip = new GZipStream(file, CompressionLevel.Optimal))
        using (var writer = new BinaryWriter(zip, Encoding.UTF8))
        {
            foreach (var type in Recorded)
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance |
                                                        BindingFlags.DeclaredOnly))
                {
                    if (!IsRecordable(method, out var why))
                    {
                        skipped.Add($"{Key(type, method)}: {why}");
                        continue;
                    }

                    Record(writer, type, method);
                    operations++;
                }
            }

            writer.Write(string.Empty);
        }

        File.WriteAllLines(Path.ChangeExtension(path, null) + ".skipped.txt", skipped);
        Console.WriteLine($"{operations} operations recorded to {path}; {skipped.Count} skipped (listed beside it).");
        return 0;
    }

    /// <summary>The name an operation is recorded under: type, method, parameter types, and "this" for an instance
    /// method.</summary>
    public static string Key(Type type, MethodInfo method)
    {
        var parameters = method.GetParameters().Select(p => p.ParameterType.Name);
        var self = method.IsStatic ? string.Empty : "this ";
        return $"{type.Name}.{method.Name}({self}{string.Join(",", parameters)})";
    }

    private static bool IsRecordable(MethodInfo method, out string why)
    {
        why = null;
        if (method.IsSpecialName && !method.Name.StartsWith("op_", StringComparison.Ordinal)
            && !(method.Name.StartsWith("get_", StringComparison.Ordinal) && method.GetParameters().Length == 0))
        {
            why = "property or event accessor";
            return false;
        }

        if (method.ContainsGenericParameters)
        {
            why = "generic";
            return false;
        }

        if (method.ReturnType != typeof(void) && !Values.IsFlat(method.ReturnType))
        {
            why = $"returns {method.ReturnType.Name}";
            return false;
        }

        var answers = method.ReturnType != typeof(void) || !method.IsStatic;
        foreach (var parameter in method.GetParameters())
        {
            var type = parameter.ParameterType;
            if (!Values.IsFlat(type.IsByRef ? type.GetElementType() : type))
            {
                why = $"takes {type.Name}";
                return false;
            }

            answers |= type.IsByRef;
        }

        if (!answers)
        {
            why = "answers nothing";
            return false;
        }

        return true;
    }

    private static void Record(BinaryWriter writer, Type type, MethodInfo method)
    {
        var parameters = method.GetParameters();
        var key = Key(type, method);
        var random = new Random(StableHash(key));
        var kinds = Enum.GetValues<ValueKind>();
        writer.Write(key);
        writer.Write(Cases + kinds.Length);
        for (var c = 0; c < Cases + kinds.Length; c++)
        {
            var same = new Dictionary<Type, object>();
            Func<Type, object> value = c < Cases
                ? t => Values.Random(t, random)
                : t => Values.Special(t, kinds[c - Cases], random, same);
            var self = method.IsStatic ? null : value(type);
            var arguments = parameters.Select(p => value(
                p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType)).ToArray();
            var inputs = new List<double>();
            if (self != null)
            {
                Values.Flatten(self, inputs);
            }

            foreach (var argument in arguments)
            {
                Values.Flatten(argument, inputs);
            }

            Write(writer, inputs);
            object answer;
            try
            {
                answer = method.Invoke(self, arguments);
            }
            catch (TargetInvocationException)
            {
                writer.Write(true);
                continue;
            }

            writer.Write(false);
            var outputs = new List<double>();
            if (method.ReturnType != typeof(void))
            {
                Values.Flatten(answer, outputs);
            }

            for (var p = 0; p < parameters.Length; p++)
            {
                if (parameters[p].ParameterType.IsByRef)
                {
                    Values.Flatten(arguments[p], outputs);
                }
            }

            if (self != null)
            {
                Values.Flatten(self, outputs);
            }

            Write(writer, outputs);
        }
    }

    private static void Write(BinaryWriter writer, List<double> values)
    {
        writer.Write(values.Count);
        foreach (var value in values)
        {
            writer.Write(value);
        }
    }

    private static int StableHash(string text)
    {
        var hash = 17;
        foreach (var c in text)
        {
            hash = unchecked(hash * 31 + c);
        }

        return hash;
    }
}

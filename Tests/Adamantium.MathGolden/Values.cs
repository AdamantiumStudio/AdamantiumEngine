using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Adamantium.Mathematics;

namespace Adamantium.MathGolden;

/// <summary>Values the recorded operations take and return, as flat lists of numbers: a primitive is one number, a
/// value type its fields in declaration order, a frustum its view-projection matrix and whether it checks its far
/// plane.</summary>
public static class Values
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>Whether <paramref name="type"/> is a number, a bool, an enum, a frustum or a value type made of them
    /// alone.</summary>
    public static bool IsFlat(Type type)
    {
        if (type == typeof(BoundingFrustum))
        {
            return true;
        }

        if (type.IsPrimitive || type.IsEnum)
        {
            return type != typeof(IntPtr) && type != typeof(UIntPtr) && type != typeof(char);
        }

        if (!type.IsValueType || type.IsGenericType)
        {
            return false;
        }

        var fields = type.GetFields(Fields);
        return fields.Length > 0 && fields.All(field => IsFlat(field.FieldType));
    }

    /// <summary>The numbers <paramref name="value"/> is made of.</summary>
    public static void Flatten(object value, List<double> into)
    {
        switch (value)
        {
            case bool b:
                into.Add(b ? 1 : 0);
                return;
            case Enum e:
                into.Add(Convert.ToDouble(e));
                return;
            case IConvertible convertible when value.GetType().IsPrimitive:
                into.Add(convertible.ToDouble(null));
                return;
            case BoundingFrustum frustum:
                Flatten(frustum.ViewProjection, into);
                into.Add(frustum.EnableFarPlaneCheck ? 1 : 0);
                return;
        }

        foreach (var field in value.GetType().GetFields(Fields))
        {
            Flatten(field.GetValue(value), into);
        }
    }

    /// <summary>A value of <paramref name="type"/> to call an operation with: numbers in a range where the
    /// operations mean something, a quaternion of unit length, a box whose minimum is below its maximum.</summary>
    public static object Random(Type type, Random random)
    {
        if (type == typeof(QuaternionF))
        {
            return QuaternionF.Normalize(new QuaternionF(F(random), F(random), F(random), F(random)));
        }

        if (type == typeof(Quaternion))
        {
            return Quaternion.Normalize(new Quaternion(F(random), F(random), F(random), F(random)));
        }

        if (type == typeof(BoundingBox))
        {
            var min = new Vector3F(F(random), F(random), F(random));
            return new BoundingBox(min, min + new Vector3F(Math.Abs(F(random)), Math.Abs(F(random)), Math.Abs(F(random))));
        }

        if (type == typeof(BoundingSphere))
        {
            return new BoundingSphere(new Vector3F(F(random), F(random), F(random)), Math.Abs(F(random)));
        }

        if (type == typeof(OrientedBoundingBox))
        {
            var halfExtent = new Vector3F(Math.Abs(F(random)), Math.Abs(F(random)), Math.Abs(F(random)));
            return new OrientedBoundingBox(new Vector3F(F(random), F(random), F(random)), halfExtent,
                (QuaternionF)Random(typeof(QuaternionF), random));
        }

        if (type == typeof(BoundingFrustum))
        {
            return new BoundingFrustum(ViewProjection(random), random.Next(2) == 1);
        }

        if (type == typeof(float))
        {
            return F(random);
        }

        if (type == typeof(double))
        {
            return (double)F(random);
        }

        if (type == typeof(int))
        {
            return random.Next(-8, 9);
        }

        if (type == typeof(uint))
        {
            return (uint)random.Next(0, 9);
        }

        if (type == typeof(byte))
        {
            return (byte)random.Next(0, 256);
        }

        if (type == typeof(bool))
        {
            return random.Next(2) == 1;
        }

        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return values.GetValue(random.Next(values.Length));
        }

        if (type.IsPrimitive)
        {
            return Convert.ChangeType(random.Next(0, 9), type);
        }

        var value = Activator.CreateInstance(type);
        foreach (var field in type.GetFields(Fields))
        {
            field.SetValue(value, Random(field.FieldType, random));
        }

        return value;
    }

    /// <summary>A value of <paramref name="type"/> for an edge case: every number zero, one, large or NaN; for
    /// <see cref="ValueKind.Same"/>, the one value <paramref name="same"/> holds for the type, so every argument of a
    /// type is equal to the others.</summary>
    public static object Special(Type type, ValueKind kind, Random random, Dictionary<Type, object> same)
    {
        if (kind == ValueKind.Same)
        {
            if (!same.TryGetValue(type, out var value))
            {
                value = Random(type, random);
                same.Add(type, value);
            }

            return value;
        }

        if (type == typeof(BoundingFrustum))
        {
            return new BoundingFrustum((Matrix4x4F)Special(typeof(Matrix4x4F), kind, random, same), kind == ValueKind.Unit);
        }

        if (kind == ValueKind.Unit && type.GetProperty("Identity", BindingFlags.Public | BindingFlags.Static) is { } identity
                                   && identity.PropertyType == type)
        {
            return identity.GetValue(null);
        }

        if (kind == ValueKind.Unit && type.GetField("Identity", BindingFlags.Public | BindingFlags.Static) is { } identityField
                                   && identityField.FieldType == type)
        {
            return identityField.GetValue(null);
        }

        var first = true;
        return Fill(type, ref first, kind, random);
    }

    private static object Fill(Type type, ref bool first, ValueKind kind, Random random)
    {
        if (type == typeof(bool))
        {
            return kind == ValueKind.Unit;
        }

        if (type.IsEnum)
        {
            return Random(type, random);
        }

        if (type.IsPrimitive)
        {
            var number = kind switch
            {
                ValueKind.Zero => 0.0,
                ValueKind.Unit => first ? 1.0 : 0.0,
                ValueKind.Large => type == typeof(float) || type == typeof(double) ? (random.NextDouble() * 8 - 4) * 1e30 : 20,
                _ => type == typeof(float) || type == typeof(double) ? double.NaN : 0
            };
            first = false;
            return type == typeof(float) ? (float)number : Convert.ChangeType(number, type);
        }

        var value = Activator.CreateInstance(type);
        foreach (var field in type.GetFields(Fields))
        {
            field.SetValue(value, Fill(field.FieldType, ref first, kind, random));
        }

        return value;
    }

    private static float F(Random random) => (float)(random.NextDouble() * 8 - 4);

    // A camera's: looking from a random point to another, through a perspective or an orthographic projection.
    private static Matrix4x4F ViewProjection(Random random)
    {
        var eye = new Vector3F(F(random), F(random), F(random));
        var target = eye + new Vector3F(F(random), F(random), F(random));
        var view = Matrix4x4F.LookAtLH(eye, target, Vector3F.UnitY);
        var near = 0.1f + (float)random.NextDouble() * 2;
        var far = near + 1 + (float)random.NextDouble() * 100;
        var projection = random.Next(2) == 1
            ? Matrix4x4F.PerspectiveFovY(0.3f + (float)random.NextDouble() * 2.2f, 0.5f + (float)random.NextDouble() * 1.5f, near, far)
            : Matrix4x4F.OrthoLH(1 + (float)random.NextDouble() * 20, 1 + (float)random.NextDouble() * 20, near, far);
        return view * projection;
    }
}

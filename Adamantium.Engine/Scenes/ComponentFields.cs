using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Adamantium.Core.TypeParsing;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;
using Serilog;

namespace Adamantium.Engine.Scenes;

/// <summary>Moves a component's data between the component and its scene entry: its public properties with a public
/// getter and setter declared above a given base type, of a type the scene writes as text - numbers,
/// <see cref="bool"/>, strings, enumerations, vectors, quaternions. A property overridden or hidden counts once, as the
/// most derived type has it. Read back through <see cref="TypeParser"/>, as markup reads its values.</summary>
internal static class ComponentFields
{
    /// <summary>The loaded type of <paramref name="fullName"/> that derives from <typeparamref name="T"/> and can be
    /// made; null when no loaded assembly has one.</summary>
    public static Type FindType<T>(string fullName)
    {
        if (string.IsNullOrEmpty(fullName)) return null;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(fullName, false);
            if (type != null && typeof(T).IsAssignableFrom(type) && !type.IsAbstract &&
                type.GetConstructor(Type.EmptyTypes) != null)
            {
                return type;
            }
        }

        return null;
    }

    /// <summary>Adds the fields of <paramref name="component"/> declared above <paramref name="below"/> to
    /// <paramref name="values"/>; one named as an attribute the entry has of its own (<paramref name="taken"/>) is
    /// logged and left out.</summary>
    public static void Write(Component component, Type below, List<SceneValue> values, IReadOnlyCollection<string> taken)
    {
        foreach (var property in Properties(component.GetType(), below))
        {
            if (Contains(taken, property.Name))
            {
                Log.Logger.Warning("Scene: {Type}.{Field} is named as the entry's own attribute; it is not kept",
                    component.GetType().FullName, property.Name);
                continue;
            }

            var value = property.GetValue(component);
            if (value != null)
            {
                values.Add(new SceneValue(property.Name, Format(value)));
            }
        }
    }

    /// <summary>Sets <paramref name="values"/> on <paramref name="component"/>; a field the type no longer has, or a value
    /// it cannot take, is logged and left out.</summary>
    public static void Read(IReadOnlyList<SceneValue> values, Component component, Type below)
    {
        var properties = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
        foreach (var property in Properties(component.GetType(), below))
        {
            properties[property.Name] = property;
        }

        var typeName = component.GetType().FullName;
        foreach (var value in values)
        {
            if (!properties.TryGetValue(value.Name, out var property))
            {
                Log.Logger.Warning("Scene: {Type} has no field {Field}; its value \"{Value}\" is left out",
                    typeName, value.Name, value.Text);
                continue;
            }

            try
            {
                property.SetValue(component, TypeParser.Parse(value.Text, property.PropertyType));
            }
            catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException
                                                  or InvalidOperationException or TargetInvocationException)
            {
                Log.Logger.Warning("Scene: {Type}.{Field} does not take \"{Value}\": {Reason}",
                    typeName, value.Name, value.Text, exception.InnerException?.Message ?? exception.Message);
            }
        }
    }

    private static bool Contains(IReadOnlyCollection<string> names, string name)
    {
        foreach (var each in names)
        {
            if (string.Equals(each, name, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static List<PropertyInfo> Properties(Type type, Type below)
    {
        var found = new List<PropertyInfo>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var each = type; each != null && each != below; each = each.BaseType)
        {
            foreach (var property in each.GetProperties(
                         BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0 &&
                    property.GetMethod.IsPublic && property.SetMethod.IsPublic && IsWritten(property.PropertyType) &&
                    names.Add(property.Name))
                {
                    found.Add(property);
                }
            }
        }

        return found;
    }

    private static bool IsWritten(Type type) =>
        type == typeof(string) || type == typeof(bool) || type.IsEnum || IsNumber(type) ||
        type == typeof(Vector2<float>) || type == typeof(Vector2<double>) ||
        type == typeof(Vector3<float>) || type == typeof(Vector3<double>) ||
        type == typeof(Vector4<float>) || type == typeof(Vector4<double>) ||
        type == typeof(Quaternion<float>) || type == typeof(Quaternion<double>);

    private static bool IsNumber(Type type) =>
        type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
        type == typeof(short) || type == typeof(ushort) || type == typeof(byte) || type == typeof(sbyte) ||
        type == typeof(float) || type == typeof(double) || type == typeof(decimal);

    private static string Format(object value) => value switch
    {
        string text => text,
        bool flag => flag ? "True" : "False",
        Enum kind => kind.ToString(),
        Vector2<float> v => SceneText.Format(v),
        Vector2<double> v => SceneText.Format(v),
        Vector3<float> v => SceneText.Format(v),
        Vector3<double> v => SceneText.Format(v),
        Vector4<float> v => SceneText.Format(v),
        Vector4<double> v => SceneText.Format(v),
        Quaternion<float> q => SceneText.Format(q),
        Quaternion<double> q => SceneText.Format(q),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Adamantium.Core.TypeParsing;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;
using Serilog;

namespace Adamantium.Engine.Scenes;

/// <summary>Moves the data of a <see cref="Behaviour"/> between the component and its scene entry: its public properties
/// with a getter and a setter declared on the game's own types - not on <see cref="Behaviour"/> or what is below it -
/// of a type the scene writes as text: numbers, <see cref="bool"/>, strings, enumerations, vectors, quaternions. Read
/// back through <see cref="TypeParser"/>, as markup reads its values.</summary>
internal static class BehaviourFields
{
    /// <summary>The loaded component type of <paramref name="fullName"/>; null when no loaded assembly has one.</summary>
    public static Type FindType(string fullName)
    {
        if (string.IsNullOrEmpty(fullName)) return null;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(fullName, false);
            if (type != null && typeof(Behaviour).IsAssignableFrom(type) && !type.IsAbstract &&
                type.GetConstructor(Type.EmptyTypes) != null)
            {
                return type;
            }
        }

        return null;
    }

    public static void Write(Behaviour behaviour, SceneBehaviour entry)
    {
        foreach (var property in Properties(behaviour.GetType()))
        {
            var value = property.GetValue(behaviour);
            if (value != null)
            {
                entry.Values.Add(new SceneValue(property.Name, Format(value)));
            }
        }
    }

    /// <summary>Sets the values of <paramref name="entry"/> on <paramref name="behaviour"/>; a field the type no longer
    /// has, or a value it cannot take, is logged and left out.</summary>
    public static void Read(SceneBehaviour entry, Behaviour behaviour)
    {
        var properties = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
        foreach (var property in Properties(behaviour.GetType()))
        {
            properties[property.Name] = property;
        }

        foreach (var value in entry.Values)
        {
            if (!properties.TryGetValue(value.Name, out var property))
            {
                Log.Logger.Warning("Scene: {Type} has no field {Field}; its value \"{Value}\" is left out",
                    entry.Type, value.Name, value.Text);
                continue;
            }

            try
            {
                property.SetValue(behaviour, TypeParser.Parse(value.Text, property.PropertyType));
            }
            catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException
                                                  or InvalidOperationException)
            {
                Log.Logger.Warning("Scene: {Type}.{Field} does not take \"{Value}\": {Reason}",
                    entry.Type, value.Name, value.Text, exception.Message);
            }
        }
    }

    private static IEnumerable<PropertyInfo> Properties(Type type)
    {
        for (var each = type; each != null && each != typeof(Behaviour); each = each.BaseType)
        {
            foreach (var property in each.GetProperties(
                         BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0 &&
                    property.SetMethod.IsPublic && IsWritten(property.PropertyType))
                {
                    yield return property;
                }
            }
        }
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

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Adamantium.Core.Exceptions;
using Adamantium.Graphics.Core.Vertices;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Core.Extensions;

/// <summary>The vertex input a vertex structure stands for, as dynamic vertex input state takes it: one binding, and
/// an input for each field marked with <see cref="VertexInputElementAttribute"/>, at locations in declaration order.
/// Worked out once a structure, from any thread.</summary>
public static class VertexUtils
{
    private static readonly ConcurrentDictionary<Type, VertexInputBindingDescription2EXT> Bindings = new();
    private static readonly ConcurrentDictionary<Type, VertexInputAttributeDescription2EXT[]> Inputs = new();

    /// <summary>The binding: the structure's size apart, per instance when it is marked
    /// <see cref="PerInstanceDataAttribute"/>, per vertex otherwise.</summary>
    public static VertexInputBindingDescription2EXT GetBindingDescription2(this Type vertexType) =>
        Bindings.GetOrAdd(vertexType, type => new VertexInputBindingDescription2EXT
        {
            Binding = 0,
            Stride = (uint)Marshal.SizeOf(type),
            InputRate = type.IsDefined(typeof(PerInstanceDataAttribute), false)
                ? VertexInputRate.Instance
                : VertexInputRate.Vertex,
            Divisor = 1
        });

    public static VertexInputBindingDescription2EXT GetBindingDescription2<T>() where T : struct =>
        typeof(T).GetBindingDescription2();

    /// <summary>The inputs, one a field. Throws <see cref="AttributeNotFoundException"/> for a field not marked.</summary>
    public static VertexInputAttributeDescription2EXT[] GetVertexAttributeDescription2(this Type vertexType) =>
        Inputs.GetOrAdd(vertexType, type => type
            .GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Where(field => !field.IsInitOnly)
            .OrderBy(field => field.MetadataToken)
            .Select((field, location) => new VertexInputAttributeDescription2EXT
            {
                Binding = 0,
                Location = (uint)location,
                Format = FormatOf(field),
                Offset = (uint)Marshal.OffsetOf(type, field.Name).ToInt32()
            })
            .ToArray());

    private static Format FormatOf(FieldInfo field)
    {
        var input = field.GetCustomAttribute<VertexInputElementAttribute>()
                    ?? throw new AttributeNotFoundException(
                        $"{field.DeclaringType?.Name}.{field.Name} has no {nameof(VertexInputElementAttribute)}");

        return input.Format != Format.UNDEFINED ? input.Format : FormatOf(field.FieldType);
    }

    private static Format FormatOf(Type type)
    {
        if (type == typeof(Vector4F) || type == typeof(Color4F) || type == typeof(RectangleF)) return Format.R32G32B32A32_SFLOAT;
        if (type == typeof(Vector3F) || type == typeof(Color3F)) return Format.R32G32B32_SFLOAT;
        if (type == typeof(Vector2F)) return Format.R32G32_SFLOAT;
        if (type == typeof(float)) return Format.R32_SFLOAT;
        if (type == typeof(Color)) return Format.R8G8B8A8_UNORM;
        if (type == typeof(ColorBGRA)) return Format.B8G8R8A8_UNORM;
        if (type == typeof(Vector4<int>)) return Format.R32G32B32A32_SINT;
        if (type == typeof(Vector3<int>)) return Format.R32G32B32_SINT;
        if (type == typeof(int)) return Format.R32_SINT;
        if (type == typeof(uint)) return Format.R32_UINT;
        if (type == typeof(Bool4)) return Format.R32G32B32A32_UINT;

        throw new NotSupportedException($"A vertex field of {type.Name} needs its format given in its attribute");
    }
}

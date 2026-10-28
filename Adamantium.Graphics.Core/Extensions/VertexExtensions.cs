using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

    private static readonly Dictionary<Type, Format> FormatsOfTypes = new()
    {
        [typeof(Vector4F)] = Format.R32G32B32A32_SFLOAT,
        [typeof(Color4F)] = Format.R32G32B32A32_SFLOAT,
        [typeof(RectangleF)] = Format.R32G32B32A32_SFLOAT,
        [typeof(Vector3F)] = Format.R32G32B32_SFLOAT,
        [typeof(Color3F)] = Format.R32G32B32_SFLOAT,
        [typeof(Vector2F)] = Format.R32G32_SFLOAT,
        [typeof(float)] = Format.R32_SFLOAT,
        [typeof(Color)] = Format.R8G8B8A8_UNORM,
        [typeof(ColorBGRA)] = Format.B8G8R8A8_UNORM,
        [typeof(Vector4<int>)] = Format.R32G32B32A32_SINT,
        [typeof(Vector3<int>)] = Format.R32G32B32_SINT,
        [typeof(int)] = Format.R32_SINT,
        [typeof(uint)] = Format.R32_UINT,
        [typeof(Bool4)] = Format.R32G32B32A32_UINT
    };

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
                Format = GetFormat(field),
                Offset = (uint)Marshal.OffsetOf(type, field.Name).ToInt32()
            })
            .ToArray());

    private static Format GetFormat(FieldInfo field)
    {
        var input = field.GetCustomAttribute<VertexInputElementAttribute>()
                    ?? throw new AttributeNotFoundException(
                        $"{field.DeclaringType?.Name}.{field.Name} has no {nameof(VertexInputElementAttribute)}");

        if (input.Format != Format.UNDEFINED)
        {
            return input.Format;
        }

        return FormatsOfTypes.TryGetValue(field.FieldType, out var format)
            ? format
            : throw new NotSupportedException($"A vertex field of {field.FieldType.Name} needs its format given in its attribute");
    }
}

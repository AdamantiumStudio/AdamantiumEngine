using System;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics;

/// <summary>The vertex input of the buffers bound together, a vertex structure each: a binding a buffer, and the inputs of
/// all of them at locations going on from one structure to the next.</summary>
internal sealed class VertexLayout
{
    private readonly Type[] _types;

    public VertexLayout(ReadOnlySpan<Type> types)
    {
        _types = types.ToArray();
        Bindings = new VertexInputBindingDescription2EXT[types.Length];
        var count = 0;
        foreach (var type in types)
        {
            count += type.GetVertexAttributeDescription2().Length;
        }

        Inputs = new VertexInputAttributeDescription2EXT[count];
        var location = 0u;
        for (var i = 0; i < types.Length; i++)
        {
            var binding = types[i].GetBindingDescription2();
            Bindings[i] = new VertexInputBindingDescription2EXT
            {
                Binding = (uint)i,
                Stride = binding.Stride,
                InputRate = binding.InputRate,
                Divisor = binding.Divisor
            };
            foreach (var input in types[i].GetVertexAttributeDescription2())
            {
                Inputs[location] = new VertexInputAttributeDescription2EXT
                {
                    Binding = (uint)i,
                    Location = location,
                    Format = input.Format,
                    Offset = input.Offset
                };
                location++;
            }
        }
    }

    public ReadOnlySpan<Type> Types => _types;

    public VertexInputBindingDescription2EXT[] Bindings { get; }

    public VertexInputAttributeDescription2EXT[] Inputs { get; }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace Adamantium.EffectsCompiler;

/// <summary>Places the resources of an effect's passes for both descriptor models: in the push data on the descriptor
/// heap, among the pushed descriptors on descriptor sets.</summary>
internal static class PassResourcePlacement
{
    /// <summary>Puts every pass's stages in pipeline order and places its resources. On the heap the addresses of the
    /// constant buffers come first, eight bytes each, then the heap indices, four bytes an element; on descriptor sets
    /// the descriptors follow the bindings. A uniform buffer is a constant buffer when some stage declares it with
    /// values.</summary>
    public static void Place(EffectData data)
    {
        var buffers = new HashSet<string>();
        foreach (var technique in data.Description.Techniques)
        {
            foreach (var pass in technique.Passes)
            {
                pass.Stages = [.. pass.Stages.OrderBy(link => GetPipelineOrder(link.Stage))];
                foreach (var link in pass.Stages)
                {
                    foreach (var buffer in data.Shaders[link.Index].ConstantBuffers)
                    {
                        if (buffer.Size > 0)
                        {
                            buffers.Add(buffer.Name);
                        }
                    }
                }
            }
        }

        foreach (var technique in data.Description.Techniques)
        {
            foreach (var pass in technique.Passes)
            {
                Place(data, pass, buffers);
            }
        }
    }

    private static int GetPipelineOrder(EffectStage stage) => stage switch
    {
        EffectStage.Vertex => 0,
        EffectStage.Hull => 1,
        EffectStage.Domain => 2,
        EffectStage.Task => 3,
        EffectStage.Mesh => 4,
        EffectStage.Pixel => 5,
        _ => 6
    };

    private static void Place(EffectData data, EffectData.Pass pass, HashSet<string> buffers)
    {
        pass.Resources = [];
        var offset = 0u;
        foreach (var link in pass.Stages)
        {
            foreach (var declared in data.Shaders[link.Index].ResourceParameters)
            {
                if (declared.Type == EffectParameterType.UniformBuffer && buffers.Contains(declared.Name)
                    && Add(pass, declared) is { } resource)
                {
                    offset = (offset + 7) & ~7u;
                    resource.PushOffset = offset;
                    offset += 8;
                }
            }
        }

        foreach (var link in pass.Stages)
        {
            var declarations = data.Shaders[link.Index].ResourceParameters;
            PlaceIndices(pass, declarations, IsSampledImage, ref offset);
            PlaceIndices(pass, declarations, type => type == EffectParameterType.Sampler, ref offset);
            PlaceIndices(pass, declarations, type => type == EffectParameterType.StorageBuffer, ref offset);
        }

        pass.PushDataSize = offset;

        var descriptor = 0;
        foreach (var resource in pass.Resources.OrderBy(resource => resource.Set).ThenBy(resource => resource.Binding))
        {
            if (IsSampledImage(resource.Type) && resource.Count == 0)
            {
                resource.Descriptor = -1;
                continue;
            }

            resource.Descriptor = descriptor;
            descriptor += (int)Math.Max(resource.Count, 1);
        }

        pass.DescriptorCount = (uint)descriptor;
    }

    private static void PlaceIndices(EffectData.Pass pass, List<EffectData.ResourceParameter> declarations,
        Func<EffectParameterType, bool> isOfKind, ref uint offset)
    {
        foreach (var declared in declarations)
        {
            if (isOfKind(declared.Type) && Add(pass, declared) is { } resource)
            {
                resource.PushOffset = offset;
                offset += Math.Max(declared.Count, 1) * 4;
            }
        }
    }

    private static EffectData.PassResource Add(EffectData.Pass pass, EffectData.ResourceParameter declared)
    {
        if (pass.Resources.Exists(resource => resource.Name == declared.Name))
        {
            return null;
        }

        var resource = new EffectData.PassResource
        {
            Name = declared.Name,
            Type = declared.Type,
            Set = declared.Set,
            Binding = declared.Binding,
            Count = declared.Count
        };
        pass.Resources.Add(resource);
        return resource;
    }

    private static bool IsSampledImage(EffectParameterType type) => type is EffectParameterType.SampledImage1D
        or EffectParameterType.SampledImage2D or EffectParameterType.SampledImage3D or EffectParameterType.SampledImageCube;
}

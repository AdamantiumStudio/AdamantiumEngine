using System;
using System.Numerics;
using Adamantium.EffectsCompiler;
using Adamantium.Graphics.Core;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Effects;

/// <summary>
/// The shaders of a pass on a logical device and what they are bound with: made once for the pass's data, shared by every
/// effect of that data on any render device, and fixed once made. How the resources reach the shaders - the descriptor
/// heap or descriptor sets - is up to the implementation the device chose.
/// </summary>
internal abstract class EffectPassShaders : IDisposable
{
    private protected readonly GraphicsDevice Device;
    private protected readonly EffectData Data;
    private protected readonly string Name;
    private readonly string cacheName;

    private protected EffectPassShaders(GraphicsDevice device, EffectData data, EffectData.Pass pass, string effectName,
        string techniqueName, string passName)
    {
        Device = device;
        Data = data;
        Pass = pass;
        Name = $"{effectName}.{passName}";
        cacheName = $"{effectName}.{techniqueName}.{passName}";
        Stages = new ShaderStageFlagBits[pass.Stages.Count];
        Shaders = new ShaderEXT[pass.Stages.Count];
        Unsupported = FindUnsupported();
    }

    /// <summary>The data the shaders are made of.</summary>
    public EffectData.Pass Pass { get; }

    /// <summary>Why the device cannot run the pass; null when it can.</summary>
    public NotSupportedException Unsupported { get; }

    /// <summary>The stages a draw with the pass binds, in pipeline order: the pass's own, then those it binds empty.
    /// </summary>
    public ShaderStageFlagBits[] BindStages { get; private set; } = [];

    /// <summary>The shaders of <see cref="BindStages"/>; null for an empty one.</summary>
    public ShaderEXT[] BindShaders { get; private set; } = [];

    /// <summary>How many control points the hull shader reads a patch of; 0 for a pass that does not tessellate.</summary>
    public uint InputPatchSize { get; private set; }

    /// <summary>How many effect passes hold these shaders; the cache counts them.</summary>
    internal int References;

    private protected ShaderStageFlagBits[] Stages { get; }

    private protected ShaderEXT[] Shaders { get; }

    /// <summary>Makes the shaders, unless the device cannot run the pass; those made are destroyed when one fails.</summary>
    public void Create()
    {
        if (Unsupported != null)
        {
            return;
        }

        try
        {
            var present = (ShaderStageFlagBits)0;
            for (var i = 0; i < Stages.Length; i++)
            {
                var link = Pass.Stages[i];
                Stages[i] = EffectPass.GetShaderStage(link.Stage);
                present |= Stages[i];
                if (link.Stage == EffectStage.Hull)
                {
                    InputPatchSize = Data.Shaders[link.Index].InputPatchSize;
                }
            }

            CreateShaders();
            FillBindArrays(present);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private protected abstract void CreateShaders();

    /// <summary>The create info of the shader at <paramref name="stage"/>: its SPIR-V and the stage after it.</summary>
    private protected ShaderCreateInfoEXT NewCreateInfo(int stage)
    {
        var link = Pass.Stages[stage];
        var shader = Data.Shaders[link.Index];
        var hasTaskShader = Pass.Stages.Exists(other => other.Stage == EffectStage.Task);
        return new ShaderCreateInfoEXT
        {
            Flags = link.Stage == EffectStage.Mesh && !hasTaskShader ? ShaderCreateFlagBitsEXT.NoTaskShaderBitExt : 0,
            Stage = Stages[stage],
            NextStage = stage + 1 < Stages.Length ? Stages[stage + 1] : 0,
            CodeType = ShaderCodeTypeEXT.SpirvExt,
            CodeSize = (uint)shader.Bytecode.Length,
            PCode = shader.Bytecode,
            PName = shader.EntryPoint
        };
    }

    /// <summary>Makes the shader at <paramref name="stage"/>; the binary cache takes only one made for the same
    /// <paramref name="layout"/>.</summary>
    private protected void CreateShader(int stage, ShaderCreateInfoEXT createInfo, ulong layout)
    {
        Shaders[stage] = Device.CreateShader(createInfo, $"{cacheName}.{Pass.Stages[stage].Stage}", layout);
    }

    private protected const ulong LayoutSeed = 0xCBF29CE484222325UL;

    private protected static ulong Mix(ulong hash, ulong value)
    {
        var mixed = (hash ^ value) * 0x9E3779B97F4A7C15UL;
        return mixed ^ (mixed >> 31);
    }

    private void FillBindArrays(ShaderStageFlagBits present)
    {
        var empty = GetEmptyStages(present);
        var count = Stages.Length + BitOperations.PopCount((uint)empty);
        var stages = new ShaderStageFlagBits[count];
        var shaders = new ShaderEXT[count];
        Array.Copy(Stages, stages, Stages.Length);
        Array.Copy(Shaders, shaders, Shaders.Length);

        var next = Stages.Length;
        for (var bit = 1u; bit != 0 && next < count; bit <<= 1)
        {
            if (((uint)empty & bit) != 0)
            {
                stages[next++] = (ShaderStageFlagBits)bit;
            }
        }

        BindStages = stages;
        BindShaders = shaders;
    }

    private ShaderStageFlagBits GetEmptyStages(ShaderStageFlagBits present)
    {
        if ((present & ShaderStageFlagBits.ComputeBit) != 0)
        {
            return 0;
        }

        var device = Device.MainDevice;
        var empty = ShaderStageFlagBits.VertexBit | ShaderStageFlagBits.FragmentBit;
        if (device.IsTessellationEnabled)
        {
            empty |= ShaderStageFlagBits.TessellationControlBit | ShaderStageFlagBits.TessellationEvaluationBit;
        }

        if (device.IsMeshShaderEnabled)
        {
            empty |= ShaderStageFlagBits.MeshBitExt;
        }

        if (device.IsTaskShaderEnabled)
        {
            empty |= ShaderStageFlagBits.TaskBitExt;
        }

        return empty & ~present;
    }

    private NotSupportedException FindUnsupported()
    {
        if (Pass.HitGroups.Count > 0)
        {
            return CreateRayTracingError();
        }

        foreach (var link in Pass.Stages)
        {
            var missing = FindMissingFeature(link.Stage) ?? FindUnboundResource(Data.Shaders[link.Index]);
            if (missing != null)
            {
                return missing;
            }
        }

        return null;
    }

    private NotSupportedException FindMissingFeature(EffectStage stage)
    {
        var device = Device.MainDevice;
        return stage switch
        {
            EffectStage.Hull or EffectStage.Domain when !device.IsTessellationEnabled =>
                CreateMissingFeatureError($"{stage} shaders", "the tessellationShader feature"),
            EffectStage.Mesh when !device.IsMeshShaderEnabled =>
                CreateMissingFeatureError("mesh shaders", "VK_EXT_mesh_shader and its meshShader feature"),
            EffectStage.Task when !device.IsTaskShaderEnabled =>
                CreateMissingFeatureError("task shaders", "VK_EXT_mesh_shader and its taskShader feature"),
            EffectStage.RayGeneration or EffectStage.Miss or EffectStage.Callable or EffectStage.ClosestHit
                or EffectStage.AnyHit or EffectStage.Intersection => CreateRayTracingError(),
            _ => null
        };
    }

    private NotSupportedException FindUnboundResource(EffectData.Shader shader)
    {
        foreach (var resource in shader.ResourceParameters)
        {
            if (!IsBoundByEffects(resource.Type))
            {
                return new NotSupportedException($"{Name}: '{resource.Name}' is a {resource.Type}; " +
                                                 "effects bind uniform and storage buffers, sampled images and samplers.");
            }
        }

        return null;
    }

    private static bool IsBoundByEffects(EffectParameterType type) => type is EffectParameterType.UniformBuffer
        or EffectParameterType.StorageBuffer or EffectParameterType.Sampler or EffectParameterType.SampledImage1D
        or EffectParameterType.SampledImage2D or EffectParameterType.SampledImage3D or EffectParameterType.SampledImageCube;

    private NotSupportedException CreateMissingFeatureError(string shaders, string feature) =>
        new($"{Name}: {shaders} need {feature}, which {Device.Adapter.AdapterProperties.DeviceName} does not have.");

    private NotSupportedException CreateRayTracingError() =>
        new($"{Name}: ray-tracing shaders run in a ray-tracing pipeline, which effects do not create yet.");

    /// <summary>Destroys the shaders; the cache retires them once no submitted work can still use them.</summary>
    public virtual void Dispose()
    {
        for (var i = 0; i < Shaders.Length; i++)
        {
            if (Shaders[i] != null)
            {
                Device.DestroyShader(Shaders[i]);
                Shaders[i] = null;
            }
        }
    }
}

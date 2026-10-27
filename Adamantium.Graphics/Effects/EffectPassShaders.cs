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
    private protected const ulong LayoutSeed = 0xCBF29CE484222325UL;

    /// <summary>How many effect passes hold these shaders; the cache counts them.</summary>
    internal int References;

    private protected readonly GraphicsDevice graphicsDevice;
    private protected readonly EffectData effectData;
    private protected readonly string passName;
    private protected readonly ShaderStageFlagBits[] stages;
    private protected readonly ShaderEXT[] shaders;
    private readonly string cacheName;
    private readonly object creating = new();
    private bool isCreated;

    private protected EffectPassShaders(GraphicsDevice device, EffectData data, EffectData.Pass pass, string effectName,
        string techniqueName, string passName)
    {
        graphicsDevice = device;
        effectData = data;
        Pass = pass;
        this.passName = $"{effectName}.{passName}";
        cacheName = $"{effectName}.{techniqueName}.{passName}";
        stages = new ShaderStageFlagBits[pass.Stages.Count];
        shaders = new ShaderEXT[pass.Stages.Count];
        UnsupportedReason = FindUnsupportedReason();
    }

    /// <summary>The data the shaders are made of.</summary>
    public EffectData.Pass Pass { get; }

    /// <summary>Why the device cannot run the pass; null when it can.</summary>
    public string UnsupportedReason { get; }

    /// <summary>The stages a draw with the pass binds, in pipeline order: the pass's own, then those it binds empty.
    /// </summary>
    public ShaderStageFlagBits[] BindStages { get; private set; } = [];

    /// <summary>The shaders of <see cref="BindStages"/>; null for an empty one.</summary>
    public ShaderEXT[] BindShaders { get; private set; } = [];

    /// <summary>How many control points the hull shader reads a patch of; 0 for a pass that does not tessellate.</summary>
    public uint InputPatchSize { get; private set; }

    /// <summary>Makes the shaders, once, unless the device cannot run the pass; those made are destroyed when one fails,
    /// and the next call tries again. Callers of other passes do not wait for it.</summary>
    /// <exception cref="NotSupportedException">The pass's resources do not fit the descriptor model or the device's
    /// limits.</exception>
    public void Create()
    {
        lock (creating)
        {
            if (isCreated)
            {
                return;
            }

            if (UnsupportedReason == null)
            {
                MakeShaders();
            }

            isCreated = true;
        }
    }

    /// <summary>Destroys the shaders; the cache retires them once no submitted work can still use them.</summary>
    public virtual void Dispose()
    {
        for (var i = 0; i < shaders.Length; i++)
        {
            if (shaders[i] != null)
            {
                graphicsDevice.DestroyShader(shaders[i]);
                shaders[i] = null;
            }
        }
    }

    private protected abstract void CreateShaders();

    private protected ShaderCreateInfoEXT NewCreateInfo(int stage)
    {
        var link = Pass.Stages[stage];
        var shader = effectData.Shaders[link.Index];
        var hasTaskShader = Pass.Stages.Exists(other => other.Stage == EffectStage.Task);
        return new ShaderCreateInfoEXT
        {
            Flags = link.Stage == EffectStage.Mesh && !hasTaskShader ? ShaderCreateFlagBitsEXT.NoTaskShaderBitExt : 0,
            Stage = stages[stage],
            NextStage = stage + 1 < stages.Length ? stages[stage + 1] : 0,
            CodeType = ShaderCodeTypeEXT.SpirvExt,
            CodeSize = (uint)shader.Bytecode.Length,
            PCode = shader.Bytecode,
            PName = shader.EntryPoint
        };
    }

    private protected void CreateShader(int stage, ShaderCreateInfoEXT createInfo, ulong layout)
    {
        shaders[stage] = graphicsDevice.CreateShader(createInfo, $"{cacheName}.{Pass.Stages[stage].Stage}", layout);
    }

    private protected static ulong Mix(ulong hash, ulong value)
    {
        var mixed = (hash ^ value) * 0x9E3779B97F4A7C15UL;
        return mixed ^ (mixed >> 31);
    }

    private void MakeShaders()
    {
        try
        {
            var present = (ShaderStageFlagBits)0;
            for (var i = 0; i < stages.Length; i++)
            {
                var link = Pass.Stages[i];
                stages[i] = EffectPass.GetShaderStage(link.Stage);
                present |= stages[i];
                if (link.Stage == EffectStage.Hull)
                {
                    InputPatchSize = effectData.Shaders[link.Index].InputPatchSize;
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

    private void FillBindArrays(ShaderStageFlagBits present)
    {
        var empty = GetEmptyStages(present);
        var count = stages.Length + BitOperations.PopCount((uint)empty);
        var bindStages = new ShaderStageFlagBits[count];
        var bindShaders = new ShaderEXT[count];
        Array.Copy(stages, bindStages, stages.Length);
        Array.Copy(shaders, bindShaders, shaders.Length);

        var next = stages.Length;
        for (var bit = 1u; bit != 0 && next < count; bit <<= 1)
        {
            if (((uint)empty & bit) != 0)
            {
                bindStages[next++] = (ShaderStageFlagBits)bit;
            }
        }

        BindStages = bindStages;
        BindShaders = bindShaders;
    }

    private ShaderStageFlagBits GetEmptyStages(ShaderStageFlagBits present)
    {
        if ((present & ShaderStageFlagBits.ComputeBit) != 0)
        {
            return 0;
        }

        var device = graphicsDevice.MainDevice;
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

    private string FindUnsupportedReason()
    {
        if (Pass.HitGroups.Count > 0)
        {
            return GetRayTracingReason();
        }

        foreach (var link in Pass.Stages)
        {
            var reason = FindMissingFeature(link.Stage) ?? FindUnboundResource(effectData.Shaders[link.Index]);
            if (reason != null)
            {
                return reason;
            }
        }

        return null;
    }

    private string FindMissingFeature(EffectStage stage)
    {
        var device = graphicsDevice.MainDevice;
        return stage switch
        {
            EffectStage.Hull or EffectStage.Domain when !device.IsTessellationEnabled =>
                GetMissingFeatureReason($"{stage} shaders", "the tessellationShader feature"),
            EffectStage.Mesh when !device.IsMeshShaderEnabled =>
                GetMissingFeatureReason("mesh shaders", "VK_EXT_mesh_shader and its meshShader feature"),
            EffectStage.Task when !device.IsTaskShaderEnabled =>
                GetMissingFeatureReason("task shaders", "VK_EXT_mesh_shader and its taskShader feature"),
            EffectStage.RayGeneration or EffectStage.Miss or EffectStage.Callable or EffectStage.ClosestHit
                or EffectStage.AnyHit or EffectStage.Intersection => GetRayTracingReason(),
            _ => null
        };
    }

    private string FindUnboundResource(EffectData.Shader shader)
    {
        foreach (var resource in shader.ResourceParameters)
        {
            if (!IsBoundByEffects(resource.Type))
            {
                return $"{passName}: '{resource.Name}' is a {resource.Type}; " +
                       "effects bind uniform and storage buffers, sampled images and samplers.";
            }
        }

        return null;
    }

    private static bool IsBoundByEffects(EffectParameterType type) => type is EffectParameterType.UniformBuffer
        or EffectParameterType.StorageBuffer or EffectParameterType.Sampler or EffectParameterType.SampledImage1D
        or EffectParameterType.SampledImage2D or EffectParameterType.SampledImage3D or EffectParameterType.SampledImageCube;

    private string GetMissingFeatureReason(string what, string feature) =>
        $"{passName}: {what} need {feature}, which {graphicsDevice.Adapter.AdapterProperties.DeviceName} does not have.";

    private string GetRayTracingReason() =>
        $"{passName}: ray-tracing shaders run in a ray-tracing pipeline, which effects do not create yet.";
}

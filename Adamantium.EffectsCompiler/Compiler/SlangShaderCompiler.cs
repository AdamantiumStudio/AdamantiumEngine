using System;
using Adamantium.Vulkan.Slang;

namespace Adamantium.EffectsCompiler.Compiler;

/// <summary>Engine-side adapter over <see cref="Adamantium.Vulkan.Slang.SlangCompiler"/>: engine configuration and type
/// mapping. One instance per effect compile; the session is reused across its stages.</summary>
internal sealed class SlangShaderCompiler : IDisposable
{
    /// <summary>The SPIR-V version every shader is compiled to.</summary>
    public const string SpirvProfile = "spirv_1_6";

    private SlangCompiler compiler;

    /// <param name="resolveInclude">Maps an <c>#include</c> path to file contents (null = not found).</param>
    public ShaderCompilationResult Compile(string source, string entryPoint, EffectShaderType stage,
        Func<string, string> resolveInclude)
    {
        compiler ??= new SlangCompiler(
            SpirvProfile,
            // VulkanUseEntryPointName keeps the real entry-point name in the SPIR-V (instead of "main") so Vulkan can
            // create shaders by the shader's name.
            options:
            [
                new SlangcCompilerOption
                {
                    Name = (int)SlangcCompilerOptionName.VulkanUseEntryPointName,
                    ValueKind = 0,
                    IntValue0 = 1
                }
            ]);
        compiler.IncludeResolver = resolveInclude;
        try
        {
            var result = compiler.Compile(source, entryPoint, MapStage(stage));
            return new ShaderCompilationResult
            {
                Bytecode = result.Spirv,
                HasErrors = !result.Success,
                Errors = result.Diagnostics
            };
        }
        finally
        {
            compiler.IncludeResolver = null;
        }
    }

    private static SlangcStage MapStage(EffectShaderType type)
    {
        switch (type)
        {
            case EffectShaderType.Vertex: return SlangcStage.Vertex;
            case EffectShaderType.Hull: return SlangcStage.Hull;
            case EffectShaderType.Domain: return SlangcStage.Domain;
            case EffectShaderType.Geometry: return SlangcStage.Geometry;
            case EffectShaderType.Fragment: return SlangcStage.Fragment;
            case EffectShaderType.Compute: return SlangcStage.Compute;
            default: throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported shader stage for Slang.");
        }
    }

    public void Dispose()
    {
        compiler?.Dispose();
        compiler = null;
    }
}

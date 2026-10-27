using System;
using Adamantium.Vulkan.Slang;

namespace Adamantium.EffectsCompiler.Compiler;

/// <summary>Engine-side adapter over <see cref="Adamantium.Vulkan.Slang.SlangCompiler"/>: engine configuration and type
/// mapping. One instance per effect compile; the session is reused across its stages.</summary>
internal sealed class SlangShaderCompiler : IDisposable
{
    /// <summary>The SPIR-V version every shader is compiled to. Its entry points keep their own names, not
    /// <c>main</c>, so shaders are created by them.</summary>
    public const string SpirvProfile = "spirv_1_6";

    private SlangCompiler compiler;

    /// <param name="resolveInclude">Maps an <c>#include</c> path to file contents (null = not found).</param>
    public ShaderCompilationResult Compile(string source, string entryPoint, EffectShaderType stage,
        Func<string, string> resolveInclude)
    {
        compiler ??= new SlangCompiler(
            SpirvProfile,
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

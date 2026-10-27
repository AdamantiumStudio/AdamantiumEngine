using System;
using Adamantium.Vulkan.Slang;

namespace Adamantium.EffectsCompiler;

/// <summary>Engine-side adapter over <see cref="Adamantium.Vulkan.Slang.SlangCompiler"/>: engine configuration and type
/// mapping. One instance per effect compile; the session is reused across its stages.</summary>
internal sealed class SlangShaderCompiler : IDisposable
{
    private SlangCompiler compiler;

    /// <param name="resolveInclude">Maps an <c>#include</c> path to file contents (null = not found).</param>
    public ShaderCompilationResult Compile(string source, string entryPoint, SlangcStage stage,
        Func<string, string> resolveInclude)
    {
        compiler ??= new SlangCompiler(
            EffectCompiler.SpirvProfile,
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
            var result = compiler.Compile(source, entryPoint, stage);
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

    public void Dispose()
    {
        compiler?.Dispose();
        compiler = null;
    }
}

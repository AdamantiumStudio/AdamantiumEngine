namespace Adamantium.EffectsCompiler;

/// <summary>
/// The result of compiling a single shader stage to SPIR-V. It stays a type of its own, rather than the backend's
/// own result, so reflection and parameter building keep not caring who emitted the bytecode - which is what let the
/// second backend be removed without touching either of them.
/// </summary>
internal sealed class ShaderCompilationResult
{
    public byte[] Bytecode { get; set; }

    public bool HasErrors { get; set; }

    public string Errors { get; set; }

    public static implicit operator byte[](ShaderCompilationResult result) => result?.Bytecode;
}

using System;
using System.Linq;
using Adamantium.EffectsCompiler;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests;

/// <summary>Every shader is compiled to SPIR-V 1.6: the version comes back out of the SPIR-V header and is recorded on
/// the shader, and a pass cannot ask for another.</summary>
[TestFixture]
public class SpirvProfileTests
{
    private const string Source = """
        float4 Tint;

        [shader("vertex")]
        float4 VS(in float4 position : POSITION) : SV_POSITION
        {
            return position;
        }

        [shader("fragment")]
        float4 PS(in float4 position : SV_POSITION) : SV_Target0
        {
            return Tint;
        }

        technique Only
        {
            pass P0
            {
        {0}        VertexShader = VS;
                PixelShader = PS;
            }
        }
        """;

    // A real directory, not a bare name: the compiler enumerates the source's folder looking for includes.
    private static string SourcePath =>
        System.IO.Path.Combine(TestContext.CurrentContext.TestDirectory, "ProfileTest.fx");

    private static string WithLine(string line) => Source.Replace("{0}", line);

    // Word 1 of a SPIR-V module is its version: major in the third byte, minor in the second.
    private static (int Major, int Minor) SpirvVersion(byte[] bytecode)
    {
        Assert.That(bytecode, Is.Not.Null.And.Length.GreaterThan(8), "no SPIR-V came back");
        Assert.That(BitConverter.ToUInt32(bytecode, 0), Is.EqualTo(0x07230203u), "not a SPIR-V module");

        var version = BitConverter.ToUInt32(bytecode, 4);
        return ((int)((version >> 16) & 0xFF), (int)((version >> 8) & 0xFF));
    }

    private static EffectData.Shader FirstShader()
    {
        var result = EffectCompiler.Compile(WithLine(string.Empty), SourcePath);
        Assert.That(result.HasErrors, Is.False,
            $"failed to compile:{Environment.NewLine}{string.Join(Environment.NewLine, result.Logger.Messages)}");
        return result.EffectData.Shaders.First(s => s.Bytecode is { Length: > 0 });
    }

    [Test]
    public void AShader_IsCompiledToSpirv16()
    {
        Assert.That(SpirvVersion(FirstShader().Bytecode), Is.EqualTo((1, 6)));
    }

    [Test]
    public void TheVersionIsRecordedOnTheShader()
    {
        Assert.That(FirstShader().Level, Is.EqualTo("spirv_1_6"));
    }

    // Profile was a pass key once; the effect language has no such key now, so naming it is an error that says so.
    [Test]
    public void AProfileInAPass_IsAnError()
    {
        var result = EffectCompiler.Compile(WithLine("            Profile = 1.3;" + Environment.NewLine), SourcePath);

        Assert.That(result.HasErrors, Is.True);
        Assert.That(string.Join(Environment.NewLine, result.Logger.Messages), Does.Contain("Profile"));
    }
}

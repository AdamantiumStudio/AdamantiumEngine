using System.Collections.Generic;
using System.Linq;
using Adamantium.EffectsCompiler;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests;

/// <summary>An effect file's techniques and passes as the effect language has them: every stage a pass may name, one
/// kind of pipeline a pass, what breaks the rules said where it stands; the code around them goes to Slang on its own
/// lines, with the macros given.</summary>
[TestFixture]
public class EffectSourceTests
{
    private const string Shaders = @"
float4 VS(float4 position : POSITION) : SV_Position { return position; }
float4 PS() : SV_Target { return float4(1, 0, 0, 1); }
[numthreads(1, 1, 1)]
void CS() {}
";

    private static EffectCompilerResult Compile(string source, List<EffectData.ShaderMacro> macros = null) =>
        EffectCompiler.Compile(source, "Test.fx", macrosArgs: macros);

    private static string Messages(EffectCompilerResult result) =>
        string.Join("\n", result.Logger.Messages.Select(message => message.ToString()));

    [Test]
    public void TechniquesAndPasses_AreReadWithTheirStages_ByName()
    {
        var result = Compile(Shaders + @"
technique Draw
{
    pass First { VertexShader = VS; PixelShader = PS; }
    pass Second { VertexShader = VS; }
}
technique { pass { ComputeShader = CS; } }");

        Assert.That(result.HasErrors, Is.False, Messages(result));
        var techniques = result.EffectData.Description.Techniques;
        Assert.Multiple(() =>
        {
            Assert.That(result.EffectData.Description.Name, Is.EqualTo("Test"), "the file's name");
            Assert.That(techniques.Select(t => t.Name), Is.EqualTo(new[] { "Draw", null }));
            Assert.That(techniques[0].Passes.Select(p => p.Name), Is.EqualTo(new[] { "First", "Second" }));
            Assert.That(techniques[0].Passes[0].Pipeline[EffectShaderType.Fragment].EntryPoint, Is.EqualTo("PS"));
            Assert.That(techniques[1].Passes[0].Pipeline[EffectShaderType.Compute].EntryPoint, Is.EqualTo("CS"));
        });
    }

    // A technique quoted in a comment or a string is code, not a technique.
    [Test]
    public void TheWordTechnique_InACommentOrAString_IsLeftAlone()
    {
        var result = Compile(@"
// technique Commented { pass { NotAStage = X; } }
/* technique Blocked { pass { NotAStage = X; } } */
static const int technique_count = 1;
" + Shaders + @"
technique Real { pass { VertexShader = VS; } }");

        Assert.That(result.HasErrors, Is.False, Messages(result));
        Assert.That(result.EffectData.Description.Techniques.Single().Name, Is.EqualTo("Real"));
    }

    [TestCase("pass { EffectName = \"X\"; VertexShader = VS; }", "(3,22)", "'EffectName' is not a stage")]
    [TestCase("pass { GeometryShader = VS; }", "(3,22)", "'GeometryShader' is not a stage")]
    [TestCase("pass { VertexShader = VS }", "(3,40)", "';' expected")]
    [TestCase("pass { VertexShader = ; }", "(3,37)", "An entry point's name")]
    [TestCase("pass { VertexShader = VS; ComputeShader = CS; }", "(3,15)", "one kind of pipeline, not graphics and compute")]
    [TestCase("pass { VertexShader = VS; VertexShader = PS; }", "(3,41)", "names its Vertex shader twice")]
    [TestCase("pass { PixelShader = PS; }", "(3,15)", "A graphics pass has a vertex shader")]
    [TestCase("pass { VertexShader = VS; HullShader = VS; }", "(3,15)", "Hull and domain shaders go together")]
    [TestCase("pass { TaskShader = CS; }", "(3,15)", "A mesh pass has a mesh shader")]
    [TestCase("pass { MissShader = CS; }", "(3,15)", "A ray-tracing pass has one ray generation shader")]
    [TestCase("pass { ComputeShader = CS; PixelShader = PS; }", "(3,15)", "A compute pass has no pixel shader")]
    [TestCase("pass { }", "(3,15)", "The pass runs no shader")]
    [TestCase("pass P { VertexShader = VS; } pass P { VertexShader = VS; }", "(3,45)", "two passes named 'P'")]
    [TestCase("pass { RayGenerationShader = CS; HitGroup H { Closest = CS; } }", "(3,61)", "not a shader of a hit group")]
    public void WhatBreaksTheRules_IsSaidWhereItStands(string pass, string where, string what)
    {
        var result = Compile($"\n\ntechnique T {{ {pass} }}\n" + Shaders);

        Assert.That(result.HasErrors, Is.True);
        Assert.That(Messages(result), Does.Contain("Test.fx" + where).And.Contain(what));
    }

    // The language describes mesh and ray-tracing passes; until effects run them, compiling one says so.
    [TestCase("pass { TaskShader = CS; MeshShader = CS; PixelShader = PS; }", "Task shaders are described")]
    [TestCase("pass { RayGenerationShader = CS; MissShader = CS; MissShader = CS; HitGroup H { ClosestHit = CS; AnyHit = CS; } }",
        "RayGeneration shaders are described")]
    public void APassOfAPipelineEffectsDoNotRunYet_IsReadAndSaidSo(string pass, string what)
    {
        var result = Compile($"technique T {{ {pass} }}\n" + Shaders);

        Assert.That(Messages(result), Does.Contain(what).And.Not.Contain("is not a stage"));
    }

    // Slang reports on the file's own lines: the technique before the code takes none of them away.
    [Test]
    public void AnErrorInTheCode_IsOnItsLine_AfterATechnique()
    {
        var result = Compile("technique T\n{\n    pass { VertexShader = VS; }\n}\nfloat4 VS() : SV_Position { return undefinedName; }\n");

        Assert.That(result.HasErrors, Is.True);
        Assert.That(Messages(result), Does.Contain(":5:36").And.Contain("undefinedName"));
    }

    [Test]
    public void TheMacrosGiven_AreDefinedForSlang()
    {
        const string source = @"
#ifdef WITH_ENTRY
float4 VS(float4 position : POSITION) : SV_Position { return position * SCALE; }
#endif
technique T { pass { VertexShader = VS; } }";

        var without = Compile(source);
        var with = Compile(source, [new EffectData.ShaderMacro("WITH_ENTRY", "1"), new EffectData.ShaderMacro("SCALE", "2")]);

        Assert.Multiple(() =>
        {
            Assert.That(without.HasErrors, Is.True, "no entry point without the macro");
            Assert.That(with.HasErrors, Is.False, Messages(with));
        });
    }
}

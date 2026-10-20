using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Adamantium.EffectsCompiler;
using MessagePack;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests
{
    [TestFixture]
    public class EffectsCompilerTests
    {
        /// <summary>A compile that FAILS must say so with the compiler's own messages. Dereferencing EffectData without
        /// asking whether the compile succeeded turned every shader error into a bare NullReferenceException with the
        /// reason thrown away - which is why this sat red without anyone being able to tell what it was complaining about.
        /// </summary>
        private static EffectData CompileOrFail(string path)
        {
            var result = EffectCompiler.CompileFromFile(path);
            var messages = string.Join(Environment.NewLine, result.Logger.Messages);
            Assert.That(result.HasErrors, Is.False, $"{path} failed to compile:{Environment.NewLine}{messages}");
            Assert.That(result.EffectData, Is.Not.Null, $"{path} compiled without errors but produced no EffectData");
            return result.EffectData;
        }

        [Test]
        public void UIEffectParsingTest()
        {
            var path = Path.Combine("CompilerEffects", "UIEffect.fx");
            if (!File.Exists(path)) Assert.Ignore($"missing test asset: {path}");

            var effectData = CompileOrFail(path);
            effectData.Save("UIEffect.fx.compiled");
            Assert.That(EffectData.Load("UIEffect.fx.compiled"), Is.Not.Null, "the saved effect did not load back");
        }
        
        [Test]
        public void BasicEffectParsingTest()
        {
            try
            {
                var path = Path.Combine("CompilerEffects", "BasicEffect.fx");
                if (File.Exists(path))
                {
                    var text = File.ReadAllText(path);
                    var result = EffectCompiler.CompileFromFile(path);
                    
                    var effectParameters = new List<string>();
                    var parametersIdentity = new HashSet<string>();
                    foreach (var shader in result.EffectData.Shaders)
                    {
                        foreach (var constantBuffer in shader.ConstantBuffers)
                        {
                            foreach (var parameter in constantBuffer.Parameters)
                            {
                                if (parametersIdentity.Contains(parameter.Name)) continue;

                                parametersIdentity.Add(parameter.Name);
                                var name = char.ToUpper(parameter.Name[0]) + parameter.Name.Substring(1);
                                effectParameters.Add(name);
                                //textGenerator.WriteLine($"{name} = Parameters[\"{parameter.Name}\"];");
                            }
                        }

                        foreach (var resource in shader.ResourceParameters)
                        {
                            if (parametersIdentity.Contains(resource.Name) || resource.Name == "type.$Globals") continue;

                            parametersIdentity.Add(resource.Name);
                            var name = char.ToUpper(resource.Name[0]) + resource.Name.Substring(1);
                            effectParameters.Add(name);
                            //textGenerator.WriteLine($"{name} = Parameters[\"{resource.Name}\"];");
                        }
                    }
            
                    //result.EffectData.Save("BasicEffect.fx.compiled");
                    //var restored = EffectData.Load("BasicEffect.fx.compiled");
                }
                
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
            
        }

        [Test]
        public void EffectSerializationTest()
        {
            var path = Path.Combine("CompilerEffects", "UIEffect.fx");
            if (File.Exists(path))
            {
                var result = EffectCompiler.CompileFromFile(path);

                var memoryStream = new MemoryStream();
                //MessagePackSerializer.DefaultOptions = MessagePack.Resolvers.ContractlessStandardResolver.Options;
                MessagePackSerializer.Serialize(memoryStream, result.EffectData);
                memoryStream.Position = 0;
                var results2 = MessagePackSerializer.Deserialize<EffectData>(memoryStream);
                Assert.NotNull(results2);
            }
        }
        
        [Test]
        public void EffectDeserializationTest()
        {
            var path = Path.Combine("CompilerEffects", "BasicEffect.fx");
            if (File.Exists(path))
            {
                var result = EffectCompiler.CompileFromFile(path);
                result.EffectData.Save("BasicEffect1");
                var restored = EffectData.Load("BasicEffect1");
            }
        }

        // Two headers. Tint.fxh is what the effect calls; it pulls in Bee.fxh, and THAT is what makes the compiler parse
        // Tint.fxh at all - an include's content is only handed to the include parser when it literally contains
        // "#include" (EffectCompilerInternal), and unlike the main source its comments are never stripped. So a header
        // that includes anything is exactly where a directive hiding in a comment gets read as code.
        private static ImmutableArray<ShaderFileInfo> TintInclude(string commentedLine = "") => ImmutableArray.Create(
            new ShaderFileInfo
            {
                FileName = "Tint.fxh",
                Path = Path.Combine("Includes", "Tint.fxh"),
                Content = "#include \"Includes/Bee.fxh\"\n" + commentedLine + "float4 Tint(float4 c) { return c * Bee(); }\n"
            },
            new ShaderFileInfo
            {
                FileName = "Bee.fxh",
                Path = Path.Combine("Includes", "Bee.fxh"),
                Content = "float Bee() { return 1; }\n"
            });

        private static string EffectCalling(string includeLine) =>
            includeLine + @"
float4 VS(float4 p : POSITION) : SV_Position { return p; }
float4 PS(float4 p : SV_Position) : SV_Target0 { return Tint(float4(1, 1, 1, 1)); }
technique T { pass P { VertexShader = VS; PixelShader = PS; } }
";

        /// <summary>A file name without a folder is in the current one, as everywhere else: looking for its headers in
        /// the folder "" threw before anything was compiled.</summary>
        [Test]
        public void ASourceNamedWithoutAFolder_Compiles()
        {
            var result = EffectCompiler.Compile(@"
float4 VS(float4 p : POSITION) : SV_Position { return p; }
float4 PS(float4 p : SV_Position) : SV_Target0 { return p; }
technique T { pass P { VertexShader = VS; PixelShader = PS; } }
", "Probe.fx");

            Assert.That(result.HasErrors, Is.False, string.Join(Environment.NewLine, result.Logger.Messages));
        }

        /// <summary>A file's headers are looked for in the folders under it too: one in its Includes folder was not found.
        /// </summary>
        [Test]
        public void AFile_FindsTheHeadersInTheFoldersUnderIt()
        {
            var folder = Path.Combine(Path.GetTempPath(), "adamantium-effect-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(folder, "Includes"));
                foreach (var header in TintInclude())
                {
                    File.WriteAllText(Path.Combine(folder, header.Path), header.Content);
                }

                var path = Path.Combine(folder, "Probe.fx");
                File.WriteAllText(path, EffectCalling("#include \"Includes/Tint.fxh\"\n"));

                CompileOrFail(path);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        /// <summary>A header is named by its whole name: "Tint.fxh" is not a header whose name only ends so.</summary>
        [Test]
        public void AHeader_IsNotTakenForAnotherWhoseNameEndsTheSame()
        {
            var offered = ImmutableArray.Create(
                new ShaderFileInfo
                {
                    FileName = "BlueTint.fxh",
                    Path = Path.Combine("Includes", "BlueTint.fxh"),
                    Content = "float4 Tint(float4 c) { return c * float4(0, 0, 1, 1); }\n"
                },
                new ShaderFileInfo
                {
                    FileName = "Tint.fxh",
                    Path = Path.Combine("Includes", "Tint.fxh"),
                    Content = "float4 Tint(float4 c) { return c; }\n"
                });

            var result = EffectCompiler.Compile(EffectCalling("#include \"Tint.fxh\"\n"), "Probe.fx", offered);

            Assert.That(result.HasErrors, Is.False, string.Join(Environment.NewLine, result.Logger.Messages));
            Assert.That(result.Includes, Is.EquivalentTo(new[] { Path.Combine("Includes", "Tint.fxh") }));
        }

        /// <summary>A value parameter is described by its own type. The reflected base type used to be cast straight to
        /// the D3D-numbered enum, so a uint came out as Texture3D and a float as Sampler3D.</summary>
        [Test]
        public void AValueParameter_IsDescribedByItsType()
        {
            var result = EffectCompiler.Compile(@"
uint64_t Address;
uint Count;
int Offset;
float Scale;
float4 Tint;
double Precise;

[shader(""compute"")]
[numthreads(1, 1, 1)]
void MainCS(uint3 tid : SV_DispatchThreadID)
{
    uint* output = (uint*)Address;
    output[0] = Count + (uint)Offset + (uint)(Scale * Tint.x) + (uint)Precise;
}

technique T { pass P { ComputeShader = MainCS; } }
", "Probe.fx");

            Assert.That(result.HasErrors, Is.False, string.Join(Environment.NewLine, result.Logger.Messages));
            var types = result.EffectData.Shaders
                .SelectMany(shader => shader.ConstantBuffers)
                .SelectMany(buffer => buffer.Parameters)
                .ToDictionary(parameter => parameter.Name, parameter => parameter.Type);
            Assert.That(types, Is.EquivalentTo(new Dictionary<string, EffectParameterType>
            {
                ["Address"] = EffectParameterType.UInt64,
                ["Count"] = EffectParameterType.UInt,
                ["Offset"] = EffectParameterType.Int,
                ["Scale"] = EffectParameterType.Float,
                ["Tint"] = EffectParameterType.Float,
                ["Precise"] = EffectParameterType.Double,
            }));
        }

        /// <summary>What a hot reload watches: every header the compile pulled in, the nested one too, and nothing it
        /// was merely offered.</summary>
        [Test]
        public void Includes_NameEveryHeaderPulledIn_NestedOnesToo()
        {
            var offered = TintInclude().Add(new ShaderFileInfo
            {
                FileName = "Unused.fxh",
                Path = Path.Combine("Includes", "Unused.fxh"),
                Content = "float Unused() { return 0; }\n"
            });

            string[] pulledIn = [Path.Combine("Includes", "Tint.fxh"), Path.Combine("Includes", "Bee.fxh")];

            var result = EffectCompiler.Compile(EffectCalling("#include \"Includes/Tint.fxh\"\n"), "Probe.fx", offered);

            Assert.That(result.HasErrors, Is.False, string.Join(Environment.NewLine, result.Logger.Messages));
            Assert.That(result.Includes, Is.EquivalentTo(pulledIn));
        }

        /// <summary>The baseline for the test below: a REAL #include is expanded, so the function it defines resolves.</summary>
        [Test]
        public void IncludeIsExpanded()
        {
            var result = EffectCompiler.Compile(EffectCalling("#include \"Includes/Tint.fxh\"\n"), "Probe.fx", TintInclude());

            var messages = string.Join(Environment.NewLine, result.Logger.Messages);
            Assert.That(result.HasErrors, Is.False, $"a real include did not resolve:{Environment.NewLine}{messages}");
        }

        /// <summary>An array of textures is reflected with its length, and one of no length - the resource heap read as an
        /// array of textures - with none. The element's type used to be read for the whole variable, so both came out as one
        /// texture.</summary>
        [Test]
        public void TextureArrays_AreReflectedWithTheirLength()
        {
            var source = @"
sampler s;
Texture2D four[4];
Texture2D heap[];
struct P { float4 pos : SV_Position; nointerpolation uint t : TEXCOORD0; };
P VS(float4 p : POSITION, uint id : SV_InstanceID) { P o; o.pos = p; o.t = id; return o; }
float4 PS(P i) : SV_Target0
{
    return heap[NonUniformResourceIndex(i.t)].Sample(s, float2(0, 0)) + four[i.t % 4].Sample(s, float2(0, 0));
}
technique T { pass P { VertexShader = VS; PixelShader = PS; } }
";
            var result = EffectCompiler.Compile(source, "Probe.fx", ImmutableArray<ShaderFileInfo>.Empty);

            Assert.That(result.HasErrors, Is.False, string.Join(Environment.NewLine, result.Logger.Messages));
            var parameters = result.EffectData.Shaders.SelectMany(shader => shader.ResourceParameters).ToList();
            Assert.That(parameters.First(p => p.Name == "four").Count, Is.EqualTo(4));
            Assert.That(parameters.First(p => p.Name == "heap").Count, Is.EqualTo(0));
        }

        /// <summary>The source's own conditions and macros are Slang's to apply: the effect parser, reading the techniques
        /// only, passes every directive it does not need over to the end of its line, leaving nothing of it to the grammar -
        /// not the value of a define, not the first word of the next line after an #endif.</summary>
        [Test]
        public void PreprocessorDirectives_ArePassedToTheBackend()
        {
            var source = "#pragma once\n"
                       + "#define TINTED 1\n"
                       + "#ifndef UNTINTED\n"
                       + "#include \"Includes/Tint.fxh\"\n"
                       + "#elif TINTED > 1\n"
                       + "#error never\n"
                       + "#else\n"
                       + "#warning never\n"
                       + "#endif\n"
                       + "#undef TINTED\n"
                       + "#define LONG(x) \\\n    ((x) + \\\n    1)\n"
                       + "#define LEAKED \\\n    technique Leaked { pass P { VertexShader = VS; PixelShader = PS; } }\n"
                       + "#if 1\n"
                       + "float Unused() { return 0; }\n"
                       + "#endif\n"
                       + EffectCalling("");

            var result = EffectCompiler.Compile(source, "Probe.fx", TintInclude());

            var messages = string.Join(Environment.NewLine, result.Logger.Messages);
            Assert.That(result.HasErrors, Is.False, $"a directive reached the grammar:{Environment.NewLine}{messages}");
            Assert.That(result.EffectData.Description.Techniques.Select(t => t.Name), Does.Not.Contain("Leaked"),
                "the line a backslash carried a define onto was read as code");
        }

        /// <summary>A preprocessor directive inside a comment is not a directive; comments reach the parser as written, so the
        /// tokenizer must hand them to the comment rule.</summary>
        [Test]
        public void DirectiveInsideACommentIsNotObeyed()
        {
            var source = "// #banana - a directive that only a parser reading comments as code would ever see\n"
                       + EffectCalling("#include \"Includes/Tint.fxh\"\n");

            var result = EffectCompiler.Compile(source, "Probe.fx", TintInclude());

            var messages = string.Join(Environment.NewLine, result.Logger.Messages);
            Assert.That(messages, Does.Not.Contain("Unsupported preprocessor token"),
                $"a `#` inside a comment was taken for a directive:{Environment.NewLine}{messages}");
            Assert.That(result.HasErrors, Is.False, $"the effect did not compile:{Environment.NewLine}{messages}");
        }
    }
}
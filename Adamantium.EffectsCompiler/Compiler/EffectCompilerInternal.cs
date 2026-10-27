using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Adamantium.Core;
using Adamantium.Vulkan.Spirv.Cross;
using Adamantium.Vulkan.Spirv.Reflection;

namespace Adamantium.EffectsCompiler.Compiler;

internal sealed class EffectCompilerInternal : IDisposable
{
    private readonly List<string> resolvedIncludes = [];
    private readonly Dictionary<(EffectShaderType Type, string EntryPoint), int> compiledShaders = [];
    private readonly Dictionary<(EffectShaderType Type, string EntryPoint), string> failedShaders = [];
    private readonly HashSet<(string Text, SourceLocation Location)> reportedSlangErrors = [];
    private readonly List<ResourceBindingKey> resourceKeys = [];
    private readonly SlangShaderCompiler slangCompiler = new();
    private EffectCompilerFlags compilerFlags;
    private EffectData effectData;
    private EffectCompilerLogger logger;
    private List<EffectData.ShaderMacro> macros;
    private ImmutableArray<ShaderFileInfo> includes;
    private string sourceDirectory;
    private string slangSource;
    private EffectData.Technique technique;
    private EffectData.Pass pass;

    /// <summary>Compiles the effect at <paramref name="filePath"/>.</summary>
    public EffectCompilerResult CompileFromFile(string filePath, EffectCompilerFlags flags = EffectCompilerFlags.None,
        List<EffectData.ShaderMacro> macros = null, List<string> includeDirectoryList = null,
        bool allowDynamicCompiling = false) =>
        Compile(File.ReadAllText(filePath, Encoding.UTF8), filePath, flags, macros, includeDirectoryList,
            allowDynamicCompiling);

    /// <summary>Compiles the effect <paramref name="sourceCode"/> of <paramref name="filePath"/>, its includes found
    /// beside it; <paramref name="macrosArgs"/> are defined for Slang. With <paramref name="allowDynamicCompiling"/> the
    /// effect keeps what it was compiled from, to compile it again.</summary>
    public EffectCompilerResult Compile(string sourceCode, string filePath, EffectCompilerFlags flags = EffectCompilerFlags.None,
        List<EffectData.ShaderMacro> macrosArgs = null, List<string> includeDirectoryList = null,
        bool allowDynamicCompiling = false)
    {
        compilerFlags = flags;
        macros = macrosArgs ?? [];
        InternalCompile(sourceCode, filePath);

        var result = new EffectCompilerResult(effectData, logger, resolvedIncludes);
        if (allowDynamicCompiling && !result.HasErrors && result.EffectData != null)
        {
            result.EffectData.Description.Arguments = new EffectData.CompilerArguments
            {
                FilePath = filePath,
                Macros = [.. macros],
                IncludeDirectoryList = [.. includeDirectoryList ?? []],
                Includes = [.. resolvedIncludes]
            };
        }

        return result;
    }

    /// <summary>Compiles the effect <paramref name="sourceCode"/> of <paramref name="filePath"/> with
    /// <paramref name="includes"/> as the files it may include.</summary>
    public EffectCompilerResult Compile(string sourceCode, string filePath, ImmutableArray<ShaderFileInfo> includes,
        List<EffectData.ShaderMacro> macrosArgs = null, List<string> includeDirectoryList = null)
    {
        macros = macrosArgs ?? [];
        this.includes = includes;
        InternalCompile(sourceCode, filePath);
        return new EffectCompilerResult(effectData, logger, resolvedIncludes);
    }

    /// <summary>The reflection of <paramref name="shader"/>'s SPIR-V.</summary>
    public SpirvReflectionResult DisassembleShader(EffectData.Shader shader)
    {
        return new SpirvReflection(shader.Bytecode).Disassemble(resourceKeys);
    }

    /// <summary>Frees the Slang session.</summary>
    public void Dispose() => slangCompiler.Dispose();

    private void InternalCompile(string sourceCode, string fileName)
    {
        effectData = null;
        resolvedIncludes.Clear();
        compiledShaders.Clear();
        failedShaders.Clear();
        reportedSlangErrors.Clear();
        logger = new EffectCompilerLogger();
        if (sourceCode is { Length: > 0 } && sourceCode[0] == (char)0xFEFF)
        {
            sourceCode = sourceCode.Substring(1);
        }

        sourceDirectory = null;
        if (includes.IsDefault)
        {
            var directory = Path.GetDirectoryName(fileName);
            sourceDirectory = string.IsNullOrEmpty(directory) ? "." : directory;
            includes = Directory.Exists(sourceDirectory)
                ? Directory.GetFiles(sourceDirectory, "*.fxh")
                    .Select(path => new ShaderFileInfo
                    {
                        Content = File.ReadAllText(path), Path = path, FileName = Path.GetFileName(path)
                    })
                    .ToImmutableArray()
                : [];
        }

        var start = new SourceLocation(fileName, 1, 1);
        var source = EffectSource.Read(sourceCode, fileName, logger);
        if (!logger.HasErrors && source.Techniques.Count == 0)
        {
            logger.Error("The effect has no technique: technique Name { pass Name { VertexShader = Main; ... } }", start);
        }

        var preamble = Preamble(fileName, start);
        if (logger.HasErrors)
        {
            return;
        }

        slangSource = preamble + source.Code;
        effectData = new EffectData
        {
            Shaders = [],
            Description = new EffectData.Effect { Name = Path.GetFileNameWithoutExtension(fileName), Techniques = [] }
        };

        foreach (var techniqueSource in source.Techniques)
        {
            technique = new EffectData.Technique { Name = techniqueSource.Name, Passes = [] };
            effectData.Description.Techniques.Add(technique);
            foreach (var passSource in techniqueSource.Passes)
            {
                CompilePass(passSource);
            }
        }
    }

    private string Preamble(string fileName, SourceLocation start)
    {
        var preamble = new StringBuilder();
        foreach (var macro in macros)
        {
            var value = macro.Value ?? string.Empty;
            if (!IsIdentifier(macro.Name) || value.IndexOfAny(['\r', '\n']) >= 0 || value.TrimEnd().EndsWith("\\")
                || value.Contains("/*") || value.Contains("//"))
            {
                logger.Error($"The macro '{macro.Name}' is not a name with a value of one line and no comment.", start);
                continue;
            }

            preamble.Append("#define ").Append(macro.Name).Append(' ').Append(value).Append('\n');
        }

        var path = fileName.Replace('\\', '/').Replace("\"", "\\\"");
        return preamble.Append("#line 1 \"").Append(path).Append("\"\n").ToString();
    }

    private static bool IsIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name) || !(char.IsLetter(name[0]) || name[0] == '_'))
        {
            return false;
        }

        return name.All(c => c < 128 && (char.IsLetterOrDigit(c) || c == '_'));
    }

    private void CompilePass(EffectPassSource passSource)
    {
        resourceKeys.Clear();
        pass = new EffectData.Pass
        {
            Name = passSource.Name,
            Pipeline = new EffectData.Pipeline(),
            Properties = new EffectPropertyCollection()
        };
        technique.Passes.Add(pass);
        foreach (var stage in passSource.Stages)
        {
            if (TypeOf(stage.Stage) is { } type)
            {
                CompileShader(type, stage);
            }
            else
            {
                logger.Error($"{stage.Stage} shaders are described, but effects do not run them yet.", stage.Location);
            }
        }

        if (passSource.HitGroups.Count > 0)
        {
            logger.Error("Hit groups are described, but effects do not run ray tracing yet.", passSource.HitGroups[0].Location);
        }
    }

    private static EffectShaderType? TypeOf(EffectStage stage) => stage switch
    {
        EffectStage.Vertex => EffectShaderType.Vertex,
        EffectStage.Pixel => EffectShaderType.Fragment,
        EffectStage.Compute => EffectShaderType.Compute,
        _ => null
    };

    private void CompileShader(EffectShaderType type, EffectStageSource stage)
    {
        var entryPoint = stage.EntryPoint;
        var key = (type, entryPoint);
        if (compiledShaders.TryGetValue(key, out var compiled))
        {
            LinkShader(type, compiled, entryPoint);
            return;
        }

        if (failedShaders.TryGetValue(key, out var slangErrors))
        {
            if (slangErrors != null)
            {
                ReportSlangErrors(slangErrors, stage);
            }

            return;
        }

        var errorsBefore = logger.Messages.Count;
        try
        {
            var result = slangCompiler.Compile(slangSource, entryPoint, type, ResolveSlangInclude);
            if (result.HasErrors || result.Bytecode == null)
            {
                failedShaders[key] = result.Errors ?? string.Empty;
                ReportSlangErrors(result.Errors, stage);
                return;
            }

            var shader = CreateEffectShader(type, entryPoint, result, stage.Location);
            if (logger.Messages.Count > errorsBefore)
            {
                failedShaders[key] = null;
                return;
            }

            ProcessShaderData(type, result, shader);
            compiledShaders[key] = pass.Pipeline[type].Index;
        }
        catch (Exception ex)
        {
            failedShaders[key] = null;
            logger.Error($"{stage.Stage} shader '{entryPoint}': {ex.Message}", stage.Location);
        }
    }

    private void ReportSlangErrors(string errors, EffectStageSource stage)
    {
        var all = SlangErrorBlocks(errors ?? string.Empty);
        var blocks = all.Where(block => !IsWarning(block)).ToList();
        if (blocks.Count == 0)
        {
            logger.Error($"{stage.Stage} shader '{stage.EntryPoint}': Slang failed" +
                         (all.Count == 0 ? " and said no more." : $", warning only:\n{string.Join("\n", all)}"),
                stage.Location);
            return;
        }

        var missingEntryPoint = blocks.Any(IsMissingEntryPoint);
        bool IsLookup(string block) =>
            missingEntryPoint && Header(block).Contains("[E30015]") && !block.Contains("--> ");
        var hints = blocks.Where(IsLookup)
            .SelectMany(block => block.Split('\n'))
            .Where(line => line.TrimStart().StartsWith("note: did you mean", StringComparison.Ordinal))
            .Select(line => line.Trim())
            .ToList();
        var main = blocks.Where(block => !IsAftermath(block) && !IsLookup(block)).ToList();
        foreach (var block in main.Count > 0 ? main : blocks)
        {
            var location = IsMissingEntryPoint(block) ? null : SlangLocation(block);
            var at = location ?? stage.Location;
            var text = IsMissingEntryPoint(block) && hints.Count > 0 ? $"{block}\n{string.Join("\n", hints)}" : block;
            if (reportedSlangErrors.Add((text, at)))
            {
                logger.Error(location == null ? $"{stage.Stage} shader '{stage.EntryPoint}': {text}" : text, at);
            }
        }
    }

    private static string Header(string block)
    {
        var end = block.IndexOf('\n');
        return end < 0 ? block : block.Substring(0, end);
    }

    private static bool IsMissingEntryPoint(string block) => Header(block).Contains("[E38000]");

    private static List<string> SlangErrorBlocks(string errors)
    {
        List<string> blocks = [];
        var current = new StringBuilder();
        foreach (var line in errors.Replace("\r\n", "\n").Split('\n'))
        {
            if (current.Length > 0 && StartsADiagnostic(line))
            {
                blocks.Add(current.ToString().TrimEnd());
                current.Clear();
            }

            current.Append(line).Append('\n');
        }

        if (current.ToString().Trim().Length > 0)
        {
            blocks.Add(current.ToString().TrimEnd());
        }

        return blocks;
    }

    private static bool StartsADiagnostic(string line) =>
        line.StartsWith("error", StringComparison.Ordinal)
        || line.StartsWith("warning", StringComparison.Ordinal)
        || line.StartsWith("internal error", StringComparison.Ordinal)
        || line.StartsWith("fatal error", StringComparison.Ordinal)
        || line.StartsWith("abort compilation", StringComparison.Ordinal);

    private static bool IsWarning(string block) => block.StartsWith("warning", StringComparison.Ordinal);

    private static bool IsAftermath(string block) =>
        block.StartsWith("fatal error", StringComparison.Ordinal)
        || block.StartsWith("abort compilation", StringComparison.Ordinal)
        || Header(block).Contains("import failed due to compilation error");

    private static SourceLocation? SlangLocation(string block)
    {
        var arrow = block.IndexOf("--> ", StringComparison.Ordinal);
        if (arrow < 0)
        {
            return null;
        }

        var end = block.IndexOf('\n', arrow);
        var place = block.Substring(arrow + 4, (end < 0 ? block.Length : end) - arrow - 4).Trim();
        var columnAt = place.LastIndexOf(':');
        var lineAt = columnAt > 0 ? place.LastIndexOf(':', columnAt - 1) : -1;
        if (lineAt <= 0 || !int.TryParse(place.Substring(lineAt + 1, columnAt - lineAt - 1), out var line)
            || !int.TryParse(place.Substring(columnAt + 1), out var column))
        {
            return null;
        }

        return new SourceLocation(place.Substring(0, lineAt), line, column);
    }

    private string ResolveSlangInclude(string requestedPath)
    {
        if (string.IsNullOrEmpty(requestedPath))
        {
            return null;
        }

        var includePath = requestedPath.Replace("\"", string.Empty).Replace('/', '\\');
        var fileName = includePath.Substring(includePath.LastIndexOf('\\') + 1);
        foreach (var include in includes.IsDefault ? ImmutableArray<ShaderFileInfo>.Empty : includes)
        {
            if (EndsWithPath(include.Path, includePath)
                || string.Equals(include.FileName, fileName, StringComparison.OrdinalIgnoreCase))
            {
                return Resolved(include.Path ?? include.FileName, include.Content);
            }
        }

        if (sourceDirectory == null)
        {
            return null;
        }

        var parts = includePath.Split(['\\'], StringSplitOptions.RemoveEmptyEntries);
        for (var skip = 0; skip < parts.Length; skip++)
        {
            var onDisk = Path.Combine(sourceDirectory, string.Join(Path.DirectorySeparatorChar.ToString(), parts.Skip(skip)));
            if (File.Exists(onDisk))
            {
                return Resolved(onDisk, File.ReadAllText(onDisk));
            }
        }

        return null;
    }

    private string Resolved(string path, string content)
    {
        if (!resolvedIncludes.Contains(path))
        {
            resolvedIncludes.Add(path);
        }

        return content;
    }

    private static bool EndsWithPath(string path, string request)
    {
        if (path == null)
        {
            return false;
        }

        var normalized = path.Replace('/', '\\');
        return normalized.Equals(request, StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith("\\" + request, StringComparison.OrdinalIgnoreCase);
    }

    private EffectData.Shader CreateEffectShader(EffectShaderType type, string entryPoint,
        ShaderCompilationResult compilationResult, SourceLocation location)
    {
        var shader = new EffectData.Shader
        {
            Name = null,
            EntryPoint = entryPoint,
            Level = SlangShaderCompiler.SpirvProfile,
            Bytecode = compilationResult.Bytecode,
            Hashcode = Utilities.ComputeHashFNV1Modified(compilationResult.Bytecode),
            Type = type,
            ConstantBuffers = [],
            ResourceParameters = [],
        };

        using (var reflect = new SpirvReflection(shader.Bytecode))
        {
            var result = reflect.Disassemble(resourceKeys);
            shader.Bytecode = reflect.GetByteCode();
            BuildParameters(shader, result, location);
        }

        return shader;
    }

    private void ProcessShaderData(EffectShaderType type, ShaderCompilationResult bytecode, EffectData.Shader shader)
    {
        if ((compilerFlags & EffectCompilerFlags.Debug) == 0)
        {
            shader.Bytecode = bytecode;
        }

        int shaderIndex;
        for (shaderIndex = 0; shaderIndex < effectData.Shaders.Count; shaderIndex++)
        {
            if (effectData.Shaders[shaderIndex].IsSimilar(shader))
            {
                break;
            }
        }

        if (shaderIndex >= effectData.Shaders.Count)
        {
            shaderIndex = effectData.Shaders.Count;
            effectData.Shaders.Add(shader);
        }

        LinkShader(type, shaderIndex, shader.EntryPoint);
    }

    private void LinkShader(EffectShaderType type, int shaderIndex, string entryPoint)
    {
        pass.Pipeline[type] ??= new EffectData.ShaderLink();
        pass.Pipeline[type].Index = shaderIndex;
        pass.Pipeline[type].EntryPoint = entryPoint;
    }

    private void BuildParameters(EffectData.Shader shader, SpirvReflectionResult reflectionResult, SourceLocation location)
    {
        var groupByIds = reflectionResult.AllResources
            .GroupBy(x => (x.Description.DescriptorSet, x.Description.SlotIndex))
            .Where(group => group.Count() > 1).SelectMany(g => g).ToArray();

        if (groupByIds.Length > 0)
        {
            var names = groupByIds.Select(x => x.Description.Name).ToArray();
            logger.Error($"{string.Join(", ", names)} take binding {groupByIds[0].Description.SlotIndex} of set " +
                         $"{groupByIds[0].Description.DescriptorSet} together, which Vulkan does not allow.", location);
            return;
        }

        for (int i = 0; i < reflectionResult.UniformBuffers.Count; i++)
        {
            var reflectConstantBuffer = reflectionResult.UniformBuffers[i];
            var description = reflectConstantBuffer.Description;

            var parameterBuffer = new EffectData.ConstantBuffer()
            {
                Name = description.Name,
                Size = description.Size,
                Slot = description.SlotIndex,
                DescriptorSet = (byte)description.DescriptorSet,
                Parameters = [],
            };
            shader.ConstantBuffers.Add(parameterBuffer);

            for (uint j = 0; j < reflectConstantBuffer.VariablesCount; j++)
            {
                var variableBuffer = reflectConstantBuffer.GetVariable(j);
                var parameter = BuildConstantBufferParameter(variableBuffer);
                parameterBuffer.Parameters.Add(parameter);
            }
        }

        var resourceParameters = new Dictionary<string, EffectData.ResourceParameter>();

        foreach (var resource in reflectionResult.AllResources)
        {
            var bindingDescription = resource.Description;
            string name = bindingDescription.Name;

            if (!resourceParameters.ContainsKey(name))
            {
                var parameter = BuildResourceParameter(name, bindingDescription);
                shader.ResourceParameters.Add(parameter);
                resourceParameters.Add(name, parameter);
            }
        }
    }

    private EffectData.ValueTypeParameter BuildConstantBufferParameter(ShaderReflectionVariable variable)
    {
        var parameter = new EffectData.ValueTypeParameter()
        {
            Name = variable.Name,
            Offset = variable.Offset,
            Size = (int)variable.Size,
            Count = variable.ElementCount,
            Class = (EffectParameterClass)variable.VariableType,
            Type = ToParameterType(variable.Type),
            RowCount = (byte)variable.RowCount,
            ColumnCount = (byte)variable.ColumnCount,
        };

        return parameter;
    }

    private static EffectParameterType ToParameterType(Basetype type)
    {
        switch (type)
        {
            case Basetype.Boolean: return EffectParameterType.Bool;
            case Basetype.Int8:
            case Basetype.Int32: return EffectParameterType.Int;
            case Basetype.Uint8: return EffectParameterType.UInt8;
            case Basetype.Uint32: return EffectParameterType.UInt;
            case Basetype.Int16: return EffectParameterType.Int16;
            case Basetype.Uint16: return EffectParameterType.UInt16;
            case Basetype.Int64: return EffectParameterType.Int64;
            case Basetype.Uint64: return EffectParameterType.UInt64;
            case Basetype.Fp16: return EffectParameterType.Float16;
            case Basetype.Fp32: return EffectParameterType.Float;
            case Basetype.Fp64: return EffectParameterType.Double;
            default: return EffectParameterType.Void;
        }
    }

    private static EffectData.ResourceParameter BuildResourceParameter(string name, ShaderReflectionVariable variableBinding)
    {
        if (variableBinding.ElementCount > byte.MaxValue)
        {
            throw new NotSupportedException(
                $"'{name}' is an array of {variableBinding.ElementCount} resources; at most {byte.MaxValue} are " +
                "supported. An array of no length is the whole heap.");
        }

        var parameter = new EffectData.ResourceParameter()
        {
            Name = name,
            Class = EffectParameterClass.Object,
            Slot = (byte)variableBinding.SlotIndex,
            DescriptorSet = (byte)variableBinding.DescriptorSet,
            Count = (byte)variableBinding.ElementCount,
        };

        switch (variableBinding.Class)
        {
            case ResourceType.UniformBuffer:
                parameter.Type = EffectParameterType.ConstantBuffer;
                break;
            case ResourceType.SeparateImage:
                switch (variableBinding.Dimension)
                {
                    case ShaderResourceDimension.Buffer:
                        parameter.Type = EffectParameterType.Buffer;
                        break;
                    case ShaderResourceDimension.Texture1D:
                        parameter.Type = EffectParameterType.Texture1D;
                        break;
                    case ShaderResourceDimension.Texture2D:
                        parameter.Type = EffectParameterType.Texture2D;
                        break;
                    case ShaderResourceDimension.Texture3D:
                        parameter.Type = EffectParameterType.Texture3D;
                        break;
                    case ShaderResourceDimension.TextureCube:
                        parameter.Type = EffectParameterType.TextureCube;
                        break;
                }
                break;
            case ResourceType.StorageBuffer:
                parameter.Type = EffectParameterType.StorageBuffer;
                break;
            case ResourceType.StorageImage:
                switch (variableBinding.Dimension)
                {
                    case ShaderResourceDimension.Buffer:
                        parameter.Type = EffectParameterType.StorageImage;
                        break;
                    case ShaderResourceDimension.Texture1D:
                        parameter.Type = EffectParameterType.RWTexture1D;
                        break;
                    case ShaderResourceDimension.Texture2D:
                        parameter.Type = EffectParameterType.RWTexture2D;
                        break;
                    case ShaderResourceDimension.Texture3D:
                        parameter.Type = EffectParameterType.RWTexture3D;
                        break;
                }
                break;
            case ResourceType.SeparateSamplers:
                parameter.Type = EffectParameterType.Sampler;
                break;
        }
        return parameter;
    }
}

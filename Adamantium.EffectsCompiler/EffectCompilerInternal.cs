using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Adamantium.Vulkan.Spirv.Cross;
using Adamantium.Vulkan.Spirv.Reflection;

namespace Adamantium.EffectsCompiler;

internal sealed class EffectCompilerInternal : IDisposable
{
    private readonly List<string> resolvedIncludes = [];
    private readonly Dictionary<(EffectStage Stage, string EntryPoint), int> compiledShaders = [];
    private readonly Dictionary<(EffectStage Stage, string EntryPoint), string> failedShaders = [];
    private readonly HashSet<(string Text, SourceLocation Location)> reportedSlangErrors = [];
    private readonly SlangShaderCompiler slangCompiler = new();
    private EffectData effectData;
    private EffectCompilerLogger logger;
    private List<EffectData.ShaderMacro> macros;
    private ImmutableArray<ShaderFileInfo> includes;
    private string sourceDirectory;
    private string slangSource;
    private EffectData.Technique technique;

    /// <summary>Compiles the effect at <paramref name="filePath"/>.</summary>
    public EffectCompilerResult CompileFromFile(string filePath, List<EffectData.ShaderMacro> macros = null,
        List<string> includeDirectoryList = null, bool allowDynamicCompiling = false) =>
        Compile(File.ReadAllText(filePath, Encoding.UTF8), filePath, macros, includeDirectoryList,
            allowDynamicCompiling);

    /// <summary>Compiles the effect <paramref name="sourceCode"/> of <paramref name="filePath"/>, its includes found
    /// beside it; <paramref name="macrosArgs"/> are defined for Slang. With <paramref name="allowDynamicCompiling"/> the
    /// effect keeps what it was compiled from, to compile it again.</summary>
    public EffectCompilerResult Compile(string sourceCode, string filePath,
        List<EffectData.ShaderMacro> macrosArgs = null, List<string> includeDirectoryList = null,
        bool allowDynamicCompiling = false)
    {
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
        using var reflection = new SpirvReflection(shader.Bytecode);
        return reflection.Disassemble();
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
        var source = EffectSource.Read(sourceCode, fileName);
        foreach (var error in source.Errors)
        {
            logger.LogMessage(error);
        }

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

        PassResourcePlacement.Place(effectData);
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
        var pass = new EffectData.Pass { Name = passSource.Name };
        technique.Passes.Add(pass);
        foreach (var stage in passSource.Stages)
        {
            CompileShader(stage, pass.Stages);
        }

        foreach (var groupSource in passSource.HitGroups)
        {
            var group = new EffectData.HitGroup { Name = groupSource.Name };
            pass.HitGroups.Add(group);
            foreach (var stage in groupSource.Stages)
            {
                CompileShader(stage, group.Shaders);
            }
        }

        ReportSharedBindings(pass, passSource.Location);
    }

    private void ReportSharedBindings(EffectData.Pass pass, SourceLocation location)
    {
        var resources = pass.Stages.Concat(pass.HitGroups.SelectMany(group => group.Shaders))
            .SelectMany(link => effectData.Shaders[link.Index].ResourceParameters);
        foreach (var shared in resources.GroupBy(resource => (resource.Set, resource.Binding)))
        {
            var names = shared.Select(resource => resource.Name).Distinct().ToList();
            if (names.Count > 1)
            {
                logger.Error($"{string.Join(", ", names)} take binding {shared.Key.Binding} of set {shared.Key.Set} " +
                             "together, which Vulkan does not allow.", location);
            }
        }
    }

    private void CompileShader(EffectStageSource stage, List<EffectData.ShaderLink> links)
    {
        var entryPoint = stage.EntryPoint;
        var key = (stage.Stage, entryPoint);
        if (compiledShaders.TryGetValue(key, out var compiled))
        {
            links.Add(new EffectData.ShaderLink { Stage = stage.Stage, Index = compiled });
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
            var result = slangCompiler.Compile(slangSource, entryPoint, stage.SlangStage, ResolveSlangInclude);
            if (result.HasErrors || result.Bytecode == null)
            {
                failedShaders[key] = result.Errors ?? string.Empty;
                ReportSlangErrors(result.Errors, stage);
                return;
            }

            var shader = CreateEffectShader(stage, result);
            if (logger.Messages.Count > errorsBefore)
            {
                failedShaders[key] = null;
                return;
            }

            var index = effectData.Shaders.Count;
            effectData.Shaders.Add(shader);
            compiledShaders[key] = index;
            links.Add(new EffectData.ShaderLink { Stage = stage.Stage, Index = index });
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

    private EffectData.Shader CreateEffectShader(EffectStageSource stage, ShaderCompilationResult compilationResult)
    {
        var shader = new EffectData.Shader
        {
            EntryPoint = stage.EntryPoint,
            Stage = stage.Stage,
            Level = EffectCompiler.SpirvProfile,
            Bytecode = compilationResult.Bytecode
        };

        using var reflection = new SpirvReflection(shader.Bytecode);
        var reflected = reflection.Disassemble();
        BuildParameters(shader, reflected);
        shader.InputPatchSize = reflected.InputPatchSize;
        return shader;
    }

    private static void BuildParameters(EffectData.Shader shader, SpirvReflectionResult reflectionResult)
    {
        for (int i = 0; i < reflectionResult.UniformBuffers.Count; i++)
        {
            var reflectConstantBuffer = reflectionResult.UniformBuffers[i];
            var description = reflectConstantBuffer.Description;

            var parameterBuffer = new EffectData.ConstantBuffer()
            {
                Name = description.Name,
                Size = description.Size,
                Set = description.DescriptorSet,
                Binding = description.SlotIndex,
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

    private static EffectData.ValueTypeParameter BuildConstantBufferParameter(ShaderReflectionVariable variable)
    {
        var parameter = new EffectData.ValueTypeParameter()
        {
            Name = variable.Name,
            Offset = variable.Offset,
            Size = (int)variable.Size,
            Count = variable.ElementCount,
            Class = ToParameterClass(variable.VariableType),
            Type = ToParameterType(variable.Type),
            RowCount = (byte)variable.RowCount,
            ColumnCount = (byte)variable.ColumnCount,
        };

        return parameter;
    }

    private static EffectParameterClass ToParameterClass(ShaderVariableClass variableClass) => variableClass switch
    {
        ShaderVariableClass.Scalar => EffectParameterClass.Scalar,
        ShaderVariableClass.Vector => EffectParameterClass.Vector,
        ShaderVariableClass.MatrixRows => EffectParameterClass.MatrixRows,
        ShaderVariableClass.MatrixColumns => EffectParameterClass.MatrixColumns,
        ShaderVariableClass.Struct => EffectParameterClass.Struct,
        ShaderVariableClass.Pointer => EffectParameterClass.Pointer,
        _ => EffectParameterClass.Object
    };

    private static EffectParameterType ToParameterType(Basetype type) => type switch
    {
        Basetype.Boolean => EffectParameterType.Bool,
        Basetype.Int8 => EffectParameterType.Int8,
        Basetype.Uint8 => EffectParameterType.UInt8,
        Basetype.Int16 => EffectParameterType.Int16,
        Basetype.Uint16 => EffectParameterType.UInt16,
        Basetype.Int32 => EffectParameterType.Int,
        Basetype.Uint32 => EffectParameterType.UInt,
        Basetype.Int64 => EffectParameterType.Int64,
        Basetype.Uint64 => EffectParameterType.UInt64,
        Basetype.Fp16 => EffectParameterType.Float16,
        Basetype.Fp32 => EffectParameterType.Float,
        Basetype.Fp64 => EffectParameterType.Double,
        _ => EffectParameterType.Void
    };

    private static EffectData.ResourceParameter BuildResourceParameter(string name, ShaderReflectionVariable variable) => new()
    {
        Name = name,
        Class = EffectParameterClass.Object,
        Type = GetDescriptorType(name, variable),
        Set = variable.DescriptorSet,
        Binding = variable.SlotIndex,
        Count = variable.ElementCount,
        Access = variable.Access switch
        {
            ShaderResourceAccess.ReadOnly => EffectResourceAccess.ReadOnly,
            ShaderResourceAccess.WriteOnly => EffectResourceAccess.WriteOnly,
            _ => EffectResourceAccess.ReadWrite
        }
    };

    private static EffectParameterType GetDescriptorType(string name, ShaderReflectionVariable variable) =>
        (variable.Class, variable.Dimension) switch
        {
            (ResourceType.UniformBuffer, _) => EffectParameterType.UniformBuffer,
            (ResourceType.StorageBuffer, _) => EffectParameterType.StorageBuffer,
            (ResourceType.SeparateSamplers, _) => EffectParameterType.Sampler,
            (ResourceType.AccelerationStructure, _) => EffectParameterType.AccelerationStructure,
            (ResourceType.SeparateImage, ShaderResourceDimension.Buffer) => EffectParameterType.UniformTexelBuffer,
            (ResourceType.SeparateImage, ShaderResourceDimension.Texture1D) => EffectParameterType.SampledImage1D,
            (ResourceType.SeparateImage, ShaderResourceDimension.Texture2D) => EffectParameterType.SampledImage2D,
            (ResourceType.SeparateImage, ShaderResourceDimension.Texture3D) => EffectParameterType.SampledImage3D,
            (ResourceType.SeparateImage, ShaderResourceDimension.TextureCube) => EffectParameterType.SampledImageCube,
            (ResourceType.StorageImage, ShaderResourceDimension.Buffer) => EffectParameterType.StorageTexelBuffer,
            (ResourceType.StorageImage, ShaderResourceDimension.Texture1D) => EffectParameterType.StorageImage1D,
            (ResourceType.StorageImage, ShaderResourceDimension.Texture2D) => EffectParameterType.StorageImage2D,
            (ResourceType.StorageImage, ShaderResourceDimension.Texture3D) => EffectParameterType.StorageImage3D,
            _ => throw new NotSupportedException(
                $"'{name}' is a {variable.Class} of {variable.Dimension}, which effects have no descriptor kind for.")
        };
}

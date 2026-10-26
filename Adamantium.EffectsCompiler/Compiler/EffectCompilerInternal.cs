using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Adamantium.Core;
using Adamantium.EffectsCompiler.Compiler;
using Adamantium.Vulkan.Spirv.Cross;
using Adamantium.Vulkan.Spirv.Reflection;

namespace Adamantium.EffectsCompiler;

internal class EffectCompilerInternal
{
    private readonly List<string> resolvedIncludes = [];
    private readonly Dictionary<(EffectShaderType Type, string EntryPoint), int> compiledShaders = [];
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

    private void InternalCompile(string sourceCode, string fileName)
    {
        effectData = null;
        resolvedIncludes.Clear();
        compiledShaders.Clear();
        fileName = fileName.Replace(@"\\", @"\");
        logger = new EffectCompilerLogger();
        sourceDirectory = null;
        if (includes.IsDefault)
        {
            var directory = Path.GetDirectoryName(fileName);
            sourceDirectory = string.IsNullOrEmpty(directory) ? "." : directory;
            includes = Directory.GetFiles(sourceDirectory, "*.fxh")
                .Select(path => new ShaderFileInfo
                {
                    Content = File.ReadAllText(path), Path = path, FileName = Path.GetFileName(path)
                })
                .ToImmutableArray();
        }

        var source = EffectSource.Read(sourceCode, fileName, logger);
        if (logger.HasErrors)
        {
            return;
        }

        slangSource = Preamble() + source.Code;
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

    private string Preamble()
    {
        if (macros.Count == 0)
        {
            return string.Empty;
        }

        var preamble = new StringBuilder();
        foreach (var macro in macros)
        {
            preamble.Append("#define ").Append(macro.Name).Append(' ').Append(macro.Value).Append('\n');
        }

        return preamble.Append("#line 1\n").ToString();
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
                CompileShader(type, stage.EntryPoint);
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
        EffectStage.Hull => EffectShaderType.Hull,
        EffectStage.Domain => EffectShaderType.Domain,
        EffectStage.Pixel => EffectShaderType.Fragment,
        EffectStage.Compute => EffectShaderType.Compute,
        _ => null
    };

    private void CompileShader(EffectShaderType type, string entryPoint)
    {
        var key = (type, entryPoint);
        if (compiledShaders.TryGetValue(key, out var compiled))
        {
            LinkShader(type, compiled, entryPoint);
            return;
        }

        try
        {
            var result = CompileStage(type, entryPoint);
            if (result.HasErrors || result.Bytecode == null)
            {
                logger.Errors(result.Errors);
                return;
            }

            var shader = CreateEffectShader(type, entryPoint, result);
            if (logger.HasErrors)
            {
                return;
            }

            ProcessShaderData(type, result, shader);
            compiledShaders[key] = pass.Pipeline[type].Index;
        }
        catch (Exception ex)
        {
            logger.Error($"{type} {entryPoint}: {ex.Message}");
        }
    }

    private ShaderCompilationResult CompileStage(EffectShaderType type, string entryPoint)
    {
        try
        {
            return slangCompiler.Compile(slangSource, entryPoint, type, SlangShaderCompiler.LatestSpirvProfile,
                ResolveSlangInclude);
        }
        catch (Exception ex)
        {
            return new ShaderCompilationResult
            {
                HasErrors = true,
                Errors = $"Slang backend threw for {type} '{entryPoint}': {ex.Message}"
            };
        }
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

    private EffectData.Shader CreateEffectShader(EffectShaderType type, string entryPoint, ShaderCompilationResult compilationResult)
    {
        var shader = new EffectData.Shader
        {
            Name = null,
            EntryPoint = entryPoint,
            Level = SlangShaderCompiler.LatestSpirvProfile,
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
            BuildParameters(shader, result);
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

    private void BuildParameters(EffectData.Shader shader, SpirvReflectionResult reflectionResult)
    {
        var groupByIds = reflectionResult.AllResources
            .GroupBy(x => (x.Description.DescriptorSet, x.Description.SlotIndex))
            .Where(group => group.Count() > 1).SelectMany(g => g).ToArray();

        if (groupByIds.Length > 0)
        {
            var names = groupByIds.Select(x => x.Description.Name).ToArray();
            logger.Error($"{string.Join(", ", names)} has the same binding id {groupByIds[0].Description.SlotIndex} in set {groupByIds[0].Description.DescriptorSet} which is not permitted by Vulkan.\nPlease, change shader code not to have same binding ids in one set.");
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
                Parameters = new List<EffectData.ValueTypeParameter>(),
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

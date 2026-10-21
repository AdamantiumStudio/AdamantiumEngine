using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Adamantium.Core;
using Adamantium.EffectsCompiler.Compiler;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Spirv.Cross;
using Adamantium.Vulkan.Spirv.Reflection;

namespace Adamantium.EffectsCompiler
{
    internal class EffectCompilerInternal
    {
        private static readonly Regex MatchVariableArray = new Regex(@"(.*)\[(\d+)\]");
        private static readonly Regex splitSODeclartionRegex = new Regex(@"\s*;\s*");
        private static readonly Regex soDeclarationItemRegex = new Regex(@"^\s*(\d+)?\s*:?\s*([A-Za-z][A-Za-z0-9_]*)(\.[xyzw]+|\.[rgba]+)?$");
        private static readonly Regex soSemanticIndex = new Regex(@"^([A-Za-z][A-Za-z0-9_]*?)([0-9]*)?$");
        private static readonly List<char> xyzwrgbaComponents = new List<char>() { 'x', 'y', 'z', 'w', 'r', 'g', 'b', 'a' };

        private static readonly Dictionary<string, ValueConverter> ValueConverters =
            new Dictionary<string, ValueConverter>()
              {
                {"float4", new ValueConverter("float4", 4, ToFloat, (compiler, parameters) => new Vector4F((float)parameters[0], (float)parameters[1], (float)parameters[2], (float)parameters[3]))},
                {"float3", new ValueConverter("float3", 3, ToFloat, (compiler, parameters) => new Vector3F((float)parameters[0], (float)parameters[1], (float)parameters[2]))},
                {"float2", new ValueConverter("float2", 2, ToFloat, (compiler, parameters) => new Vector2F((float)parameters[0], (float)parameters[1]))},
                {"float", new ValueConverter("float", 1, ToFloat, (compiler, parameters) => (float)parameters[0])},
              };

        private readonly List<string> currentExports = new List<string>();
        private EffectCompilerFlags compilerFlags;
        private EffectData effectData;

        private EffectData.Effect effect;

        private List<string> includeDirectoryList;
        private EffectCompilerLogger logger;
        private List<EffectData.ShaderMacro> macros;
        private ImmutableArray<ShaderFileInfo> includes;
        private string sourceDirectory;
        private EffectParserResult parserResult;
        private string slangRawSource;
        private EffectData.Pass pass;
        private string preprocessorText;
        private EffectData.Technique technique;
        private int nextSubPassCount;
        // The SPIR-V profile THIS pass asked for, or null when it named none - then the newest is used. Reset per pass:
        // a Profile is a statement inside one pass and must not leak into the next, which is what a plain field did.
        private string passSpirvProfile;
        private List<ResourceBindingKey> resourceKeys = new List<ResourceBindingKey>();

        //private StreamOutputElement[] currentStreamOutputElements;

        private readonly List<string> resolvedIncludes = [];
        private readonly Dictionary<(EffectShaderType Type, string EntryPoint, string Profile, string Preprocessor), int> compiledShaders = [];
        private SlangShaderCompiler slangCompiler;

        public EffectCompilerInternal()
        {
            slangCompiler = new SlangShaderCompiler();
        }

        /// <summary>
        /// Compiles an effect from file.
        /// </summary>
        /// <param name="filePath">The file path.</param>
        /// <param name="flags">The flags.</param>
        /// <param name="macros">The macrosArgs.</param>
        /// <param name="includeDirectoryList">The include directory list.</param>
        /// <param name="allowDynamicCompiling">if set to <c>true</c> [allo dynamic compiling].</param>
        /// <returns>The result of compilation.</returns>
        public EffectCompilerResult CompileFromFile(string filePath, EffectCompilerFlags flags = EffectCompilerFlags.None, List<EffectData.ShaderMacro> macros = null, List<string> includeDirectoryList = null, bool allowDynamicCompiling = false)
        {
            return Compile(File.ReadAllText(filePath, Encoding.UTF8), filePath, flags, macros, includeDirectoryList, allowDynamicCompiling);
        }

        /// <summary>
        /// Compiles an effect from the specified source code and file path.
        /// </summary>
        /// <param name="sourceCode">The source code.</param>
        /// <param name="filePath">The file path.</param>
        /// <param name="flags">The flags.</param>
        /// <param name="macrosArgs">The macrosArgs.</param>
        /// <param name="includeDirectoryList">The include directory list.</param>
        /// <param name="allowDynamicCompiling">if set to <c>true</c> [allow dynamic compiling].</param>
        /// <returns>The result of compilation.</returns>
        public EffectCompilerResult Compile(string sourceCode, string filePath, EffectCompilerFlags flags = EffectCompilerFlags.None, List<EffectData.ShaderMacro> macrosArgs = null, List<string> includeDirectoryList = null, bool allowDynamicCompiling = false)
        {
            var nativeMacros = new List<EffectData.ShaderMacro>();
            if (macrosArgs != null)
            {
                foreach (var macro in macrosArgs)
                {
                    nativeMacros.Add(new EffectData.ShaderMacro(macro.Name, macro.Value));
                }
            }

            compilerFlags = flags;
            macros = nativeMacros;
            this.includeDirectoryList = includeDirectoryList ?? new List<string>();

            InternalCompile(sourceCode, filePath);

            var result = new EffectCompilerResult(effectData, logger, resolvedIncludes);

            // If dynamic compiling, store the parameters used to compile this effect directly in the bytecode
            if (allowDynamicCompiling && !result.HasErrors && result.EffectData != null)
            {
                var compilerArguments = new EffectData.CompilerArguments { FilePath = filePath, Macros = new List<EffectData.ShaderMacro>(), IncludeDirectoryList = new List<string>(), Includes = new List<string>(resolvedIncludes) };
                if (macrosArgs != null)
                {
                    compilerArguments.Macros.AddRange(macrosArgs);
                }

                if (includeDirectoryList != null)
                {
                    compilerArguments.IncludeDirectoryList.AddRange(includeDirectoryList);
                }

                result.EffectData.Description.Arguments = compilerArguments;
            }

            return result;
        }
        
        public EffectCompilerResult Compile(string sourceCode, string filePath, ImmutableArray<ShaderFileInfo> includes, List<EffectData.ShaderMacro> macrosArgs = null, List<string> includeDirectoryList = null)
        {
            var nativeMacros = new List<EffectData.ShaderMacro>();
            if (macrosArgs != null)
            {
                foreach (var macro in macrosArgs)
                {
                    nativeMacros.Add(new EffectData.ShaderMacro(macro.Name, macro.Value));
                }
            }
            
            macros = nativeMacros;
            this.includes = includes;
            this.includeDirectoryList = includeDirectoryList ?? new List<string>();

            InternalCompile(sourceCode, filePath);

            var result = new EffectCompilerResult(effectData, logger, resolvedIncludes);

            return result;
        }

        /// <summary>
        /// Disassembles a shader HLSL bytecode to asm code.
        /// </summary>
        /// <param name="shader">The shader.</param>
        /// <returns>A string containing asm code decoded from HLSL bytecode.</returns>
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

            // Slang pulls each header itself through its VFS callback, so sources go to it untouched and its diagnostics
            // keep honest line numbers.
            sourceDirectory = null;
            if (includes == null)
            {
                var directory = Path.GetDirectoryName(fileName);
                if (string.IsNullOrEmpty(directory))
                {
                    directory = ".";
                }

                sourceDirectory = directory;
                var includeFiles = Directory.GetFiles(directory, "*.fxh");
                includes = includeFiles.Select(x => new ShaderFileInfo()
                { Content = File.ReadAllText(x), Path = x, FileName = Path.GetFileName(x) }).ToImmutableArray();
            }

            slangRawSource = sourceCode;

            var parser = new EffectParser { Logger = logger };
            parser.Macros.AddRange(macros);
            parser.IncludeDirectoryList.AddRange(includeDirectoryList);
            parserResult = parser.Parse(sourceCode, fileName);

            if (logger.HasErrors) return;

            SetupEffectData(Path.GetFileNameWithoutExtension(fileName));

            if (parserResult.Shader != null)
            {
                foreach (var techniqueAst in parserResult.Shader.Techniques)
                {
                    HandleTechnique(techniqueAst);
                }
            }
        }

        private void SetupEffectData(string name)
        {
            effectData = new EffectData
            {
                Shaders = new List<EffectData.Shader>(),
                Description = new EffectData.Effect()
                {
                    Name = name,
                    Techniques = new List<EffectData.Technique>(),
                }
            };

            effect = effectData.Description;
        }

        private void HandleTechnique(Ast.Technique techniqueAst)
        {
            SetupTechnique(techniqueAst.Name, techniqueAst.Span);
            
            // Process passes for this technique
            foreach (var passAst in techniqueAst.Passes)
            {
                HandlePass(passAst);
            }
        }

        private void SetupTechnique(string techniqueName, SourceSpan sourceSpan)
        {
            technique = new EffectData.Technique()
            {
                Name = techniqueName,
                Passes = new List<EffectData.Pass>()
            };

            // Check that the name of this technique is not already used.
            if (technique.Name != null)
            {
                foreach (var registeredTechnique in effect.Techniques)
                {
                    if (registeredTechnique.Name == technique.Name)
                    {
                        logger.Error("A technique with the same name [{0}] already exist", sourceSpan, technique.Name);
                        break;
                    }
                }
            }

            // Adds the technique to the list of technique for the current effect
            effect.Techniques.Add(technique);
        }

        private void HandlePass(Ast.Pass passAst)
        {
            resourceKeys.Clear();
            passSpirvProfile = null;
            SetupPass(passAst.Name, passAst.Span);

            if (nextSubPassCount > 0)
            {
                pass.IsSubPass = true;
                nextSubPassCount--;
            }

            // Process all statements inside this pass.
            foreach (var statement in passAst.Statements)
            {
                if (!(statement is Ast.ExpressionStatement expressionStatement))
                    continue;
                HandleExpression(expressionStatement.Expression);
            }

            CheckPassConsistency(passAst.Span);
        }

        private void SetupPass(string name, SourceSpan span)
        {
            pass = new EffectData.Pass()
            {
                Name = name,
                Pipeline = new EffectData.Pipeline(),
                Properties = new EffectPropertyCollection()
            };

            // Clear current exports
            currentExports.Clear();

            // Check that the name of this pass is not already used.
            if (pass.Name != null)
            {
                foreach (var registeredPass in technique.Passes)
                {
                    if (registeredPass.Name == pass.Name)
                    {
                        logger.Error("A pass with the same name [{0}] already exist", span, pass.Name);
                        break;
                    }
                }
            }

            // Adds the pass to the list of pass for the current technique
            technique.Passes.Add(pass);
        }

        private void CheckPassConsistency(SourceSpan span)
        {
            // Check pass consistency (mainly for the GS)
            var gsLink = pass.Pipeline[EffectShaderType.Geometry];
            if (gsLink != null && gsLink.IsNullShader && (gsLink.StreamOutputRasterizedStream >= 0 /*|| gsLink.StreamOutputElements != null*/))
            {
                if (pass.Pipeline[EffectShaderType.Vertex] == null || pass.Pipeline[EffectShaderType.Vertex].IsNullShader)
                    logger.Error("Cannot specify StreamOutput for null geometry shader / vertex shader", span);
                else
                {
                    // For null geometry shaders with StreamOutput, directly use the VertexShader
                    gsLink.Index = pass.Pipeline[EffectShaderType.Vertex].Index;
                    gsLink.ImportName = pass.Pipeline[EffectShaderType.Vertex].ImportName;
                }
            }
        }

        private void HandleExpression(Ast.Expression expression)
        {
            if (expression is Ast.AssignExpression)
            {
                HandleAssignExpression((Ast.AssignExpression)expression);
            }
            else if (expression is Ast.MethodExpression)
            {
                HandleMethodExpression((Ast.MethodExpression)expression);
            }
            else
            {
                logger.Error("Unsupported expression type [{0}]", expression.Span, expression.GetType().Name);
            }
        }

        private void HandleAssignExpression(Ast.AssignExpression expression)
        {
            switch (expression.Name.Text)
            {
                case "Export":
                    HandleExport(expression.Value);
                    break;
                case EffectData.PropertyKeys.Blending:
                case EffectData.PropertyKeys.DepthStencil:
                case EffectData.PropertyKeys.Rasterizer:
                    HandleAttribute<string>(expression);
                    break;
                case EffectData.PropertyKeys.BlendingColor:
                    HandleAttribute<Vector4F>(expression);
                    break;
                case EffectData.PropertyKeys.BlendingSampleMask:
                    HandleAttribute<uint>(expression);
                    break;
                case EffectData.PropertyKeys.DepthStencilReference:
                    HandleAttribute<int>(expression);
                    break;
                case "ShareConstantBuffers":
                    HandleShareConstantBuffers(expression.Value);
                    break;
                case "EffectName":
                    HandleEffectName(expression.Value);
                    break;
                case "SubPassCount":
                    HandleSubPassCount(expression.Value);
                    break;
                case "Preprocessor":
                    HandlePreprocessor(expression.Value);
                    break;
                case "Language":
                    HandleLanguage(expression.Value);
                    break;
                case "Profile":
                    HandleProfile(expression.Value);
                    break;
                case "VertexShader":
                    CompileShader(EffectShaderType.Vertex, expression.Value);
                    break;
                case "PixelShader":
                case "FragmentShader":
                    CompileShader(EffectShaderType.Fragment, expression.Value);
                    break;
                case "GeometryShader":
                    CompileShader(EffectShaderType.Geometry, expression.Value);
                    break;
                case "StreamOutputRasterizedStream":
                    HandleStreamOutputRasterizedStream(expression.Value);
                    break;
                case "DomainShader":
                    CompileShader(EffectShaderType.Domain, expression.Value);
                    break;
                case "HullShader":
                    CompileShader(EffectShaderType.Hull, expression.Value);
                    break;
                case "ComputeShader":
                    CompileShader(EffectShaderType.Compute, expression.Value);
                    break;
                default:
                    HandleAttribute(expression);
                    break;
            }
        }

        private void HandleStreamOutputRasterizedStream(Ast.Expression expression)
        {
            object value;
            if (!ExtractValue(expression, out value))
                return;

            if (!(value is int))
            {
                logger.Error("StreamOutputRasterizedStream value [{0}] must be an integer", expression.Span, value);
                return;
            }

            if (pass.Pipeline[EffectShaderType.Geometry] == null)
            {
                pass.Pipeline[EffectShaderType.Geometry] = new EffectData.ShaderLink();
            }

            pass.Pipeline[EffectShaderType.Geometry].StreamOutputRasterizedStream = (int)value;
        }

        private void HandleExport(Ast.Expression expression)
        {
            var values = ExtractStringOrArrayOfStrings(expression);
            if (values != null)
            {
                currentExports.AddRange(values);
            }
        }

        private List<string> ExtractStringOrArrayOfStrings(Ast.Expression expression)
        {
            // TODO implement this method using generics
            if (!ExtractValue(expression, out var value))
                return null;

            var values = new List<string>();

            if (value is string str)
            {
                values.Add(str);
            }
            else if (value is object[] arrayValue)
            {
                foreach (var exportItem in arrayValue)
                {
                    if (!(exportItem is string))
                    {
                        logger.Error("Unexpected value [{0}]. Expecting a string.", expression.Span, exportItem);
                        return null;
                    }
                    values.Add((string)exportItem);
                }
            }
            else
            {
                logger.Error("Unexpected value. Expecting a identifier/string or an array of identifier/string.", expression.Span);
            }

            return values;
        }

        private void HandleShareConstantBuffers(Ast.Expression expression)
        {
            if (!ExtractValue(expression, out var value))
                return;

            if (!(value is bool))
            {
                logger.Error("ShareConstantBuffers must be a bool", expression.Span);
            }
            else
            {
                effect.ShareConstantBuffers = (bool)value;
            }
        }

        private void HandleEffectName(Ast.Expression expression)
        {
            if (!ExtractValue(expression, out var value))
                return;

            if (!(value is string))
            {
                logger.Error("Effect name must be a string/identifier", expression.Span);
            }
            else
            {
                effect.Name = (string)value;
            }
        }

        private void HandleSubPassCount(Ast.Expression expression)
        {
            object value;
            if (!ExtractValue(expression, out value))
                return;

            if (!(value is int))
            {
                logger.Error("SubPassCount must be an integer", expression.Span);
            }
            else
            {
                nextSubPassCount = (int)value;
            }
        }

        private void HandlePreprocessor(Ast.Expression expression)
        {
            if (!ExtractValue(expression, out var value))
                return;

            // If null, then preprocessor is reset
            if (value == null)
                preprocessorText = null;

            // Else parse preprocessor directive
            var builder = new StringBuilder();
            if (value is string str)
            {
                builder.AppendLine(str);
            }
            else if (value is object[] arrayValue)
            {
                foreach (var stringItem in arrayValue)
                {
                    if (!(stringItem is string))
                    {
                        logger.Error("Unexpected type. Preprocessor only support strings in array declaration", expression.Span);
                    }
                    builder.AppendLine((string)stringItem);
                }
            }
            preprocessorText = builder.ToString();
        }

        private void HandleAttribute(Ast.AssignExpression expression)
        {
            // Extract the value and store it in the attribute
            if (ExtractValue(expression.Value, out var value))
            {
                pass.Properties[expression.Name.Text] = value;
            }
        }

        private void HandleAttribute<T>(Ast.AssignExpression expression)
        {
            // Extract the value and store it in the attribute
            if (ExtractValue(expression.Value, out var value))
            {
                if (typeof(T) == typeof(uint) && value is int i)
                {
                    value = unchecked((uint)i);
                }
                else
                {
                    try
                    {
                        value = Convert.ChangeType(value, typeof(T));
                    }
                    catch (Exception)
                    {
                        logger.Error("Invalid type for attribute [{0}]. Expecting [{1}]", expression.Value.Span, expression.Name.Text, typeof(T).Name);
                    }
                }

                pass.Properties[expression.Name.Text] = value;
            }
        }

        private bool ExtractValue(Ast.Expression expression, out object value)
        {
            value = null;
            if (expression is Ast.LiteralExpression)
            {
                value = ((Ast.LiteralExpression)expression).Value.Value;
            }
            else if (expression is Ast.IdentifierExpression)
            {
                value = ((Ast.IdentifierExpression)expression).Name.Text;
            }
            else if (expression is Ast.ArrayInitializerExpression)
            {
                var arrayExpression = (Ast.ArrayInitializerExpression)expression;
                var arrayValue = new object[arrayExpression.Values.Count];
                for (int i = 0; i < arrayValue.Length; i++)
                {
                    if (!ExtractValue(arrayExpression.Values[i], out arrayValue[i]))
                        return false;
                }
                value = arrayValue;
            }
            else if (expression is Ast.MethodExpression)
            {
                var methodExpression = (Ast.MethodExpression)expression;
                value = ExtractValueFromMethodExpression(methodExpression);
                if (value == null)
                {
                    logger.Error("Unable to convert method expression to value.", expression.Span);
                    return false;
                }
            }
            else
            {
                logger.Error("Unsupported value type. Only [literal, identifier, array]", expression.Span);
                return false;
            }

            return true;
        }

        private object ExtractValueFromMethodExpression(Ast.MethodExpression methodExpression)
        {
            ValueConverter converter;
            if (!ValueConverters.TryGetValue(methodExpression.Name.Text, out converter))
            {
                logger.Error("Unexpected method [{0}]", methodExpression.Span, methodExpression.Name.Text);
                return null;
            }

            // Check for arguments
            if (converter.ArgumentCount != methodExpression.Arguments.Count)
            {
                logger.Error("Unexpected number of arguments (expecting {0} arguments)", methodExpression.Span, converter.ArgumentCount);
                return null;
            }

            var values = new object[methodExpression.Arguments.Count];
            for (int i = 0; i < methodExpression.Arguments.Count; i++)
            {
                if (!ExtractValue(methodExpression.Arguments[i], out var localValue))
                    return null;
                values[i] = converter.ConvertItem(this, localValue);
                if (localValue == null)
                    return null;
            }

            return converter.ConvertFullItem(this, values);
        }
        
        private void HandleLanguage(Ast.Expression expression)
        {
            if (expression is Ast.LiteralExpression)
            {
                var literalValue = (string)((Ast.LiteralExpression)expression).Value.Value;
                try
                {
                    var rawLevel = (int)(Convert.ToSingle(literalValue, CultureInfo.InvariantCulture) * 10);
                }
                catch (Exception)
                {
                    // ignored
                }

                if (string.IsNullOrEmpty(literalValue))
                    logger.Error("Unexpected assignment for [Profile] attribute: expecting only [identifier (fx_4_0, fx_4_1... etc.), or number (9.3, 10.0, 11.0... etc.)]", expression.Span);
            }
        }

        /// <summary>A pass's <c>Profile</c> - the SPIR-V version its shaders are compiled to, e.g. <c>Profile = 1.4</c>.
        /// A pass that names none takes the newest the compiler supports.</summary>
        private void HandleProfile(Ast.Expression expression)
        {
            // INVARIANT all the way: the literal's value arrives as a number, and ToString() on a comma-decimal locale
            // renders 1.4 as "1,4", which no invariant parse accepts. The same trap the markup numbers fell into.
            var text = expression switch
            {
                Ast.IdentifierExpression identifier => identifier.Name.Text,
                Ast.LiteralExpression literal => Convert.ToString(literal.Value.Value, CultureInfo.InvariantCulture),
                _ => null
            };

            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var version))
            {
                logger.Error("Unexpected assignment for [Profile] attribute: expecting a SPIR-V version number ({0})",
                    expression.Span, string.Join(", ", SupportedProfileVersions()));
                return;
            }

            // Named, so it must be a version this compiler can actually target. Anything else is an error and not a
            // fallback: a number that silently means nothing is the whole reason this attribute was rewritten.
            var name = "spirv_" + version.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', '_');
            if (!SlangShaderCompiler.SpirvProfiles.Contains(name))
            {
                logger.Error("Unsupported [Profile] {0}: this compiler targets SPIR-V {1}",
                    expression.Span, text, string.Join(", ", SupportedProfileVersions()));
                return;
            }

            passSpirvProfile = name;
        }

        private static IEnumerable<string> SupportedProfileVersions() =>
            SlangShaderCompiler.SpirvProfiles.Select(p => p.Replace("spirv_", string.Empty).Replace('_', '.'));

        private string ExtractShaderName(EffectShaderType effectShaderType, Ast.Expression expression)
        {
            string shaderName = null;

            if (expression is Ast.IdentifierExpression)
            {
                shaderName = ((Ast.IdentifierExpression)expression).Name.Text;
            }
            else if (expression is Ast.LiteralExpression)
            {
                var value = ((Ast.LiteralExpression)expression).Value.Value;
                if (Equals(value, 0) || Equals(value, null))
                {
                    return null;
                }
            }
            else if (expression is Ast.CompileExpression)
            {
                var compileExpression = (Ast.CompileExpression)expression;

                var profileName = compileExpression.Profile.Text;

                if (compileExpression.Method is Ast.MethodExpression)
                {
                    var shaderMethod = (Ast.MethodExpression)compileExpression.Method;

                    if (shaderMethod.Arguments.Count > 0)
                    {
                        logger.Error("Default arguments for shader methods are not supported", shaderMethod.Span);
                        return null;
                    }

                    shaderName = shaderMethod.Name.Text;
                }
                else
                {
                    logger.Error("Unsupported expression for compile. Excepting method", expression.Span);
                    return null;
                }
            }
            else if (expression is Ast.MethodExpression)
            {
                var compileExpression = (Ast.MethodExpression)expression;

                if (compileExpression.Name.Text == "CompileShader")
                {
                    if (compileExpression.Arguments.Count != 2)
                    {
                        logger.Error("Unexpected number of arguments [{0}] instead of 2", expression.Span, compileExpression.Arguments.Count);
                        return null;
                    }

                    // Extract level (11.0 10.0) from profile name (
                    if (!ExtractValue(compileExpression.Arguments[0], out var profileName))
                        return null;

                    if (!(profileName is string))
                    {
                        logger.Error("Invalid profile [{0}]. Expecting identifier", compileExpression.Arguments[0].Span, profileName);
                        return null;
                    }

                    var shaderMethod = compileExpression.Arguments[1] as Ast.MethodExpression;
                    if (shaderMethod == null)
                    {
                        logger.Error("Unexpected expression. Only method expression are supported", compileExpression.Arguments[1].Span);
                        return null;
                    }

                    if (shaderMethod.Arguments.Count > 0)
                    {
                        logger.Error("Default arguments for shader methods are not supported", shaderMethod.Span);
                        return null;
                    }
                    
                    

                    shaderName = shaderMethod.Name.Text;
                }
            }

            if (shaderName == null)
            {
                logger.Error("Unexpected compile expression", expression.Span);
            }

            return shaderName;
        }

        private void CompileShader(EffectShaderType type, Ast.Expression assignValue)
        {
            CompileShader(type, ExtractShaderName(type, assignValue), assignValue.Span);
        }

        private void CompileShader(EffectShaderType type, string entryPoint, SourceSpan span)
        {
            if (entryPoint == null)
            {
                pass.Pipeline[type] = EffectData.ShaderLink.NullShader;
                return;
            }

            // Passes often share a stage: compile it once and link the rest to the same shader.
            var key = (type, entryPoint, passSpirvProfile, preprocessorText);
            if (compiledShaders.TryGetValue(key, out var compiled))
            {
                LinkShader(type, compiled, entryPoint);
                return;
            }

            try
            {
                var result = CompileParsedShader(type, entryPoint);

                if (result.HasErrors || result.Bytecode == null)
                {
                    logger.Errors(result.Errors);
                }
                else
                {
                    //logger.Warnings(result.Warnings);

                    // Only an exported shader is named: the pool merges unnamed copies silently and treats a name as
                    // the one other effects import it by.
                    var shaderName = currentExports.Contains(entryPoint) ? entryPoint : null;
                    var shader = CreateEffectShader(type, shaderName, entryPoint, result);

                    if (logger.HasErrors)
                        return;

                    ProcessShaderData(type, result, shader);
                    compiledShaders[key] = pass.Pipeline[type].Index;
                }
            }
            catch (Exception ex)
            {
                logger.Error($"{type} {entryPoint}: {ex.Message}");
                Console.WriteLine(ex);
            }
            finally
            {
            }
        }

        /// <summary>Compiles ONE stage. Slang is the only backend - there is no second one to fall back to, and the
        /// difference shows in what this hands it: the source with its `#include` directives INTACT, resolved by Slang
        /// itself through <see cref="ResolveSlangInclude"/>. Only the `technique { pass { ... } }` blocks are blanked,
        /// because that grammar is the effect file's own and Slang's HLSL front end has never seen it.</summary>
        private ShaderCompilationResult CompileParsedShader(EffectShaderType shaderKind, string entryPoint)
        {
            var slangSourceBuilder = new StringBuilder();
            if (!string.IsNullOrEmpty(preprocessorText))
                slangSourceBuilder.Append(preprocessorText);
            slangSourceBuilder.Append(slangRawSource);
            var slangSource = BlankTechniqueBlocks(slangSourceBuilder.ToString());

            try
            {
                return slangCompiler.Compile(slangSource, entryPoint, shaderKind,
                    passSpirvProfile ?? SlangShaderCompiler.LatestSpirvProfile, ResolveSlangInclude);
            }
            catch (Exception ex)
            {
                return new ShaderCompilationResult
                {
                    HasErrors = true,
                    Errors = $"Slang backend threw for {shaderKind} '{entryPoint}': {ex.Message}"
                };
            }
        }

        // Resolves a Slang #include request against the engine's include collection (same matching as
        // IncludeParser: by path suffix or file name). Returns null if not found, so Slang reports it.
        // A file compiled from disk also finds a header by its path from the file's folder ("Includes/X.fxh").
        private string ResolveSlangInclude(string requestedPath)
        {
            if (string.IsNullOrEmpty(requestedPath))
                return null;

            var includePath = requestedPath.Replace("\"", string.Empty).Replace('/', '\\');
            // Not Path.GetFileName: '\' separates nothing on Linux, where that would hand back the whole request.
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

            // Slang asks for a nested header from its includer's folder ("Includes/Includes/X.fxh"): the shorter
            // tails of the request are tried too.
            var parts = includePath.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
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

        // "Shading.fxh" names Shading.fxh, not ForwardPlusShading.fxh: the request ends the path at a folder boundary.
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

        // Replaces every top-level `technique { ... }` block with spaces (newlines kept so line numbers in
        // Slang diagnostics stay aligned with the source). Scans the exact string handed to Slang rather than
        // relying on AST span offsets, which don't survive include expansion. Comments and strings are skipped
        // so a stray `technique` inside them is never matched.
        private static string BlankTechniqueBlocks(string source)
        {
            if (string.IsNullOrEmpty(source) || source.IndexOf("technique", StringComparison.Ordinal) < 0)
                return source;

            var s = source.ToCharArray();
            int i = 0, n = s.Length;
            while (i < n)
            {
                var c = s[i];

                if (c == '/' && i + 1 < n && s[i + 1] == '/')        // line comment
                {
                    i += 2;
                    while (i < n && s[i] != '\n') i++;
                }
                else if (c == '/' && i + 1 < n && s[i + 1] == '*')   // block comment
                {
                    i += 2;
                    while (i + 1 < n && !(s[i] == '*' && s[i + 1] == '/')) i++;
                    i = Math.Min(i + 2, n);
                }
                else if (c == '"')                                   // string literal
                {
                    i++;
                    while (i < n && s[i] != '"') { if (s[i] == '\\') i++; i++; }
                    i++;
                }
                else if (c == 't' && IsKeywordAt(s, i, "technique"))
                {
                    var end = FindBlockEnd(s, i + "technique".Length);
                    if (end < 0) break; // unbalanced; leave the rest untouched
                    for (var k = i; k < end; k++)
                        if (s[k] != '\n' && s[k] != '\r') s[k] = ' ';
                    i = end;
                }
                else
                {
                    i++;
                }
            }

            return new string(s);
        }

        private static bool IsKeywordAt(char[] s, int pos, string word)
        {
            if (pos + word.Length > s.Length) return false;
            for (var j = 0; j < word.Length; j++)
                if (s[pos + j] != word[j]) return false;
            var before = pos > 0 ? s[pos - 1] : ' ';
            var after = pos + word.Length < s.Length ? s[pos + word.Length] : ' ';
            return !IsIdentChar(before) && !IsIdentChar(after);
        }

        private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        // From just past the `technique` keyword, returns the index one past the matching closing brace.
        private static int FindBlockEnd(char[] s, int pos)
        {
            var n = s.Length;
            while (pos < n && s[pos] != '{') pos++;
            if (pos >= n) return -1;

            var depth = 0;
            for (; pos < n; pos++)
            {
                if (s[pos] == '{') depth++;
                else if (s[pos] == '}' && --depth == 0) return pos + 1;
            }
            return -1;
        }

        private EffectData.Shader CreateEffectShader(EffectShaderType type, string shaderName, string entryPoint, ShaderCompilationResult compilationResult)
        {
            var shader = new EffectData.Shader()
            {
                Name = shaderName,
                EntryPoint = entryPoint,
                Level = passSpirvProfile ?? SlangShaderCompiler.LatestSpirvProfile,
                Bytecode = compilationResult.Bytecode,
                Hashcode = Utilities.ComputeHashFNV1Modified(compilationResult.Bytecode),
                Type = type,
                ConstantBuffers = new List<EffectData.ConstantBuffer>(),
                ResourceParameters = new List<EffectData.ResourceParameter>(),
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
            // if No debug is required, take the bytecode without any debug/reflection info.
            if ((compilerFlags & EffectCompilerFlags.Debug) == 0)
                shader.Bytecode = bytecode;

            // Check that this shader was not already generated
            int shaderIndex;
            for (shaderIndex = 0; shaderIndex < effectData.Shaders.Count; shaderIndex++)
            {
                var shaderItem = effectData.Shaders[shaderIndex];
                if (shaderItem.IsSimilar(shader))
                    break;
            }

            // Check if there is a new shader to store in the archive
            if (shaderIndex >= effectData.Shaders.Count)
            {
                shaderIndex = effectData.Shaders.Count;

                effectData.Shaders.Add(shader);
            }

            LinkShader(type, shaderIndex, shader.EntryPoint);
        }

        private void LinkShader(EffectShaderType type, int shaderIndex, string entryPoint)
        {
            if (pass.Pipeline[type] == null)
                pass.Pipeline[type] = new EffectData.ShaderLink();

            pass.Pipeline[type].Index = shaderIndex;
            pass.Pipeline[type].EntryPoint = entryPoint;
        }

        private void HandleMethodExpression(Ast.MethodExpression expression)
        {
            if (expression.Arguments.Count == 1)
            {
                var argument = expression.Arguments[0];

                switch (expression.Name.Text)
                {
                    case "SetVertexShader":
                        CompileShader(EffectShaderType.Vertex, argument);
                        break;
                    case "SetPixelShader":
                        CompileShader(EffectShaderType.Fragment, argument);
                        break;
                    case "SetGeometryShader":
                        CompileShader(EffectShaderType.Geometry, argument);
                        break;
                    case "SetDomainShader":
                        CompileShader(EffectShaderType.Domain, argument);
                        break;
                    case "SetHullShader":
                        CompileShader(EffectShaderType.Hull, argument);
                        break;
                    case "SetComputeShader":
                        CompileShader(EffectShaderType.Compute, argument);
                        break;
                    default:
                        logger.Warning("Unhandled method [{0}]", expression.Span, expression.Name);
                        break;
                }
            }
            else
            {
                logger.Warning("Unhandled method [{0}]", expression.Span, expression.Name);
            }
        }

        /// <summary>
        ///   Builds the parameters for a particular shader.
        /// </summary>
        /// <param name="shader"> The shader to build parameters. </param>
        /// <param name="reflectionResult"> A shader-reflection interface accesses shader information.</param>
        private void BuildParameters(EffectData.Shader shader, SpirvReflectionResult reflectionResult)
        {
            var groupByIds = reflectionResult.AllResources.GroupBy(x => x.Description.SlotIndex).Where(group=>group.Count() > 1).SelectMany(g=>g).ToArray();

            if (groupByIds.Length > 0)
            {
                var names = groupByIds.Select(x => x.Description.Name).ToArray();
                logger.Error($"{Utilities.Join(", ", names)} has the same binding id {groupByIds[0].Description.SlotIndex} which is not permitted by Vulkan.\nPlease, change shader code not to have same binding ids in one shader.");
                return;
            }

            // Iterate on all Constant buffers used by this shader
            // Build all ParameterBuffers
            for (int i = 0; i < reflectionResult.UniformBuffers.Count; i++)
            {
                var reflectConstantBuffer = reflectionResult.UniformBuffers[i];
                var description = reflectConstantBuffer.Description;

                // Create the buffer
                var parameterBuffer = new EffectData.ConstantBuffer()
                {
                    Name = description.Name,
                    Size = description.Size,
                    Slot = description.SlotIndex,
                    Parameters = new List<EffectData.ValueTypeParameter>(),
                };
                shader.ConstantBuffers.Add(parameterBuffer);

                // Iterate on each variable declared inside this buffer
                for (uint j = 0; j < reflectConstantBuffer.VariablesCount; j++)
                {
                    var variableBuffer = reflectConstantBuffer.GetVariable(j);

                    // Build constant buffer parameter
                    var parameter = BuildConstantBufferParameter(variableBuffer);

                    // Add this parameter to the ConstantBuffer
                    parameterBuffer.Parameters.Add(parameter);
                }
            }

            var resourceParameters = new Dictionary<string, EffectData.ResourceParameter>();

            // Iterate on all resources bound in order to resolve resource dependencies for this shader.
            // If the shader is dependent from an object variable, then create this variable as well.
            foreach (var resource in reflectionResult.AllResources)
            {
                var bindingDescription = resource.Description;
                string name = bindingDescription.Name;

                // In the case of SM5.0 and texture array, there can be several input binding descriptions, so we ignore them
                // here, as we are going to recover them outside this loop.
                if (!resourceParameters.ContainsKey(name))
                {
                    // Build resource parameter
                    var parameter = BuildResourceParameter(name, bindingDescription);
                    shader.ResourceParameters.Add(parameter);
                    resourceParameters.Add(name, parameter);
                }
            }
        }

        /// <summary>
        ///   Builds an effect parameter from a reflection variable.
        /// </summary>
        /// <returns> an EffectParameter, null if not handled </returns>
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

        // spirv-cross numbers its base types its own way; the effect keeps D3D's numbering, so a cast lands on the
        // wrong names.
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

        /// <summary>
        ///   Builds an effect parameter from a reflection variable.
        /// </summary>
        /// <returns> an EffectParameter, null if not handled </returns>
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

        private static object ToFloat(EffectCompilerInternal compiler, object value)
        {
            try
            {
                return Convert.ToSingle(value);
            }
            catch (Exception)
            {
                compiler.logger.Error("Unable to convert [{0}] to float", new SourceSpan(), value);
            }
            return null;
        }

        #region Nested type: IndexedInputBindingDescription

        private class IndexedInputBindingDescription
        {
            public IndexedInputBindingDescription(int index, ShaderReflectionVariable description)
            {
                Index = index;
                Description = description;
            }

            public int Index;

            public ShaderReflectionVariable Description;
        }

        #endregion

        #region Nested type: ConvertFullItem

        private delegate object ConvertFullItem(EffectCompilerInternal compiler, object[] parameters);

        #endregion

        #region Nested type: ConvertItem

        private delegate object ConvertItem(EffectCompilerInternal compiler, object value);

        #endregion

        #region Nested type: ValueConverter

        private struct ValueConverter
        {
            public int ArgumentCount;
            public ConvertFullItem ConvertFullItem;
            public ConvertItem ConvertItem;

            public ValueConverter(string name, int argumentCount, ConvertItem convertItem, ConvertFullItem convertFullItem)
            {
                ArgumentCount = argumentCount;
                ConvertItem = convertItem;
                ConvertFullItem = convertFullItem;
            }
        }

        #endregion
    }
}

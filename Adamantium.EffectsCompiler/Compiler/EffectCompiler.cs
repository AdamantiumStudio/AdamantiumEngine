using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using Adamantium.EffectsCompiler.Compiler;
using Adamantium.Vulkan.Spirv.Reflection;

namespace Adamantium.EffectsCompiler
{
   /// <summary>
   /// Main class used to compile a Toolkit FX file.
   /// </summary>
   public static class EffectCompiler
   {
      /// <summary>
      /// Compiles an effect from file.
      /// </summary>
      /// <param name="filePath">The file path.</param>
      /// <param name="flags">The flags.</param>
      /// <param name="macros">The macrosArgs.</param>
      /// <param name="includeDirectoryList">The include directory list.</param>
      /// <param name="allowDynamicCompiling">Whether or not to allow dynamic compilation.</param>
      /// <returns>The result of compilation.</returns>
      public static EffectCompilerResult CompileFromFile(string filePath, EffectCompilerFlags flags = EffectCompilerFlags.None, List<EffectData.ShaderMacro> macros = null, List<string> includeDirectoryList = null, bool allowDynamicCompiling = false)
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
      /// <param name="allowDynamicCompiling">Whether or not to allow dynamic compilation.</param>
      /// <returns>The result of compilation.</returns>
      public static EffectCompilerResult Compile(string sourceCode, string filePath, EffectCompilerFlags flags = EffectCompilerFlags.None, List<EffectData.ShaderMacro> macrosArgs = null, List<string> includeDirectoryList = null, bool allowDynamicCompiling = false)
      {
         using var compiler = new EffectCompilerInternal();
         return compiler.Compile(sourceCode, filePath, flags, macrosArgs, includeDirectoryList, allowDynamicCompiling);
      }

      public static EffectCompilerResult Compile(string sourceCode, string filePath, ImmutableArray<ShaderFileInfo> includes, List<EffectData.ShaderMacro> macrosArgs = null, List<string> includeDirectoryList = null)
      {
         using var compiler = new EffectCompilerInternal();
         return compiler.Compile(sourceCode, filePath, includes, macrosArgs, includeDirectoryList);
      }

      /// <summary>
      /// Disassembles a shader HLSL bytecode to asm code.
      /// </summary>
      /// <param name="shader">The shader.</param>
      /// <returns>A string containing asm code decoded from HLSL bytecode.</returns>
      public static SpirvReflectionResult DisassembleShader(EffectData.Shader shader)
      {
         using var compiler = new EffectCompilerInternal();
         return compiler.DisassembleShader(shader);
      }

   }
}

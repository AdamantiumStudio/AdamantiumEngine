using System.Collections.Generic;
using MessagePack;

namespace Adamantium.EffectsCompiler;

public sealed partial class EffectData
{
    /// <summary>A compiled shader: an entry point at its stage, its SPIR-V, and the constant buffers and resources it
    /// reads.</summary>
    [MessagePackObject]
    public sealed class Shader
    {
        /// <summary>The name the shader goes by; null for none.</summary>
        [Key(0)]
        public string Name;

        /// <summary>The entry point the SPIR-V keeps its name for.</summary>
        [Key(1)]
        public string EntryPoint;

        /// <summary>The stage the shader runs at.</summary>
        [Key(2)]
        public EffectStage Stage;

        /// <summary>What the bytecode was compiled to: <see cref="EffectCompiler.SpirvProfile"/>.</summary>
        [Key(3)]
        public string Level;

        /// <summary>The SPIR-V.</summary>
        [Key(4)]
        public byte[] Bytecode;

        /// <summary>The constant buffers the shader reads.</summary>
        [Key(5)]
        public List<ConstantBuffer> ConstantBuffers = [];

        /// <summary>The resources the shader reads or writes.</summary>
        [Key(6)]
        public List<ResourceParameter> ResourceParameters = [];

        /// <summary>How many control points a hull shader reads a patch of, built-in inputs included; 0 for another
        /// stage, or a hull shader with no input per control point.</summary>
        [Key(7)]
        public uint InputPatchSize;

        public override string ToString() => $"{Stage} shader '{EntryPoint}' {Level}";
    }
}

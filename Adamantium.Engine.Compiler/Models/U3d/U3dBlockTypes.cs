namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>The block types of ECMA-363 clause 9.</summary>
internal static class U3dBlockTypes
{
    /// <summary>The file header, the first block of every file: "U3D\0" read as a little-endian U32.</summary>
    internal const uint FileHeader = 0x00443355;

    internal const uint FileReference = 0xFFFFFF12;
    internal const uint ModifierChain = 0xFFFFFF14;
    internal const uint PriorityUpdate = 0xFFFFFF15;
    internal const uint NewObjectType = 0xFFFFFF16;

    internal const uint GroupNode = 0xFFFFFF21;
    internal const uint ModelNode = 0xFFFFFF22;
    internal const uint LightNode = 0xFFFFFF23;
    internal const uint ViewNode = 0xFFFFFF24;

    internal const uint MeshDeclaration = 0xFFFFFF31;
    internal const uint PointSetDeclaration = 0xFFFFFF36;
    internal const uint LineSetDeclaration = 0xFFFFFF37;
    internal const uint MeshBaseContinuation = 0xFFFFFF3B;
    internal const uint MeshProgressiveContinuation = 0xFFFFFF3C;
    internal const uint PointSetContinuation = 0xFFFFFF3E;
    internal const uint LineSetContinuation = 0xFFFFFF3F;

    internal const uint GlyphModifier = 0xFFFFFF41;
    internal const uint SubdivisionModifier = 0xFFFFFF42;
    internal const uint AnimationModifier = 0xFFFFFF43;
    internal const uint BoneWeightModifier = 0xFFFFFF44;
    internal const uint ShadingModifier = 0xFFFFFF45;
    internal const uint ClodModifier = 0xFFFFFF46;

    internal const uint LightResource = 0xFFFFFF51;
    internal const uint ViewResource = 0xFFFFFF52;
    internal const uint LitTextureShader = 0xFFFFFF53;
    internal const uint MaterialResource = 0xFFFFFF54;
    internal const uint TextureDeclaration = 0xFFFFFF55;
    internal const uint MotionResource = 0xFFFFFF56;
    internal const uint TextureContinuation = 0xFFFFFF5C;

    /// <summary>The lowest block type a New Object Type may give its blocks.</summary>
    internal const uint FirstNewObjectBlock = 0x00000100;

    /// <summary>The highest block type a New Object Type may give its blocks.</summary>
    internal const uint LastNewObjectBlock = 0x00FFFFFF;
}

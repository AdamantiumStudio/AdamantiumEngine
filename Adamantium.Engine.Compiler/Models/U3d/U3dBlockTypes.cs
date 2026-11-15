namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>The block types of ECMA-363 clause 9.</summary>
public static class U3dBlockTypes
{
    /// <summary>The file header, the first block of every file: "U3D\0" read as a little-endian U32.</summary>
    public const uint FileHeader = 0x00443355;

    public const uint FileReference = 0xFFFFFF12;
    public const uint ModifierChain = 0xFFFFFF14;
    public const uint PriorityUpdate = 0xFFFFFF15;
    public const uint NewObjectType = 0xFFFFFF16;

    public const uint GroupNode = 0xFFFFFF21;
    public const uint ModelNode = 0xFFFFFF22;
    public const uint LightNode = 0xFFFFFF23;
    public const uint ViewNode = 0xFFFFFF24;

    public const uint MeshDeclaration = 0xFFFFFF31;
    public const uint PointSetDeclaration = 0xFFFFFF36;
    public const uint LineSetDeclaration = 0xFFFFFF37;
    public const uint MeshBaseContinuation = 0xFFFFFF3B;
    public const uint MeshProgressiveContinuation = 0xFFFFFF3C;
    public const uint PointSetContinuation = 0xFFFFFF3E;
    public const uint LineSetContinuation = 0xFFFFFF3F;

    public const uint GlyphModifier = 0xFFFFFF41;
    public const uint SubdivisionModifier = 0xFFFFFF42;
    public const uint AnimationModifier = 0xFFFFFF43;
    public const uint BoneWeightModifier = 0xFFFFFF44;
    public const uint ShadingModifier = 0xFFFFFF45;
    public const uint ClodModifier = 0xFFFFFF46;

    public const uint LightResource = 0xFFFFFF51;
    public const uint ViewResource = 0xFFFFFF52;
    public const uint LitTextureShader = 0xFFFFFF53;
    public const uint MaterialResource = 0xFFFFFF54;
    public const uint TextureDeclaration = 0xFFFFFF55;
    public const uint MotionResource = 0xFFFFFF56;
    public const uint TextureContinuation = 0xFFFFFF5C;

    /// <summary>The lowest block type a New Object Type may give its blocks.</summary>
    public const uint FirstNewObjectBlock = 0x00000100;

    /// <summary>The highest block type a New Object Type may give its blocks.</summary>
    public const uint LastNewObjectBlock = 0x00FFFFFF;
}

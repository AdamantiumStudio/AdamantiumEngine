namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>The dynamic contexts of ECMA-363 9.6 that more than one geometry block reads with.</summary>
internal static class U3dContexts
{
    internal static readonly U3dContext Shading = U3dContext.CreateDynamic("cShading");
    internal static readonly U3dContext PositionSigns = U3dContext.CreateDynamic("cPosDiffSign");
    internal static readonly U3dContext PositionX = U3dContext.CreateDynamic("cPosDiffX");
    internal static readonly U3dContext PositionY = U3dContext.CreateDynamic("cPosDiffY");
    internal static readonly U3dContext PositionZ = U3dContext.CreateDynamic("cPosDiffZ");
    internal static readonly U3dContext NormalCount = U3dContext.CreateDynamic("cNormlCnt");
    internal static readonly U3dContext NormalSigns = U3dContext.CreateDynamic("cDiffNormalSign");
    internal static readonly U3dContext NormalX = U3dContext.CreateDynamic("cDiffNormalX");
    internal static readonly U3dContext NormalY = U3dContext.CreateDynamic("cDiffNormalY");
    internal static readonly U3dContext NormalZ = U3dContext.CreateDynamic("cDiffNormalZ");
    internal static readonly U3dContext NormalIndex = U3dContext.CreateDynamic("cNormlIdx");
    internal static readonly U3dContext DiffuseSigns = U3dContext.CreateDynamic("cDiffuseColorSign");
    internal static readonly U3dContext SpecularSigns = U3dContext.CreateDynamic("cSpecularColorSign");
    internal static readonly U3dContext ColorRed = U3dContext.CreateDynamic("cColorDiffR");
    internal static readonly U3dContext ColorGreen = U3dContext.CreateDynamic("cColorDiffG");
    internal static readonly U3dContext ColorBlue = U3dContext.CreateDynamic("cColorDiffB");
    internal static readonly U3dContext ColorAlpha = U3dContext.CreateDynamic("cColorDiffA");
    internal static readonly U3dContext TextureDuplicate = U3dContext.CreateDynamic("cTexCDup");
    internal static readonly U3dContext TextureSigns = U3dContext.CreateDynamic("cTexCoordSign");
    internal static readonly U3dContext TextureU = U3dContext.CreateDynamic("cTexCDiffU");
    internal static readonly U3dContext TextureV = U3dContext.CreateDynamic("cTexCDiffV");
    internal static readonly U3dContext TextureS = U3dContext.CreateDynamic("cTexCDiffS");
    internal static readonly U3dContext TextureT = U3dContext.CreateDynamic("cTexCDiffT");
}

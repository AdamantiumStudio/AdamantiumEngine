namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>What a view node says of its instance of the view (ECMA-363 9.5.4).</summary>
internal sealed class U3dViewNodeParameters
{
    /// <summary>Three-point perspective, the default projection.</summary>
    internal const uint ThreePointPerspective = 0;

    /// <summary>Orthographic projection.</summary>
    internal const uint Orthographic = 2;

    internal uint Attributes { get; set; }

    /// <summary>The projection bits of <see cref="Attributes"/>: 0 three-point, 2 orthographic, 4 two-point, 6
    /// one-point perspective.</summary>
    internal uint ProjectionMode => Attributes & 0x6;

    internal float NearClip { get; set; }

    internal float FarClip { get; set; }

    /// <summary>The field of view in degrees, for a three-point perspective.</summary>
    internal float FieldOfView { get; set; }

    /// <summary>The height seen, for an orthographic projection.</summary>
    internal float OrthographicHeight { get; set; }

    internal float PortWidth { get; set; }

    internal float PortHeight { get; set; }

    internal uint BackdropCount { get; set; }

    internal uint OverlayCount { get; set; }
}

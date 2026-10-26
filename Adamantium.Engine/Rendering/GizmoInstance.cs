using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Rendering;

/// <summary>One drawn instance of a gizmo's mesh, as the instanced passes of <c>BasicEffect</c> read it. Laid out as the
/// shader's <c>GizmoInstance</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct GizmoInstance(Matrix4x4F world, Vector4F color)
{
    /// <summary>Where the instance stands.</summary>
    public readonly Matrix4x4F World = world;

    /// <summary>Its color.</summary>
    public readonly Vector4F Color = color;
}

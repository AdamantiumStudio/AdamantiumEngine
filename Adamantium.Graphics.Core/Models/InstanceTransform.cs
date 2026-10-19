using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Models;

/// <summary>Where one copy of a node's meshes stands, relative to the node.</summary>
public class InstanceTransform
{
    public Vector3F Position { get; set; }

    public QuaternionF Rotation { get; set; } = QuaternionF.Identity;

    public Vector3F Scale { get; set; } = Vector3F.One;
}

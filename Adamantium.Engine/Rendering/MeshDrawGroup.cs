using System.Collections.Generic;
using Adamantium.Graphics.Core.EffectsFramework;

namespace Adamantium.Engine.Rendering;

/// <summary>The copies of a mesh one instanced call draws in a frame, and where they start in the frame's instance
/// buffer.</summary>
public sealed class MeshDrawGroup
{
    /// <summary>What the copies share.</summary>
    public MeshDrawKey Key { get; set; }

    /// <summary>The effect <see cref="MeshDrawKey.Pass"/> belongs to, whose parameters the call sets.</summary>
    public Effect Effect { get; set; }

    /// <summary>The copies, in the order they were met.</summary>
    public List<MeshInstance> Instances { get; } = [];

    /// <summary>Where the first copy is in the frame's instance buffer.</summary>
    public uint First { get; set; }
}

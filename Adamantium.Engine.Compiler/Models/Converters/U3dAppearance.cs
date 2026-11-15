using Adamantium.Graphics.Core.Models;

namespace Adamantium.Engine.Compiler.Models.Converters;

/// <summary>A node waiting to be placed under the model of its parent, with its parent's place in the world.</summary>
internal sealed class U3dAppearance
{
    internal U3dAppearance(U3dChildLink link, SceneData.Model parent, Matrix4x4F parentWorld)
    {
        Link = link;
        Parent = parent;
        ParentWorld = parentWorld;
    }

    internal U3dChildLink Link { get; }

    internal SceneData.Model Parent { get; }

    internal Matrix4x4F ParentWorld { get; }
}

using System;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;

namespace Adamantium.ECS.Components;

/// <summary>
/// The parameters a shape's mesh is generated from. A change to any of them makes the entity's mesh again - in place,
/// so what draws it uploads it anew - and its collider with it, once, on the universe's next frame; nothing is made
/// while they stay as they are.
/// </summary>
[ViewModel]
public abstract partial class ProceduralShape : ActivatableComponent
{
    private const double LeastSize = 1e-3;

    /// <summary>The finest a shape is divided: past it a slip of the keyboard - 3000 for 30 - would make tens of millions
    /// of vertices on the universe's thread.</summary>
    public const int MostTessellation = 512;
    private volatile bool changed;

    /// <summary>How finely the shape is divided, passed to its generator as it is: sides of a polygon, segments round
    /// a circle, cells of a plane.</summary>
    [Bindable] private int tessellation;

    partial void OnTessellationChanged(int value) => Changed();

    /// <summary>The mesh the parameters describe now; what is made from them is taken as up to date.</summary>
    public Mesh Build()
    {
        changed = false;
        return Generate();
    }

    /// <summary>Makes the entity's mesh again if a parameter changed since it was last made; on the universe's thread,
    /// which draws it.</summary>
    public void RebuildWhenChanged()
    {
        if (!changed || Owner?.GetComponent<MeshData>() is not { } data)
        {
            return;
        }

        var mesh = Build();
        if (data.Mesh == null)
        {
            data.Mesh = mesh;
        }
        else
        {
            data.Mesh.ReplaceWith(mesh);
        }

        if (Owner.GetComponent<Collider>() is { } collider)
        {
            collider.CalculateFromMesh(data.Mesh);
            collider.RefreshShownGeometry();
        }

        Owner.Transform.IsWorldDirty = true;
    }

    protected abstract Mesh Generate();

    /// <summary>Called by a shape when one of its parameters changed.</summary>
    protected void Changed() => changed = true;

    /// <summary><see cref="Tessellation"/> kept between <paramref name="least"/> and <see cref="MostTessellation"/>.</summary>
    protected int TessellationFrom(int least) => Math.Clamp(Tessellation, least, MostTessellation);

    protected static Vector2 Positive(Vector2 size) => new(Math.Max(size.X, LeastSize), Math.Max(size.Y, LeastSize));

    protected static Vector3 Positive(Vector3 size) =>
        new(Math.Max(size.X, LeastSize), Math.Max(size.Y, LeastSize), Math.Max(size.Z, LeastSize));
}

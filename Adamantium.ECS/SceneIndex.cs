using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.ECS;

/// <summary>
/// Where the meshes of a world stand, so a ray asks a <see cref="DynamicAabbTree{T}"/> instead of visiting every entity:
/// each mesh entity's box in the world, camera-independent. Kept frame by frame by whoever walks the world's transforms:
/// <see cref="BeginFrame"/>, then <see cref="Place"/> for each entity whose box may have changed and <see cref="Keep"/>
/// for the rest, then <see cref="EndFrame"/>, which lets go of the ones the frame did not meet.
/// </summary>
public sealed class SceneIndex
{
    private readonly DynamicAabbTree<Entity> tree = new();
    private readonly Dictionary<Entity, Entry> entries = new();
    private readonly List<Entity> unseen = [];
    private readonly List<(Entity Entity, float Entry)> found = [];
    private int frame;

    /// <summary>How many entities the index holds.</summary>
    public int Count => entries.Count;

    /// <summary>Starts a frame of <see cref="Place"/> and <see cref="Keep"/>.</summary>
    public void BeginFrame()
    {
        frame++;
    }

    /// <summary>Whether <paramref name="entity"/> stands in the index with the box of this version of
    /// <paramref name="mesh"/> drawn unbent, so <see cref="Keep"/> will do.</summary>
    public bool IsCurrent(Entity entity, Mesh mesh)
    {
        return entries.TryGetValue(entity, out var entry) && !entry.Bent && ReferenceEquals(entry.Mesh, mesh)
               && entry.Version == mesh.Version;
    }

    /// <summary>Leaves <paramref name="entity"/> where it stands for this frame.</summary>
    public void Keep(Entity entity)
    {
        if (entries.TryGetValue(entity, out var entry))
        {
            entries[entity] = entry with { Seen = frame };
        }
    }

    /// <summary>Gives <paramref name="entity"/>, drawn with <paramref name="mesh"/>, <paramref name="box"/> in the world;
    /// <paramref name="bent"/> when the mesh is bent by joints, whose box is placed afresh every frame.</summary>
    public void Place(Entity entity, Mesh mesh, in BoundingBox box, bool bent = false)
    {
        if (entries.TryGetValue(entity, out var entry))
        {
            tree.Move(entry.Handle, box);
            entries[entity] = new Entry(entry.Handle, mesh, mesh.Version, frame, bent);
            return;
        }

        entries.Add(entity, new Entry(tree.Add(box, entity), mesh, mesh.Version, frame, bent));
    }

    /// <summary>The box <paramref name="entity"/> stands in, grown a little past its mesh's; false when the index does not
    /// hold it.</summary>
    public bool TryGetBox(Entity entity, out BoundingBox box)
    {
        if (entries.TryGetValue(entity, out var entry))
        {
            box = tree.BoxOf(entry.Handle);
            return true;
        }

        box = default;
        return false;
    }

    /// <summary>Lets go of every entity this frame did not meet: taken out of the world, or no longer a mesh.</summary>
    public void EndFrame()
    {
        foreach (var (entity, entry) in entries)
        {
            if (entry.Seen != frame)
            {
                unseen.Add(entity);
            }
        }

        foreach (var entity in unseen)
        {
            tree.Remove(entries[entity].Handle);
            entries.Remove(entity);
        }

        unseen.Clear();
    }

    /// <summary>Every entity whose box, grown a little, <paramref name="ray"/> passes through - the ray in the world - with
    /// how far along it, in lengths of its direction, it enters. Good until the next ray cast.</summary>
    public ReadOnlySpan<(Entity Entity, float Entry)> RayCast(in Ray ray)
    {
        found.Clear();
        tree.RayCast(ray, found);
        return CollectionsMarshal.AsSpan(found);
    }

    private readonly record struct Entry(int Handle, Mesh Mesh, int Version, int Seen, bool Bent);
}

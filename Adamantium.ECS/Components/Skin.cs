using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.ECS.Components;

/// <summary>
/// What a skinned mesh bends with: its <see cref="Skeleton"/> - entities standing for its joints, nodes of the model
/// posed by its animation or by hand, and the inverse bind matrix of each - shared by the parts of a mesh that bend
/// with the same joints.
/// </summary>
public class Skin : Component
{
    private static readonly ConditionalWeakTable<IReadOnlyDictionary<Entity, Entity>, Dictionary<Skeleton, Skeleton>>
        ClonedSkeletons = new();

    private readonly Vector3F[] boxCorners = new Vector3F[8];
    private Matrix4x4F[] pose;
    private Matrix4x4F[] posedBy;
    private int posedByCount = -1;
    private Mesh posedMesh;
    private int posedVersion;
    private Vector3F[] posed = [];
    private TriangleTree posedTree;
    private int treeVersion;
    private bool treeFitted;

    /// <summary>The joints and their inverse bind matrices; the same one for every part of a mesh bending with them.</summary>
    [DoNotClone]
    public Skeleton Skeleton { get; set; } = new();

    /// <summary>The joints of the <see cref="Skeleton"/>, as a vertex's joint indices count them.</summary>
    public List<Entity> Joints => Skeleton.Joints;

    /// <summary>The inverse bind matrices of the <see cref="Skeleton"/>'s joints.</summary>
    public List<Matrix4x4F> InverseBindMatrices => Skeleton.InverseBindMatrices;

    /// <summary>The skin <paramref name="entity"/>'s mesh is drawn with; null when it has none or is drawn unbent.</summary>
    public static Skin DrawnOn(Entity entity)
    {
        return entity.GetComponent<MeshData>() is { RenderMode: MeshRenderMode.Skinned } ? entity.GetComponent<Skin>() : null;
    }

    /// <summary>
    /// Fills <paramref name="palette"/> with each joint's matrix as the mesh is drawn now: its inverse bind matrix, the
    /// joint where it stands in the world, and back into the mesh's own space - so the draw, placing the mesh as
    /// always, puts each vertex where its joints carry it. Gives how many it filled; a joint lost is left where it was
    /// bound.
    /// </summary>
    public int FillPalette(Matrix4x4F[] palette) => FillPalette(palette, null);

    /// <summary>As <see cref="FillPalette(Matrix4x4F[])"/>, from the world matrices the transforms were worked out with
    /// for <paramref name="camera"/> this frame: what a draw for that camera takes, for each of the meshes sharing the
    /// joints without composing every joint's parents again.</summary>
    public int FillPalette(Matrix4x4F[] palette, CameraBase camera)
    {
        var mesh = Owner.Transform.GetWorldMatrixF(camera);
        Matrix4x4F.Invert(ref mesh, out var toMesh);
        var count = Math.Min(Joints.Count, palette.Length);
        for (int i = 0; i < count; i++)
        {
            var joint = Joints[i];
            var inverseBind = i < InverseBindMatrices.Count ? InverseBindMatrices[i] : Matrix4x4F.Identity;
            palette[i] = joint == null
                ? Matrix4x4F.Identity
                : inverseBind * joint.Transform.GetWorldMatrixF(camera) * toMesh;
        }

        return count;
    }

    public override void CloneReferences(IComponent clone, IReadOnlyDictionary<Entity, Entity> copies)
    {
        var cloned = ClonedSkeletons.GetValue(copies, _ => new Dictionary<Skeleton, Skeleton>());
        if (!cloned.TryGetValue(Skeleton, out var skeleton))
        {
            skeleton = new Skeleton();
            skeleton.InverseBindMatrices.AddRange(InverseBindMatrices);
            foreach (var joint in Joints)
            {
                skeleton.Joints.Add(CopyOf(joint, copies));
            }

            cloned[Skeleton] = skeleton;
        }

        ((Skin)clone).Skeleton = skeleton;
    }

    /// <summary>
    /// A box in the mesh's own space that holds every point of <paramref name="mesh"/> however its joints stand now,
    /// found without posing the points: the mesh's own box as each joint carries it, all together.
    /// </summary>
    public BoundingBox PosedBounds(Mesh mesh)
    {
        BoundingBox bound = mesh.Bounds;
        var reach = bound;
        var filled = FillPose();
        bound.GetCorners(boxCorners);
        for (int i = 0; i < filled; i++)
        {
            foreach (var corner in boxCorners)
            {
                var carried = Vector3F.TransformCoordinate(corner, pose[i]);
                reach.Minimum = Vector3F.Min(reach.Minimum, carried);
                reach.Maximum = Vector3F.Max(reach.Maximum, carried);
            }
        }

        return reach;
    }

    /// <summary>
    /// Where the points of <paramref name="mesh"/> stand now, in the mesh's own space, as the draw puts them: each
    /// carried by its joints as its weights say, a weight below zero taken as none; a point weighed to none, or a mesh
    /// without joints, stays where it is. The array is the skin's own: the next call gives it back as it is while
    /// nothing moved, or fills it again.
    /// </summary>
    public Vector3F[] PosePoints(Mesh mesh)
    {
        Pose(mesh);
        return posed;
    }

    /// <summary>The nearest triangle of <paramref name="mesh"/> along <paramref name="ray"/>, both in the mesh's own space,
    /// as the skin puts it now; the distance in lengths of the ray's direction.</summary>
    public bool Intersect(Mesh mesh, Ray ray, out float distance)
    {
        Pose(mesh);
        if (posedTree == null || treeVersion != mesh.Version || !posedTree.Describes(mesh.Points, mesh.Indices, mesh.MeshTopology))
        {
            posedTree = new TriangleTree(mesh.Points, mesh.Indices, mesh.MeshTopology);
            treeVersion = mesh.Version;
            treeFitted = false;
        }

        if (!treeFitted)
        {
            posedTree.Refit(posed);
            treeFitted = true;
        }

        return posedTree.Intersect(ray, out distance);
    }

    private int FillPose()
    {
        pose ??= new Matrix4x4F[SceneData.Controller.MaxDrawnJoints];
        return FillPalette(pose);
    }

    private void Pose(Mesh mesh)
    {
        var filled = FillPose();
        posedBy ??= new Matrix4x4F[pose.Length];
        if (ReferenceEquals(mesh, posedMesh) && mesh.Version == posedVersion && filled == posedByCount
            && pose.AsSpan(0, filled).SequenceEqual(posedBy.AsSpan(0, filled)))
        {
            return;
        }

        var points = mesh.Points;
        if (posed.Length != points.Length)
        {
            posed = new Vector3F[points.Length];
        }

        var indices = mesh.JointIndices;
        var weights = mesh.JointWeights;
        var jointed = mesh.IsAnimationPresent && indices.Length == points.Length && weights.Length == points.Length;
        for (int i = 0; i < points.Length; i++)
        {
            var point = (Vector3F)points[i];
            posed[i] = jointed ? Carry(point, indices[i], Vector4F.Max(weights[i], Vector4F.Zero), filled) : point;
        }

        pose.AsSpan(0, filled).CopyTo(posedBy);
        posedByCount = filled;
        posedMesh = mesh;
        posedVersion = mesh.Version;
        treeFitted = false;
    }

    private Vector3F Carry(Vector3F point, Vector4F joints, Vector4F weights, int filled)
    {
        var total = weights.X + weights.Y + weights.Z + weights.W;
        if (total < 1e-6f)
        {
            return point;
        }

        var carried = By(point, joints.X, filled) * weights.X + By(point, joints.Y, filled) * weights.Y
            + By(point, joints.Z, filled) * weights.Z + By(point, joints.W, filled) * weights.W;
        return carried / total;
    }

    private Vector3F By(Vector3F point, float joint, int filled)
    {
        var at = Math.Clamp((int)joint, 0, pose.Length - 1);
        return at < filled ? Vector3F.TransformCoordinate(point, pose[at]) : point;
    }
}

using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.ECS.Components
{
    /// <summary>
    /// What a skinned mesh bends with: entities standing for its joints - nodes of the model, posed by its animation or
    /// by hand - and the inverse bind matrix of each, in the order a vertex's joint indices count them.
    /// </summary>
    public class Skin : Component
    {
        /// <summary>The joints, as a vertex's joint indices count them.</summary>
        public List<Entity> Joints { get; } = new();

        /// <summary>For each joint, what takes the mesh from its own space to the joint's at the pose it was bound in.</summary>
        public List<Matrix4x4F> InverseBindMatrices { get; } = new();

        /// <summary>
        /// Fills <paramref name="palette"/> with each joint's matrix as the mesh is drawn now: its inverse bind matrix, the
        /// joint where it stands in the world, and back into the mesh's own space - so the draw, placing the mesh as
        /// always, puts each vertex where its joints carry it. Gives how many it filled; a joint lost is left where it was
        /// bound.
        /// </summary>
        public int FillPalette(Matrix4x4F[] palette)
        {
            var mesh = Owner.Transform.GetWorldMatrixF();
            Matrix4x4F.Invert(ref mesh, out var toMesh);
            var count = System.Math.Min(Joints.Count, palette.Length);
            for (int i = 0; i < count; i++)
            {
                var joint = Joints[i];
                var inverseBind = i < InverseBindMatrices.Count ? InverseBindMatrices[i] : Matrix4x4F.Identity;
                palette[i] = joint == null
                    ? Matrix4x4F.Identity
                    : inverseBind * joint.Transform.GetWorldMatrixF() * toMesh;
            }

            return count;
        }
    }
}

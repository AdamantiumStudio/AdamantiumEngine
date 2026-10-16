using System;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;

namespace Adamantium.ECS.Components
{
    [ViewModel]
    public abstract partial class Collider : ActivatableComponent
    {
        public Vector3F Size { get; set; }

        public Vector3F LocalCenter => Bounds.Center;

        public Vector3F Scale { get; set; }

        public QuaternionF Rotation { get; set; }

        public Vector3F Position { get; set; }

        public Bounds Bounds { get; set; }

        public Vector3F Center { get; set; }

        [Bindable]
        public partial bool DisplayCollider { get; set; }

        // The CPU mesh used to visualize the collider bounds (debug draw). Its GPU buffers are built + owned by the
        // render-layer geometry cache, keyed by this mesh - the collider no longer holds any GPU resource itself.
        public Mesh Geometry { get; protected set; }

        public override void Initialize()
        {
            var meshData = Owner?.GetComponent<MeshData>();
            if (meshData == null) return;
            CalculateFromMesh(meshData.Mesh);
            base.Initialize();
        }

        public abstract void ClearData();


        public abstract bool ContainsDataFor(CameraBase camera);

        protected Collider()
        {
        }

        public abstract Mesh GetVisualRepresentation();

        public abstract void UpdateForCamera(CameraBase camera);

        public abstract ContainmentType IsInsideCameraFrustum(Camera camera);

        public abstract void Transform(ref Vector3F scale, ref QuaternionF rotation, ref Vector3F translation);

        public abstract void Transform(ref float uniformScale, ref QuaternionF rotation, ref Vector3F translation);

        public virtual void CalculateFromMesh(Mesh mesh)
        {
            Bounds = mesh.Bounds;
        }

        public abstract void Merge(Collider collider);

        public abstract bool Intersects(ref Ray ray, out Vector3F point);

        public abstract bool IntersectsForCamera(Camera camera, ref Ray ray, out Vector3F point);

        /// <summary>
        /// The collider where its entity stands in the world, as a shape <see cref="Gjk"/> can test; null while it holds
        /// nothing.
        /// </summary>
        public abstract IConvexShape GetWorldShape();

        /// <summary>Whether this collider and <paramref name="other"/> share a point where their entities stand.</summary>
        public bool Intersects(Collider other)
        {
            var first = GetWorldShape();
            var second = other.GetWorldShape();
            return first != null && second != null && Gjk.Intersects(first, second);
        }

        protected Matrix4x4F PlacementOf(Entity part)
        {
            var placement = Matrix4x4F.Identity;
            for (var at = part; at != Owner; at = at.Owner)
            {
                if (at == null)
                {
                    throw new ArgumentException("Only a collider of this entity or of one under it can be merged in.");
                }

                placement *= at.Transform.GetLocalMatrixF();
            }

            return placement;
        }
    }
}

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
        private bool outlineIsStale;

        [Bindable]
        public partial Vector3F Size { get; set; }

        public Vector3F LocalCenter => Bounds.Center.As<float>();

        [Bindable]
        public partial Vector3F Scale { get; set; }

        [Bindable]
        public partial QuaternionF Rotation { get; set; }

        [Bindable]
        public partial Vector3F Position { get; set; }

        /// <summary>The box the collider is fitted to; a change tells what follows the collider's shape - the physics.
        /// </summary>
        [Bindable]
        public partial Bounds Bounds { get; set; }

        partial void OnBoundsChanged(Bounds value) => FollowBounds();

        /// <summary>Called when <see cref="Bounds"/> changes: a collider whose shape stands on them builds it anew.
        /// </summary>
        protected virtual void FollowBounds() => MarkOutlineStale();

        [Bindable]
        public partial Vector3F Center { get; set; }

        [Bindable]
        public partial bool DisplayCollider { get; set; }

        // The CPU mesh used to visualize the collider bounds (debug draw). Its GPU buffers are built + owned by the
        // render-layer geometry cache, keyed by this mesh - the collider no longer holds any GPU resource itself.
        [Bindable]
        public partial Mesh Geometry { get; protected set; }

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

        /// <summary>The shown collider: built the first time it is asked for, and filled anew in the same mesh when the
        /// shape changed since.</summary>
        public Mesh GetVisualRepresentation()
        {
            // Cleared before building, so a change made meanwhile is built next time.
            var stale = outlineIsStale;
            outlineIsStale = false;
            if (Geometry == null)
            {
                Geometry = BuildOutline();
            }
            else if (stale)
            {
                Geometry.ReplaceWith(BuildOutline());
            }

            return Geometry;
        }

        /// <summary>A new outline mesh of what the collider holds now.</summary>
        protected abstract Mesh BuildOutline();

        public abstract void UpdateForCamera(CameraBase camera);

        public abstract ContainmentType IsInsideCameraFrustum(Camera camera);

        public abstract void Transform(ref Vector3F scale, ref QuaternionF rotation, ref Vector3F translation);

        public abstract void Transform(ref float uniformScale, ref QuaternionF rotation, ref Vector3F translation);

        public virtual void CalculateFromMesh(Mesh mesh)
        {
            Bounds = mesh.Bounds;
        }

        /// <summary>Tells that the shape changed: the shown outline is built anew when it is next asked for.</summary>
        protected void MarkOutlineStale() => outlineIsStale = true;

        public abstract void Merge(Collider collider);

        public abstract bool Intersects(ref Ray<float> ray, out Vector3F point);

        public abstract bool IntersectsForCamera(Camera camera, ref Ray<float> ray, out Vector3F point);

        /// <summary>
        /// The collider where its entity stands in the world, as a shape <see cref="Gjk"/> can test; null while it holds
        /// nothing.
        /// </summary>
        public abstract IConvexShape<float> GetWorldShape();

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

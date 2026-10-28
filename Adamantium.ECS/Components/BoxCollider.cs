using System;
using System.Collections.Generic;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.ECS.Components
{
    public class BoxCollider : Collider
    {
        private OrientedBoundingBox<float> obb;
        private bool hasBox;

        public BoxCollider()
        {
            ColliderData = new Dictionary<CameraBase, OrientedBoundingBox<float>>();
        }
        
        protected Dictionary<CameraBase, OrientedBoundingBox<float>> ColliderData { get; }

        public override void ClearData()
        {
            ColliderData.Clear();
        }

        public override bool ContainsDataFor(CameraBase camera)
        {
            return ColliderData.ContainsKey(camera);
        }

        public override Mesh GetVisualRepresentation()
        {
            Geometry = Shapes.Cube.GenerateGeometry(GeometryType.Outlined, Bounds.Size.X, Bounds.Size.Y, Bounds.Size.Z, 1, Matrix4x4.CreateTranslation<double>(Bounds.Center));
            return Geometry;
        }

        public override void UpdateForCamera(CameraBase camera)
        {
            // Transform the LOCAL box by the entity's composed world matrix - the exact same matrix the mesh is drawn with
            // - so the culling bounds follow the transform hierarchy. Using the node's own Scale/Rotation/local position
            // instead left a child's bounds at its local offset while the mesh drew at its composed world position, so the
            // frustum test culled parts that were actually on screen (they vanished as the camera moved in).
            var bb = obb.Transform(Owner.Transform.GetMetadata(camera).WorldMatrixF);

            ColliderData[camera] = bb;
        }

        public override ContainmentType IsInsideCameraFrustum(Camera camera)
        {
            return camera.Frustum.Contains(ColliderData.TryGetValue(camera, out var bb) ? bb : obb);
        }

        public override void Transform(ref Vector3F scale, ref QuaternionF rotation, ref Vector3F translation)
        {
            obb = obb.Transform(scale, rotation, translation);
        }

        public override void Transform(ref float uniformScale, ref QuaternionF rotation, ref Vector3F translation)
        {
            obb = obb.Transform(uniformScale, rotation, translation);
        }

        public override void CalculateFromMesh(Mesh mesh)
        {
            obb = mesh.Bounds.ToOrientedBoundingBox().As<float>();
            hasBox = true;
            base.CalculateFromMesh(mesh);
        }

        public void CalculateFromPoints(Vector3F[] points)
        {
            obb = OrientedBoundingBox.CreateFromPoints<float>(points);
            hasBox = true;
            Bounds = Bounds.FromBoundingBox(obb.As<double>());
        }

        /// <summary>
        /// Grows the box to take in <paramref name="collider"/> of this entity or of one under it, as it stands here.
        /// </summary>
        public override void Merge(Collider collider)
        {
            Span<Vector3F> corners = stackalloc Vector3F[OrientedBoundingBox<float>.CornerCount];
            collider.Bounds.ToOrientedBoundingBox().As<float>().Transform(PlacementOf(collider.Owner)).GetCorners(corners);
            var placed = OrientedBoundingBox.CreateFromPoints<float>(corners);
            obb = hasBox ? OrientedBoundingBox.CreateMerged(obb, placed) : placed;
            hasBox = true;
            Bounds = Bounds.FromBoundingBox(obb.As<double>());
        }

        public override IConvexShape<float> GetWorldShape()
        {
            return obb.Transform(Owner.Transform.GetWorldMatrixF());
        }

        public override bool Intersects(ref Ray<float> ray, out Vector3F point)
        {
            return obb.Intersects(ray, out point);
        }

        public override bool IntersectsForCamera(Camera camera, ref Ray<float> ray, out Vector3F point)
        {
            point = Vector3F.Zero;
            if (ColliderData.ContainsKey(camera))
            {
                return ColliderData[camera].Intersects(ray, out point);
            }
            return false;
        }
    }

}

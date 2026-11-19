using System.Collections.Generic;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.ECS.Components
{
    /// <summary>A box round an entity: <see cref="Size"/> on its own axes, placed by the collider's placement.</summary>
    [ViewModel]
    public partial class BoxCollider : Collider
    {
        private OrientedBoundingBox<float> obb;

        public BoxCollider()
        {
            ColliderData = new Dictionary<CameraBase, OrientedBoundingBox<float>>();
        }

        protected Dictionary<CameraBase, OrientedBoundingBox<float>> ColliderData { get; }

        /// <summary>The box's size along its own axes, before <see cref="Collider.Scale"/>.</summary>
        [Bindable]
        public partial Vector3F Size { get; set; }

        partial void OnSizeChanged(Vector3F value) => Resize();

        public override void ClearData()
        {
            ColliderData.Clear();
        }

        public override bool ContainsDataFor(CameraBase camera)
        {
            return ColliderData.ContainsKey(camera);
        }

        public override void UpdateForCamera(CameraBase camera)
        {
            // Transform the LOCAL box by the entity's composed world matrix - the exact same matrix the mesh is drawn with
            // - so the culling bounds follow the transform hierarchy.
            ColliderData[camera] = obb.Transform(Owner.Transform.GetMetadata(camera).WorldMatrixF);
        }

        public override ContainmentType IsInsideCameraFrustum(Camera camera)
        {
            return camera.Frustum.Contains(ColliderData.TryGetValue(camera, out var bb) ? bb : obb);
        }

        /// <summary>Fits the box to hold <paramref name="points"/>, given in its entity's own space.</summary>
        public void CalculateFromPoints(Vector3F[] points)
        {
            FitToBox(Bounds.FromBoundingBox(OrientedBoundingBox.CreateFromPoints<float>(points).As<double>()));
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
            return ColliderData.TryGetValue(camera, out var placed) && placed.Intersects(ray, out point);
        }

        // Stretched or turned, a box's volume is its own times its entity's scale: the scale stretches all space alike.
        public override double MeasureVolume(Vector3F scale)
        {
            var own = Vector3F.Abs(Size * Scale);
            var size = Vector3F.Abs(scale);
            return (double)own.X * own.Y * own.Z * size.X * size.Y * size.Z;
        }

        protected override void FitBox(Bounds box)
        {
            Size = box.Size.As<float>();
            Position = box.Center.As<float>();
            Rotation = TurnOf(box);
        }

        protected override void WriteSizes(ColliderShapeEdits shape) => shape.Size = Size;

        protected override void TakeSizes(ColliderShapeEdits shape) => Size = shape.Size;

        protected override Bounds PlaceShape()
        {
            obb = new OrientedBoundingBox<float>(Vector3F.Zero, Vector3F.Abs(Size) * 0.5f, QuaternionF.Identity).Transform(Placement);
            return Bounds.FromBoundingBox(obb.As<double>());
        }

        protected override Mesh BuildOutline()
        {
            return Shapes.Cube.GenerateGeometry(GeometryType.Outlined, Size.X, Size.Y, Size.Z, 1,
                Matrix4x4.CreateScale<double>(Scale.As<double>()) * Matrix4x4.CreateFromQuaternion<double>(Turn.As<double>()) *
                Matrix4x4.CreateTranslation<double>(Position.As<double>()));
        }
    }
}

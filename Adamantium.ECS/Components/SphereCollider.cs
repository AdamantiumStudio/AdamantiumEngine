using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.ECS.Components
{
    /// <summary>A sphere round an entity: <see cref="Radius"/> about the collider's <see cref="Collider.Position"/>,
    /// grown by the largest of its <see cref="Collider.Scale"/>.</summary>
    [ViewModel]
    [Addable(ComponentGroups.Physics, Icon = "PhysicsSphereIcon")]
    public partial class SphereCollider : Collider
    {
        private BoundingSphere<float> sphere;

        public SphereCollider()
        {
            ColliderData = new Dictionary<CameraBase, BoundingSphere<float>>();
        }

        protected Dictionary<CameraBase, BoundingSphere<float>> ColliderData { get; }

        /// <summary>The radius, before <see cref="Collider.Scale"/>; a fit gives half the longest side of the mesh.
        /// </summary>
        [Bindable]
        public partial float Radius { get; set; }

        partial void OnRadiusChanged(float value) => Resize();

        /// <summary>The sphere in its entity's own space.</summary>
        public BoundingSphere<float> Sphere => sphere;

        // A ball stays round: it grows by its entity's largest scale.
        public override double MeasureVolume(Vector3F scale)
        {
            double radius = sphere.Radius * Vector3F.Max(Vector3F.Abs(scale));
            return 4.0 / 3.0 * Math.PI * radius * radius * radius;
        }

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
            // The LOCAL sphere by the composed world matrix (what the mesh is drawn with), so a child culls where it is.
            ColliderData[camera] = sphere.Transform(Owner.Transform.GetMetadata(camera).WorldMatrixF);
        }

        public override ContainmentType IsInsideCameraFrustum(Camera camera)
        {
            return camera.Frustum.Contains(ColliderData.TryGetValue(camera, out var placed) ? placed : sphere);
        }

        public override bool Intersects(ref Ray<float> ray, out Vector3F point)
        {
            return sphere.Intersects(ray, out point);
        }

        public override IConvexShape<float> GetWorldShape(WorldPosition origin)
        {
            return sphere.Transform(Owner.Transform.GetWorldMatrixF(origin));
        }

        public override bool IntersectsForCamera(Camera camera, ref Ray<float> ray, out Vector3F point)
        {
            point = Vector3F.Zero;
            return ColliderData.TryGetValue(camera, out var placed) && placed.Intersects(ray, out point);
        }

        protected override void FitBox(Bounds box)
        {
            Radius = Vector3F.Max(box.Size.As<float>()) * 0.5f;
            Position = box.Center.As<float>();
            Rotation = TurnOf(box);
        }

        protected override void WriteSizes(ColliderShapeEdits shape) => shape.Radius = Radius;

        protected override void TakeSizes(ColliderShapeEdits shape) => Radius = shape.Radius;

        protected override Bounds PlaceShape()
        {
            sphere = new BoundingSphere<float>(Vector3F.Zero, Math.Abs(Radius)).Transform(Placement);
            return new Bounds(sphere.Center.As<double>(), new Vector3<double>(sphere.Radius), Quaternion<double>.Identity);
        }

        protected override Mesh BuildOutline()
        {
            return Shapes.Sphere.GenerateGeometry(GeometryType.Outlined, SphereType.UVSphere, sphere.Radius * 2, 40,
                Matrix4x4.CreateTranslation<double>(sphere.Center.As<double>()));
        }
    }
}

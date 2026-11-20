using System;
using System.Collections.Generic;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;

namespace Adamantium.ECS.Components.Extensions
{
    public static class EntityExtentions
    {
        public static void SetWireFrame(this Entity entity, bool isWireFrame = true)
        {
            entity.TraverseInDepth(
               current =>
               {
                   var geometry = current.GetComponent<MeshData>();

                   if (geometry != null)
                   {
                       geometry.IsWireFrame = isWireFrame;
                   }
               });
        }

        /// <summary>The box, in the entity's own axes, that holds the enabled meshes of the entity and of the enabled
        /// entities under it - an enabled collider's bounds for one without a mesh - but <paramref name="leftOut"/> and what
        /// is under it; false when none of them has any.</summary>
        public static bool TryMeasureBounds(this Entity owner, out Bounds bounds, Entity leftOut = null)
        {
            bounds = default;
            if (owner == null)
            {
                return false;
            }

            var minimum = new Vector3F(float.MaxValue);
            var maximum = new Vector3F(float.MinValue);
            var found = false;
            var entities = Entity.StackPool.Get();
            var placements = Entity.MatrixStackPool.Get();
            try
            {
                entities.Push(owner);
                placements.Push(Matrix4x4F.Identity);
                while (entities.Count > 0)
                {
                    var current = entities.Pop();
                    var placement = placements.Pop();
                    foreach (var child in current.Dependencies.AsSpan())
                    {
                        if (child == null || child == leftOut || !child.IsEnabled) continue;
                        entities.Push(child);
                        placements.Push(child.Transform.GetLocalMatrixF() * placement);
                    }

                    if (!TryGetOwnBounds(current, out var own))
                    {
                        continue;
                    }

                    Grow(own, placement, ref minimum, ref maximum);
                    found = true;
                }
            }
            finally
            {
                Entity.StackPool.Return(entities);
                Entity.MatrixStackPool.Return(placements);
            }

            if (found)
            {
                bounds = new Bounds(((minimum + maximum) * 0.5f).As<double>(), ((maximum - minimum) * 0.5f).As<double>(),
                    Quaternion<double>.Identity);
            }

            return found;
        }

        /// <summary>The bounds of <paramref name="owner"/> and of every enabled entity under it, each in its own axes,
        /// measured in one walk from the leaves up - a box of its children's boxes, so a little looser than
        /// <see cref="TryMeasureBounds"/> under turned parts; one with nothing to measure is left out.</summary>
        public static void MeasureBoundsBelow(this Entity owner, Dictionary<Entity, Bounds> into)
        {
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentNullException.ThrowIfNull(into);
            var walked = new List<Entity>();
            owner.TraverseInDepth(walked.Add, ignoreDisabled: true);
            for (var index = walked.Count - 1; index >= 0; index--)
            {
                var current = walked[index];
                var minimum = new Vector3F(float.MaxValue);
                var maximum = new Vector3F(float.MinValue);
                var found = TryGetOwnBounds(current, out var own);
                if (found)
                {
                    Grow(own, Matrix4x4F.Identity, ref minimum, ref maximum);
                }

                foreach (var child in current.Dependencies.AsSpan())
                {
                    if (child != null && into.TryGetValue(child, out var below))
                    {
                        Grow(below, child.Transform.GetLocalMatrixF(), ref minimum, ref maximum);
                        found = true;
                    }
                }

                if (found)
                {
                    into[current] = new Bounds(((minimum + maximum) * 0.5f).As<double>(),
                        ((maximum - minimum) * 0.5f).As<double>(), Quaternion<double>.Identity);
                }
            }
        }

        // Grows the box to hold the turned box own, placed by placement, without visiting its corners.
        private static void Grow(in Bounds own, in Matrix4x4F placement, ref Vector3F minimum, ref Vector3F maximum)
        {
            var orientation = own.Orientation.As<float>();
            if (orientation.LengthSquared() == 0)
            {
                orientation = QuaternionF.Identity;
            }

            var half = own.HalfExtent.As<float>();
            var axisX = Vector3F.TransformNormal(Vector3F.Transform(Vector3F.UnitX, orientation) * half.X, placement);
            var axisY = Vector3F.TransformNormal(Vector3F.Transform(Vector3F.UnitY, orientation) * half.Y, placement);
            var axisZ = Vector3F.TransformNormal(Vector3F.Transform(Vector3F.UnitZ, orientation) * half.Z, placement);
            var reach = Vector3F.Abs(axisX) + Vector3F.Abs(axisY) + Vector3F.Abs(axisZ);
            var center = Vector3F.TransformCoordinate(own.Center.As<float>(), placement);
            minimum = Vector3F.Min(minimum, center - reach);
            maximum = Vector3F.Max(maximum, center + reach);
        }

        public static WorldPosition GetCenterAbsolute(this Entity owner)
        {
            var transform = owner.Transform;
            return owner.TryMeasureBounds(out var bounds)
                ? new WorldPosition(Vector3<double>.TransformCoordinate(bounds.Center, transform.GetWorldMatrix()))
                : transform.WorldPosition;
        }

        public static Vector3F GetLocalCenter(this Entity owner)
        {
            return owner.TryMeasureBounds(out var bounds) ? bounds.Center.As<float>() : Vector3F.Zero;
        }

        public static Vector3F GetCenterRelative(this Entity owner, CameraBase camera)
        {
            var collision = owner?.GetComponent<Collider>();
            if (collision != null && collision.ContainsDataFor(camera))
            {
                return owner.Transform.GetMetadata(camera).RelativePosition + owner.GetLocalCenter();
            }
            return Vector3F.Zero;
        }

        public static Double GetRadius(this Entity owner)
        {
            if (!owner.TryMeasureBounds(out var bounds))
            {
                throw new InvalidOperationException($"{owner.Name} has no mesh or collider to measure.");
            }
            return bounds.HalfExtent.Length();
        }

        public static Double GetDiameter(this Entity owner)
        {
            // No bounds, no size. It used to answer with the largest component of the entity's POSITION, which was
            // never a diameter and is now a position relative to a parent besides.
            return owner.TryMeasureBounds(out var bounds) ? Vector3F.Max(bounds.Size.As<float>()) : 0;
        }

        private static bool TryGetOwnBounds(Entity entity, out Bounds bounds)
        {
            if (entity.GetComponent<MeshData>() is { IsEnabled: true, Mesh: { HasPoints: true } mesh })
            {
                bounds = mesh.Bounds;
                return true;
            }

            bounds = entity.GetComponent<Collider>() is { IsEnabled: true } collider ? collider.Bounds : default;
            return bounds.HalfExtent != Vector3<double>.Zero || bounds.Center != Vector3<double>.Zero;
        }

        public static Single GetDistanceToCamera(this Entity owner, CameraBase camera)
        {
            return owner.GetRelativePosition(camera).Length();
        }

        public static Vector3F GetRelativePosition(this Entity owner, CameraBase camera)
        {
            return owner.Transform.GetRelativePosition(camera.GetOwnerPosition());
        }

        public static WorldPosition GetOwnerPosition(this IEntityOwner component)
        {
            return component.Owner.Transform.WorldPosition;
        }

        public static QuaternionF GetOwnerRotation(this IEntityOwner component)
        {
            return component.Owner.Transform.Rotation;
        }

        public static WorldPosition GetPositionForNewObject(this Entity owner, CameraBase camera)
        {
            return camera.GetOwnerPosition() + owner.GetDiameter() * (Vector3<double>)camera.Forward * camera.Fov;
        }

        public static WorldPosition GetPositionForNewObject(this Entity owner, CameraBase camera, double diameter)
        {
            return camera.GetOwnerPosition() + diameter * (Vector3<double>)camera.Forward * (camera.Fov / 4);
        }
    }
}

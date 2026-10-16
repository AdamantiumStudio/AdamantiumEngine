using System;
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

        public static Vector3 GetCenterAbsolute(this Entity owner)
        {
            var transform = owner.Transform;
            var collision = owner.GetComponent<Collider>();

            return collision == null
                ? transform.WorldPosition
                : (Vector3)Vector3F.TransformCoordinate(collision.LocalCenter, transform.GetWorldMatrixF());
        }

        public static Vector3F GetLocalCenter(this Entity owner)
        {
            var collision = owner?.GetComponent<Collider>();
            if (collision == null)
            {
                return Vector3F.Zero;
            }

            return collision.LocalCenter;
        }

        public static Vector3F GetCenterRelative(this Entity owner, CameraBase camera)
        {
            var collision = owner?.GetComponent<Collider>();
            if (collision != null && collision.ContainsDataFor(camera))
            {
                return owner.Transform.GetMetadata(camera).RelativePosition + collision.LocalCenter;
            }
            return Vector3F.Zero;
        }

        public static Double GetRadius(this Entity owner)
        {
            var collision = owner.GetComponent<Collider>();

            if (collision == null)
            {
                throw new NullReferenceException("GetObjectCenter. Collision component is null");
            }
            return collision.Bounds.HalfExtent.Length();
        }

        public static Double GetDiameter(this Entity owner)
        {
            if (owner == null) return 0;
            
            // No bounds, no size. It used to answer with the largest component of the entity's POSITION, which was
            // never a diameter and is now a position relative to a parent besides.
            var collision = owner.GetComponent<Collider>();

            return collision == null ? 0 : Vector3F.Max(collision.Bounds.Size);
        }

        public static Matrix4x4F GetActualMatrixF(this Entity owner, CameraBase camera)
        {
            return owner.Transform.GetMetadata(camera).WorldMatrixF;
        }
        
        public static Matrix4x4 GetActualMatrix(this Entity owner, CameraBase camera)
        {
            return owner.Transform.GetMetadata(camera).WorldMatrix;
        }

        public static Single GetDistanceToCamera(this Entity owner, CameraBase camera)
        {
            return owner.GetRelativePosition(camera).Length();
        }

        public static Vector3F GetRelativePosition(this Entity owner, CameraBase camera)
        {
            return owner.Transform.GetRelativePosition(camera.GetOwnerPosition());
        }

        public static Vector3 GetOwnerPosition(this IEntityOwner component)
        {
            return component.Owner.Transform.WorldPosition;
        }

        public static QuaternionF GetOwnerRotation(this IEntityOwner component)
        {
            return component.Owner.Transform.Rotation;
        }

        public static Vector3 GetPositionForNewObject(this Entity owner, CameraBase camera)
        {
            return camera.GetOwnerPosition() + (owner.GetDiameter() * (Vector3)camera.Forward * camera.Fov);
        }

        public static Vector3 GetPositionForNewObject(this Entity owner, CameraBase camera, double diameter)
        {
            return camera.GetOwnerPosition() + (diameter * (Vector3)camera.Forward * (camera.Fov/4));
        }
    }
}

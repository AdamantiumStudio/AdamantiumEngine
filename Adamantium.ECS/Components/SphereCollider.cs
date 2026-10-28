using System;
using System.Collections.Generic;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.ECS.Components
{
   public class SphereCollider: Collider
   {
      private BoundingSphere<float> sphere;
      protected Dictionary<CameraBase, BoundingSphere<float>> ColliderData { get; }

      public SphereCollider()
      {
         ColliderData = new Dictionary<CameraBase, BoundingSphere<float>>();
      }

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
         Geometry = Shapes.Sphere.GenerateGeometry(GeometryType.Outlined, SphereType.UVSphere, Vector3F.Max(Bounds.Size.As<float>()), 40);
         return Geometry;
      }

      public override void CalculateFromMesh(Mesh mesh)
      {
         sphere = mesh.Bounds.ToBoundingSphere().As<float>();
         base.CalculateFromMesh(mesh);
      }

      public override void UpdateForCamera(CameraBase camera)
      {
         // Same fix as BoxCollider: transform the LOCAL sphere by the composed world matrix (what the mesh is drawn with)
         // so hierarchical (child) entities cull against their real world position, not their local offset.
         var bb = sphere.Transform(Owner.Transform.GetMetadata(camera).WorldMatrixF);
         if (!ColliderData.ContainsKey(camera))
         {
            ColliderData.Add(camera, bb);
         }
         else
         {
            ColliderData[camera] = bb;
         }
      }

      public override ContainmentType IsInsideCameraFrustum(Camera camera)
      {
         BoundingSphere<float> data;
         if (ColliderData.TryGetValue(camera, out data))
         {
            return camera.Frustum.Contains(data);
         }
         return camera.Frustum.Contains(sphere);
      }

      public override void Transform(ref Vector3F scale, ref QuaternionF rotation, ref Vector3F translation)
      {
         sphere = sphere.Transform(Matrix4x4.CreateScale<float>(scale) * Matrix4x4.CreateFromQuaternion<float>(rotation) * Matrix4x4.CreateTranslation<float>(translation));
      }

      public override void Transform(ref float uniformScale, ref QuaternionF rotation, ref Vector3F translation)
      {
         sphere = sphere.Transform(Matrix4x4.CreateScale<float>(uniformScale) * Matrix4x4.CreateFromQuaternion<float>(rotation) * Matrix4x4.CreateTranslation<float>(translation));
      }

      public override void Merge(Collider collider)
      {
         throw new NotImplementedException();
      }

      public override bool Intersects(ref Ray<float> ray, out Vector3F point)
      {
         return sphere.Intersects(ray, out point);
      }

      public override IConvexShape<float> GetWorldShape()
      {
         return sphere.Transform(Owner.Transform.GetWorldMatrixF());
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

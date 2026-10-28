using System;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.Engine.Templates.CameraTemplates;

public class CameraVisualTemplate
{
    public Entity BuildEntity(Entity owner, string name)
    {
        float halfSize = 1f;
        var nearRectangle = Shapes.Rectangle.GenerateGeometry(GeometryType.Outlined, halfSize/2, halfSize/4, new CornerRadius(0));

        var farRectangle = Shapes.Rectangle.GenerateGeometry(GeometryType.Outlined, halfSize*2, halfSize, new CornerRadius(0), 2, Matrix4x4.CreateTranslation<double>(Vector3<double>.UnitZ*2));

        var direction1 = Shapes.Line.GenerateGeometry(GeometryType.Outlined, new Vector3<double>(-halfSize / 4, halfSize / 8, 0), new Vector3<double>(-halfSize, halfSize / 2, halfSize * 2), 0);
        var direction2 = Shapes.Line.GenerateGeometry(GeometryType.Outlined, new Vector3<double>(-halfSize / 4, -halfSize / 8, 0), new Vector3<double>(-halfSize, -halfSize / 2, halfSize * 2), 0);
        var direction3 = Shapes.Line.GenerateGeometry(GeometryType.Outlined, new Vector3<double>(halfSize / 4, halfSize / 8, 0), new Vector3<double>(halfSize, halfSize / 2, halfSize * 2), 0);
        var direction4 = Shapes.Line.GenerateGeometry(GeometryType.Outlined, new Vector3<double>(halfSize / 4, -halfSize / 8, 0), new Vector3<double>(halfSize, -halfSize / 2, halfSize * 2), 0);

        nearRectangle.Merge(farRectangle, direction1, direction2, direction3, direction4);
            
        var root = BuildSubEntity(owner, name, Colors.Blue, nearRectangle, BoundingVolume.OrientedBox);
        return root;
    }

    protected Entity BuildSubEntity(Entity owner, String name, Color color, Mesh geometry, BoundingVolume volume = BoundingVolume.None, float transparency = 1.0f)
    {
        Material material = new Material();
        material.MeshColor = color.ToVector3();
        material.HighlightColor = Colors.Yellow.ToVector4();
        material.Transparency = transparency;

        var entity = new Entity(owner, name);

        var meshData = new MeshData();
        meshData.Mesh = geometry;
        entity.AddComponent(meshData);
        Collider collider = null;
        switch (volume)
        {
            case BoundingVolume.OrientedBox:
                collider = new BoxCollider();
                entity.AddComponent(collider);
                break;
            case BoundingVolume.Sphere:
                collider = new SphereCollider();
                entity.AddComponent(collider);
                break;
        }

        entity.AddComponent(material);

        return entity;
    }
}
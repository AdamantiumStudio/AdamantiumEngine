using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class MeshMergeTests
{
    [Test]
    public void Merge_WithAPartWithoutNormals_KeepsNone_AndStaysDrawable()
    {
        var merged = Triangle(withNormals: false).Merge(Triangle(withNormals: true));

        Assert.That(merged.IsNormalsPresent, Is.False);
        Assert.That(merged.Normals, Is.Empty);
        Assert.DoesNotThrow(() => merged.ToMeshVertices());
    }

    [Test]
    public void Merge_EveryPartWithNormals_KeepsThemAll_TurnedWithTheirPart()
    {
        var turned = new MergeInstance(Triangle(withNormals: true), Matrix4x4.CreateRotationY<double>(float.DegreesToRadians(90)), true);

        var merged = Triangle(withNormals: true).Merge([turned]);

        Assert.That(merged.Normals.Length, Is.EqualTo(merged.Points.Length));
        Assert.That((merged.Normals[0] - Vector3F.UnitZ).Length(), Is.LessThan(1e-5f));
        var expected = Vector3F.Normalize(Vector3F.TransformNormal(Vector3F.UnitZ, Matrix4x4.CreateRotationY<double>(float.DegreesToRadians(90)).As<float>()));
        Assert.That((merged.Normals[3] - expected).Length(), Is.LessThan(1e-5f));
    }

    [Test]
    public void Merge_TextureCoordinates_OfSomeParts_AreKeptInStep()
    {
        var textured = Triangle(withNormals: false).SetUVs(0, [new Vector2F(0, 0), new Vector2F(1, 0), new Vector2F(0, 1)]);

        var merged = Triangle(withNormals: false).Merge(textured);

        Assert.That(merged.UV0.Length, Is.EqualTo(merged.Points.Length));
        Assert.That(merged.UV0[4], Is.EqualTo(new Vector2F(1, 0)));
        Assert.DoesNotThrow(() => merged.ToMeshVertices());
    }

    private static Mesh Triangle(bool withNormals)
    {
        var mesh = new Mesh(PrimitiveType.TriangleList)
            .SetPoints([new Vector3<double>(0, 0, 0), new Vector3<double>(1, 0, 0), new Vector3<double>(0, 1, 0)])
            .SetIndices([0, 1, 2]);
        return withNormals ? mesh.SetNormals([Vector3F.UnitZ, Vector3F.UnitZ, Vector3F.UnitZ]) : mesh;
    }
}

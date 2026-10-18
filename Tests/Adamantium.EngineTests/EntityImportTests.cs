using System.Linq;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.Templates;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class EntityImportTests
{
    [Test]
    public void NodeTransforms_AreApplied()
    {
        var scene = NewScene();
        var node = scene.CreateMesh(scene.Models, "node", "Node");
        node.Position = new Vector3F(0, 0, 100);
        node.Meshes.Add(Box(1));

        var model = Import(scene, Vector3.Zero);

        Assert.That(Find(model, node).Transform.Position, Is.EqualTo(new Vector3(0, 0, 100)));
    }

    [Test]
    public void Units_ScaleTheModelOnce()
    {
        var scene = NewScene();
        scene.Units.Value = 0.01f;
        var group = scene.CreateMesh(scene.Models, "group", "Group");
        var inner = scene.CreateMesh(group, "inner", "Inner");
        var part = scene.CreateMesh(inner, "part", "Part");
        part.Meshes.Add(Box(1));

        var model = Import(scene, Vector3.Zero);

        Assert.That(ScaleAlongTheChain(Find(model, part)), Is.EqualTo(0.01f).Within(1e-6f));
    }

    [Test]
    public void PartsOfANode_DoNotRepeatItsTransform()
    {
        var scene = NewScene();
        var node = scene.CreateMesh(scene.Models, "node", "Node");
        node.Position = new Vector3F(0, 0, 100);
        node.Meshes.Add(Box(1));
        node.Meshes.Add(Box(1));

        var model = Import(scene, Vector3.Zero);

        var parts = Find(model, node).Dependencies;
        Assert.That(parts.Count, Is.EqualTo(2));
        Assert.That(parts.All(p => p.Transform.Position == Vector3.Zero), Is.True);
    }

    [Test]
    public void AnEmptyNode_DoesNotStopTheImport()
    {
        var scene = NewScene();
        scene.CreateMesh(scene.Models, "part", "Part").Meshes.Add(Box(1));
        scene.CreateMesh(scene.Models, "empty", "Empty");

        Assert.That(Import(scene, Vector3.Zero), Is.Not.Null);
    }

    // As the file has it: the model's own origin lands where it is placed, and its position reads that; the geometry
    // stays where its author put it about the origin - here five units off, 500 centimeters - not moved to its center.
    [Test]
    public void TheModelsOrigin_LandsWhereItIsPlaced_AsTheFileHasIt()
    {
        var scene = NewScene();
        scene.Units.Value = 0.01f;
        scene.CreateMesh(scene.Models, "part", "Part").Meshes.Add(Box(1, 500));

        var model = Import(scene, new Vector3(0, 0, 5));

        Assert.Multiple(() =>
        {
            Assert.That(model.Transform.Position, Is.EqualTo(new Vector3(0, 0, 5)));
            Assert.That(((Vector3F)model.GetCenterAbsolute() - new Vector3F(0, 0, 5)).Length(), Is.EqualTo(5f).Within(1e-3f));
        });
    }

    [Test]
    public void ARootMovedInItsFile_IsPlacedFromThere_AndTurnsAboutItsOrigin()
    {
        var scene = NewScene();
        scene.Models.Position = new Vector3F(1, 2, 3);
        scene.CreateMesh(scene.Models, "part", "Part").Meshes.Add(Box(1, 500));

        var model = Import(scene, new Vector3(0, 0, 5));
        model.Transform.Rotation = QuaternionF.RotationAxis(Vector3F.UnitY, MathHelper.DegreesToRadians(70));

        Assert.Multiple(() =>
        {
            Assert.That(model.Transform.Position, Is.EqualTo(new Vector3(1, 2, 8)));
            Assert.That((model.Transform.GetWorldMatrixF().TranslationVector - new Vector3F(1, 2, 8)).Length(),
                Is.LessThan(1e-4f), "the origin holds still as the model turns");
        });
    }

    [Test]
    public void EveryPart_TurnsAboutItsOwnCenter()
    {
        var scene = NewScene();
        var part = scene.CreateMesh(scene.Models, "part", "Part");
        part.Meshes.Add(Box(1, 500));
        scene.CreateMesh(scene.Models, "other", "Other").Meshes.Add(Box(1));
        var model = Import(scene, Vector3.Zero);
        var entity = Find(model, part);
        var before = (Vector3F)entity.GetCenterAbsolute();

        entity.Transform.Rotation = QuaternionF.RotationAxis(Vector3F.UnitX, MathHelper.DegreesToRadians(40));

        Assert.That(((Vector3F)entity.GetCenterAbsolute() - before).Length(), Is.LessThan(1e-3f));
    }

    [Test]
    public void NodePlacement_IsKept()
    {
        var scene = NewScene();
        var turned = scene.CreateMesh(scene.Models, "turned", "Turned");
        turned.Position = new Vector3F(10, 0, 0);
        turned.Rotation = QuaternionF.RotationAxis(Vector3F.UnitY, MathHelper.DegreesToRadians(90));
        turned.Meshes.Add(Box(1, 0, 5));
        var still = scene.CreateMesh(scene.Models, "still", "Still");
        still.Meshes.Add(Box(1));

        var model = Import(scene, Vector3.Zero);

        var expected = Vector3F.TransformCoordinate(new Vector3F(0, 0, 5),
            Matrix4x4F.RotationQuaternion(turned.Rotation) * Matrix4x4F.Translation(turned.Position));
        var apart = (Vector3F)(Find(model, turned).GetCenterAbsolute() - Find(model, still).GetCenterAbsolute());
        Assert.That((apart - expected).Length(), Is.LessThan(1e-3f));
    }

    [Test]
    public void SpecularColor_IsCarried()
    {
        var scene = NewScene();
        var specular = new Vector4F(0.25f, 0.5f, 0.75f, 1);
        scene.Materials.Add("shiny", new SceneData.Material { ID = "shiny", SpecularColor = specular });
        var box = Box(1);
        box.MaterialID = "shiny";
        var part = scene.CreateMesh(scene.Models, "part", "Part");
        part.Meshes.Add(box);

        var model = Import(scene, Vector3.Zero);

        Assert.That(Find(model, part).GetComponent<Material>().SpecularColor, Is.EqualTo(specular));
    }

    [Test]
    public void ADiffuseGivenAsATexture_LeavesTheMaterialItsOpaqueGrey()
    {
        var scene = NewScene();
        scene.Images.Add("paint", new SceneData.Image("paint", "no such paint.png"));
        scene.Materials.Add("painted", new SceneData.Material { ID = "painted", DiffuseMap = "paint" });
        var box = Box(1);
        box.MaterialID = "painted";
        var part = scene.CreateMesh(scene.Models, "part", "Part");
        part.Meshes.Add(box);

        var model = Import(scene, Vector3.Zero);

        Assert.That(Find(model, part).GetComponent<Material>().DiffuseColor, Is.EqualTo(Material.DefaultDiffuseColor));
    }

    [Test]
    public void ADiffuseColor_IsCarried()
    {
        var scene = NewScene();
        var diffuse = new Vector4F(0.5f, 0.25f, 0, 1);
        scene.Materials.Add("red", new SceneData.Material { ID = "red", DiffuseColor = diffuse });
        var box = Box(1);
        box.MaterialID = "red";
        var part = scene.CreateMesh(scene.Models, "part", "Part");
        part.Meshes.Add(box);

        var model = Import(scene, Vector3.Zero);

        Assert.That(Find(model, part).GetComponent<Material>().DiffuseColor, Is.EqualTo(diffuse));
    }

    private static SceneData NewScene()
    {
        var scene = new SceneData();
        scene.Models = scene.CreateMesh(null, "", "Model");
        return scene;
    }

    private static Entity Import(SceneData scene, Vector3 at)
    {
        return new EntityImportTemplate(scene, null, at).BuildEntity(new Entity()).Result;
    }

    private static Mesh Box(double half, double offsetY = 0, double offsetZ = 0)
    {
        return new Mesh(PrimitiveType.TriangleList).SetPoints(
        [
            new Vector3(-half, offsetY - half, offsetZ - half), new Vector3(half, offsetY + half, offsetZ + half)
        ]);
    }

    private static Entity Find(Entity root, SceneData.Model node)
    {
        Entity found = null;
        root.TraverseInDepth(current =>
        {
            if (current.Name == node.ToString())
            {
                found = current;
            }
        });
        return found;
    }

    private static float ScaleAlongTheChain(Entity entity)
    {
        var scale = 1f;
        for (var at = entity; at != null; at = at.Owner)
        {
            scale *= at.Transform.Scale.X;
        }

        return scale;
    }
}

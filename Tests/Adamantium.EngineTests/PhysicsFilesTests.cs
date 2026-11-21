using System;
using System.IO;
using Adamantium.Core;
using Adamantium.DarkMatter;
using Adamantium.ECS.Physics;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>The physics' own files written and read back as they were, and a write that fails leaving the file there was.
/// </summary>
[TestFixture]
public class PhysicsFilesTests
{
    private DirectoryInfo _folder;

    [SetUp]
    public void MakeFolder() => _folder = Directory.CreateTempSubdirectory("adamantium-physics-files-");

    [TearDown]
    public void RemoveFolder() => _folder.Delete(true);

    [Test]
    public void AMaterialWritten_IsReadAsItWas()
    {
        var path = Path.Combine(_folder.FullName, "Materials", "Ice" + PhysicsMaterialFile.Extension);
        var ice = new PhysicsMaterial
        {
            Friction = 0.05f, Restitution = 0.1f, FrictionCombine = CombineRule.Minimum, RestitutionCombine = CombineRule.Maximum
        };

        PhysicsMaterialFile.From(ice).Write(path);

        Assert.That(PhysicsMaterialFile.Read(path).ToMaterial(), Is.EqualTo(ice));
    }

    [Test]
    public void AProjectsPhysicsWritten_IsReadAsItWas()
    {
        var path = Path.Combine(_folder.FullName, "Game" + ProjectPhysics.Extension);
        var physics = new ProjectPhysics
        {
            Gravity = new Vector3F(0, 3.71f, 0),
            StepRate = 240,
            Layers = [new ProjectLayer { Name = "Debris" }, new ProjectLayer { Name = "Ghosts" }],
            Apart = [new LayerPair { First = "Ghosts", Second = "Default" }]
        };

        physics.Write(path);
        var back = ProjectPhysics.Read(path);

        Assert.Multiple(() =>
        {
            Assert.That(back.Gravity, Is.EqualTo(physics.Gravity));
            Assert.That(back.StepRate, Is.EqualTo(240));
            Assert.That(back.Layers.ConvertAll(layer => layer.Name), Is.EqualTo((string[])["Debris", "Ghosts"]));
            Assert.That(back.Apart[0].First + "/" + back.Apart[0].Second, Is.EqualTo("Ghosts/Default"));
        });
    }

    [Test]
    public void AWriteThatFails_LeavesTheFileThereWas_AndNoTraceBeside()
    {
        var path = Path.Combine(_folder.FullName, "kept.txt");
        File.WriteAllText(path, "before");

        Assert.Throws<InvalidOperationException>(() => AtomicFile.Write(path, stream =>
        {
            stream.WriteByte(1);
            throw new InvalidOperationException("failed halfway");
        }));

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(path), Is.EqualTo("before"));
            Assert.That(Directory.GetFiles(_folder.FullName), Is.EqualTo((string[])[path]));
        });
    }
}

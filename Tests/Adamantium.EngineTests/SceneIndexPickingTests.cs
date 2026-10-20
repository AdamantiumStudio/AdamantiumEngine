using System;
using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.EntityServices;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Picking through the scene's index finds what a walk over every entity finds, with the camera away from the
/// world's origin, and follows what moves and what goes.</summary>
public class SceneIndexPickingTests
{
    private TestUniverse universe;
    private TestOutput output;
    private Camera camera;
    private TransformService transforms;
    private readonly List<Entity> cubes = [];

    [SetUp]
    public void BuildScene()
    {
        universe = new TestUniverse();
        universe.Satellites.Add<IUniverse>(universe);
        output = new TestOutput();
        universe.Add(output);
        var cameraEntity = new CameraTemplate().BuildEntity(null, "Camera", new Vector3(40, 3, -30), Vector3.ForwardLH,
            -Vector3.Up, 800, 600, 0.1f, 1000f);
        camera = cameraEntity.GetComponent<Camera>();
        output.Camera = camera;
        universe.EntityWorld.EntityManager.AddEntity(cameraEntity);

        var random = new Random(3);
        cubes.Clear();
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 6; y++)
            {
                var size = 0.5 + random.NextDouble() * 2;
                var cube = Cube($"Cube {x},{y}", new Vector3(30 + x * 3, y * 2.5 - 6, -10 + random.NextDouble() * 20), size);
                cube.Visible = random.Next(8) != 0;
                cube.IsEnabled = random.Next(8) != 0;
                universe.EntityWorld.EntityManager.AddEntity(cube);
                cubes.Add(cube);
            }
        }

        // One under another, so the index places a child by its parent's world.
        var parent = Cube("Parent", new Vector3(36, 0, -5), 1);
        var child = Cube("Child", new Vector3(0, 0, 4), 1.5, parent);
        universe.EntityWorld.EntityManager.AddEntity(parent);
        cubes.Add(parent);
        cubes.Add(child);

        transforms = new TransformService(universe.EntityWorld);
        transforms.Initialize();
        Frame();
    }

    [Test]
    public void TheIndex_FindsWhatTheWalkFinds()
    {
        var index = universe.EntityWorld.Satellites.Get<SceneIndex>();
        var hits = 0;
        for (int x = 0; x < 800; x += 37)
        {
            for (int y = 0; y < 600; y += 41)
            {
                var ray = PickRay.FromCamera(camera, new Vector2F(x, y));
                foreach (var withDisabled in new[] { false, true })
                {
                    var walked = Picking.Pick(universe.EntityWorld.RootEntities, ray, PickMode.Triangles, withDisabled: withDisabled);
                    var indexed = Picking.PickMeshes(index, ray, withDisabled);

                    Assert.That(indexed.Entity, Is.SameAs(walked.Entity), $"pixel {x},{y}, disabled too: {withDisabled}");
                    Assert.That(indexed.Depth, Is.EqualTo(walked.Depth).Within(1e-3f));
                    hits += walked.IsHit ? 1 : 0;
                }
            }
        }

        Assert.That(hits, Is.GreaterThan(50), "too few rays met a cube to tell the two apart");
    }

    [Test]
    public void TheIndex_FollowsAMove_AndForgetsWhatWent()
    {
        var index = universe.EntityWorld.Satellites.Get<SceneIndex>();
        var cube = cubes.Find(c => c.Visible && c.IsEnabled && c.Owner == null);
        var target = new Vector3(40, 3, 10);
        var ray = PickRay.FromCamera(camera, PixelOf(target));
        var before = Picking.PickMeshes(index, ray);

        cube.Transform.Position = target;
        Frame();
        var moved = Picking.PickMeshes(index, ray);

        universe.EntityWorld.EntityManager.RemoveEntity(cube);
        Frame();
        var gone = Picking.PickMeshes(index, ray);

        Assert.Multiple(() =>
        {
            Assert.That(before.Entity, Is.Not.SameAs(cube));
            Assert.That(moved.Entity, Is.SameAs(cube));
            Assert.That(gone.Entity, Is.Not.SameAs(cube));
        });
    }

    private static Entity Cube(string name, Vector3 at, double size, Entity owner = null)
    {
        var cube = new Entity(owner, name);
        cube.Transform.Position = at;
        cube.AddComponent(new MeshData { Mesh = Shapes.Cube.GenerateGeometry(GeometryType.Solid, size, size, size) });
        return cube;
    }

    private void Frame()
    {
        universe.EntityWorld.ForceUpdate();
        universe.Settle();
        output.Frame();
        camera.Update(new AppTime { FrameTime = 1.0 / 60 });
        transforms.Update(new AppTime());
    }

    private Vector2F PixelOf(Vector3 world)
    {
        var inRender = (Vector3F)(world - camera.WorldPosition);
        var pixel = Vector3F.Project(inRender, 0, 0, camera.Width, camera.Height, 0, 1, camera.ViewMatrix * camera.ProjectionMatrix);
        return new Vector2F(pixel.X, pixel.Y);
    }
}

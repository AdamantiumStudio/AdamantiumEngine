using System;
using System.IO;
using System.Linq;
using Adamantium.Engine.Compiler.Models;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests
{
    /// <summary>
    /// Wavefront .obj import. The checks mirror <see cref="ColladaImportTests"/> and for the same reason: .obj
    /// carries per-face vertex normals (vn), so losing them means losing the hard edges they describe.
    /// </summary>
    [TestFixture]
    public class ObjImportTests
    {
        private string tempDirectory;

        [SetUp]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "adamantium-obj-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
        }

        [Test]
        public void Import_KeepsNormalsFromTheFile()
        {
            // The normal is deliberately across its face: a triangle in the XZ plane is given a normal along Z.
            // Re-deriving it from the geometry would produce one ALONG the face, agreeing with it - that is the tell.
            var scene = Import(@"
v 0 0 0
v 1 0 0
v 0 0 1
vn 0 0 1
f 1//1 2//1 3//1
");

            var mesh = SingleMesh(scene);
            Assert.That(mesh.Normals.Length, Is.EqualTo(mesh.Points.Length), "every vertex must have a normal");

            var a = mesh.Points[mesh.Indices[0]].As<float>();
            var b = mesh.Points[mesh.Indices[1]].As<float>();
            var c = mesh.Points[mesh.Indices[2]].As<float>();
            var face = Vector3F.Cross(b - a, c - a);
            face.Normalize();

            foreach (var index in mesh.Indices)
            {
                var normal = mesh.Normals[index];
                normal.Normalize();
                Assert.That(Math.Abs(Vector3F.Dot(face, normal)), Is.LessThan(0.1f),
                    "the normal agrees with its face - it was recomputed even though the file supplied it");
            }
        }

        [Test]
        public void Import_KeepsHardEdges()
        {
            // Two triangles sharing an edge, each with its own normal. Weld them and the crease is gone.
            var scene = Import(@"
v 0 0 0
v 1 0 0
v 1 1 0
v 0 0 1
vn 0 0 1
vn 0 -1 0
f 1//1 2//1 3//1
f 1//2 2//2 4//2
");

            var mesh = SingleMesh(scene);

            var distinct = mesh.Normals.Select(n => { n.Normalize(); return n; })
                .Select(n => $"{n.X:N2} {n.Y:N2} {n.Z:N2}").Distinct().Count();
            Assert.That(distinct, Is.EqualTo(2), "two faces with their own normals must stay two distinct normals");
        }

        [Test]
        public void Import_ReadsTextureCoordinates()
        {
            var scene = Import(@"
v 0 0 0
v 1 0 0
v 0 1 0
vt 0 0
vt 1 0
vt 0 1
vn 0 0 1
f 1/1/1 2/2/1 3/3/1
");

            var mesh = SingleMesh(scene);
            Assert.That(mesh.UV0.Length, Is.EqualTo(mesh.Points.Length));
        }

        [Test]
        public void Import_TriangulatesQuads()
        {
            var scene = Import(@"
v 0 0 0
v 1 0 0
v 1 1 0
v 0 1 0
vn 0 0 1
f 1//1 2//1 3//1 4//1
");

            var mesh = SingleMesh(scene);
            Assert.That(mesh.Indices.Length, Is.EqualTo(6), "a quad is two triangles");
        }

        private static Mesh SingleMesh(SceneData scene)
        {
            Assert.That(scene, Is.Not.Null, "the import returned nothing");

            var meshes = Models(scene).SelectMany(m => m.Meshes).ToArray();
            Assert.That(meshes.Length, Is.EqualTo(1), "exactly one mesh was expected");
            return meshes[0];
        }

        private static System.Collections.Generic.IEnumerable<SceneData.Model> Models(SceneData scene)
        {
            var stack = new System.Collections.Generic.Stack<SceneData.Model>();
            stack.Push(scene.Models);
            while (stack.Count > 0)
            {
                var model = stack.Pop();
                yield return model;
                foreach (var child in model.Dependencies) stack.Push(child);
            }
        }

        private SceneData Import(string body)
        {
            var path = Path.Combine(tempDirectory, "model.obj");
            File.WriteAllText(path, body.Trim() + Environment.NewLine);
            return new ModelConverter().ImportFile(path);
        }
    }
}

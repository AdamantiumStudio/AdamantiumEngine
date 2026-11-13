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

        // Faces that name positions alone - the plainest .obj there is - read three numbers a corner like any other.
        [Test]
        public void Import_ReadsFacesOfPositionsAlone()
        {
            var scene = Import(@"
v 0 0 0
v 1 0 0
v 1 1 0
v 0 1 0
f 1 2 3
f 1 3 4
");

            var mesh = SingleMesh(scene);
            Assert.Multiple(() =>
            {
                Assert.That(mesh.Indices.Length, Is.EqualTo(6), "two triangles");
                Assert.That(mesh.Points.Max(point => point.X) - mesh.Points.Min(point => point.X),
                    Is.EqualTo(1).Within(1e-6), "the square's width");
                Assert.That(mesh.Points.Max(point => point.Y) - mesh.Points.Min(point => point.Y),
                    Is.EqualTo(1).Within(1e-6), "the square's height");
            });
        }

        [Test]
        public void Import_ReadsFacesOfPositionsAndTextureCoordinates()
        {
            var scene = Import(@"
v 0 0 0
v 1 0 0
v 0 1 0
vt 0 0
vt 1 0
vt 0 1
f 1/1 2/2 3/3
");

            var mesh = SingleMesh(scene);
            Assert.Multiple(() =>
            {
                Assert.That(mesh.Indices.Length, Is.EqualTo(3), "one triangle");
                Assert.That(mesh.UV0.Length, Is.EqualTo(mesh.Points.Length));
            });
        }

        // A box of eight shared corners with no normals of its own - the owner's box.obj: in no smoothing group, every
        // face is flat, each of its corners with the normal of its face. Averaged over the shared corners, the normals
        // pointed along the box's diagonals and it was lit like a ball. This box is wound inside out, so its normals face
        // in, as its winding says; which way does not matter here, only that each is its face's.
        [Test]
        public void ABoxGivenNoNormals_HasFlatFaces()
        {
            var scene = Import(@"
v -1 -1 -3
v 1 -1 -3
v 1 1 -3
v -1 1 -3
v -1 -1 3
v 1 -1 3
v 1 1 3
v -1 1 3
f 1 2 3 4
f 5 8 7 6
f 1 5 6 2
f 2 6 7 3
f 3 7 8 4
f 5 1 4 8
");

            var mesh = SingleMesh(scene);
            var center = new Vector3<double>(
                (mesh.Points.Max(point => point.X) + mesh.Points.Min(point => point.X)) / 2,
                (mesh.Points.Max(point => point.Y) + mesh.Points.Min(point => point.Y)) / 2,
                (mesh.Points.Max(point => point.Z) + mesh.Points.Min(point => point.Z)) / 2);
            var halfSize = new Vector3<double>(
                (mesh.Points.Max(point => point.X) - mesh.Points.Min(point => point.X)) / 2,
                (mesh.Points.Max(point => point.Y) - mesh.Points.Min(point => point.Y)) / 2,
                (mesh.Points.Max(point => point.Z) - mesh.Points.Min(point => point.Z)) / 2);
            Assert.That(mesh.Normals, Has.Length.EqualTo(mesh.Points.Length));
            Assert.That(mesh.Points, Has.Length.EqualTo(24), "four corners a face, six faces");
            for (int i = 0; i < mesh.Points.Length; i++)
            {
                var normal = mesh.Normals[i].As<double>();
                var outward = mesh.Points[i] - center;
                var along = Math.Max(Math.Abs(normal.X), Math.Max(Math.Abs(normal.Y), Math.Abs(normal.Z)));
                Assert.That(along, Is.EqualTo(1).Within(1e-4), $"vertex {i}: {normal} lies along an axis");
                // On the face the normal is across: there the corner sits on the box's side along that axis.
                var side = Math.Abs(normal.X) > 0.5 ? halfSize.X : Math.Abs(normal.Y) > 0.5 ? halfSize.Y : halfSize.Z;
                Assert.That(Math.Abs(Vector3<double>.Dot(normal, outward)), Is.EqualTo(side).Within(1e-4),
                    $"vertex {i} at {mesh.Points[i]}: {normal} is across its own face");
            }
        }

        private const string BoxCorners = @"
v -1 -1 -3
v 1 -1 -3
v 1 1 -3
v -1 1 -3
v -1 -1 3
v 1 -1 3
v 1 1 3
v -1 1 3
";

        private static readonly string[] BoxFaces =
            ["f 1 2 3 4", "f 5 8 7 6", "f 1 5 6 2", "f 2 6 7 3", "f 3 7 8 4", "f 5 1 4 8"];

        // In one smoothing group the box's faces share its eight corners, each corner's normal between its three faces -
        // the same way round as the faces' own, an equal share of each: every face meets a corner at a right angle,
        // however long its sides.
        [Test]
        public void ABoxInOneSmoothingGroup_IsSmoothAtItsCorners()
        {
            var flat = SingleMesh(Import(BoxCorners + string.Join(Environment.NewLine, BoxFaces)));
            var smooth = SingleMesh(Import(BoxCorners + "s 1" + Environment.NewLine + string.Join(Environment.NewLine, BoxFaces)));

            Assert.That(smooth.Points, Has.Length.EqualTo(8), "one vertex a corner");
            Assert.That(smooth.Normals, Has.Length.EqualTo(8));
            for (int i = 0; i < smooth.Points.Length; i++)
            {
                var facesAround = Vector3F.Zero;
                for (int j = 0; j < flat.Points.Length; j++)
                {
                    if ((flat.Points[j] - smooth.Points[i]).Length() < 1e-6) facesAround += flat.Normals[j];
                }

                facesAround.Normalize();
                var normal = smooth.Normals[i];
                Assert.Multiple(() =>
                {
                    Assert.That(Vector3F.Dot(normal, facesAround), Is.EqualTo(1).Within(1e-4),
                        $"corner {smooth.Points[i]}: {normal}, the faces around it {facesAround}");
                    Assert.That(Math.Abs(normal.X), Is.EqualTo(1 / Math.Sqrt(3)).Within(1e-4));
                    Assert.That(Math.Abs(normal.Z), Is.EqualTo(1 / Math.Sqrt(3)).Within(1e-4));
                });
            }
        }

        [TestCase("s off")]
        [TestCase("s 0")]
        public void ABoxWithSmoothingOff_StaysFlat(string smoothing)
        {
            var mesh = SingleMesh(Import(BoxCorners + smoothing + Environment.NewLine + string.Join(Environment.NewLine, BoxFaces)));

            AssertFlatBox(mesh);
        }

        // Where two groups meet the edge stays hard: a face in a group of its own is flat, as one in none.
        [Test]
        public void EachFaceInAGroupOfItsOwn_KeepsTheEdges()
        {
            var lines = BoxFaces.Select((face, index) => $"s {index + 1}{Environment.NewLine}{face}");
            var mesh = SingleMesh(Import(BoxCorners + string.Join(Environment.NewLine, lines)));

            AssertFlatBox(mesh);
        }

        // A smoothing group spans materials: the corners two faces share are smooth across the seam between the meshes
        // the materials split them into.
        [Test]
        public void ASmoothingGroup_IsSmoothAcrossMaterials()
        {
            var scene = Import(@"
v 0 0 0
v 1 0 0
v 0 1 0
v 0 0 1
s 1
usemtl first
f 1 2 3
usemtl second
f 1 4 2
");

            var meshes = Models(scene).SelectMany(model => model.Meshes).ToArray();
            Assert.That(meshes, Has.Length.EqualTo(2), "a mesh a material");
            var shared = 0;
            for (int i = 0; i < meshes[0].Points.Length; i++)
            {
                var point = meshes[0].Points[i];
                var other = Array.FindIndex(meshes[1].Points, candidate => (candidate - point).Length() < 1e-6);
                if (other < 0) continue;

                shared++;
                var normal = meshes[0].Normals[i];
                var otherNormal = meshes[1].Normals[other];
                Assert.That(Vector3F.Dot(normal, otherNormal), Is.EqualTo(1).Within(1e-5),
                    $"at {point}: {normal} and {otherNormal}");
            }

            Assert.That(shared, Is.EqualTo(2), "the faces share an edge");
        }

        // Normals the file gives are the file's, smoothing group or not.
        [Test]
        public void NormalsFromTheFile_StandInASmoothingGroup()
        {
            var mesh = SingleMesh(Import(@"
v 0 0 0
v 1 0 0
v 0 0 1
vn 0 0 1
s 1
f 1//1 2//1 3//1
"));

            var face = Vector3F.Cross(mesh.Points[mesh.Indices[1]].As<float>() - mesh.Points[mesh.Indices[0]].As<float>(),
                mesh.Points[mesh.Indices[2]].As<float>() - mesh.Points[mesh.Indices[0]].As<float>());
            face.Normalize();
            Assert.That(mesh.Normals.Select(normal => Math.Abs(Vector3F.Dot(face, normal))), Is.All.LessThan(0.1f));
        }

        private static void AssertFlatBox(Mesh mesh)
        {
            Assert.That(mesh.Points, Has.Length.EqualTo(24), "four corners a face, six faces");
            Assert.That(mesh.Normals, Has.Length.EqualTo(mesh.Points.Length));
            foreach (var normal in mesh.Normals)
            {
                var along = Math.Max(Math.Abs(normal.X), Math.Max(Math.Abs(normal.Y), Math.Abs(normal.Z)));
                Assert.That(along, Is.EqualTo(1).Within(1e-4), $"{normal} lies along an axis");
            }
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

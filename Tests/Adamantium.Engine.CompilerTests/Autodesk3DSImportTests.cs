using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Engine.Compiler.Models;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests
{
    /// <summary>
    /// Autodesk .3ds import over files built right here, because the format is small enough to write by hand and
    /// nothing else proves the smoothing groups actually run: 3DS stores no vertex normals at all, so a group mask
    /// per face is the only thing that tells a crease from a smooth surface.
    /// </summary>
    [TestFixture]
    public class Autodesk3DSImportTests
    {
        private string tempDirectory;

        [SetUp]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "adamantium-3ds-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
        }

        // A folded pair of triangles sharing the edge 0-1: one in the XY plane, one bent up along Z.
        private static readonly float[] Positions = { 0, 0, 0, 1, 0, 0, 1, 1, 0, 1, 0, 1 };
        private static readonly ushort[] Faces = { 0, 1, 2, 0, 3, 1 };

        [Test]
        public void Import_SharedGroupBlendsTheNormals()
        {
            // Both faces in group 1: the two shared vertices must blend, so their normal follows neither face
            // exactly. The other two belong to one face each and stay flat, which is why this looks at the
            // smallest agreement rather than all of them.
            var mesh = SingleMesh(Import(Build(new uint[] { 1, 1 })));

            Assert.That(Agreement(mesh).Min(), Is.LessThan(0.99f),
                "with one smoothing group the shared vertices must be blended, not left flat");
        }

        [Test]
        public void Import_SeparateGroupsKeepTheCrease()
        {
            // Groups 1 and 2 share no bit, so nothing blends and every normal stays its own face's.
            var mesh = SingleMesh(Import(Build(new uint[] { 1, 2 })));

            foreach (var dot in Agreement(mesh))
            {
                Assert.That(dot, Is.GreaterThan(0.99f), "faces in different groups must keep their own normals");
            }
        }

        [Test]
        public void Import_NoGroupsMeansFlat()
        {
            // No smoothing chunk at all: the format calls that flat, and flat is what it must come out as.
            var mesh = SingleMesh(Import(Build(null)));

            foreach (var dot in Agreement(mesh))
            {
                Assert.That(dot, Is.GreaterThan(0.99f), "without smoothing groups every face stays flat");
            }
        }

        [Test]
        public void Import_ReadsTextureCoordinates()
        {
            var mesh = SingleMesh(Import(Build(new uint[] { 1, 1 }, withUVs: true)));

            Assert.That(mesh.UV0.Length, Is.EqualTo(mesh.Points.Length),
                "the coordinates were read into a local and never kept, so every model arrived untextured");
        }

        [Test]
        public void Import_KeepsTheSceneName()
        {
            // The parser builds its own SceneData, and taking it wholesale used to drop the name
            var scene = Import(Build(null));

            Assert.That(scene.Name, Is.EqualTo("model.3ds"));
        }

        //How closely each vertex normal follows the triangle it belongs to
        private static IEnumerable<float> Agreement(Mesh mesh)
        {
            Assert.That(mesh.Normals.Length, Is.EqualTo(mesh.Points.Length), "every vertex must have a normal");

            for (var t = 0; t + 2 < mesh.Indices.Length; t += 3)
            {
                var a = mesh.Points[mesh.Indices[t]].As<float>();
                var b = mesh.Points[mesh.Indices[t + 1]].As<float>();
                var c = mesh.Points[mesh.Indices[t + 2]].As<float>();
                var face = Vector3F.Cross(b - a, c - a);
                if (face.LengthSquared() < 1e-10f) continue;
                face.Normalize();

                for (var k = 0; k < 3; k++)
                {
                    var normal = mesh.Normals[mesh.Indices[t + k]];
                    normal.Normalize();
                    yield return Math.Abs(Vector3F.Dot(face, normal));
                }
            }
        }

        private static Mesh SingleMesh(SceneData scene)
        {
            Assert.That(scene, Is.Not.Null, "the import returned nothing");

            var meshes = new List<Mesh>();
            var stack = new Stack<SceneData.Model>();
            stack.Push(scene.Models);
            while (stack.Count > 0)
            {
                var model = stack.Pop();
                meshes.AddRange(model.Meshes);
                foreach (var child in model.Dependencies) stack.Push(child);
            }

            Assert.That(meshes.Count, Is.EqualTo(1), "exactly one mesh was expected");
            return meshes[0];
        }

        private SceneData Import(byte[] content)
        {
            var path = Path.Combine(tempDirectory, "model.3ds");
            File.WriteAllBytes(path, content);
            return new ModelConverter().ImportFile(path);
        }

        #region Building the file

        private static byte[] Build(uint[] smoothingGroups, bool withUVs = false)
        {
            var faces = new List<byte>();
            faces.AddRange(BitConverter.GetBytes((ushort)(Faces.Length / 3)));
            for (var i = 0; i < Faces.Length; i += 3)
            {
                faces.AddRange(BitConverter.GetBytes(Faces[i]));
                faces.AddRange(BitConverter.GetBytes(Faces[i + 1]));
                faces.AddRange(BitConverter.GetBytes(Faces[i + 2]));
                faces.AddRange(BitConverter.GetBytes((ushort)0));
            }

            if (smoothingGroups != null)
            {
                var groups = new List<byte>();
                foreach (var group in smoothingGroups) groups.AddRange(BitConverter.GetBytes(group));
                faces.AddRange(Chunk(0x4150, groups));
            }

            var vertices = new List<byte>();
            vertices.AddRange(BitConverter.GetBytes((ushort)(Positions.Length / 3)));
            foreach (var value in Positions) vertices.AddRange(BitConverter.GetBytes(value));

            var trimesh = new List<byte>();
            trimesh.AddRange(Chunk(0x4110, vertices));
            trimesh.AddRange(Chunk(0x4120, faces));

            if (withUVs)
            {
                var uvs = new List<byte>();
                uvs.AddRange(BitConverter.GetBytes((ushort)(Positions.Length / 3)));
                for (var i = 0; i < Positions.Length / 3; i++)
                {
                    uvs.AddRange(BitConverter.GetBytes(i * 0.25f));
                    uvs.AddRange(BitConverter.GetBytes(i * 0.5f));
                }
                trimesh.AddRange(Chunk(0x4140, uvs));
            }

            var obj = new List<byte>();
            obj.AddRange(System.Text.Encoding.ASCII.GetBytes("bend"));
            obj.Add(0);
            obj.AddRange(Chunk(0x4100, trimesh));

            var editor = Chunk(0x3D3D, Chunk(0x4000, obj));
            return Chunk(0x4D4D, editor).ToArray();
        }

        //A 3DS chunk is a two-byte id, a four-byte length counting the header, and the body
        private static List<byte> Chunk(ushort id, IEnumerable<byte> body)
        {
            var content = body.ToList();
            var chunk = new List<byte>();
            chunk.AddRange(BitConverter.GetBytes(id));
            chunk.AddRange(BitConverter.GetBytes(content.Count + 6));
            chunk.AddRange(content);
            return chunk;
        }

        #endregion
    }
}

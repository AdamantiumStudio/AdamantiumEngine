using System;
using System.Linq;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests
{
    /// <summary>
    /// The baked model format stores its arrays narrower where that provably costs nothing. Everything here is an
    /// EXACT comparison on purpose: a format that quietly rounds a model is the worst kind of defect, because the
    /// artifacts show up on screen and nothing points back at the file.
    /// </summary>
    [TestFixture]
    public class MeshFormatTests
    {
        [Test]
        public void Positions_SurviveExactlyWhenTheyFitInSingle()
        {
            // Values a float holds exactly - which is what a model file's coordinates are
            var points = new[]
            {
                new Vector3<double>(0.5, -0.25, 1024),
                new Vector3<double>(0.0625, 3.375, -0.125),
                new Vector3<double>(-16384, 0.75, 2.5)
            };

            var loaded = RoundTrip(Mesh(points));

            for (var i = 0; i < points.Length; i++)
            {
                Assert.That(loaded.Points[i], Is.EqualTo(points[i]), $"point {i} came back changed");
            }
        }

        [Test]
        public void Positions_KeepTheirDoubleWhenTheyDoNotFit()
        {
            // 0.1 has no exact float, so the writer has to keep all eight bytes rather than round it
            var points = new[]
            {
                new Vector3<double>(0.1, 0.2, 0.3),
                new Vector3<double>(1.0 / 3.0, 2.0 / 7.0, 1e-17),
                new Vector3<double>(1, 2, 3)
            };

            var loaded = RoundTrip(Mesh(points));

            for (var i = 0; i < points.Length; i++)
            {
                Assert.That(loaded.Points[i], Is.EqualTo(points[i]), $"point {i} was rounded");
            }
        }

        [Test]
        public void Indices_SurviveBeyondWhatAUShortHolds()
        {
            // 70000 does not fit in a ushort, so the narrow path must not be taken
            var points = Enumerable.Range(0, 3).Select(i => new Vector3<double>(i, 0, 0)).ToArray();
            var mesh = Mesh(points);
            mesh.SetIndices([0, 70000, 65535]);

            Assert.That(RoundTrip(mesh).Indices, Is.EqualTo(new[] { 0, 70000, 65535 }));
        }

        [Test]
        public void Bitangents_ComeBackExactlyWhenTheyAreDerived()
        {
            // How CalculateTangentsAndBinormals makes them: cross(normal, tangent) * tangent.W
            var mesh = Mesh([new Vector3<double>(0, 0, 0), new Vector3<double>(1, 0, 0), new Vector3<double>(0, 1, 0)]);
            var normals = new[] { new Vector3F(0, 0, 1), new Vector3F(0, 0, 1), new Vector3F(0, 0, 1) };
            var tangents = new[] { new Vector4F(1, 0, 0, 1), new Vector4F(1, 0, 0, 1), new Vector4F(1, 0, 0, -1) };
            var bitangents = tangents
                .Select((t, i) => Vector3F.Cross(normals[i], (Vector3F)t) * t.W).ToArray();

            mesh.SetNormals(normals);
            mesh.SetTangentsAndBiTangents(tangents, bitangents);

            var loaded = RoundTrip(mesh);
            for (var i = 0; i < bitangents.Length; i++)
            {
                Assert.That(loaded.BiTangents[i], Is.EqualTo(bitangents[i]), $"bitangent {i} came back changed");
            }
        }

        [Test]
        public void Bitangents_AreKeptWhenTheyAreNotDerivable()
        {
            // A mesh whose bitangents came from somewhere else must not have them replaced by the rebuild
            var mesh = Mesh([new Vector3<double>(0, 0, 0), new Vector3<double>(1, 0, 0), new Vector3<double>(0, 1, 0)]);
            mesh.SetNormals([new Vector3F(0, 0, 1), new Vector3F(0, 0, 1), new Vector3F(0, 0, 1)]);
            var tangents = new[] { new Vector4F(1, 0, 0, 1), new Vector4F(1, 0, 0, 1), new Vector4F(1, 0, 0, 1) };
            var odd = new[] { new Vector3F(0.3f, 0.7f, 0.1f), new Vector3F(0, 1, 0), new Vector3F(0, 1, 0) };
            mesh.SetTangentsAndBiTangents(tangents, odd);

            var loaded = RoundTrip(mesh);
            Assert.That(loaded.BiTangents[0], Is.EqualTo(odd[0]), "a bitangent that is not the rebuild must be stored");
        }

        [Test]
        public void EverySemanticSurvivesTheRoundTrip()
        {
            var mesh = Mesh([new Vector3<double>(0, 0, 0), new Vector3<double>(1, 0, 0), new Vector3<double>(0, 1, 0)]);
            mesh.SetNormals([new Vector3F(0, 0, 1), new Vector3F(0, 1, 0), new Vector3F(1, 0, 0)]);
            mesh.SetUVs(0, [new Vector2F(0, 0), new Vector2F(0.5f, 0.25f), new Vector2F(1, 1)]);
            mesh.SetUVs(3, [new Vector2F(2, 3), new Vector2F(4, 5), new Vector2F(6, 7)]);
            mesh.SetColors([Colors.Red, Colors.Green, Colors.Blue]);
            mesh.SetJointIndices([new Vector4F(1, 2, 3, 4), Vector4F.Zero, Vector4F.Zero]);
            mesh.SetJointWeights([new Vector4F(0.5f, 0.25f, 0.125f, 0.125f), Vector4F.Zero, Vector4F.Zero]);

            var loaded = RoundTrip(mesh);

            Assert.That(loaded.Normals, Is.EqualTo(mesh.Normals));
            Assert.That(loaded.UV0, Is.EqualTo(mesh.UV0));
            Assert.That(loaded.UV3, Is.EqualTo(mesh.UV3));
            Assert.That(loaded.Colors, Is.EqualTo(mesh.Colors));
            Assert.That(loaded.JointIndices, Is.EqualTo(mesh.JointIndices));
            Assert.That(loaded.JointWeights, Is.EqualTo(mesh.JointWeights));
            Assert.That(loaded.UV1, Is.Empty, "a set the mesh never had must not appear out of nowhere");
        }

        [Test]
        public void ABakedMeshOfAnotherVersionIsRefusedByName()
        {
            // A mesh row written the way an older - or newer - engine would: the shape is fine, the version is not
            var buffer = new System.Buffers.ArrayBufferWriter<byte>();
            var writer = new MessagePack.MessagePackWriter(buffer);
            writer.WriteArrayHeader(17);
            writer.Write(99);
            for (var i = 1; i < 17; i++) writer.WriteNil();
            writer.Flush();

            var error = Assert.Throws<MessagePack.MessagePackSerializationException>(() =>
            {
                var reader = new MessagePack.MessagePackReader(buffer.WrittenMemory);
                new MeshFormatter().Deserialize(ref reader, MessagePack.MessagePackSerializerOptions.Standard);
            });

            Assert.That(FullText(error), Does.Contain("Re-bake"),
                "a stale artifact has to say what it is, not fail as garbage");
            Assert.That(FullText(error), Does.Contain("99"), "and say which version it turned out to be");
        }

        #region Plumbing

        private static Mesh Mesh(Vector3<double>[] points)
        {
            var mesh = new Mesh(PrimitiveType.TriangleList) { MaterialID = "mat" };
            mesh.SetPoints(points);
            mesh.SetIndices(Enumerable.Range(0, points.Length).ToArray());
            return mesh;
        }

        private static SceneData Scene(Mesh mesh)
        {
            var scene = new SceneData { Name = "test" };
            var model = scene.CreateMesh(scene.Models, "node", "node");
            model.Meshes.Add(mesh);
            return scene;
        }

        private static Mesh RoundTrip(Mesh mesh)
        {
            var loaded = SceneDataSerializer.Deserialize(SceneDataSerializer.Serialize(Scene(mesh)));
            return loaded.Models.Dependencies[0].Meshes[0];
        }

        private static string FullText(Exception error)
        {
            var text = error.Message;
            for (var inner = error.InnerException; inner != null; inner = inner.InnerException)
            {
                text += " " + inner.Message;
            }
            return text;
        }

        #endregion
    }
}

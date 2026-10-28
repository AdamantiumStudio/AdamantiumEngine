using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests
{
    [TestFixture]
    public class SceneDataSerializerTests
    {
        [Test]
        public void RoundTrip_PreservesHierarchyMeshAndSkeleton()
        {
            var scene = new SceneData { Name = "TestScene" };

            // model tree: root -> child (carrying a mesh)
            var child = scene.CreateMesh(scene.Models, "node1", "Body");
            child.Position = new Vector3F(1, 2, 3);
            child.Scale = new Vector3F(2, 2, 2);

            var mesh = new Mesh(PrimitiveType.TriangleList) { MaterialID = "mat1" };
            mesh.SetPoints([new Vector3<double>(0, 0, 0), new Vector3<double>(1, 0, 0), new Vector3<double>(0, 1, 0)]);
            mesh.SetIndices([0, 1, 2]);
            mesh.SetNormals([new Vector3F(0, 0, 1), new Vector3F(0, 0, 1), new Vector3F(0, 0, 1)]);
            mesh.SetUVs(0, [new Vector2F(0, 0), new Vector2F(1, 0), new Vector2F(0, 1)]);
            mesh.SetColors([Colors.White, Colors.White, Colors.White]);
            mesh.SetTangentsAndBiTangents(
                [new Vector4F(1, 0, 0, 1), new Vector4F(1, 0, 0, 1), new Vector4F(1, 0, 0, 1)],
                [new Vector3F(0, 1, 0), new Vector3F(0, 1, 0), new Vector3F(0, 1, 0)]);
            child.Meshes.Add(mesh);

            scene.Materials["mat1"] = new SceneData.Material { ID = "mat1", DiffuseColor = new Vector4F(1, 0, 0, 1) };

            // a skin: its joints are nodes, by ID, with their inverse bind matrices
            scene.Controllers["node1"] = new SceneData.Controller
            {
                ControllerId = "skin1",
                JointNames = ["node1"],
                JointMatrices = [Matrix4x4.CreateTranslation<float>(0, -1, 0)]
            };

            var bytes = SceneDataSerializer.Serialize(scene);
            var loaded = SceneDataSerializer.Deserialize(bytes);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.Name, Is.EqualTo("TestScene"));

            // hierarchy + Parent back-reference rebuilt
            Assert.That(loaded.Models.Dependencies.Count, Is.EqualTo(1));
            var loadedChild = loaded.Models.Dependencies[0];
            Assert.That(loadedChild.ID, Is.EqualTo("node1"));
            Assert.That(loadedChild.Name, Is.EqualTo("Body"));
            Assert.That(loadedChild.Parent, Is.SameAs(loaded.Models));
            Assert.That(loadedChild.Position, Is.EqualTo(new Vector3F(1, 2, 3)));

            // mesh round-trip through MeshFormatter / MeshGeometry
            Assert.That(loadedChild.Meshes.Count, Is.EqualTo(1));
            var loadedMesh = loadedChild.Meshes[0];
            Assert.That(loadedMesh.MaterialID, Is.EqualTo("mat1"));
            Assert.That(loadedMesh.MeshTopology, Is.EqualTo(PrimitiveType.TriangleList));
            Assert.That(loadedMesh.Points.Length, Is.EqualTo(3));
            Assert.That(loadedMesh.Points[1], Is.EqualTo(new Vector3<double>(1, 0, 0)));
            Assert.That(loadedMesh.Indices, Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(loadedMesh.Normals[0], Is.EqualTo(new Vector3F(0, 0, 1)));
            Assert.That(loadedMesh.UV0[2], Is.EqualTo(new Vector2F(0, 1)));
            Assert.That(loadedMesh.Tangents[0], Is.EqualTo(new Vector4F(1, 0, 0, 1)));
            Assert.That(loadedMesh.BiTangents[0], Is.EqualTo(new Vector3F(0, 1, 0)));
            Assert.That(loadedMesh.Semantic.HasFlag(VertexSemantic.Normal), Is.True);
            Assert.That(loadedMesh.Semantic.HasFlag(VertexSemantic.TangentBiNormal), Is.True);

            // material round-trip (Dictionary<,> subclass)
            Assert.That(loaded.Materials.ContainsKey("mat1"), Is.True);
            Assert.That(loaded.Materials["mat1"].DiffuseColor, Is.EqualTo(new Vector4F(1, 0, 0, 1)));

            // skin round-trip (Dictionary<,> subclass)
            var loadedSkin = loaded.Controllers["node1"];
            Assert.That(loadedSkin.ControllerId, Is.EqualTo("skin1"));
            Assert.That(loadedSkin.JointNames, Is.EqualTo(new[] { "node1" }));
            Assert.That(loadedSkin.JointMatrices[0], Is.EqualTo(Matrix4x4.CreateTranslation<float>(0, -1, 0)));
        }

        // A picture carried inside a model goes into the artifact with it; a material's PBR values with it too.
        [Test]
        public void RoundTrip_KeepsAnImageCarriedInside_AndAMaterialsPbrValues()
        {
            var scene = new SceneData { Name = "Carried" };
            scene.Images["image0"] = new SceneData.Image { ID = "image0", Data = [0x89, 0x50, 0x4E, 0x47], MimeType = "image/png" };
            scene.Materials["material0"] = new SceneData.Material
            {
                ID = "material0",
                DiffuseMap = "image0",
                MetallicFactor = 0.25f,
                RoughnessFactor = 0.5f,
                NormalMap = "image0",
                AlphaMode = AlphaMode.Mask,
                AlphaCutoff = 0.3f,
                DoubleSided = true
            };

            var loaded = SceneDataSerializer.Deserialize(SceneDataSerializer.Serialize(scene));

            var image = loaded.Images["image0"];
            var material = loaded.Materials["material0"];
            Assert.Multiple(() =>
            {
                Assert.That(image.Data, Is.EqualTo(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
                Assert.That(image.MimeType, Is.EqualTo("image/png"));
                Assert.That(material.MetallicFactor, Is.EqualTo(0.25f));
                Assert.That(material.RoughnessFactor, Is.EqualTo(0.5f));
                Assert.That(material.NormalMap, Is.EqualTo("image0"));
                Assert.That(material.AlphaMode, Is.EqualTo(AlphaMode.Mask));
                Assert.That(material.AlphaCutoff, Is.EqualTo(0.3f));
                Assert.That(material.DoubleSided, Is.True);
            });
        }
    }
}

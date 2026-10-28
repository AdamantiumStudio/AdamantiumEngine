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
    /// COLLADA import over small files written right here. Every assertion is on a quantity that survives a change
    /// of coordinate system (edge lengths, a normal's agreement with the triangle winding): otherwise the test would
    /// be catching which way Y points rather than how the file was parsed.
    /// </summary>
    [TestFixture]
    public class ColladaImportTests
    {
        private string tempDirectory;

        [SetUp]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "adamantium-dae-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
        }

        [Test]
        public void Import_KeepsHardEdgeNormalsFromTheFile()
        {
            // Two triangles at a right angle sharing an edge. The normals are per face: if the import averages them
            // (or welds the shared edge's vertices), a normal stops agreeing with its face.
            var geometry = Mesh(
                "bend",
                positions: "0 0 0  1 0 0  1 1 0  0 1 0   0 0 1  1 0 1",
                normals: "0 0 1  0 -1 0",
                polylist: "4 3",
                p: "0 0  1 0  2 0  3 0    0 1  1 1  5 1  4 1");

            var scene = Import(Document(geometry, Node("bend")));
            var mesh = SingleMesh(scene);

            Assert.That(mesh.Normals.Length, Is.EqualTo(mesh.Points.Length), "every vertex must have a normal");

            foreach (var (normal, face) in FaceSamples(mesh))
            {
                Assert.That(Vector3F.Dot(normal, face), Is.GreaterThan(0.99f),
                    "the normal drifted off its face - it was averaged rather than taken from the file");
            }
        }

        [Test]
        public void Import_DoesNotRecomputeNormalsTheFileAlreadyGave()
        {
            // The normal is deliberately across its face: a triangle in the XZ plane is given a normal along Z.
            // Re-deriving it from the geometry would produce one ALONG the face, agreeing with it - that is the tell.
            var geometry = Mesh(
                "tilted",
                positions: "0 0 0  1 0 0  0 0 1",
                normals: "0 0 1",
                polylist: "3",
                p: "0 0  1 0  2 0");

            var scene = Import(Document(geometry, Node("tilted")));
            var mesh = SingleMesh(scene);

            foreach (var (normal, face) in FaceSamples(mesh))
            {
                Assert.That(Math.Abs(Vector3F.Dot(normal, face)), Is.LessThan(0.1f),
                    "the normal agrees with its face - it was recomputed even though the file supplied it");
            }
        }

        [Test]
        public void Import_DerivesNormalsWhenTheFileGivesNone()
        {
            // No NORMAL input at all, so the import has to work them out - and the only right answer is the one the
            // winding gives. It matters WHEN that happens: ChangeCoordinateSystem both maps the normals and reverses
            // the winding, so deriving them on the wrong side of it flips them against their own faces.
            var geometry = MeshWithoutNormals("plain", "0 0 0  1 0 0  0 1 0  1 1 0", "4", "0 1 3 2");

            var scene = Import(Document(geometry, Node("plain")));
            var mesh = SingleMesh(scene);

            Assert.That(mesh.Normals.Length, Is.EqualTo(mesh.Points.Length), "every vertex must have a normal");

            // The quad runs counter-clockwise seen from +Z: that is the side the file means as its front.
            var front = Vector3F.Normalize(Vector3F.TransformNormal(Vector3F.UnitZ, AxisConversion.Between(UpAxis.Y_UP_RH, UpAxis.Y_DOWN_RH)));
            foreach (var (normal, face) in FaceSamples(mesh))
            {
                Assert.That(Vector3F.Dot(normal, face), Is.GreaterThan(0.99f),
                    "a derived normal has to agree with the face it was derived from");
                Assert.That(Vector3F.Dot(normal, front), Is.GreaterThan(0.99f), "the front the file meant, kept on import");
            }
        }

        [Test]
        public void Import_CountsEveryInputInTheStride()
        {
            // We do not read TEXTANGENT, but it takes a slot in <p>. Leave it out of the stride and every index after
            // it shifts - the triangle gets built from the wrong vertices. Edge lengths catch that: 3-4-5.
            var geometry = Mesh(
                "stride",
                positions: "0 0 0  3 0 0  0 4 0",
                normals: "0 0 1",
                polylist: "3",
                p: "0 0 0 0   1 0 0 0   2 0 0 0",
                extraInput: "<input semantic=\"TEXTANGENT\" source=\"#stride-normals\" offset=\"3\"/>",
                uvSource: true);

            var scene = Import(Document(geometry, Node("stride")));
            var mesh = SingleMesh(scene);

            var sides = EdgeLengths(mesh).OrderBy(x => x).ToArray();
            Assert.That(sides.Length, Is.EqualTo(3));
            Assert.That(sides[0], Is.EqualTo(3f).Within(0.001f));
            Assert.That(sides[1], Is.EqualTo(4f).Within(0.001f));
            Assert.That(sides[2], Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void Import_TriangulatesQuads()
        {
            var geometry = Mesh(
                "quad",
                positions: "0 0 0  1 0 0  1 1 0  0 1 0",
                normals: "0 0 1",
                polylist: "4",
                p: "0 0  1 0  2 0  3 0");

            var scene = Import(Document(geometry, Node("quad")));
            var mesh = SingleMesh(scene);

            Assert.That(mesh.Indices.Length, Is.EqualTo(6), "a quad is two triangles");
        }

        [Test]
        public void Import_ReportsPolygonWithHole()
        {
            // <ph> is a polygon with a hole. We cannot parse one; what matters is that this is said out loud rather
            // than the hole being filled in silence.
            var geometry = $@"
    <geometry id=""holed"" name=""holed"">
      <mesh>
        {Source("holed-positions", "0 0 0  4 0 0  4 4 0  0 4 0  1 1 0  2 1 0  2 2 0", 3, "X Y Z")}
        <vertices id=""holed-vertices""><input semantic=""POSITION"" source=""#holed-positions""/></vertices>
        <polygons count=""1"">
          <input semantic=""VERTEX"" source=""#holed-vertices"" offset=""0""/>
          <ph><p>0 1 2 3</p><h>4 5 6</h></ph>
        </polygons>
      </mesh>
    </geometry>";

            var converter = new ModelConverter();
            converter.ImportFile(Write(Document(geometry, Node("holed"))));

            Assert.That(converter.UnsupportedFeatures.Any(x => x.Contains("<ph>")), Is.True,
                "skipping a polygon with a hole must be reported");
        }

        [Test]
        public void Import_ReportsLibraryNodes()
        {
            var document = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<COLLADA xmlns=""http://www.collada.org/2005/11/COLLADASchema"" version=""1.4.1"">
  <asset><up_axis>Y_UP</up_axis></asset>
  <library_geometries>{Mesh("tri", "0 0 0  1 0 0  0 1 0", "0 0 1", "3", "0 0  1 0  2 0")}</library_geometries>
  <library_nodes><node id=""shared"" name=""shared""/></library_nodes>
  <library_visual_scenes><visual_scene id=""Scene"" name=""Scene"">{Node("tri")}</visual_scene></library_visual_scenes>
  <scene><instance_visual_scene url=""#Scene""/></scene>
</COLLADA>";

            var converter = new ModelConverter();
            converter.ImportFile(Write(document));

            Assert.That(converter.UnsupportedFeatures.Any(x => x.Contains("library_nodes")), Is.True);
        }

        [Test]
        public void Import_ReturnsNullForBrokenFile()
        {
            // A broken file used to open a MessageBox with a stack trace - on a build machine, a wait without end.
            var path = Path.Combine(tempDirectory, "broken.dae");
            File.WriteAllText(path, "<COLLADA> this is not a collada file at all");

            Assert.That(new ModelConverter().ImportFile(path), Is.Null);
        }

        [Test]
        public void Import_KeepsMeshOrderWithManyGeometries()
        {
            // Geometries are parsed in parallel but hung on the tree sequentially: the order must be the file's,
            // not whichever thread got there first.
            var names = Enumerable.Range(0, 32).Select(i => "geom" + i).ToArray();
            var geometries = String.Join(Environment.NewLine,
                names.Select(name => Mesh(name, "0 0 0  1 0 0  0 1 0", "0 0 1", "3", "0 0  1 0  2 0")));
            var nodes = String.Join(Environment.NewLine, names.Select(Node));

            var scene = Import(Document(geometries, nodes));

            var imported = Models(scene).Where(m => m.Meshes.Count > 0).Select(m => m.ID).ToArray();
            Assert.That(imported, Is.EqualTo(names));
        }

        #region Checks that do not depend on the coordinate system

        private static Mesh SingleMesh(SceneData scene)
        {
            var meshes = Models(scene).SelectMany(m => m.Meshes).ToArray();
            Assert.That(meshes.Length, Is.EqualTo(1), "exactly one mesh was expected");
            return meshes[0];
        }

        private static System.Collections.Generic.IEnumerable<SceneData.Model> Models(SceneData scene)
        {
            var stack = new System.Collections.Generic.Stack<SceneData.Model>();
            stack.Push(scene.Models);
            var ordered = new System.Collections.Generic.List<SceneData.Model>();
            while (stack.Count > 0)
            {
                var model = stack.Pop();
                ordered.Add(model);
                for (var i = model.Dependencies.Count - 1; i >= 0; i--) stack.Push(model.Dependencies[i]);
            }
            return ordered;
        }

        // The way each triangle's front faces, as the engine takes it: the side it is clockwise on screen from, which
        // cross(b - a, c - a) points away from.
        private static System.Collections.Generic.IEnumerable<(Vector3F Normal, Vector3F Face)> FaceSamples(Mesh mesh)
        {
            for (var t = 0; t + 2 < mesh.Indices.Length; t += 3)
            {
                var a = mesh.Points[mesh.Indices[t]].As<float>();
                var b = mesh.Points[mesh.Indices[t + 1]].As<float>();
                var c = mesh.Points[mesh.Indices[t + 2]].As<float>();
                var face = Vector3F.Cross(c - a, b - a);
                if (face.LengthSquared() < 1e-10f) continue;
                face.Normalize();

                for (var k = 0; k < 3; k++)
                {
                    var normal = mesh.Normals[mesh.Indices[t + k]];
                    if (normal.LengthSquared() < 1e-10f) continue;
                    normal.Normalize();
                    yield return (normal, face);
                }
            }
        }

        private static System.Collections.Generic.IEnumerable<float> EdgeLengths(Mesh mesh)
        {
            var a = mesh.Points[mesh.Indices[0]].As<float>();
            var b = mesh.Points[mesh.Indices[1]].As<float>();
            var c = mesh.Points[mesh.Indices[2]].As<float>();
            yield return (b - a).Length();
            yield return (c - b).Length();
            yield return (a - c).Length();
        }

        #endregion

        #region Building the file

        private SceneData Import(string document) => new ModelConverter().ImportFile(Write(document));

        private string Write(string document)
        {
            var path = Path.Combine(tempDirectory, "model.dae");
            File.WriteAllText(path, document);
            return path;
        }

        private static string Document(string geometries, string nodes) => $@"<?xml version=""1.0"" encoding=""utf-8""?>
<COLLADA xmlns=""http://www.collada.org/2005/11/COLLADASchema"" version=""1.4.1"">
  <asset><up_axis>Y_UP</up_axis></asset>
  <library_geometries>{geometries}</library_geometries>
  <library_visual_scenes><visual_scene id=""Scene"" name=""Scene"">{nodes}</visual_scene></library_visual_scenes>
  <scene><instance_visual_scene url=""#Scene""/></scene>
</COLLADA>";

        private static string Node(string id) => $@"
      <node id=""{id}-node"" name=""{id}"" type=""NODE"">
        <matrix sid=""transform"">1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1</matrix>
        <instance_geometry url=""#{id}"" name=""{id}""/>
      </node>";

        private static string Mesh(string id, string positions, string normals, string polylist, string p,
            string extraInput = "", bool uvSource = false)
        {
            var count = polylist.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            return $@"
    <geometry id=""{id}"" name=""{id}"">
      <mesh>
        {Source(id + "-positions", positions, 3, "X Y Z")}
        {Source(id + "-normals", normals, 3, "X Y Z")}
        {(uvSource ? Source(id + "-map", "0 0", 2, "S T") : "")}
        <vertices id=""{id}-vertices""><input semantic=""POSITION"" source=""#{id}-positions""/></vertices>
        <polylist count=""{count}"">
          <input semantic=""VERTEX"" source=""#{id}-vertices"" offset=""0""/>
          <input semantic=""NORMAL"" source=""#{id}-normals"" offset=""1""/>
          {(uvSource ? $@"<input semantic=""TEXCOORD"" source=""#{id}-map"" offset=""2"" set=""0""/>" : "")}
          {extraInput}
          <vcount>{polylist}</vcount>
          <p>{p}</p>
        </polylist>
      </mesh>
    </geometry>";
        }

        //A mesh whose faces name positions only - the form a file takes when it leaves normals to the importer
        private static string MeshWithoutNormals(string id, string positions, string polylist, string p)
        {
            var count = polylist.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            return $@"
    <geometry id=""{id}"" name=""{id}"">
      <mesh>
        {Source(id + "-positions", positions, 3, "X Y Z")}
        <vertices id=""{id}-vertices""><input semantic=""POSITION"" source=""#{id}-positions""/></vertices>
        <polylist count=""{count}"">
          <input semantic=""VERTEX"" source=""#{id}-vertices"" offset=""0""/>
          <vcount>{polylist}</vcount>
          <p>{p}</p>
        </polylist>
      </mesh>
    </geometry>";
        }

        private static string Source(string id, string values, int stride, string parameters)
        {
            var count = values.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
            var accessorParams = String.Join("", parameters.Split(' ')
                .Select(name => $@"<param name=""{name}"" type=""float""/>"));
            return $@"
        <source id=""{id}"">
          <float_array id=""{id}-array"" count=""{count}"">{values}</float_array>
          <technique_common>
            <accessor source=""#{id}-array"" count=""{count / stride}"" stride=""{stride}"">{accessorParams}</accessor>
          </technique_common>
        </source>";
        }

        #endregion
    }
}

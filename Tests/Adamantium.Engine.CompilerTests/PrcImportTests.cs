using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Engine.Compiler.Converter;
using Adamantium.Engine.Compiler.Converter.Configs;
using Adamantium.Engine.Compiler.Models.Converters;
using Adamantium.Engine.Compiler.Models.Prc;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests;

/// <summary>PRC import (PRC Format Specification 8137) from files Asymptote wrote from the .asy scenes beside them,
/// and from files the tests write: tessellations of every kind of triangulation, wires, markups, the product tree and
/// its transformations, schemas stepping over newer data, and data cut short. PRC is Z up: the engine's axes take
/// (x, y, z) to (-x, -z, y).</summary>
[TestFixture]
public class PrcImportTests
{
    private const float Tolerance = 1e-4f;

    private static readonly double[][] Colours = [[1, 0, 0], [0, 0.5, 1], [0.2, 0.4, 0.6, 128]];

    private static string Generated(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "CompilerPrc", name);

    [Test]
    public void AsymptoteTriangles_GiveTheTetrahedronOfTheirSource()
    {
        var scene = Import(File.ReadAllBytes(Generated("triangles.prc")), out var notes);

        var mesh = AllMeshes(scene).Single();
        var source = new[] { new Vector3F(0, 0, 0), new Vector3F(2, 0, 0), new Vector3F(0, 3, 0), new Vector3F(0, 0, 4) };
        int[][] faces = [[0, 2, 1], [0, 1, 3], [0, 3, 2], [1, 2, 3]];
        var read = new List<string>();
        for (var index = 0; index < mesh.Indices.Length; index += 3)
        {
            var corners = Enumerable.Range(0, 3)
                .Select(corner => Nearest(source.Select(Engine).ToList(), mesh.Points[mesh.Indices[index + corner]].As<float>()))
                .OrderBy(corner => corner);
            read.Add(string.Join(" ", corners));
        }

        Assert.Multiple(() =>
        {
            Assert.That(notes, Is.Empty);
            Assert.That(mesh.MeshTopology, Is.EqualTo(PrimitiveType.TriangleList));
            Assert.That(read, Is.EquivalentTo(faces.Select(face => string.Join(" ", face.OrderBy(corner => corner)))));
            Assert.That(mesh.Normals, Has.Length.EqualTo(mesh.Points.Length), "the file's normals are kept");
            Assert.That(scene.Materials[mesh.MaterialID].DiffuseColor, Is.EqualTo(new Vector4F(1, 0, 0, 1)), "red, as drawn");
            Assert.That(scene.Units.Value, Is.EqualTo(0.0254f / 72).Within(1e-9f), "Asymptote's unit, the PostScript point");
        });
    }

    [Test]
    public void AsymptoteGroups_GiveBothTrianglesInTheirColours()
    {
        var scene = Import(File.ReadAllBytes(Generated("groups.prc")), out var notes);

        var meshes = AllMeshes(scene).ToList();
        var outer = meshes.Single(mesh => mesh.Points.All(point => point.X > -2));
        var inner = meshes.Single(mesh => mesh.Points.All(point => point.X < -9));
        Assert.Multiple(() =>
        {
            Assert.That(notes, Is.Empty);
            Assert.That(outer.Points.Select(point => point.As<float>()),
                Is.EquivalentTo(new[] { Engine(new Vector3F(0, 0, 0)), Engine(new Vector3F(1, 0, 0)), Engine(new Vector3F(0, 1, 0)) })
                    .Using<Vector3F>(Near));
            Assert.That(inner.Points.Select(point => point.As<float>()),
                Is.EquivalentTo(new[] { Engine(new Vector3F(10, 0, 0)), Engine(new Vector3F(11, 0, 0)), Engine(new Vector3F(10, 1, 0)) })
                    .Using<Vector3F>(Near));
            Assert.That(Rgb(scene.Materials[outer.MaterialID]), Is.EqualTo(new Vector3F(0.2f, 0.4f, 0.6f)).Using<Vector3F>(Near));
            Assert.That(Rgb(scene.Materials[inner.MaterialID]), Is.EqualTo(new Vector3F(0.8f, 0.6f, 0.2f)).Using<Vector3F>(Near));
        });
    }

    [TestCase("nurbs.prc", "surface")]
    [TestCase("lines.prc", "Curve")]
    public void AsymptoteExactGeometry_IsToldPerItemAndNotDrawn(string name, string item)
    {
        var scene = Import(File.ReadAllBytes(Generated(name)), out var notes);

        Assert.Multiple(() =>
        {
            Assert.That(AllMeshes(scene), Is.Empty);
            Assert.That(notes, Has.Some.Contains($"representation item {item} has exact geometry alone"));
            Assert.That(notes, Has.Some.Contains("B-rep topology, curves and surfaces"));
        });
    }

    [Test]
    public void Triangulations_FansStripesAndOneNormalKindsBecomeTriangles()
    {
        double[] points = [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0, 2, 0, 0, 2, 1, 0];
        double[] normals = [0, 0, 1];
        var triangulated = new List<uint>();
        void Corner(uint point) => triangulated.AddRange([0, point * 3]);
        Corner(0);
        Corner(1);
        Corner(2);
        foreach (var point in new uint[] { 0, 1, 2, 3 })
        {
            Corner(point);
        }

        foreach (var point in new uint[] { 1, 4, 2, 5 })
        {
            Corner(point);
        }

        triangulated.Add(0);
        triangulated.AddRange(new uint[] { 0, 3, 6, 9 });
        var face = new PrcTestFace
        {
            UsedEntities = PrcTessellationReader.Triangle | PrcTessellationReader.TriangleFan | PrcTessellationReader.TriangleStripe |
                           PrcTessellationReader.TriangleFanOneNormal,
            Sizes = [1, 1, 4, 1, 4, 1, 4 | PrcTessellationReader.SingleNormal]
        };
        var bytes = PartFile([PrcTestEntities.Tessellation3D(points, normals, triangulated.ToArray(), [face])],
            writer => PrcTestEntities.PolyBrep(writer, "Plate", 0, 0), 1);

        var scene = Import(bytes, out var notes);

        var mesh = AllMeshes(scene).Single();
        var turns = new List<float>();
        for (var index = 0; index < mesh.Indices.Length; index += 3)
        {
            var first = mesh.Points[mesh.Indices[index]].As<float>();
            var normal = Vector3F.Cross(mesh.Points[mesh.Indices[index + 1]].As<float>() - first,
                mesh.Points[mesh.Indices[index + 2]].As<float>() - first);
            turns.Add(MathF.Sign(Vector3F.Dot(normal, mesh.Normals[mesh.Indices[index]])));
        }

        Assert.Multiple(() =>
        {
            Assert.That(notes, Is.Empty);
            Assert.That(mesh.Indices.Length / 3, Is.EqualTo(1 + 2 + 2 + 2), "a triangle, a fan, a stripe and a one-normal fan");
            Assert.That(turns.Distinct().Single(), Is.Not.EqualTo(0), "every triangle wound alike - the stripe's every other one turned round");
            Assert.That(scene.Materials[mesh.MaterialID].DiffuseColor, Is.EqualTo(new Vector4F(1, 0, 0, 1)));
        });
    }

    [Test]
    public void RecalculatedNormals_FacesStartWhereThePreviousEnded()
    {
        double[] points = [0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1];
        uint[] triangulated = [0, 3, 6, 0, 6, 9];
        PrcTestFace[] faces =
        [
            new() { UsedEntities = PrcTessellationReader.Triangle, Start = 0, Sizes = [1], Style = 0 },
            new() { UsedEntities = PrcTessellationReader.Triangle, Start = 6, Sizes = [1], Style = 1 }
        ];
        var bytes = PartFile([PrcTestEntities.Tessellation3D(points, null, triangulated, faces)],
            writer => PrcTestEntities.PolyBrep(writer, "Corner", -1, 0), 1);

        var scene = Import(bytes, out var notes);

        var meshes = AllMeshes(scene).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(meshes, Has.Count.EqualTo(2), "one mesh for each face's style");
            Assert.That(meshes.Select(mesh => mesh.Indices.Length), Is.All.EqualTo(3));
            Assert.That(meshes.Select(mesh => Rgb(scene.Materials[mesh.MaterialID])),
                Is.EquivalentTo(new[] { new Vector3F(1, 0, 0), new Vector3F(0, 0.5f, 1) }).Using<Vector3F>(Near));
            Assert.That(meshes.Select(mesh => mesh.Normals.Length), Is.All.EqualTo(3), "made flat by the import");
            Assert.That(notes, Has.Some.Contains("crease angle"));
        });
    }

    [Test]
    public void VertexColours_ColourEachVertex()
    {
        double[] points = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        uint[] triangulated = [0, 0, 0, 3, 0, 6];
        var face = new PrcTestFace
        {
            UsedEntities = PrcTessellationReader.Triangle, Sizes = [1], Colors = [[255, 0, 0], [0, 255, 0], [0, 0, 255]]
        };
        var bytes = PartFile([PrcTestEntities.Tessellation3D(points, [0, 0, 1], triangulated, [face])],
            writer => PrcTestEntities.PolyBrep(writer, "Gradient", -1, 0), 1);

        var mesh = AllMeshes(Import(bytes, out _)).Single();

        Assert.That(mesh.Colors.Select(colour => colour.R + colour.G + colour.B), Is.All.EqualTo(255));
        Assert.That(mesh.Colors.Select(colour => colour.R), Is.EquivalentTo(new[] { 255, 0, 0 }));
    }

    [Test]
    public void WireTessellation_GivesItsPolylinesClosedAndContinued()
    {
        double[] points = [0, 0, 0, 1, 0, 0, 1, 1, 0, 5, 5, 5, 6, 5, 5];
        uint[] wires = [2, 0, 3, 0x10000000 | 2, 6, 3, 0x20000000 | 2, 9, 12];
        var bytes = PartFile([PrcTestEntities.Wire(points, wires)], writer => PrcTestEntities.PolyWire(writer, "Wire", 2, 0), 1);

        var scene = Import(bytes, out var notes);

        var mesh = AllMeshes(scene).Single();
        var segments = Enumerable.Range(0, mesh.Indices.Length / 2)
            .Select(segment => $"{Key(mesh.Points[mesh.Indices[segment * 2]])}>{Key(mesh.Points[mesh.Indices[segment * 2 + 1]])}")
            .ToList();
        Assert.Multiple(() =>
        {
            Assert.That(notes, Is.Empty);
            Assert.That(mesh.MeshTopology, Is.EqualTo(PrimitiveType.LineList));
            Assert.That(segments, Is.EqualTo(new[]
            {
                Segment(0, 0, 0, 1, 0, 0), Segment(1, 1, 0, 1, 0, 0), Segment(1, 0, 0, 1, 1, 0), Segment(1, 0, 0, 5, 5, 5),
                Segment(5, 5, 5, 6, 5, 5)
            }), "the first wire, the second closed back on itself, the third continuing it");
            Assert.That(scene.Materials[mesh.MaterialID].DiffuseColor.W, Is.EqualTo(128 / 255f).Within(1e-6f), "the style's transparency");
        });
    }

    [Test]
    public void MarkupTessellation_GivesLinesAndTrianglesUnderItsMarkup()
    {
        double[] coordinates =
        [
            0, 0, 0, 1, 0, 0, 1, 1, 0,
            1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 10, 0, 0, 1,
            0, 0, 0, 1, 0, 0,
            0, 0, 0, 1, 0, 0, 0, 1, 0
        ];
        uint[] codes =
        [
            0, 9,
            0x08000000 | 2, 16 + 6 + 9,
            0, 6,
            0x04000000 | (2u << 21), 9,
            0x08000000, 0,
            0x04000000 | (14u << 21) | 1, 2, 0
        ];
        var bytes = PartFile([PrcTestEntities.Markup(coordinates.Concat([4.0, 2.0]).ToArray(), codes, "Note")],
            writer => PrcTestEntities.PointSet(writer, "Probe", [[1, 2, 3]]), 1, PrcTestEntities.Markups("Dimension.1", 0));

        var scene = Import(bytes, out var notes);

        var markup = scene.Models.Dependencies.Single().Dependencies.Single(model => model.Name == "Markups").Dependencies.Single();
        var lines = markup.Meshes.Single(mesh => mesh.MeshTopology == PrimitiveType.LineList);
        var triangles = markup.Meshes.Single(mesh => mesh.MeshTopology == PrimitiveType.TriangleList);
        var points = AllMeshes(scene).Single(mesh => mesh.MeshTopology == PrimitiveType.PointList);
        Assert.Multiple(() =>
        {
            Assert.That(markup.Name, Is.EqualTo("Dimension.1"));
            Assert.That(lines.Indices, Has.Length.EqualTo((2 + 1) * 2), "a polyline of three points, one of two");
            Assert.That(lines.Points.Select(point => point.As<float>()), Has.Some.Matches<Vector3F>(point => Near(point, Engine(new Vector3F(10, 0, 0)))),
                "the second polyline moved by its matrix block");
            Assert.That(triangles.Indices, Has.Length.EqualTo(3));
            Assert.That(triangles.Points.Select(point => point.As<float>()).Max(point => -point.X), Is.EqualTo(11).Within(Tolerance),
                "the triangle moved by the block too");
            Assert.That(Near(points.Points.Single().As<float>(), Engine(new Vector3F(1, 2, 3))));
            Assert.That(notes, Has.Some.Contains("text of markups"));
        });
    }

    [Test]
    public void ProductTree_PlacesOccurrencesByTheirLocations()
    {
        double[] points = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        uint[] triangulated = [0, 0, 0, 3, 0, 6];
        var tessellation = PrcTestEntities.Tessellation3D(points, [0, 0, 1], triangulated,
            [new PrcTestFace { UsedEntities = PrcTessellationReader.Triangle, Sizes = [1] }]);
        var globals = PrcTestEntities.Globals(Colours, coordinateSystemCount: 1,
            coordinateSystems: writer => PrcTestEntities.CoordinateSystem(writer, PrcTestEntities.Cartesian([0, 0, 7])));
        var part = PrcTestEntities.Part("Bracket", 1, writer => PrcTestEntities.PolyBrep(writer, "Body", 0, 0, 0));
        var general = PrcTestEntities.General([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 5, 1]);
        var tree = PrcTestEntities.Tree([part],
        [
            PrcTestEntities.Occurrence("Left", 0, [], PrcTestEntities.Cartesian([1, 2, 3], [0, 1, 0], [-1, 0, 0])),
            PrcTestEntities.Occurrence("Right", -1, [], general, prototype: 0),
            PrcTestEntities.Occurrence("Hidden", 0, [], behavior: 0),
            PrcTestEntities.Occurrence("Assembly", -1, [0, 1, 2])
        ]);
        var bytes = PrcTestFile.Single(globals, tree, PrcTestEntities.Tessellations([tessellation]), 4).ToArray();

        var scene = Import(bytes, out var notes);

        var assembly = scene.Models.Dependencies.Single();
        var left = assembly.Dependencies.Single(model => model.Name == "Left");
        var right = assembly.Dependencies.Single(model => model.Name == "Right");
        var body = left.Dependencies.Single();
        Assert.Multiple(() =>
        {
            Assert.That(assembly.Name, Is.EqualTo("Assembly"));
            Assert.That(assembly.Dependencies.Select(model => model.Name), Is.EqualTo(new[] { "Left", "Right" }));
            Assert.That(Near(left.Position, Engine(new Vector3F(1, 2, 3))), $"Left stands at {left.Position}");
            Assert.That(Near(right.Position, Engine(new Vector3F(0, 0, 5))), $"Right stands at {right.Position}");
            Assert.That(right.Dependencies.Single().Meshes, Has.Count.EqualTo(1), "the prototype's part, shown again");
            Assert.That(Near(body.Position, Engine(new Vector3F(0, 0, 7))), $"the body stands at {body.Position}, by its coordinate system");
            Assert.That(body.Name, Is.EqualTo("Body"));
            Assert.That(notes, Has.Some.Contains("set not to be shown").And.Contains("Hidden"));
        });

        var turned = Vector3.Transform(Engine(Vector3F.UnitX), left.Rotation);
        Assert.That(Near(turned, Engine(Vector3F.UnitY)), $"Left's x axis turns to {turned}, the location's x axis");
    }

    [Test]
    public void Schema_StepsOverNewerDataAndUnknownEntities()
    {
        const uint blockStart = 19;
        const uint blockVersion = 20;
        const uint blockEnd = 21;
        const uint unknownItem = 990;
        PrcTestSchema[] schemas =
        [
            new(PrcTypes.PrcBaseWithGraphics, blockStart, 39, 1, blockVersion, 9000, 4, 1, blockEnd, blockVersion, 7000, 5, blockEnd, blockEnd),
            new(PrcTypes.PartDefinition, blockStart, blockVersion, 9000, 24, 0, 3, 15, 25, 0, 1, 17, 37, 25, 0, 26, 2, 0, 18, 2,
                blockEnd, blockEnd),
            new(unknownItem, blockStart, 39, 1, blockVersion, 9000, 5, 16, blockStart, 8, blockEnd, blockEnd, blockEnd)
        ];
        double[] points = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        var tessellation = PrcTestEntities.Tessellation3D(points, [0, 0, 1], [0, 0, 0, 3, 0, 6],
            [new PrcTestFace { UsedEntities = PrcTessellationReader.Triangle, Sizes = [1] }]);
        var tree = new PrcTestBitWriter();
        var part = PrcTestEntities.Part("Part", 2, writer =>
            {
                writer.WriteUnsignedInteger(unknownItem);
                writer.WriteString("from the future");
                writer.WriteUnsignedInteger(2);
                foreach (var value in new double[] { 1, 2, 3, 4, 5, 6 })
                {
                    writer.WriteDouble(value);
                }

                writer.WriteUnsignedInteger(PrcTypes.PolyBrepModel);
                PrcTestEntities.BaseWithGraphics(writer, "Known", 0);
                writer.WriteInteger(-17);
                writer.WriteDouble(2.5);
                writer.WriteUnsignedInteger(0);
                writer.WriteUnsignedInteger(1);
                writer.WriteBoolean(true);
                PrcTestEntities.UserData(writer);
            },
            beforeUserData: writer =>
            {
                writer.WriteUnsignedInteger(3);
                writer.WriteDouble(0.25);
                writer.WriteDouble(0.5);
                writer.WriteDouble(0.75);
                writer.WriteCharacter(42);
            });
        var file = PrcTestFile.Single(PrcTestEntities.Globals(Colours, schemas),
            PrcTestEntities.Tree([WithGraphicsExtension(part)], [WithGraphicsExtension(PrcTestEntities.Occurrence("Root", 0, []))], tree),
            PrcTestEntities.Tessellations([tessellation]), 1);

        var scene = Import(file.ToArray(), out var notes);

        Assert.Multiple(() =>
        {
            Assert.That(AllMeshes(scene).Single().Indices, Has.Length.EqualTo(3), "the known item after the unknown one");
            Assert.That(notes, Has.Some.Contains("type 990"));
            Assert.That(notes.Where(note => note.Contains("left after the end")), Is.Empty);
        });
    }

    [Test]
    public void CompressedTessellation_IsToldAndTheSectionReadNoFurther()
    {
        double[] points = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        var regular = PrcTestEntities.Tessellation3D(points, [0, 0, 1], [0, 0, 0, 3, 0, 6],
            [new PrcTestFace { UsedEntities = PrcTessellationReader.Triangle, Sizes = [1] }]);
        var bytes = PartFile([regular, PrcTestEntities.Compressed(), regular], writer =>
        {
            PrcTestEntities.PolyBrep(writer, "Drawn", 0, 0);
            PrcTestEntities.PolyBrep(writer, "Compressed", 0, 1);
            PrcTestEntities.PolyBrep(writer, "After", 0, 2);
        }, 3);

        var scene = Import(bytes, out var notes);

        Assert.Multiple(() =>
        {
            Assert.That(AllMeshes(scene).Count(), Is.EqualTo(1));
            Assert.That(notes, Has.Some.Contains("2 tessellations of 1 file structures are not read").And.Contains("type 173"));
            Assert.That(notes, Has.Some.Contains("2 representation items show a tessellation that is not read").And.Contains("Compressed, After"));
        });
    }

    [Test]
    public void Sets_NestTheirItems()
    {
        double[] points = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        var tessellation = PrcTestEntities.Tessellation3D(points, [0, 0, 1], [0, 0, 0, 3, 0, 6],
            [new PrcTestFace { UsedEntities = PrcTessellationReader.Triangle, Sizes = [1] }]);
        var bytes = PartFile([tessellation], writer =>
        {
            PrcTestEntities.SetStart(writer, "Outer", 2);
            PrcTestEntities.SetStart(writer, "Inner", 1);
            PrcTestEntities.PolyBrep(writer, "Deep", 0, 0);
            PrcTestEntities.SetEnd(writer);
            PrcTestEntities.BrepModel(writer, "Exact", 0, 3);
            PrcTestEntities.SetEnd(writer);
        }, 1);

        var scene = Import(bytes, out var notes);

        var outer = scene.Models.Dependencies.Single().Dependencies.Single();
        Assert.Multiple(() =>
        {
            Assert.That(outer.Name, Is.EqualTo("Outer"));
            Assert.That(outer.Dependencies.Select(model => model.Name), Is.EqualTo(new[] { "Inner", "Exact" }));
            Assert.That(outer.Dependencies[0].Dependencies.Single().Meshes, Has.Count.EqualTo(1));
            Assert.That(notes, Has.Some.Contains("representation item Exact has exact geometry alone (a B-rep body, topological context 0 body 3)"));
        });
    }

    [Test, Timeout(120000)]
    public void CutShort_ThrowsInvalidDataAtEveryLength()
    {
        var bytes = TriangleFile();
        for (var length = 0; length < bytes.Length; length++)
        {
            Assert.That(() => Import(bytes[..length], out _), Throws.TypeOf<InvalidDataException>(), $"cut at {length}");
        }
    }

    [Test, Timeout(120000)]
    public void SectionCutShort_ThrowsNamingTheSection()
    {
        var tree = TreeOfTriangle();
        for (var length = 0; length < tree.Length - 1; length++)
        {
            var file = PrcTestFile.Single(PrcTestEntities.Globals(Colours), tree[..length],
                PrcTestEntities.Tessellations([TriangleTessellation()]), 1);
            Assert.That(() => Import(file.ToArray(), out _),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("tree of file structure 0"), $"cut at {length}");
        }
    }

    [Test]
    public void CountPastTheData_Throws()
    {
        var writer = new PrcTestBitWriter();
        writer.WriteUnsignedInteger(PrcTypes.FileStructureTree);
        PrcTestEntities.ContentPrcBase(writer, null, false);
        writer.WriteUnsignedInteger(0x7FFFFFFF);
        var file = PrcTestFile.Single(PrcTestEntities.Globals(Colours), writer.ToArray(),
            PrcTestEntities.Tessellations([TriangleTessellation()]), 1);

        Assert.That(() => Import(file.ToArray(), out _),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("part definitions"));
    }

    [Test]
    public void NotPrcOrTooNew_Throws()
    {
        var newer = new PrcTestFile { MinimalVersion = 20000, AuthoringVersion = 20000 };
        newer.Structures.Add(new PrcTestStructure(1) { Globals = [], Tree = [], Tessellation = [] });
        newer.ModelFile = [];

        Assert.Multiple(() =>
        {
            Assert.That(() => Import("glTF and more"u8.ToArray(), out _),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("not a PRC file"));
            Assert.That(() => Import(newer.ToArray(), out _),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("version 20000"));
        });
    }

    [Test]
    public void Factory_ReadsAPrcFromBytesAndFromItsExtension()
    {
        var bytes = TriangleFile();
        var converter = ModelConverterFactory.GetConverter("artwork.prc", bytes, new ConversionConfig(true));

        Assert.That(converter, Is.TypeOf<PrcConverter>());
        Assert.That(AllMeshes(converter.StartConversion()).Single().Indices, Has.Length.EqualTo(3));
        Assert.That(ModelConverterFactory.GetConverter(Generated("triangles.prc"), new ConversionConfig(true)), Is.TypeOf<PrcConverter>());
    }

    private static Action<PrcTestBitWriter> WithGraphicsExtension(Action<PrcTestBitWriter> entity) => writer =>
    {
        var inner = new PrcTestBitWriter();
        entity(inner);
        var bits = inner.ToArray();
        var reader = new PrcBitReader(bits, "copy");
        var type = reader.ReadUnsignedInteger();
        writer.WriteUnsignedInteger(type);
        CopyBaseWithGraphics(reader, writer);
        writer.WriteInteger(-17);
        writer.WriteDouble(2.5);
        var left = inner.BitCount - (int)reader.BitPosition;
        for (var bit = 0; bit < left; bit++)
        {
            writer.WriteBit(reader.ReadBoolean());
        }
    };

    private static void CopyBaseWithGraphics(PrcBitReader reader, PrcTestBitWriter writer)
    {
        writer.WriteUnsignedInteger(reader.ReadUnsignedInteger());
        writer.WriteBoolean(reader.ReadBoolean());
        writer.WriteString(reader.ReadString());
        for (var identifier = 0; identifier < 3; identifier++)
        {
            writer.WriteUnsignedInteger(reader.ReadUnsignedInteger());
        }

        writer.WriteBoolean(reader.ReadBoolean());
        writer.WriteUnsignedInteger(reader.ReadUnsignedInteger());
        writer.WriteUnsignedInteger(reader.ReadUnsignedInteger());
        writer.WriteCharacter(reader.ReadCharacter());
        writer.WriteCharacter(reader.ReadCharacter());
    }

    private static Action<PrcTestBitWriter> TriangleTessellation() =>
        PrcTestEntities.Tessellation3D([0, 0, 0, 1, 0, 0, 0, 1, 0], [0, 0, 1], [0, 0, 0, 3, 0, 6],
            [new PrcTestFace { UsedEntities = PrcTessellationReader.Triangle, Sizes = [1] }]);

    private static byte[] TreeOfTriangle() =>
        PrcTestEntities.Tree([PrcTestEntities.Part("Part", 1, writer => PrcTestEntities.PolyBrep(writer, "Triangle", 0, 0))],
            [PrcTestEntities.Occurrence("Root", 0, [])]);

    private static byte[] TriangleFile() =>
        PrcTestFile.Single(PrcTestEntities.Globals(Colours), TreeOfTriangle(), PrcTestEntities.Tessellations([TriangleTessellation()]), 1)
            .ToArray();

    private static byte[] PartFile(IReadOnlyList<Action<PrcTestBitWriter>> tessellations, Action<PrcTestBitWriter> items, int itemCount,
        Action<PrcTestBitWriter> markups = null)
    {
        var tree = PrcTestEntities.Tree([PrcTestEntities.Part("Part", itemCount, items, markups)], [PrcTestEntities.Occurrence("Root", 0, [])]);
        return PrcTestFile.Single(PrcTestEntities.Globals(Colours), tree, PrcTestEntities.Tessellations(tessellations), 1).ToArray();
    }

    private static SceneData Import(byte[] bytes, out List<string> notes)
    {
        var converter = new PrcConverter("test.prc", bytes, new ConversionConfig(true));
        var scene = converter.StartConversion();
        notes = converter.UnsupportedFeatures;
        return scene;
    }

    private static IEnumerable<Mesh> AllMeshes(SceneData scene)
    {
        var pending = new Stack<SceneData.Model>();
        pending.Push(scene.Models);
        while (pending.Count > 0)
        {
            var model = pending.Pop();
            foreach (var mesh in model.Meshes)
            {
                yield return mesh;
            }

            foreach (var child in model.Dependencies)
            {
                pending.Push(child);
            }
        }
    }

    private static int Nearest(List<Vector3F> positions, Vector3F point)
    {
        var nearest = Enumerable.Range(0, positions.Count).OrderBy(index => (positions[index] - point).Length()).First();
        Assert.That((positions[nearest] - point).Length(), Is.LessThan(Tolerance), $"{point} is no vertex of the source");
        return nearest;
    }

    private static string Key(Vector3<double> point)
    {
        var source = new Vector3F((float)-point.X, (float)point.Z, (float)-point.Y);
        return $"{Math.Round(source.X)},{Math.Round(source.Y)},{Math.Round(source.Z)}";
    }

    private static string Segment(int fromX, int fromY, int fromZ, int toX, int toY, int toZ) =>
        $"{fromX},{fromY},{fromZ}>{toX},{toY},{toZ}";

    private static Vector3F Rgb(SceneData.Material material) =>
        new(material.DiffuseColor.X, material.DiffuseColor.Y, material.DiffuseColor.Z);

    private static Vector3F Engine(Vector3F prc) => new(-prc.X, -prc.Z, prc.Y);

    private static bool Near(Vector3F expected, Vector3F actual) =>
        MathF.Abs(expected.X - actual.X) <= Tolerance && MathF.Abs(expected.Y - actual.Y) <= Tolerance &&
        MathF.Abs(expected.Z - actual.Z) <= Tolerance;
}

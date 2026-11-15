using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Adamantium.Engine.Compiler.Converter;
using Adamantium.Engine.Compiler.Converter.Configs;
using Adamantium.Engine.Compiler.Models.Converters;
using Adamantium.Engine.Compiler.Models.Prc;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests;

/// <summary>PRC import from files Asymptote wrote from the .asy scenes beside them and from files the tests write. PRC is
/// Z up: the engine's axes take (x, y, z) to (x, -z, y).</summary>
[TestFixture]
public class PrcImportTests
{
    private const float Tolerance = 1e-4f;

    private static readonly double[][] Colors = [[1, 0, 0], [0, 0.5, 1], [0.2, 0.4, 0.6, 128]];

    [Test]
    public void AsymptoteTriangles_GiveTheTetrahedronOfTheirSource()
    {
        var scene = Import(File.ReadAllBytes(GetGeneratedPath("triangles.prc")), out var notes);

        var mesh = GetAllMeshes(scene).Single();
        Vector3F[] source = [new Vector3F(0, 0, 0), new Vector3F(2, 0, 0), new Vector3F(0, 3, 0), new Vector3F(0, 0, 4)];
        int[][] faces = [[0, 2, 1], [0, 1, 3], [0, 3, 2], [1, 2, 3]];
        List<string> read = [];
        for (var index = 0; index < mesh.Indices.Length; index += 3)
        {
            var corners = Enumerable.Range(0, 3)
                .Select(corner => FindNearest(source.Select(ToEngine).ToList(), mesh.Points[mesh.Indices[index + corner]].As<float>()))
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
    public void AsymptoteGroups_GiveBothTrianglesInTheirColors()
    {
        var scene = Import(File.ReadAllBytes(GetGeneratedPath("groups.prc")), out var notes);

        var meshes = GetAllMeshes(scene).ToList();
        var outer = meshes.Single(mesh => mesh.Points.All(point => point.X < 2));
        var inner = meshes.Single(mesh => mesh.Points.All(point => point.X > 9));
        Vector3F[] outerPoints = [ToEngine(new Vector3F(0, 0, 0)), ToEngine(new Vector3F(1, 0, 0)), ToEngine(new Vector3F(0, 1, 0))];
        Vector3F[] innerPoints = [ToEngine(new Vector3F(10, 0, 0)), ToEngine(new Vector3F(11, 0, 0)), ToEngine(new Vector3F(10, 1, 0))];
        Assert.Multiple(() =>
        {
            Assert.That(notes, Is.Empty);
            Assert.That(outer.Points.Select(point => point.As<float>()), Is.EquivalentTo(outerPoints).Using<Vector3F>(IsNear));
            Assert.That(inner.Points.Select(point => point.As<float>()), Is.EquivalentTo(innerPoints).Using<Vector3F>(IsNear));
            Assert.That(GetRgb(scene.Materials[outer.MaterialID]), Is.EqualTo(new Vector3F(0.2f, 0.4f, 0.6f)).Using<Vector3F>(IsNear));
            Assert.That(GetRgb(scene.Materials[inner.MaterialID]), Is.EqualTo(new Vector3F(0.8f, 0.6f, 0.2f)).Using<Vector3F>(IsNear));
        });
    }

    [TestCase("nurbs.prc", "surface")]
    [TestCase("lines.prc", "Curve")]
    public void AsymptoteExactGeometry_IsToldPerItemAndNotDrawn(string name, string item)
    {
        var scene = Import(File.ReadAllBytes(GetGeneratedPath(name)), out var notes, out var remarks);

        Assert.Multiple(() =>
        {
            Assert.That(GetAllMeshes(scene), Is.Empty);
            Assert.That(notes, Has.Some.Contains($"representation item {item} has exact geometry alone"),
                "an item drawn by its exact geometry alone is not drawn: a loss");
            Assert.That(remarks, Has.Some.Contains("B-rep topology, curves and surfaces"),
                "that the file has exact geometry at all leaves nothing out where a tessellation is drawn");
            Assert.That(notes, Has.None.Contains("B-rep topology, curves and surfaces"));
        });
    }

    [Test]
    public void Triangulations_FansStripesAndOneNormalKindsBecomeTriangles()
    {
        double[] points = [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0, 2, 0, 0, 2, 1, 0];
        double[] normals = [0, 0, 1];
        List<uint> triangulated = [];
        void AddCorner(uint point) => triangulated.AddRange([0, point * 3]);
        AddCorner(0);
        AddCorner(1);
        AddCorner(2);
        uint[] fan = [0, 1, 2, 3];
        foreach (var point in fan)
        {
            AddCorner(point);
        }

        uint[] stripe = [1, 4, 2, 5];
        foreach (var point in stripe)
        {
            AddCorner(point);
        }

        triangulated.Add(0);
        triangulated.AddRange([0, 3, 6, 9]);
        var face = new PrcTestFace
        {
            UsedEntities = PrcTessellationReader.Triangle | PrcTessellationReader.TriangleFan | PrcTessellationReader.TriangleStripe |
                           PrcTessellationReader.TriangleFanOneNormal,
            Sizes = [1, 1, 4, 1, 4, 1, 4 | PrcTessellationReader.SingleNormal]
        };
        var bytes = CreatePartFile([PrcTestEntities.CreateTessellation3D(points, normals, triangulated.ToArray(), [face])],
            writer => PrcTestEntities.WritePolyBrep(writer, "Plate", 0, 0), 1);

        var scene = Import(bytes, out var notes);

        var mesh = GetAllMeshes(scene).Single();
        List<float> turns = [];
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
        var bytes = CreatePartFile([PrcTestEntities.CreateTessellation3D(points, null, triangulated, faces)],
            writer => PrcTestEntities.WritePolyBrep(writer, "Corner", -1, 0), 1);

        var scene = Import(bytes, out var notes);

        var meshes = GetAllMeshes(scene).ToList();
        Vector3F[] colors = [new Vector3F(1, 0, 0), new Vector3F(0, 0.5f, 1)];
        Assert.Multiple(() =>
        {
            Assert.That(meshes, Has.Count.EqualTo(2), "one mesh for each face's style");
            Assert.That(meshes.Select(mesh => mesh.Indices.Length), Is.All.EqualTo(3));
            Assert.That(meshes.Select(mesh => GetRgb(scene.Materials[mesh.MaterialID])), Is.EquivalentTo(colors).Using<Vector3F>(IsNear));
            Assert.That(meshes.Select(mesh => mesh.Normals.Length), Is.All.EqualTo(3));
            Assert.That(meshes.Select(mesh => mesh.Normals.Distinct().Count()), Is.All.EqualTo(1),
                "the faces meet at a right angle, past the crease angle of half a degree: each is flat");
            Assert.That(notes, Has.None.Contains("crease angle"));
        });
    }

    [TestCase(30, true)]
    [TestCase(5, false)]
    public void RecalculatedNormals_AreSharedUpToTheCreaseAngle(double creaseAngle, bool shared)
    {
        var lift = Math.Tan(10 * Math.PI / 180);
        double[] points = [0, 0, 0, 0, 1, 0, -1, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, lift];
        uint[] triangulated = [0, 3, 6, 9, 15, 12];
        PrcTestFace[] faces =
        [
            new() { UsedEntities = PrcTessellationReader.Triangle, Start = 0, Sizes = [1] },
            new() { UsedEntities = PrcTessellationReader.Triangle, Start = 6, Sizes = [1] }
        ];
        var bytes = CreatePartFile([PrcTestEntities.CreateTessellation3D(points, null, triangulated, faces, creaseAngle: creaseAngle)],
            writer => PrcTestEntities.WritePolyBrep(writer, "Fold", 0, 0), 1);

        var mesh = GetAllMeshes(Import(bytes, out var notes)).Single();

        var fold = Enumerable.Range(0, mesh.Indices.Length)
            .Where(corner => Math.Abs(mesh.Points[mesh.Indices[corner]].X) < 1e-9)
            .GroupBy(corner => GetKey(mesh.Points[mesh.Indices[corner]]))
            .Select(point => point.Select(corner => mesh.Normals[mesh.Indices[corner]]).ToList()).ToList();
        int[] cornersOfFold = [2, 2];
        Assert.Multiple(() =>
        {
            Assert.That(notes, Is.Empty);
            Assert.That(fold.Select(point => point.Count), Is.EqualTo(cornersOfFold), "both triangles have both points of the fold");
            Assert.That(mesh.Normals.Select(normal => normal.Length()), Is.All.EqualTo(1).Within(1e-5f));
            foreach (var point in fold)
            {
                var apart = MathF.Acos(Math.Clamp(Vector3F.Dot(point[0], point[1]), -1, 1)) * 180 / MathF.PI;
                Assert.That(apart, shared ? Is.EqualTo(0).Within(1e-3f) : Is.EqualTo(10).Within(1e-2f),
                    shared ? "ten degrees apart, within the crease angle: one normal on the fold" : "past the crease angle: each its own");
            }
        });
    }

    [Test]
    public void VertexColors_ColorEachVertex()
    {
        double[] points = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        uint[] triangulated = [0, 0, 0, 3, 0, 6];
        var face = new PrcTestFace
        {
            UsedEntities = PrcTessellationReader.Triangle, Sizes = [1], Colors = [[255, 0, 0], [0, 255, 0], [0, 0, 255]]
        };
        var bytes = CreatePartFile([PrcTestEntities.CreateTessellation3D(points, [0, 0, 1], triangulated, [face])],
            writer => PrcTestEntities.WritePolyBrep(writer, "Gradient", -1, 0), 1);

        var mesh = GetAllMeshes(Import(bytes, out _)).Single();

        int[] reds = [255, 0, 0];
        Assert.That(mesh.Colors.Select(color => color.R + color.G + color.B), Is.All.EqualTo(255));
        Assert.That(mesh.Colors.Select(color => color.R), Is.EquivalentTo(reds));
    }

    [Test]
    public void WireTessellation_GivesItsPolylinesClosedAndContinued()
    {
        double[] points = [0, 0, 0, 1, 0, 0, 1, 1, 0, 5, 5, 5, 6, 5, 5];
        uint[] wires = [2, 0, 3, PrcMeshBuilder.IsClosing | 2, 6, 3, PrcMeshBuilder.IsContinuous | 2, 9, 12];
        var bytes = CreatePartFile([PrcTestEntities.CreateWire(points, wires)], writer => PrcTestEntities.WritePolyWire(writer, "Wire", 2, 0), 1);

        var scene = Import(bytes, out var notes);

        var mesh = GetAllMeshes(scene).Single();
        var segments = Enumerable.Range(0, mesh.Indices.Length / 2)
            .Select(segment => $"{GetKey(mesh.Points[mesh.Indices[segment * 2]])}>{GetKey(mesh.Points[mesh.Indices[segment * 2 + 1]])}")
            .ToList();
        string[] expected =
        [
            FormatSegment(0, 0, 0, 1, 0, 0), FormatSegment(1, 1, 0, 1, 0, 0), FormatSegment(1, 0, 0, 1, 1, 0), FormatSegment(1, 0, 0, 5, 5, 5),
            FormatSegment(5, 5, 5, 6, 5, 5)
        ];
        Assert.Multiple(() =>
        {
            Assert.That(notes, Is.Empty);
            Assert.That(mesh.MeshTopology, Is.EqualTo(PrimitiveType.LineList));
            Assert.That(segments, Is.EqualTo(expected), "the first wire, the second closed back on itself, the third continuing it");
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
            0x04000000 | (13u << 21) | 1, 0, 0,
            0x04000000 | (14u << 21) | 1, 2, 0
        ];
        var bytes = CreatePartFile([PrcTestEntities.CreateMarkup(coordinates.Concat([4.0, 2.0]).ToArray(), codes, "Note")],
            writer => PrcTestEntities.WritePointSet(writer, "Probe", [[1, 2, 3]]), 1, PrcTestEntities.CreateMarkups("Dimension.1", 0),
            PrcTestEntities.CreateGlobals(Colors, fonts: ["Arial"]));

        var scene = Import(bytes, out var notes);

        var markup = scene.Models.Dependencies.Single().Dependencies.Single(model => model.Name == "Markups").Dependencies.Single();
        var lines = markup.Meshes.Single(mesh => mesh.MeshTopology == PrimitiveType.LineList);
        var triangles = markup.Meshes.Single(mesh => mesh.MeshTopology == PrimitiveType.TriangleList);
        var points = GetAllMeshes(scene).Single(mesh => mesh.MeshTopology == PrimitiveType.PointList);
        Assert.Multiple(() =>
        {
            Assert.That(markup.Name, Is.EqualTo("Dimension.1"));
            Assert.That(lines.Indices, Has.Length.EqualTo((2 + 1) * 2), "a polyline of three points, one of two");
            Assert.That(lines.Points.Select(point => point.As<float>()), Has.Some.Matches<Vector3F>(point => IsNear(point, ToEngine(new Vector3F(10, 0, 0)))),
                "the second polyline moved by its matrix block");
            Assert.That(triangles.Indices, Has.Length.EqualTo(3));
            Assert.That(triangles.Points.Select(point => point.As<float>()).Max(point => point.X), Is.EqualTo(11).Within(Tolerance),
                "the triangle moved by the block too");
            Assert.That(IsNear(points.Points.Single().As<float>(), ToEngine(new Vector3F(1, 2, 3))));
            Assert.That(notes, Has.Some.Contains("texts of markups are kept"));
            Assert.That(notes, Has.None.Contains("left out").And.None.Contains("type 13"));
        });

        using var texts = JsonDocument.Parse(markup.MeshExtra.Data);
        var text = texts.RootElement.GetProperty("markupTexts").EnumerateArray().Single();
        double[] origin = [0, 0, 0];
        Assert.Multiple(() =>
        {
            Assert.That(text.GetProperty("text").GetString(), Is.EqualTo("Ø 12"));
            Assert.That(text.GetProperty("width").GetDouble(), Is.EqualTo(4));
            Assert.That(text.GetProperty("height").GetDouble(), Is.EqualTo(2));
            Assert.That(text.GetProperty("font").GetString(), Is.EqualTo("Arial"), "the font entity before it, the globals' first font");
            Assert.That(text.GetProperty("fontSize").GetInt32(), Is.EqualTo(10));
            Assert.That(text.GetProperty("position").EnumerateArray().Select(value => value.GetDouble()), Is.EqualTo(origin));
        });
    }

    [Test]
    public void ProductTree_PlacesOccurrencesByTheirLocations()
    {
        double[] points = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        uint[] triangulated = [0, 0, 0, 3, 0, 6];
        var tessellation = PrcTestEntities.CreateTessellation3D(points, [0, 0, 1], triangulated,
            [new PrcTestFace { UsedEntities = PrcTessellationReader.Triangle, Sizes = [1] }]);
        var globals = PrcTestEntities.CreateGlobals(Colors, coordinateSystemCount: 1,
            coordinateSystems: writer => PrcTestEntities.WriteCoordinateSystem(writer, PrcTestEntities.CreateCartesian([0, 0, 7])));
        var part = PrcTestEntities.CreatePart("Bracket", 1, writer => PrcTestEntities.WritePolyBrep(writer, "Body", 0, 0, 0));
        var general = PrcTestEntities.CreateGeneral([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 5, 1]);
        var tree = PrcTestEntities.CreateTree([part],
        [
            PrcTestEntities.CreateOccurrence("Left", 0, [], PrcTestEntities.CreateCartesian([1, 2, 3], [0, 1, 0], [-1, 0, 0])),
            PrcTestEntities.CreateOccurrence("Right", -1, [], general, prototype: 0),
            PrcTestEntities.CreateOccurrence("Hidden", 0, [], behavior: 0),
            PrcTestEntities.CreateOccurrence("Assembly", -1, [0, 1, 2])
        ]);
        var bytes = PrcTestFile.CreateSingle(globals, tree, PrcTestEntities.CreateTessellations([tessellation]), 4).ToArray();

        var scene = Import(bytes, out var notes, out var remarks);

        var assembly = scene.Models.Dependencies.Single();
        var left = assembly.Dependencies.Single(model => model.Name == "Left");
        var right = assembly.Dependencies.Single(model => model.Name == "Right");
        var body = left.Dependencies.Single();
        string[] shown = ["Left", "Right"];
        Assert.Multiple(() =>
        {
            Assert.That(assembly.Name, Is.EqualTo("Assembly"));
            Assert.That(assembly.Dependencies.Select(model => model.Name), Is.EqualTo(shown));
            Assert.That(IsNear(left.Position, ToEngine(new Vector3F(1, 2, 3))), $"Left stands at {left.Position}");
            Assert.That(IsNear(right.Position, ToEngine(new Vector3F(0, 0, 5))), $"Right stands at {right.Position}");
            Assert.That(right.Dependencies.Single().Meshes, Has.Count.EqualTo(1), "the prototype's part, shown again");
            Assert.That(IsNear(body.Position, ToEngine(new Vector3F(0, 0, 7))), $"the body stands at {body.Position}, by its coordinate system");
            Assert.That(body.Name, Is.EqualTo("Body"));
            Assert.That(remarks, Has.Some.Contains("set not to be shown").And.Contains("Hidden"),
                "the file keeps it out of sight: a remark, not a loss");
            Assert.That(notes, Has.None.Contains("set not to be shown"));
        });

        var turned = Vector3.Transform(ToEngine(Vector3F.UnitX), left.Rotation);
        Assert.That(IsNear(turned, ToEngine(Vector3F.UnitY)), $"Left's x axis turns to {turned}, the location's x axis");
    }

    [TestCase(true, true, true, 0.001f, false)]
    [TestCase(false, false, true, 0.0254f, false)]
    [TestCase(false, true, true, 0.001f, false)]
    [TestCase(false, false, false, 0.001f, true)]
    public void Units_AreTheFirstOneACadSystemGaveAndScaleNothing(bool modelFromCad, bool rootFromCad, bool partFromCad, float meters,
        bool toldNotMeasured)
    {
        var tree = PrcTestEntities.CreateTree([PrcTestEntities.CreatePart("Part", 1, writer => PrcTestEntities.WritePolyBrep(writer, "Body", 0, 0))],
        [
            PrcTestEntities.CreateOccurrence("Inch part", 0, [], unit: 25.4, unitFromCad: partFromCad),
            PrcTestEntities.CreateOccurrence("Assembly", -1, [0], unit: 1, unitFromCad: rootFromCad)
        ]);
        var bytes = PrcTestFile.CreateSingle(PrcTestEntities.CreateGlobals(Colors), tree, PrcTestEntities.CreateTessellations([CreateTriangleTessellation()]), 2,
            unit: 1, unitFromCad: modelFromCad).ToArray();

        var scene = Import(bytes, out var notes, out var remarks);

        var part = scene.Models.Dependencies.Single().Dependencies.Single();
        Assert.Multiple(() =>
        {
            Assert.That(scene.Units.Value, Is.EqualTo(meters).Within(1e-9f), "the first unit a CAD system gave, down the tree");
            Assert.That(part.Scale, Is.EqualTo(Vector3F.One), "a location scales between units itself; the import adds no scale");
            Assert.That(GetAllMeshes(scene).Single().Points.Select(point => point.As<float>()),
                Has.Some.Matches<Vector3F>(point => IsNear(point, ToEngine(new Vector3F(1, 0, 0)))), "the points as written");
            Assert.That(remarks.Any(note => note.Contains("not to be measured")), Is.EqualTo(toldNotMeasured));
            Assert.That(notes, Has.None.Contains("not to be measured"));
            Assert.That(notes.Concat(remarks), Has.None.Contains("unit other than"));
        });
    }

    [Test]
    public void EntityReferences_RestyleHideAndPlaceTheEntitiesUnderTheirOccurrence()
    {
        var globals = PrcTestEntities.CreateGlobals(Colors, coordinateSystemCount: 1,
            coordinateSystems: writer => PrcTestEntities.WriteCoordinateSystem(writer, PrcTestEntities.CreateCartesian([0, 0, 9])));
        var part = PrcTestEntities.CreatePart("Part", 3, writer =>
        {
            PrcTestEntities.WritePolyBrep(writer, "Body", 0, 0, uniqueId: 21);
            PrcTestEntities.WritePolyBrep(writer, "Spare", 0, 0, uniqueId: 22);
            PrcTestEntities.WritePolyBrep(writer, "Moved", 0, 0, uniqueId: 23);
        });
        var tree = PrcTestEntities.CreateTree([part],
        [
            PrcTestEntities.CreateOccurrence("Bracket", 0, []),
            PrcTestEntities.CreateOccurrence("Assembly", -1, [0], references:
            [
                PrcTestEntities.CreateEntityReference(PrcTypes.PolyBrepModel, 21, style: 1, behavior: 0x0009),
                PrcTestEntities.CreateEntityReference(PrcTypes.PolyBrepModel, 22, behavior: 0x2000),
                PrcTestEntities.CreateEntityReference(PrcTypes.PolyBrepModel, 23, coordinateSystem: 0),
                PrcTestEntities.CreateEntityReference(PrcTypes.PolyBrepModel, 99, style: 2)
            ])
        ]);
        var bytes = PrcTestFile.CreateSingle(globals, tree, PrcTestEntities.CreateTessellations([CreateTriangleTessellation()]), 2).ToArray();

        var scene = Import(bytes, out var notes, out var remarks);

        var bracket = scene.Models.Dependencies.Single().Dependencies.Single();
        var body = bracket.Dependencies.Single(model => model.Name == "Body");
        var moved = bracket.Dependencies.Single(model => model.Name == "Moved");
        string[] shown = ["Body", "Moved"];
        Assert.Multiple(() =>
        {
            Assert.That(bracket.Dependencies.Select(model => model.Name), Is.EqualTo(shown), "Spare is taken away");
            Assert.That(GetRgb(scene.Materials[body.Meshes.Single().MaterialID]), Is.EqualTo(new Vector3F(0, 0.5f, 1)).Using<Vector3F>(IsNear),
                "drawn by the reference's style, not its own");
            Assert.That(GetRgb(scene.Materials[moved.Meshes.Single().MaterialID]), Is.EqualTo(new Vector3F(1, 0, 0)).Using<Vector3F>(IsNear),
                "a reference of no style leaves the style");
            Assert.That(IsNear(moved.Position, ToEngine(new Vector3F(0, 0, 9))), $"Moved stands at {moved.Position}, by the reference's coordinate system");
            Assert.That(remarks, Has.Some.Contains("takes away or hides by reference").And.Contains("Spare"));
            Assert.That(remarks, Has.Some.Contains("1 redefinitions by reference of entities of type 237 meet no entity"));
            Assert.That(notes, Is.Empty, "what the file takes away or redefines in vain leaves nothing it shows out");
        });
    }

    [Test]
    public void DisplayFilters_TheActiveOneTakesAwayByLayerAndByEntity()
    {
        var part = PrcTestEntities.CreatePart("Part", 4, writer =>
        {
            PrcTestEntities.WritePolyBrep(writer, "Kept", 0, 0, uniqueId: 31, layer: 3);
            PrcTestEntities.WritePolyBrep(writer, "Off layer", 0, 0, uniqueId: 32, layer: 5);
            PrcTestEntities.WritePolyBrep(writer, "Named", 0, 0, uniqueId: 33, layer: 3);
            PrcTestEntities.WritePolyBrep(writer, "No layer", 0, 0, uniqueId: 34);
        });

        string[] GetShown(Action<PrcTestBitWriter> filter)
        {
            var tree = PrcTestEntities.CreateTree([part],
            [
                PrcTestEntities.CreateOccurrence("Bracket", 0, []),
                PrcTestEntities.CreateOccurrence("Assembly", -1, [0], displayFilters:
                [
                    PrcTestEntities.CreateFilter("Inactive", false, true, [5]),
                    filter
                ])
            ]);
            var bytes = PrcTestFile.CreateSingle(PrcTestEntities.CreateGlobals(Colors), tree, PrcTestEntities.CreateTessellations([CreateTriangleTessellation()]), 2)
                .ToArray();
            return Import(bytes, out _).Models.Dependencies.Single().Dependencies.Single().Dependencies.Select(model => model.Name).ToArray();
        }

        string[] byLayer = ["Kept", "No layer"];
        string[] byName = ["Kept"];
        string[] notLayer = ["Kept", "Named", "No layer"];
        string[] onLayer3 = ["Kept", "Named"];
        Assert.Multiple(() =>
        {
            Assert.That(GetShown(PrcTestEntities.CreateFilter("Layer 3", true, true, [3])), Is.EqualTo(onLayer3),
                "the occurrence holding them is on no layer, and a layer filter keeps containers for what is under them");
            Assert.That(GetShown(PrcTestEntities.CreateFilter("Layers", true, true, [3, 0xFFFF], false, [33])), Is.EqualTo(byLayer),
                "kept on layer 3 and on none, Named taken away by name; the inactive filter does nothing");
            Assert.That(GetShown(PrcTestEntities.CreateFilter("Only", true, false, [], true, [31])), Is.EqualTo(byName),
                "only the entity it names is kept");
            Assert.That(GetShown(PrcTestEntities.CreateFilter("Not layer 5", true, false, [5])), Is.EqualTo(notLayer));
        });
    }

    [Test]
    public void TextureApplication_LaysItsDiffusePictureOnTheTessellationsCoordinates()
    {
        var picture = PrcTestImages.CreatePng(4, 2, 200, 100, 50);
        var reflection = PrcTestImages.CreatePng(1, 1, 255, 255, 255);
        var globals = PrcTestEntities.CreateGlobals(Colors, pictures: PrcTestEntities.CreatePictures(2),
            textures: writer =>
            {
                writer.WriteUnsignedInteger(2);
                PrcTestEntities.WriteTextureDefinition(writer, 0, 0x0001, PrcTextureDefinition.FunctionReplace, PrcTextureDefinition.WrapRepeat,
                    PrcTextureDefinition.WrapClampToEdge, 2, [0, 1], [0.5, 0]);
                PrcTestEntities.WriteTextureDefinition(writer, 1, 0x0008, PrcTextureDefinition.FunctionModulate, PrcTextureDefinition.WrapRepeat,
                    PrcTextureDefinition.WrapRepeat);
            },
            materials: writer =>
            {
                writer.WriteUnsignedInteger(3);
                PrcTestEntities.WriteMaterial(writer, "Base", 0);
                PrcTestEntities.WriteTextureApplication(writer, "Textured", 0, 0, 2);
                PrcTestEntities.WriteTextureApplication(writer, "Reflection", 0, 1);
            },
            materialStyles: [PrcTestEntities.CreateMaterialStyle("Textured style", 1)]);
        var texturedStyle = Colors.Length;
        double[] points = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        var regular = PrcTestEntities.CreateTessellation3D(points, [0, 0, 1], [0, 0, 0, 0, 2, 3, 0, 4, 6],
            [new PrcTestFace { UsedEntities = PrcTessellationReader.TriangleTextured, Sizes = [1], TextureIndexes = 1 }], [0, 0, 1, 0, 0, 1]);
        var square = new PrcTestMesh();
        square.Points.AddRange([[0, 0, 0], [1, 0, 0], [1, 1, 0], [0, 1, 0]]);
        square.AddTriangle(0, 1, 2, 0);
        square.AddTriangle(0, 2, 3, 0);
        square.CornerNormals = [[[0, 0, 1], [0, 0, 1], [0, 0, 1]], [[0, 0, 1], [0, 0, 1], [0, 0, 1]]];
        square.CornerTextures = square.Triangles.Select(triangle => triangle.Select(float[] (point) => [(float)square.Points[point][0], (float)square.Points[point][1]]).ToArray()).ToList();
        var compressed = PrcTestCompressedTessellation.CreateEntityWriter(square, 0.01, true);
        var tree = PrcTestEntities.CreateTree([PrcTestEntities.CreatePart("Part", 2, writer =>
            {
                PrcTestEntities.WritePolyBrep(writer, "Decal", texturedStyle, 0);
                PrcTestEntities.WritePolyBrep(writer, "Packed", texturedStyle, 1);
            })],
            [PrcTestEntities.CreateOccurrence("Root", 0, [])]);
        var file = PrcTestFile.CreateSingle(globals, tree, PrcTestEntities.CreateTessellations([regular, compressed]), 1);
        file.Structures[0].UncompressedFiles.AddRange([picture, reflection]);

        var scene = Import(file.ToArray(), out var notes);

        var meshes = GetAllMeshes(scene).ToList();
        var material = scene.Materials[meshes[0].MaterialID];
        var texture = scene.Textures[material.BaseColorTexture.Texture];
        var sampler = scene.Samplers[texture.Sampler];
        Vector2F GetMoved(float textureS, float textureT) => new(0.5f - 2 * textureT, 1 - 2 * textureS);
        Vector2F[] decalCoordinates = [GetMoved(0, 0), GetMoved(1, 0), GetMoved(0, 1)];
        Assert.Multiple(() =>
        {
            Assert.That(notes, Has.Some.Contains("mapping attributes 0x8"), "the reflection level is told and left out");
            Assert.That(notes, Has.None.Contains("texture definitions").And.None.Contains("generic material"));
            Assert.That(meshes.Select(mesh => mesh.MaterialID).Distinct().Single(), Is.EqualTo(material.ID));
            Assert.That(material.DiffuseMap, Is.EqualTo(texture.Image));
            Assert.That(scene.Images[texture.Image].Data, Is.EqualTo(picture), "the picture's PNG, from its file structure's header");
            Assert.That(scene.Images[texture.Image].MimeType, Is.EqualTo("image/png"));
            Assert.That(PrcTestImages.Decode(scene.Images[texture.Image].Data).Width, Is.EqualTo(4u));
            Assert.That(scene.Images, Has.Count.EqualTo(1), "the reflection's picture is not kept");
            Assert.That(sampler.WrapS, Is.EqualTo(TextureWrap.Repeat));
            Assert.That(sampler.WrapT, Is.EqualTo(TextureWrap.ClampToEdge));
            Assert.That(GetRgb(material), Is.EqualTo(Vector3F.One), "the texture replaces the color");
            var decal = meshes.Single(mesh => mesh.Indices.Length == 3);
            Assert.That(Enumerable.Range(0, 3).Select(corner => decal.UV0[decal.Indices[corner]]),
                Is.EquivalentTo(decalCoordinates).Using<Vector2F>(IsNear),
                "scaled by 2, turned a quarter by its x axis alone and moved half along s - then v turned for the engine");
            var packed = meshes.Single(mesh => mesh.Indices.Length == 6);
            Assert.That(Enumerable.Range(0, packed.Points.Length).All(vertex =>
                    IsNear(packed.UV0[vertex], GetMoved(MathF.Round((float)packed.Points[vertex].X), MathF.Round((float)packed.Points[vertex].Z)))),
                Is.True, "the compressed tessellation's coordinates, each its corner's x and y, transformed alike");
        });
    }

    [Test]
    public void ViewsAndScenes_GiveCamerasNamedAfterThemAndLights()
    {
        var overview = PrcTestEntities.CreateScene("Lit",
            [PrcTestEntities.CreateDirectionalLight("Sun", 1, [0, 0, -1], 0.5), PrcTestEntities.CreatePointLight("Bulb", 2, [0, 0, 5], 0.1)],
            PrcTestEntities.CreateCamera("Overview camera", false, [0, -10, 0], [0, 0, 0], [0, 0, 1], Math.PI / 4), clippingPlane: true);
        var front = PrcTestEntities.CreateScene(null, [], PrcTestEntities.CreateCamera(null, true, [0, 0, 20], [0, 0, 0], [0, 1, 0], 30));
        var gone = PrcTestEntities.CreateScene(null, [], PrcTestEntities.CreateCamera(null, true, [0, 0, -20], [0, 0, 0], [0, 1, 0], 30));
        var tree = PrcTestEntities.CreateTree([PrcTestEntities.CreatePart("Part", 1, writer => PrcTestEntities.WritePolyBrep(writer, "Body", 0, 0))],
        [
            PrcTestEntities.CreateOccurrence("Assembly", 0, [], PrcTestEntities.CreateCartesian([10, 0, 0]),
                references: [PrcTestEntities.CreateEntityReference(PrcTypes.View, 42, behavior: 0x2000)],
                views: [PrcTestEntities.CreateView("Front", 41, front, true), PrcTestEntities.CreateView("Gone", 42, gone, false)],
                scenes: [overview])
        ]);
        var bytes = PrcTestFile.CreateSingle(PrcTestEntities.CreateGlobals(Colors), tree, PrcTestEntities.CreateTessellations([CreateTriangleTessellation()]), 1)
            .ToArray();

        var scene = Import(bytes, out var notes, out var remarks);

        var cameras = scene.CameraData.Values.ToList();
        var camera = cameras.Single(candidate => candidate.Name == "Lit");
        var frontCamera = cameras.Single(candidate => candidate.Name == "Front");
        var sun = scene.LightData.Values.Single(light => light.Name == "Sun");
        var bulb = scene.LightData.Values.Single(light => light.Name == "Bulb");
        string[] cameraNames = ["Lit", "Front"];
        Assert.Multiple(() =>
        {
            Assert.That(cameras.Select(candidate => candidate.Name), Is.EquivalentTo(cameraNames), "the view taken away by reference is not");
            Assert.That(IsNear(camera.Translation, ToEngine(new Vector3F(10, -10, 0))), $"the camera stands at {camera.Translation}, in its occurrence");
            Assert.That(IsNear(Vector3.Transform(Vector3F.UnitZ, camera.Rotation), ToEngine(Vector3F.UnitY)),
                "an engine camera looks along its +Z: at the point it looks at in the file");
            Assert.That(MathF.Abs(Vector3F.Dot(Vector3.Transform(Vector3F.UnitY, camera.Rotation), ToEngine(Vector3F.UnitZ))),
                Is.GreaterThan(0.999f), "its y axis is the file's up");
            Assert.That(camera.ProjectionType, Is.EqualTo(CameraProjectionType.Perspective));
            Assert.That(camera.Fov, Is.EqualTo(45).Within(1e-4f));
            Assert.That(camera.AspectRatio, Is.EqualTo(1.5f));
            Assert.That(frontCamera.ProjectionType, Is.EqualTo(CameraProjectionType.Orthogonal));
            Assert.That(frontCamera.YMag, Is.EqualTo(30));
            Assert.That(sun.LightType, Is.EqualTo(LightType.Directional));
            Assert.That(sun.Color, Is.EqualTo(new Vector3F(0, 0.5f, 1)).Using<Vector3F>(IsNear));
            Assert.That(sun.Intensity, Is.EqualTo(0.5f));
            Assert.That(IsNear(Vector3.Transform(Vector3F.UnitZ, sun.Rotation), ToEngine(-Vector3F.UnitZ)),
                "the sun shines down, along its +Z in the engine as a camera looks");
            Assert.That(bulb.LightType, Is.EqualTo(LightType.Point));
            Assert.That(IsNear(bulb.Position, ToEngine(new Vector3F(10, 0, 5))), $"the bulb hangs at {bulb.Position}");
            Assert.That(bulb.LinearAttenuation, Is.EqualTo(0.1f).Within(1e-6f));
            Assert.That(remarks, Has.Some.Contains("views of the file are kept as their cameras"));
            Assert.That(remarks, Has.Some.Contains("clipping planes"));
            Assert.That(notes, Has.None.Contains("views of the file").And.None.Contains("clipping planes"),
                "what views and scenes set is theirs, not the model's: nothing the model shows is missing");
            Assert.That(notes.Concat(remarks).Where(note => note.Contains("left after the end") || note.Contains("meet no entity")), Is.Empty);
        });
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
        var tessellation = PrcTestEntities.CreateTessellation3D(points, [0, 0, 1], [0, 0, 0, 3, 0, 6],
            [new PrcTestFace { UsedEntities = PrcTessellationReader.Triangle, Sizes = [1] }]);
        var tree = new PrcTestBitWriter();
        var part = PrcTestEntities.CreatePart("Part", 2, writer =>
            {
                writer.WriteUnsignedInteger(unknownItem);
                writer.WriteString("from the future");
                writer.WriteUnsignedInteger(2);
                double[] values = [1, 2, 3, 4, 5, 6];
                foreach (var value in values)
                {
                    writer.WriteDouble(value);
                }

                writer.WriteUnsignedInteger(PrcTypes.PolyBrepModel);
                PrcTestEntities.WriteBaseWithGraphics(writer, "Known", 0);
                writer.WriteInteger(-17);
                writer.WriteDouble(2.5);
                writer.WriteUnsignedInteger(0);
                writer.WriteUnsignedInteger(1);
                writer.WriteBoolean(true);
                PrcTestEntities.WriteUserData(writer);
            },
            beforeUserData: writer =>
            {
                writer.WriteUnsignedInteger(3);
                writer.WriteDouble(0.25);
                writer.WriteDouble(0.5);
                writer.WriteDouble(0.75);
                writer.WriteCharacter(42);
            });
        var file = PrcTestFile.CreateSingle(PrcTestEntities.CreateGlobals(Colors, schemas),
            PrcTestEntities.CreateTree([AddGraphicsExtension(part)], [AddGraphicsExtension(PrcTestEntities.CreateOccurrence("Root", 0, []))], tree),
            PrcTestEntities.CreateTessellations([tessellation]), 1);

        var scene = Import(file.ToArray(), out var notes);

        Assert.Multiple(() =>
        {
            Assert.That(GetAllMeshes(scene).Single().Indices, Has.Length.EqualTo(3), "the known item after the unknown one");
            Assert.That(notes, Has.Some.Contains("type 990"));
            Assert.That(notes.Where(note => note.Contains("left after the end")), Is.Empty);
        });
    }

    [Test]
    public void CompressedTessellation_IsDrawnAndTheSectionReadOn()
    {
        double[] points = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        var regular = PrcTestEntities.CreateTessellation3D(points, [0, 0, 1], [0, 0, 0, 3, 0, 6],
            [new PrcTestFace { UsedEntities = PrcTessellationReader.Triangle, Sizes = [1] }]);
        var square = new PrcTestMesh();
        square.Points.AddRange([[0, 0, 0], [1, 0, 0], [1, 1, 0], [0, 1, 0]]);
        square.AddTriangle(0, 1, 2, 0);
        square.AddTriangle(0, 2, 3, 0);
        square.CornerNormals = [[[0, 0, 1], [0, 0, 1], [0, 0, 1]], [[0, 0, 1], [0, 0, 1], [0, 0, 1]]];
        var compressed = PrcTestCompressedTessellation.CreateEntityWriter(square, 0.01, true);
        var bytes = CreatePartFile([regular, compressed, regular], writer =>
        {
            PrcTestEntities.WritePolyBrep(writer, "Drawn", 0, 0);
            PrcTestEntities.WritePolyBrep(writer, "Compressed", 0, 1);
            PrcTestEntities.WritePolyBrep(writer, "After", 0, 2);
        }, 3);

        var scene = Import(bytes, out var notes);

        int[] triangleCounts = [1, 2, 1];
        Assert.Multiple(() =>
        {
            Assert.That(GetAllMeshes(scene).Select(mesh => mesh.Indices.Length / 3), Is.EquivalentTo(triangleCounts));
            Assert.That(notes, Has.None.Contains("173").And.None.Contains("not read").And.None.Contains("left after the end"));
        });
    }

    [Test]
    public void Sets_NestTheirItems()
    {
        double[] points = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        var tessellation = PrcTestEntities.CreateTessellation3D(points, [0, 0, 1], [0, 0, 0, 3, 0, 6],
            [new PrcTestFace { UsedEntities = PrcTessellationReader.Triangle, Sizes = [1] }]);
        var bytes = CreatePartFile([tessellation], writer =>
        {
            PrcTestEntities.WriteSetStart(writer, "Outer", 2);
            PrcTestEntities.WriteSetStart(writer, "Inner", 1);
            PrcTestEntities.WritePolyBrep(writer, "Deep", 0, 0);
            PrcTestEntities.WriteSetEnd(writer);
            PrcTestEntities.WriteBrepModel(writer, "Exact", 0, 3);
            PrcTestEntities.WriteSetEnd(writer);
        }, 1);

        var scene = Import(bytes, out var notes);

        var outer = scene.Models.Dependencies.Single().Dependencies.Single();
        string[] children = ["Inner", "Exact"];
        Assert.Multiple(() =>
        {
            Assert.That(outer.Name, Is.EqualTo("Outer"));
            Assert.That(outer.Dependencies.Select(model => model.Name), Is.EqualTo(children));
            Assert.That(outer.Dependencies[0].Dependencies.Single().Meshes, Has.Count.EqualTo(1));
            Assert.That(notes, Has.Some.Contains("representation item Exact has exact geometry alone (a B-rep body, topological context 0 body 3)"));
        });
    }

    [Test, Timeout(120000)]
    public void CutShort_ThrowsInvalidDataAtEveryLength()
    {
        var bytes = CreateTriangleFile();
        for (var length = 0; length < bytes.Length; length++)
        {
            Assert.That(() => Import(bytes[..length], out _), Throws.TypeOf<InvalidDataException>(), $"cut at {length}");
        }
    }

    [Test, Timeout(120000)]
    public void SectionCutShort_ThrowsNamingTheSection()
    {
        var tree = CreateTreeOfTriangle();
        for (var length = 0; length < tree.Length - 1; length++)
        {
            var file = PrcTestFile.CreateSingle(PrcTestEntities.CreateGlobals(Colors), tree[..length],
                PrcTestEntities.CreateTessellations([CreateTriangleTessellation()]), 1);
            Assert.That(() => Import(file.ToArray(), out _),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("tree of file structure 0"), $"cut at {length}");
        }
    }

    [Test]
    public void CountPastTheData_Throws()
    {
        var writer = new PrcTestBitWriter();
        writer.WriteUnsignedInteger(PrcTypes.FileStructureTree);
        PrcTestEntities.WriteContentPrcBase(writer, null, false);
        writer.WriteUnsignedInteger(0x7FFFFFFF);
        var file = PrcTestFile.CreateSingle(PrcTestEntities.CreateGlobals(Colors), writer.ToArray(),
            PrcTestEntities.CreateTessellations([CreateTriangleTessellation()]), 1);

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
        var bytes = CreateTriangleFile();
        var converter = ModelConverterFactory.GetConverter("artwork.prc", bytes, new ConversionConfig(true));

        Assert.That(converter, Is.TypeOf<PrcConverter>());
        Assert.That(GetAllMeshes(converter.StartConversion()).Single().Indices, Has.Length.EqualTo(3));
        Assert.That(ModelConverterFactory.GetConverter(GetGeneratedPath("triangles.prc"), new ConversionConfig(true)), Is.TypeOf<PrcConverter>());
    }

    private static string GetGeneratedPath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "CompilerPrc", name);

    private static Action<PrcTestBitWriter> AddGraphicsExtension(Action<PrcTestBitWriter> entity) => writer =>
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

    private static Action<PrcTestBitWriter> CreateTriangleTessellation() =>
        PrcTestEntities.CreateTessellation3D([0, 0, 0, 1, 0, 0, 0, 1, 0], [0, 0, 1], [0, 0, 0, 3, 0, 6],
            [new PrcTestFace { UsedEntities = PrcTessellationReader.Triangle, Sizes = [1] }]);

    private static byte[] CreateTreeOfTriangle() =>
        PrcTestEntities.CreateTree([PrcTestEntities.CreatePart("Part", 1, writer => PrcTestEntities.WritePolyBrep(writer, "Triangle", 0, 0))],
            [PrcTestEntities.CreateOccurrence("Root", 0, [])]);

    private static byte[] CreateTriangleFile() =>
        PrcTestFile.CreateSingle(PrcTestEntities.CreateGlobals(Colors), CreateTreeOfTriangle(), PrcTestEntities.CreateTessellations([CreateTriangleTessellation()]), 1)
            .ToArray();

    private static byte[] CreatePartFile(IReadOnlyList<Action<PrcTestBitWriter>> tessellations, Action<PrcTestBitWriter> items, int itemCount,
        Action<PrcTestBitWriter> markups = null, byte[] globals = null)
    {
        var tree = PrcTestEntities.CreateTree([PrcTestEntities.CreatePart("Part", itemCount, items, markups)], [PrcTestEntities.CreateOccurrence("Root", 0, [])]);
        return PrcTestFile.CreateSingle(globals ?? PrcTestEntities.CreateGlobals(Colors), tree, PrcTestEntities.CreateTessellations(tessellations), 1).ToArray();
    }

    private static SceneData Import(byte[] bytes, out List<string> notes) => Import(bytes, out notes, out _);

    private static SceneData Import(byte[] bytes, out List<string> notes, out List<string> remarks)
    {
        var converter = new PrcConverter("test.prc", bytes, new ConversionConfig(true));
        var scene = converter.StartConversion();
        notes = converter.UnsupportedFeatures;
        remarks = converter.Remarks;
        return scene;
    }

    private static IEnumerable<Mesh> GetAllMeshes(SceneData scene) => TestScenes.GetAllMeshes(scene);

    private static int FindNearest(List<Vector3F> positions, Vector3F point)
    {
        var nearest = Enumerable.Range(0, positions.Count).OrderBy(index => (positions[index] - point).Length()).First();
        Assert.That((positions[nearest] - point).Length(), Is.LessThan(Tolerance), $"{point} is no vertex of the source");
        return nearest;
    }

    private static string GetKey(Vector3<double> point)
    {
        var source = FromEngine(point.As<float>());
        return $"{Math.Round(source.X)},{Math.Round(source.Y)},{Math.Round(source.Z)}";
    }

    private static string FormatSegment(int fromX, int fromY, int fromZ, int toX, int toY, int toZ) =>
        $"{fromX},{fromY},{fromZ}>{toX},{toY},{toZ}";

    private static Vector3F GetRgb(SceneData.Material material) =>
        new(material.DiffuseColor.X, material.DiffuseColor.Y, material.DiffuseColor.Z);

    private static Vector3F ToEngine(Vector3F prc) => new(prc.X, -prc.Z, prc.Y);

    private static Vector3F FromEngine(Vector3F engine) => new(engine.X, engine.Z, -engine.Y);

    private static bool IsNear(Vector3F expected, Vector3F actual) =>
        MathF.Abs(expected.X - actual.X) <= Tolerance && MathF.Abs(expected.Y - actual.Y) <= Tolerance &&
        MathF.Abs(expected.Z - actual.Z) <= Tolerance;

    private static bool IsNear(Vector2F expected, Vector2F actual) =>
        MathF.Abs(expected.X - actual.X) <= Tolerance && MathF.Abs(expected.Y - actual.Y) <= Tolerance;
}

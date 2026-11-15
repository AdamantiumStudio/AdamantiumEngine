using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Engine.Compiler.Converter.Configs;
using Adamantium.Engine.Compiler.Models.Converters;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Pdf.Document;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests;

/// <summary>The PRC artwork of real CAD PDFs - files that are not ours and never enter the repository - read from a
/// folder beside it, TestFiles/3d-pdf-samples, when the machine has one. Explicit: it runs only when asked for, so no
/// build depends on files it cannot have. Each artwork must convert without error; what it gives and what it leaves out
/// is written to the test output. Every section must be read to its end in step - no bits left after it, no compressed
/// tessellation storing more than its triangles use - and every mesh sane: finite points within a building's reach,
/// unit normals.</summary>
[TestFixture, Explicit("Runs over CAD PDFs outside the repository"), Category("RealPdf3D")]
public class PrcRealFileTests
{
    [Test]
    public void SamplePdfs_EveryPrcArtworkConverts()
    {
        var folder = FindSamples();
        if (folder == null)
        {
            Assert.Ignore("No TestFiles/3d-pdf-samples folder beside the repository");
        }

        var artworks = 0;
        foreach (var path in Directory.GetFiles(folder, "*.pdf").OrderBy(path => path, StringComparer.Ordinal))
        {
            var document = PdfDocument.Open(path);
            foreach (var annotation in document.Pages.SelectMany(page => page.Annotations))
            {
                var artwork = annotation.Artwork3D;
                if (artwork == null || !string.Equals(artwork.Format, "PRC", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                artworks++;
                var converter = new PrcConverter(Path.GetFileNameWithoutExtension(path) + ".prc", artwork.ReadData().ToArray(),
                    new ConversionConfig(true));
                var scene = converter.StartConversion();
                TestContext.Out.WriteLine($"{Path.GetFileName(path)}: {Describe(scene)}");
                foreach (var note in converter.UnsupportedFeatures)
                {
                    TestContext.Out.WriteLine("    " + note);
                }

                var name = Path.GetFileName(path);
                Assert.Multiple(() =>
                {
                    Assert.That(converter.UnsupportedFeatures, Has.None.Contains("left after the end"), name);
                    Assert.That(converter.UnsupportedFeatures, Has.None.Contains("compressed tessellation stores more"), name);
                    Assert.That(converter.UnsupportedFeatures, Has.None.Contains("compressed tessellation has fewer"), name);
                    CheckMeshes(scene, name);
                });
            }
        }

        Assert.That(artworks, Is.GreaterThan(0), "the folder holds no PRC artwork");
    }

    private static void CheckMeshes(SceneData scene, string name)
    {
        const double reach = 1e7;
        var pending = new Stack<SceneData.Model>();
        pending.Push(scene.Models);
        while (pending.Count > 0)
        {
            var model = pending.Pop();
            foreach (var mesh in model.Meshes)
            {
                var points = mesh.Points ?? [];
                Assert.That(points.All(point => double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z)), Is.True,
                    $"{name}: mesh {mesh.Name} has a point that is not finite");
                Assert.That(points.All(point => Math.Abs(point.X) < reach && Math.Abs(point.Y) < reach && Math.Abs(point.Z) < reach), Is.True,
                    $"{name}: mesh {mesh.Name} has a point past {reach}");
                var lengths = (mesh.Normals ?? []).Select(normal => Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z));
                Assert.That(lengths.All(length => Math.Abs(length - 1) < 1e-3 || length < 1e-6), Is.True,
                    $"{name}: mesh {mesh.Name} has a normal neither of unit length nor - of a triangle of no area - zero");
            }

            foreach (var child in model.Dependencies)
            {
                pending.Push(child);
            }
        }
    }

    private static string FindSamples()
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "TestFiles", "3d-pdf-samples");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string Describe(SceneData scene)
    {
        var nodes = 0;
        var meshes = 0;
        long triangles = 0;
        long lines = 0;
        var pending = new Stack<SceneData.Model>();
        pending.Push(scene.Models);
        while (pending.Count > 0)
        {
            var model = pending.Pop();
            nodes++;
            foreach (var mesh in model.Meshes)
            {
                meshes++;
                if (mesh.MeshTopology == PrimitiveType.TriangleList)
                {
                    triangles += mesh.Indices.Length / 3;
                }
                else if (mesh.MeshTopology == PrimitiveType.LineList)
                {
                    lines += mesh.Indices.Length / 2;
                }
            }

            foreach (var child in model.Dependencies)
            {
                pending.Push(child);
            }
        }

        return $"{nodes} nodes, {meshes} meshes, {triangles} triangles, {lines} lines, {scene.Materials.Count} materials";
    }
}

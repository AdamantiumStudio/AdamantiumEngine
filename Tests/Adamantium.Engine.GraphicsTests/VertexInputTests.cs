using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Adamantium.Graphics;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Graphics.Core.Models;
using Adamantium.Graphics.Core.Vertices;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>The vertex input a vertex structure stands for: its fields in declaration order, each at its place.</summary>
[TestFixture]
public class VertexInputTests
{
    [Test]
    public void TheInputs_AreTheFieldsInOrder_AtTheirPlaces_InTheirFormats()
    {
        var inputs = typeof(MeshVertex).GetVertexAttributeDescription2();
        string[] fields =
        [
            nameof(MeshVertex.Position), nameof(MeshVertex.Color), nameof(MeshVertex.Normal), nameof(MeshVertex.UV0),
            nameof(MeshVertex.UV1), nameof(MeshVertex.UV2), nameof(MeshVertex.UV3), nameof(MeshVertex.Tangent),
            nameof(MeshVertex.BiTangent)
        ];

        Assert.Multiple(() =>
        {
            Assert.That(inputs.Select(input => input.Location), Is.EqualTo(Enumerable.Range(0, 9).Select(i => (uint)i)));
            Assert.That(inputs.Select(input => input.Offset),
                Is.EqualTo(fields.Select(field => (uint)Marshal.OffsetOf<MeshVertex>(field))));
            Assert.That(inputs[0].Format, Is.EqualTo(Format.R32G32B32_SFLOAT));
            Assert.That(inputs[1].Format, Is.EqualTo(Format.R32G32B32A32_SFLOAT));
            Assert.That(inputs[3].Format, Is.EqualTo(Format.R32G32_SFLOAT));
        });
    }

    [Test]
    public void TheBinding_IsTheStructuresSize_PerInstanceWhenMarkedSo()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(MeshVertex).GetBindingDescription2().Stride, Is.EqualTo((uint)MeshVertex.Size));
            Assert.That(typeof(MeshVertex).GetBindingDescription2().InputRate, Is.EqualTo(VertexInputRate.Vertex));
            Assert.That(typeof(SpriteBatchItem).GetBindingDescription2().InputRate, Is.EqualTo(VertexInputRate.Instance));
        });
    }

    [Test]
    public void AUIVertex_IsAPointIn3D_AndSoIsAPicturesVertex()
    {
        var plain = typeof(UIVertex).GetVertexAttributeDescription2();
        var picture = typeof(UITexturedVertex).GetVertexAttributeDescription2();

        Assert.Multiple(() =>
        {
            Assert.That(plain.Single().Format, Is.EqualTo(Format.R32G32B32_SFLOAT));
            Assert.That(typeof(UIVertex).GetBindingDescription2().Stride, Is.EqualTo(12u));
            Assert.That(picture.Select(input => input.Format),
                Is.EqualTo(new[] { Format.R32G32B32_SFLOAT, Format.R32G32_SFLOAT }));
            Assert.That(typeof(UITexturedVertex).GetBindingDescription2().Stride, Is.EqualTo(20u));
        });
    }

    [Test]
    public void AMeshsDepth_ReachesTheUIVertices()
    {
        var mesh = new Mesh().SetPoints(new[] { new Vector3<double>(1, 2, 3), new Vector3<double>(4, 5, -6) });
        mesh.SetUVs(0, new[] { new Vector2F(0.25f, 0.5f), new Vector2F(1, 0) });

        var plain = mesh.ToUIVertices();
        var picture = mesh.ToUITexturedVertices();

        Assert.Multiple(() =>
        {
            Assert.That(plain.Select(v => v.Position.Z), Is.EqualTo(new[] { 3f, -6f }));
            Assert.That(picture.Select(v => v.Position.Z), Is.EqualTo(new[] { 3f, -6f }));
            Assert.That(picture[0].UV, Is.EqualTo(new Vector2F(0.25f, 0.5f)));
        });
    }

    [Test]
    public void ManyThreadsAskingAtOnce_GetTheSameInputs()
    {
        var answers = new VertexInputAttributeDescription2EXT[16][];

        Parallel.For(0, answers.Length, i => answers[i] = typeof(SkinnedMeshVertex).GetVertexAttributeDescription2());

        Assert.That(answers.Distinct().Count(), Is.EqualTo(1));
    }
}

using System.Collections.Generic;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>A material's opacity is its diffuse color's alpha, said as a number of its own; whether the alpha and the
/// cutoff count follows its alpha mode, and a view hears of each change.</summary>
[TestFixture]
public class MaterialAlphaTests
{
    [Test]
    public void Opacity_IsTheDiffuseAlpha_KeepingTheColor_HeldFromZeroToOne()
    {
        var material = new Material { DiffuseColor = new Vector4F(0.2f, 0.4f, 0.6f, 1) };

        material.Opacity = 0.3f;
        var set = material.DiffuseColor;
        material.Opacity = 2;
        var above = material.Opacity;
        material.Opacity = float.NaN;

        Assert.Multiple(() =>
        {
            Assert.That(set, Is.EqualTo(new Vector4F(0.2f, 0.4f, 0.6f, 0.3f)));
            Assert.That(above, Is.EqualTo(1f));
            Assert.That(material.Opacity, Is.EqualTo(1f), "no number is no opacity: left as it was");
        });
    }

    [Test]
    public void AViewHears_OfOpacityWithTheColor_AndOfWhatCountsWithTheMode()
    {
        var material = new Material();
        var heard = new List<string>();
        material.PropertyChanged += (_, change) => heard.Add(change.PropertyName);

        material.DiffuseColor = new Vector4F(1, 0, 0, 0.5f);
        material.AlphaMode = AlphaMode.Mask;

        Assert.That(heard, Is.SupersetOf(new[] { nameof(Material.Opacity), nameof(Material.UsesAlpha),
            nameof(Material.IsMasked) }));
        Assert.That((material.UsesAlpha, material.IsMasked), Is.EqualTo((true, true)));
    }

    [Test]
    public void ACopy_TakesTheColor_NotTheOpacityApart()
    {
        var material = new Material { DiffuseColor = new Vector4F(1, 0, 0, 0.25f) };
        var copy = new Material();

        material.CloneValues(copy);

        Assert.That(copy.DiffuseColor, Is.EqualTo(new Vector4F(1, 0, 0, 0.25f)));
    }
}

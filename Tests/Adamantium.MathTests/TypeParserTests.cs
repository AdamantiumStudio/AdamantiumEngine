using Adamantium.Core.TypeParsing;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests;

/// <summary>Markup values of the maths types are read by the parsers their types name, as the UI's types are.</summary>
[TestFixture]
public class TypeParserTests
{
    [Test]
    public void AColor_IsReadByItsName()
    {
        Assert.That(TypeParser.Parse<Color>("Red"), Is.EqualTo(Colors.Red));
    }

    [Test]
    public void AThickness_IsReadFromItsSides()
    {
        Assert.That(TypeParser.Parse<Thickness>("1,2"), Is.EqualTo(new Thickness(1, 2)));
    }

    [TestCase("3 4")]
    [TestCase("3,4")]
    public void AVector2_IsReadFromItsComponents(string text)
    {
        Assert.That(TypeParser.Parse<Vector2>(text), Is.EqualTo(new Vector2(3, 4)));
    }

    [Test]
    public void ANullableOne_IsReadAsItsType_OrAsNull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TypeParser.Parse<Color?>("Red"), Is.EqualTo(Colors.Red));
            Assert.That(TypeParser.Parse<Thickness?>("null"), Is.Null);
        });
    }
}

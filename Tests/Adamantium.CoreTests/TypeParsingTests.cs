using System;
using System.Threading;
using Adamantium.Core.Collections;
using Adamantium.Core.TypeParsing;
using NUnit.Framework;

namespace Adamantium.CoreTests;

[TestFixture]
public class TypeParsingTests
{
    // The AUML codegen routes a collection set from a STRING attribute (e.g. StrokeDashArray="36,24") through
    // TypeParser.Parse<TrackingCollection<double>>; DoubleCollectionParser must turn that string into a populated
    // collection (the bug was an empty/null collection -> the dash array carried no data).
    [Test]
    public void ATypeWithItsOwnParse_NeedsNoParser()
    {
        Assert.That(TypeParser.Parse<SelfParsingValue>("21").Value, Is.EqualTo(42));
    }

    // A parser that returns a value cannot be bound as a delegate; finding that out must not throw, or a debugger stops
    // on every type parsed this way.
    [Test]
    public void AParserOfAValue_ParsesWithoutThrowing()
    {
        var thrown = 0;
        EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> count = (_, e) =>
        {
            if (e.Exception is ArgumentException) Interlocked.Increment(ref thrown);
        };
        AppDomain.CurrentDomain.FirstChanceException += count;
        int value;
        try
        {
            value = TypeParser.Parse<ParsedValue>("7").Value;
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= count;
        }

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo(7));
            Assert.That(thrown, Is.Zero);
        });
    }

    [TypeParser(typeof(ParsedValueParser))]
    private struct ParsedValue
    {
        public int Value;
    }

    private sealed class ParsedValueParser : ITypeParser<ParsedValue>
    {
        public ParsedValue Parse(string value) => new() { Value = int.Parse(value) };
    }

    [Test]
    public void DoubleCollection_ParsesCommaList()
    {
        var result = TypeParser.Parse<TrackingCollection<double>>("36,24");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result[0], Is.EqualTo(36.0));
        Assert.That(result[1], Is.EqualTo(24.0));
    }

    [Test]
    public void DoubleCollection_ParsesSpaceAndCommaSeparators()
    {
        var result = TypeParser.Parse<TrackingCollection<double>>("10 6, 2  6");

        Assert.That(result.Count, Is.EqualTo(4));
        Assert.That(result[0], Is.EqualTo(10.0));
        Assert.That(result[3], Is.EqualTo(6.0));
    }

    [Test]
    public void DoubleCollection_EmptyString_GivesEmptyCollection()
    {
        var result = TypeParser.Parse<TrackingCollection<double>>("");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(0));
    }
}

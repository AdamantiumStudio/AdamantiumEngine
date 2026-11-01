using System;
using Adamantium.Wayland;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>
/// A handler that throws while libwayland calls it: the exception cannot cross the native frame, so its connection keeps
/// it and the dispatching call of that connection rethrows it - once, and never from another connection's dispatch.
/// Needs Linux and a compositor; elsewhere the test is inconclusive.
/// </summary>
[TestFixture]
public class WaylandDispatchTests
{
    [Test]
    public void AHandlerThatThrows_IsRethrownByTheDispatchOfItsOwnConnection_Once()
    {
        Assume.That(OperatingSystem.IsLinux(), "Wayland is Linux's");
        using var display = WaylandDisplay.TryConnect();
        Assume.That(display, Is.Not.Null, "no Wayland compositor to connect to");
        using var other = WaylandDisplay.TryConnect();
        Assume.That(other, Is.Not.Null, "no second connection to the compositor");
        var registry = display.GetRegistry();
        registry.Global += (_, _, _) => throw new InvalidOperationException("from the handler");
        var otherRegistry = other.GetRegistry();
        var otherGlobals = 0;
        otherRegistry.Global += (_, _, _) => otherGlobals++;

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => display.Roundtrip());
            Assert.DoesNotThrow(() => other.Roundtrip(), "the other connection's dispatch");
            Assert.That(otherGlobals, Is.GreaterThan(0));
            Assert.DoesNotThrow(() => display.Roundtrip(), "rethrown once");
        });
    }
}

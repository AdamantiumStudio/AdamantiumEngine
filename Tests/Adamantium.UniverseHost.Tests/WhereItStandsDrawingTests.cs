using Adamantium.Multiverse;
using Adamantium.Multiverse.Remote;
using NUnit.Framework;

namespace Adamantium.UniverseHost.Tests;

// A debugger holds the drawing's thread only once none of its frames is in the method it is told by name: renamed or
// moved, the debugger would hold the thread in the middle of a drawing, and the game's own drawing would wait on it.
[TestFixture]
public class WhereItStandsDrawingTests
{
    [Test]
    public void TheDrawing_IsInTheMethodADebuggerIsToldOf()
    {
        var method = typeof(Universe).GetMethod(nameof(Universe.DrawWhereItStands));

        Assert.That(method!.DeclaringType!.FullName + "." + method.Name, Is.EqualTo(UniverseHostThreads.DrawingWorksIn));
    }
}

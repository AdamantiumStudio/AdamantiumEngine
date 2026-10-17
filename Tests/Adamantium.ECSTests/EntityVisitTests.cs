using System.Collections.Generic;
using Adamantium.ECS;
using NUnit.Framework;

namespace Adamantium.ECSTests;

[TestFixture]
public class EntityVisitTests
{
    [Test]
    public void VisitInDepth_GoesUnderAnEntity_OnlyWhenTheVisitSaysSo()
    {
        var root = new Entity(null, "Root");
        var shut = new Entity(root, "Shut");
        new Entity(shut, "UnderShut");
        var open = new Entity(root, "Open");
        new Entity(open, "UnderOpen");
        var visited = new List<string>();

        root.VisitInDepth(entity =>
        {
            visited.Add(entity.Name);
            return entity != shut;
        });

        Assert.That(visited, Is.EquivalentTo(new[] { "Root", "Shut", "Open", "UnderOpen" }));
    }

    [Test]
    public void VisitInDepth_ARootThatSaysNo_IsAllThatIsVisited()
    {
        var root = new Entity(null, "Root");
        new Entity(root, "Child");
        var visited = 0;

        root.VisitInDepth(_ =>
        {
            visited++;
            return false;
        });

        Assert.That(visited, Is.EqualTo(1));
    }
}

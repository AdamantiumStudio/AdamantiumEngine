using Adamantium.Pulsar.Mixing;
using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

[TestFixture]
public class HighestRankedTests
{
    [Test]
    public void WhatIsLeft_AreTheHighest_WhateverTheOrder()
    {
        var random = new Random(1);
        for (var round = 0; round < 500; round++)
        {
            var ranks = Enumerable.Range(0, random.Next(1, 60)).Select(_ => (long)random.Next(-20, 20)).ToArray();
            var room = random.Next(1, 30);
            var highest = new HighestRanked(room);
            var kept = Enumerable.Range(0, ranks.Length).ToHashSet();
            for (var slot = 0; slot < ranks.Length; slot++)
            {
                var dropped = highest.Add(ranks[slot], slot);
                Assert.That(dropped < 0 || kept.Remove(dropped), Is.True, "what is dropped was kept till then");
            }

            Assert.That(highest.Count, Is.EqualTo(Math.Min(room, ranks.Length)));
            Assert.That(kept.Select(slot => ranks[slot]).Order(), Is.EqualTo(ranks.OrderDescending().Take(room).Order()));
        }
    }
}

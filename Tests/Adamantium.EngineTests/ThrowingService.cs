using System;
using Adamantium.ECS;

namespace Adamantium.EngineTests;

/// <summary>A game service of the tests' code that fails on every frame.</summary>
public sealed class ThrowingService : GameService
{
    public override void Update() => throw new InvalidOperationException("A game service's own mistake");
}

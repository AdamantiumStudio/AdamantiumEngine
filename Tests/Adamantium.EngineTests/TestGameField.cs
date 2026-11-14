using System;

namespace Adamantium.EngineTests;

/// <summary>A field of a component <see cref="TestGameCode"/> builds: its name and type.</summary>
internal readonly record struct TestGameField(string Name, Type Type);

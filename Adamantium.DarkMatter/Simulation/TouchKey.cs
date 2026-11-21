namespace Adamantium.DarkMatter.Simulation;

/// <summary>Which two parts of which two bodies a touch is of, told the same way at its beginning and its end.</summary>
internal readonly record struct TouchKey(BodyId First, BodyId Second, int FirstPart, int SecondPart);

namespace Adamantium.Core.Compression;

/// <summary>A codeword of a prefix code: <paramref name="Length"/> bits of <paramref name="Code"/>, the first of them its
/// highest, standing for <paramref name="Symbol"/>.</summary>
/// <param name="Symbol">0 or more, less than 2^24.</param>
public readonly record struct PrefixCodeWord(uint Code, int Length, int Symbol);

namespace Adamantium.Core.Compression;

/// <summary>A codeword of a prefix code: <paramref name="Length"/> bits of <paramref name="Code"/>, the first of them its
/// highest, standing for <paramref name="Symbol"/>.</summary>
/// <param name="Code">The codeword's bits, in the low <paramref name="Length"/> bits.</param>
/// <param name="Length">How many bits the codeword has.</param>
/// <param name="Symbol">0 or more, less than 2^24.</param>
public readonly record struct PrefixCodeWord(uint Code, int Length, int Symbol);

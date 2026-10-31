using System;

namespace Adamantium.Graphics.Core;

/// <summary>New pixels for one rectangle of one layer and mip level of a texture, tightly packed rows in the texture's
/// own format.</summary>
public readonly record struct TextureRegionUpdate(
    uint Layer,
    uint Level,
    int X,
    int Y,
    uint Width,
    uint Height,
    ReadOnlyMemory<byte> Pixels);

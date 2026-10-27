using System;

namespace Adamantium.Graphics.Core;

/// <summary>The shaders of effect passes: one per logical device, owned by <see cref="MainGraphicsDevice"/>. A pass's
/// shaders are made once for its data and shared by every effect of that data on any of the render devices.</summary>
public interface IEffectPassShaderCache : IDisposable
{
    /// <summary>How many passes have their shaders made.</summary>
    int Count { get; }
}

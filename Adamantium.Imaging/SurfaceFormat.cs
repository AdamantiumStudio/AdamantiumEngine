using System;
using System.Runtime.InteropServices;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging;

/// <summary>A pixel format: a Vulkan <see cref="Format"/>, to and from which it converts, with its size. The formats
/// pictures are most often in have names of their own here, <c>SurfaceFormat.R8G8B8A8.UNorm</c> and the like; any
/// other is its <see cref="Format"/>.</summary>
[StructLayout(LayoutKind.Sequential, Size = 4)]
public readonly struct SurfaceFormat : IEquatable<SurfaceFormat>
{
    /// <summary>The Vulkan format.</summary>
    public readonly Format Value;

    private SurfaceFormat(Format format)
    {
        Value = format;
    }

    /// <summary>No format.</summary>
    public static readonly SurfaceFormat Undefined = new(Format.UNDEFINED);

    /// <summary>Bits a pixel takes - a block's for a compressed format.</summary>
    public int SizeInBits => FormatHelper.SizeOfInBits(Value);

    /// <summary>Bytes a pixel takes - a block's for a compressed format.</summary>
    public int SizeInBytes => FormatHelper.SizeOfInBytes(Value);

    /// <summary>Whether the format has a stencil part.</summary>
    public bool HasStencilFormat() =>
        Value is Format.D16_UNORM_S8_UINT or Format.D24_UNORM_S8_UINT or Format.D32_SFLOAT_S8_UINT or Format.S8_UINT;

    /// <summary>Gray of 8 bits.</summary>
    public static class R8
    {
        /// <summary><see cref="Format.R8_UNORM"/>.</summary>
        public static readonly SurfaceFormat UNorm = new(Format.R8_UNORM);
    }

    /// <summary>RGB of 8 bits a channel.</summary>
    public static class R8G8B8
    {
        /// <summary><see cref="Format.R8G8B8_UNORM"/>.</summary>
        public static readonly SurfaceFormat UNorm = new(Format.R8G8B8_UNORM);
    }

    /// <summary>RGBA of 8 bits a channel.</summary>
    public static class R8G8B8A8
    {
        /// <summary><see cref="Format.R8G8B8A8_UNORM"/>.</summary>
        public static readonly SurfaceFormat UNorm = new(Format.R8G8B8A8_UNORM);
    }

    /// <summary>BGRA of 8 bits a channel.</summary>
    public static class B8G8R8A8
    {
        /// <summary><see cref="Format.B8G8R8A8_UNORM"/>.</summary>
        public static readonly SurfaceFormat UNorm = new(Format.B8G8R8A8_UNORM);
    }

    /// <summary>RGBA of 16 bits a channel.</summary>
    public static class R16G16B16A16
    {
        /// <summary><see cref="Format.R16G16B16A16_UNORM"/>.</summary>
        public static readonly SurfaceFormat UNorm = new(Format.R16G16B16A16_UNORM);
    }

    public static implicit operator Format(SurfaceFormat format) => format.Value;

    public static implicit operator SurfaceFormat(Format format) => new(format);

    public bool Equals(SurfaceFormat other) => Value == other.Value;

    public override bool Equals(object obj) => obj is SurfaceFormat other && Equals(other);

    public override int GetHashCode() => (int)Value;

    public static bool operator ==(SurfaceFormat left, SurfaceFormat right) => left.Value == right.Value;

    public static bool operator !=(SurfaceFormat left, SurfaceFormat right) => left.Value != right.Value;

    public override string ToString() => Value.ToString();
}

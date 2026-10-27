using System;
using Adamantium.Core;
using Adamantium.EffectsCompiler;

namespace Adamantium.Graphics.Core.EffectsFramework;

/// <summary>The values of one constant buffer of an effect as the CPU writes them: a block the size the shaders declare,
/// rounded up to 16 bytes, all zero to begin with. The passes that declare the buffer alike share it and upload it as it
/// stands when they are applied.</summary>
public sealed class EffectConstantBuffer : DisposableObject
{
    public EffectConstantBuffer(EffectData.ConstantBuffer description)
    {
        Description = description;
        Name = description.Name;
        BackingBuffer = new DataBuffer(Utilities.AlignSize((uint)description.Size, 16));
        BackingBuffer.Clear();
    }

    /// <summary>The declaration the buffer was made from.</summary>
    public EffectData.ConstantBuffer Description { get; }

    /// <summary>The bytes of the buffer.</summary>
    public DataBuffer BackingBuffer { get; }

    /// <summary>Writes this buffer's bytes over <paramref name="other"/>'s, a buffer of the same size.</summary>
    public void CopyTo(EffectConstantBuffer other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.BackingBuffer.Size != BackingBuffer.Size)
        {
            throw new ArgumentException($"{other.Name} is {other.BackingBuffer.Size} bytes, {Name} {BackingBuffer.Size}.",
                nameof(other));
        }

        Utilities.CopyMemory(other.BackingBuffer.DataPointer, BackingBuffer.DataPointer, (long)BackingBuffer.Size);
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        BackingBuffer.Dispose();
        base.Dispose(disposeManagedResources);
    }
}

namespace Adamantium.Graphics.Core;

/// <summary>How shaders reach the resources they are given.</summary>
public enum DescriptorModel
{
    /// <summary>The descriptor heap where the device has it, descriptor sets where it does not.</summary>
    Auto,

    /// <summary>One descriptor heap (<c>VK_EXT_descriptor_heap</c>) every resource has a slot in.</summary>
    Heap,

    /// <summary>Descriptor sets: each draw's own resources pushed with it, textures of no fixed count in one bindless
    /// set.</summary>
    Sets
}

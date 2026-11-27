using Adamantium.Core;
using Adamantium.Graphics.Core;

namespace Adamantium.Graphics
{
    public partial class Buffer
    {
        public static class Uniform
        {
            private const MemoryPropertyFlags MemoryFlags = MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent;

            /// <summary>
            /// Creates a new uniform buffer in <see cref="MemoryPropertyFlags.HostVisible"/> and
            /// <see cref="MemoryPropertyFlags.HostCoherent"/> memory.
            /// </summary>
            /// <param name="device">The <see cref="GraphicsDevice"/>.</param>
            /// <param name="size">The size in bytes.</param>
            /// <returns>A uniform buffer</returns>
            public static Buffer New(IGraphicsDevice device, ulong size)
            {
                return Buffer.New(device, size, BufferUsageFlags.UniformBuffer, MemoryFlags);
            }
            
            public static Buffer New(IGraphicsDevice device, ulong size, BufferUsageFlags usageFlags)
            {
                return Buffer.New(device, size, usageFlags | BufferUsageFlags.UniformBuffer, MemoryFlags);
            }

            /// <summary>
            /// Creates a new uniform buffer in <see cref="MemoryPropertyFlags.HostVisible"/> and
            /// <see cref="MemoryPropertyFlags.HostCoherent"/> memory by default.
            /// </summary>
            /// <typeparam name="T">Type of the uniform buffer to get the sizeof from</typeparam>
            /// <param name="device">The <see cref="GraphicsDevice"/>.</param>
            /// <param name="value">The value to initialize the uniform buffer.</param>
            /// <param name="memoryFlags">The memoryFlags of this resource.</param>
            /// <returns>A uniform buffer</returns>
            public static Buffer<T> New<T>(IGraphicsDevice device, ref T value, MemoryPropertyFlags memoryFlags = MemoryFlags) where T : struct
            {
                return Buffer.New(device, ref value, BufferUsageFlags.UniformBuffer, memoryFlags);
            }

            /// <summary>
            /// Creates a new uniform buffer in <see cref="MemoryPropertyFlags.HostVisible"/> and
            /// <see cref="MemoryPropertyFlags.HostCoherent"/> memory by default.
            /// </summary>
            /// <typeparam name="T">Type of the uniform buffer to get the sizeof from</typeparam>
            /// <param name="device">The <see cref="GraphicsDevice"/>.</param>
            /// <param name="value">The value to initialize the uniform buffer.</param>
            /// <param name="memoryFlags">The memoryFlags of this resource.</param>
            /// <returns>A uniform buffer</returns>
            public static Buffer<T> New<T>(IGraphicsDevice device, T[] value, MemoryPropertyFlags memoryFlags = MemoryFlags) where T : struct
            {
                return Buffer.New(device, value, BufferUsageFlags.UniformBuffer, memoryFlags);
            }

            /// <summary>
            /// Creates a new uniform buffer in <see cref="MemoryPropertyFlags.HostVisible"/> and
            /// <see cref="MemoryPropertyFlags.HostCoherent"/> memory by default.
            /// </summary>
            /// <param name="device">The <see cref="GraphicsDevice"/>.</param>
            /// <param name="value">The value to initialize the uniform buffer.</param>
            /// <param name="is32BitIndex">The element size the buffer is made with: 4 bytes when true, 2 when false.</param>
            /// <param name="memoryFlags">The memoryFlags of this resource.</param>
            /// <returns>A uniform buffer</returns>
            public static Buffer New(IGraphicsDevice device, byte[] value, bool is32BitIndex, MemoryPropertyFlags memoryFlags = MemoryFlags)
            {
                return Buffer.New(device, value, is32BitIndex ? 4 : 2, BufferUsageFlags.UniformBuffer, memoryFlags);
            }

            /// <summary>
            /// Creates a new uniform buffer in <see cref="MemoryPropertyFlags.HostVisible"/> and
            /// <see cref="MemoryPropertyFlags.HostCoherent"/> memory by default.
            /// </summary>
            /// <param name="device">The <see cref="GraphicsDevice"/>.</param>
            /// <param name="value">The value to initialize the uniform buffer.</param>
            /// <param name="memoryFlags">The memoryFlags of this resource.</param>
            /// <returns>A uniform buffer</returns>
            public static Buffer New(IGraphicsDevice device, DataPointer value, MemoryPropertyFlags memoryFlags = MemoryFlags)
            {
                return Buffer.New(device, value, BufferUsageFlags.UniformBuffer, memoryFlags);
            }
        }
    }
}

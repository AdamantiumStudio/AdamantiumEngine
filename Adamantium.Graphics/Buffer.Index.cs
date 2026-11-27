using Adamantium.Core;
using Adamantium.Graphics.Core;

namespace Adamantium.Graphics
{
    public partial class Buffer
    {
        /// <summary>
        /// Index buffer helper methods.
        /// </summary>
        public static class Index
        {
            private static BufferUsageFlags BufferUsage = BufferUsageFlags.TransferDst | BufferUsageFlags.IndexBuffer;

            /// <summary>
            /// Creates a new index buffer with <see cref="MemoryPropertyFlags.DeviceLocal"/> memoryFlags by default.
            /// </summary>
            /// <param name="device">The <see cref="GraphicsDevice"/>.</param>
            /// <param name="size">The size in bytes.</param>
            /// <param name="memoryFlags">The memoryFlags.</param>
            /// <returns>A index buffer</returns>
            public static Adamantium.Graphics.Buffer New(IGraphicsDevice device, ulong size, MemoryPropertyFlags memoryFlags = MemoryPropertyFlags.DeviceLocal)
            {
                return Adamantium.Graphics.Buffer.New(device, size, BufferUsage, memoryFlags);
            }

            /// <summary>
            /// Creates a new index buffer with <see cref="MemoryPropertyFlags.DeviceLocal"/> memoryFlags by default.
            /// </summary>
            /// <typeparam name="T">Type of the index buffer to get the sizeof from</typeparam>
            /// <param name="device">The <see cref="GraphicsDevice"/>.</param>
            /// <param name="indexCount">Number of indices.</param>
            /// <param name="memoryFlags">The memoryFlags.</param>
            /// <returns>A index buffer</returns>
            public static Buffer<T> New<T>(IGraphicsDevice device, uint indexCount, MemoryPropertyFlags memoryFlags = MemoryPropertyFlags.DeviceLocal) where T : struct
            {
                return Adamantium.Graphics.Buffer.New<T>(device, indexCount, BufferUsage, memoryFlags);
            }

            /// <summary>
            /// Creates a new index buffer with <see cref="MemoryPropertyFlags.DeviceLocal"/> memoryFlags by default.
            /// </summary>
            /// <typeparam name="T">Type of the index buffer to get the sizeof from</typeparam>
            /// <param name="device">The <see cref="GraphicsDevice"/>.</param>
            /// <param name="value">The value to initialize the index buffer.</param>
            /// <param name="memoryFlags">The memoryFlags of this resource.</param>
            /// <returns>A index buffer</returns>
            public static Buffer<T> New<T>(IGraphicsDevice device, ref T value, MemoryPropertyFlags memoryFlags = MemoryPropertyFlags.DeviceLocal) where T : struct
            {
                return Adamantium.Graphics.Buffer.New(device, ref value, BufferUsage, memoryFlags);
            }

            /// <summary>
            /// Creates a new index buffer with <see cref="MemoryPropertyFlags.DeviceLocal"/> memoryFlags by default.
            /// </summary>
            /// <typeparam name="T">Type of the index buffer to get the sizeof from</typeparam>
            /// <param name="device">The <see cref="GraphicsDevice"/>.</param>
            /// <param name="value">The value to initialize the index buffer.</param>
            /// <param name="memoryFlags">The memoryFlags of this resource.</param>
            /// <returns>A index buffer</returns>
            public static Buffer<T> New<T>(IGraphicsDevice device, T[] value, MemoryPropertyFlags memoryFlags = MemoryPropertyFlags.DeviceLocal) where T : struct
            {
                return Adamantium.Graphics.Buffer.New(device, value, BufferUsage, memoryFlags);
            }

            /// <summary>
            /// Creates a new index buffer with <see cref="MemoryPropertyFlags.DeviceLocal"/> memoryFlags by default.
            /// </summary>
            /// <param name="device">The <see cref="GraphicsDevice"/>.</param>
            /// <param name="value">The value to initialize the index buffer.</param>
            /// <param name="is32BitIndex">Set to true if the buffer is using a 32 bit index or false for 16 bit index.</param>
            /// <param name="memoryFlags">The memoryFlags of this resource.</param>
            /// <returns>A index buffer</returns>
            public static Adamantium.Graphics.Buffer New(IGraphicsDevice device, byte[] value, bool is32BitIndex, MemoryPropertyFlags memoryFlags = MemoryPropertyFlags.DeviceLocal)
            {
                return Adamantium.Graphics.Buffer.New(device, value, is32BitIndex ? 4 : 2, BufferUsage, memoryFlags);
            }

            /// <summary>
            /// Creates a new index buffer with <see cref="MemoryPropertyFlags.DeviceLocal"/> memoryFlags by default.
            /// </summary>
            /// <param name="device">The <see cref="GraphicsDevice"/>.</param>
            /// <param name="value">The value to initialize the index buffer.</param>
            /// <param name="memoryFlags">The memoryFlags of this resource.</param>
            /// <returns>A index buffer</returns>
            public static Adamantium.Graphics.Buffer New(IGraphicsDevice device, DataPointer value, MemoryPropertyFlags memoryFlags = MemoryPropertyFlags.DeviceLocal)
            {
                return Adamantium.Graphics.Buffer.New(device, value, BufferUsage, memoryFlags);
            }
        }
    }
}

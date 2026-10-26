using System;
using Adamantium.Core;
using Adamantium.Graphics.Core;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics
{
    /// <summary>
    /// A buffer with typed information.
    /// </summary>
    /// <typeparam name="T">Type of an element of this buffer.</typeparam>
    public class Buffer<T> : Buffer where T : struct
    {
        internal Buffer(IGraphicsDevice device, BufferUsageFlags bufferFlags, DataPointer dataPointer,
            MemoryPropertyFlags memoryFlags, SharingMode sharingMode = SharingMode.Exclusive)
            : base(device, bufferFlags, dataPointer, memoryFlags, sharingMode)
        {
        }

        internal Buffer(IGraphicsDevice device, BufferCreateInfo bufferInfo, ulong count,
            MemoryPropertyFlags memoryFlags)
            : base(device, bufferInfo, count, memoryFlags)
        {
        }

        /// <summary>
        /// Gets the content of this texture to an array of data.
        /// </summary>
        /// <returns>An array of data.</returns>
        /// <remarks>This method is only working when called from the main thread that is accessing the main <see cref="GraphicsDevice" />.
        /// This method creates internally a staging resource if this texture is not already a staging resource, copies to it and map it to memory. Use method with explicit staging resource
        /// for optimal performances.</remarks>
        public T[] GetData()
        {
            return GetData<T>();
        }

        /// <summary>
        /// Copies the content of a single structure data from CPU memory to this buffer into GPU memory.
        /// </summary>
        /// <param name="fromData">The data to copy from.</param>
        /// <param name="offsetInBytes">The offset in bytes to write to.</param>
        /// <param name="options">Buffer data behavior.</param>
        /// <exception cref="System.ArgumentException"></exception>
        /// <remarks>
        /// This method is only working when called from the main thread that is accessing the main <see cref="GraphicsDevice"/>.
        /// </remarks>
        public void SetData(ref T fromData, uint offsetInBytes = 0)
        {
            base.SetData(ref fromData, offsetInBytes);
        }

        /// <summary>
        /// Copies the content an array of data from CPU memory to this buffer into GPU memory.
        /// </summary>
        /// <param name="fromData">The data to copy from.</param>
        /// <param name="startIndex">Index to start copying from.</param>
        /// <param name="elementCount">Number of elements to copy.</param>
        /// <param name="offsetInBytes">The offset in bytes to write to.</param>
        /// <param name="options">Buffer data behavior.</param>
        /// <remarks>
        /// This method is only working when called from the main thread that is accessing the main <see cref="GraphicsDevice"/>.
        /// </remarks>
        public void SetData(T[] fromData, uint startIndex = 0, uint elementCount = 0, uint offsetInBytes = 0)
        {
            base.SetData(fromData, startIndex, elementCount, offsetInBytes);
        }

        /// <summary>
        /// Copies a span of elements from CPU memory to this buffer into GPU memory, pinning the span in place
        /// (no intermediate array/copy). Handy for uploading an exact sub-range, e.g. <c>array.AsSpan(0, count)</c>.
        /// </summary>
        /// <param name="fromData">The data to copy from.</param>
        /// <param name="offsetInBytes">The offset in bytes to write to.</param>
        public void SetData(ReadOnlySpan<T> fromData, uint offsetInBytes = 0)
        {
            base.SetData(fromData, offsetInBytes);
        }
    }
}
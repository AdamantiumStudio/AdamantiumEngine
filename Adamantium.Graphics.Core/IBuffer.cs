using System;
using Adamantium.Core;
using Adamantium.Vulkan.Core;
using Buffer = Adamantium.Vulkan.Core.Buffer;

namespace Adamantium.Graphics.Core;

public interface IBuffer: IDisposable
{
    BufferUsageFlags Usage { get; }
    SharingMode SharingMode { get; }
    MemoryPropertyFlags MemoryFlags { get; }
    uint ElementSize { get; }
    ulong ElementCount { get; }
    ulong TotalSize { get; }

    /// <summary>
    /// Gets the name of this component.
    /// </summary>
    /// <value>The name.</value>
    string Name { get; set; }

    /// <summary>
    /// Gets or sets the tag associated to this object.
    /// </summary>
    /// <value>The tag.</value>
    object Tag { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the name of this instance is immutable.
    /// </summary>
    /// <value><c>true</c> if this instance is name immutable; otherwise, <c>false</c>.</value>
    bool IsNameImmutable { get; set; }

    bool HasName { get; }

    /// <summary>
    /// Gets a value indicating whether this instance is disposed.
    /// </summary>
    /// <value>
    /// <c>true</c> if this instance is disposed; otherwise, <c>false</c>.
    /// </value>
    bool IsDisposed { get; }

    /// <summary>
    /// <see cref="GraphicsDevice"/>
    /// </summary>
    IGraphicsDevice GraphicsDevice { get; }

    void CopyFrom(DataBuffer buffer, ulong offset = 0);

    /// <summary>
    /// Gets the content of this buffer to an array of data.
    /// </summary>
    /// <typeparam name="TData">The type of the T data.</typeparam>
    /// <remarks>
    /// This method is only working when called from the main thread that is accessing the main <see cref="GraphicsDevice"/>.
    /// This method creates internally a staging resource if this texture is not already a staging resource, copies to it and map it to memory. Use method with explicit staging resource
    /// for optimal performances.</remarks>
    /// <msdn-id>ff476457</msdn-id>	
    /// <unmanaged>HRESULT ID3D11DeviceContext::Map([In] ID3D11Resource* pResource,[In] unsigned int Subresource,[In] D3D11_MAP MapType,[In] D3D11_MAP_FLAG MapFlags,[Out] D3D11_MAPPED_SUBRESOURCE* pMappedResource)</unmanaged>	
    /// <unmanaged-short>ID3D11DeviceContext::Map</unmanaged-short>	
    TData[] GetData<TData>() where TData : struct;

    /// <summary>
    /// Copies the content of this buffer from GPU memory to an array of data on CPU memory using a specific staging resource.
    /// </summary>
    /// <typeparam name="TData">The type of the T data.</typeparam>
    /// <param name="toData">To data.</param>
    /// <exception cref="System.ArgumentException">When strides is different from optimal strides, and TData is not the same size as the pixel format, or Width * Height != toData.Length</exception>
    /// <remarks>
    /// This method is only working when called from the main thread that is accessing the main <see cref="GraphicsDevice"/>.
    /// </remarks>
    /// <msdn-id>ff476457</msdn-id>	
    /// <unmanaged>HRESULT ID3D11DeviceContext::Map([In] ID3D11Resource* pResource,[In] unsigned int Subresource,[In] D3D11_MAP MapType,[In] D3D11_MAP_FLAG MapFlags,[Out] D3D11_MAPPED_SUBRESOURCE* pMappedResource)</unmanaged>	
    /// <unmanaged-short>ID3D11DeviceContext::Map</unmanaged-short>	
    void GetData<TData>(ref TData toData) where TData : struct;

    /// <summary>
    /// Copies the content of this buffer from GPU memory to an array of data on CPU memory using a specific staging resource.
    /// </summary>
    /// <typeparam name="TData">The type of the T data.</typeparam>
    /// <param name="toData">To data.</param>
    /// <exception cref="System.ArgumentException">When strides is different from optimal strides, and TData is not the same size as the pixel format, or Width * Height != toData.Length</exception>
    /// <remarks>
    /// This method is only working when called from the main thread that is accessing the main <see cref="GraphicsDevice"/>.
    /// </remarks>
    /// <msdn-id>ff476457</msdn-id>	
    /// <unmanaged>HRESULT ID3D11DeviceContext::Map([In] ID3D11Resource* pResource,[In] unsigned int Subresource,[In] D3D11_MAP MapType,[In] D3D11_MAP_FLAG MapFlags,[Out] D3D11_MAPPED_SUBRESOURCE* pMappedResource)</unmanaged>	
    /// <unmanaged-short>ID3D11DeviceContext::Map</unmanaged-short>	
    void GetData<TData>(TData[] toData) where TData : struct;

    /// <summary>
    /// Copies the content of this buffer from GPU memory to a CPU memory using a specific staging resource.
    /// </summary>
    /// <param name="toData">To data pointer.</param>
    /// <exception cref="System.ArgumentException">When strides is different from optimal strides, and TData is not the same size as the pixel format, or Width * Height != toData.Length</exception>
    /// <remarks>
    /// This method is only working when called from the main thread that is accessing the main <see cref="GraphicsDevice"/>.
    /// </remarks>
    /// <msdn-id>ff476457</msdn-id>	
    /// <unmanaged>HRESULT ID3D11DeviceContext::Map([In] ID3D11Resource* pResource,[In] unsigned int Subresource,[In] D3D11_MAP MapType,[In] D3D11_MAP_FLAG MapFlags,[Out] D3D11_MAPPED_SUBRESOURCE* pMappedResource)</unmanaged>	
    /// <unmanaged-short>ID3D11DeviceContext::Map</unmanaged-short>	
    void GetData(DataPointer toData);

    nuint MapMemory();
    void UnmapMemory();

    /// <summary>
    /// Copies the content an array of data on CPU memory to this buffer into GPU memory.
    /// </summary>
    /// <typeparam name="TData">The type of the T data.</typeparam>
    /// <param name="fromData">The data to copy from.</param>
    /// <param name="offsetInBytes">The offset in bytes to write to.</param>
    /// <exception cref="System.ArgumentException"></exception>
    /// <remarks>
    /// See the unmanaged documentation about Map/UnMap for usage and restrictions.
    /// </remarks>
    /// <msdn-id>ff476457</msdn-id>
    /// <unmanaged>HRESULT ID3D11DeviceContext::Map([In] ID3D11Resource* pResource,[In] unsigned int Subresource,[In] D3D11_MAP MapType,[In] D3D11_MAP_FLAG MapFlags,[Out] D3D11_MAPPED_SUBRESOURCE* pMappedResource)</unmanaged>
    /// <unmanaged-short>ID3D11DeviceContext::Map</unmanaged-short>
    void SetData<TData>(ref TData fromData, uint offsetInBytes = 0) where TData : struct;

    /// <summary>
    /// Copies the content an array of data on CPU memory to this buffer into GPU memory.
    /// </summary>
    /// <typeparam name="TData">The type of the T data.</typeparam>
    /// <param name="fromData">The data to copy from.</param>
    /// <param name="startIndex">The starting index to begin setting data from.</param>
    /// <param name="elementCount">The number of elements to set.</param>
    /// <param name="offsetInBytes">The offset in bytes to write to.</param>
    /// <exception cref="System.ArgumentException"></exception>
    /// <remarks>
    /// See the unmanaged documentation about Map/UnMap for usage and restrictions.
    /// </remarks>
    /// <msdn-id>ff476457</msdn-id>
    /// <unmanaged>HRESULT ID3D11DeviceContext::Map([In] ID3D11Resource* pResource,[In] unsigned int Subresource,[In] D3D11_MAP MapType,[In] D3D11_MAP_FLAG MapFlags,[Out] D3D11_MAPPED_SUBRESOURCE* pMappedResource)</unmanaged>
    /// <unmanaged-short>ID3D11DeviceContext::Map</unmanaged-short>
    unsafe void SetData<TData>(TData[] fromData, uint startIndex = 0, uint elementCount = 0, uint offsetInBytes = 0) where TData : struct;

    /// <summary>
    /// Copies the content an array of data on CPU memory to this buffer into GPU memory.
    /// </summary>
    /// <param name="fromData">A data pointer.</param>
    /// <param name="offsetInBytes">The offset in bytes to write to.</param>
    /// <exception cref="System.ArgumentException"></exception>
    /// <msdn-id>ff476457</msdn-id>
    ///   <unmanaged>HRESULT ID3D11DeviceContext::Map([In] ID3D11Resource* pResource,[In] unsigned int Subresource,[In] D3D11_MAP MapType,[In] D3D11_MAP_FLAG MapFlags,[Out] D3D11_MAPPED_SUBRESOURCE* pMappedResource)</unmanaged>
    ///   <unmanaged-short>ID3D11DeviceContext::Map</unmanaged-short>
    /// <remarks>
    /// See the unmanaged documentation about Map/UnMap for usage and restrictions.
    /// </remarks>
    void SetData(DataPointer fromData, ulong offsetInBytes = 0);

    UInt64 GetDeviceAddress();

    Buffer GetBuffer();

    /// <summary>
    /// Occurs when Dispose is called.
    /// </summary>
    event EventHandler<EventArgs> Disposing;
}
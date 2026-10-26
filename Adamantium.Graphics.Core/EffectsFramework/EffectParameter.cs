using Adamantium.Core;
using Adamantium.EffectsCompiler;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.EffectsFramework;

/// <summary>
/// Contains information about effect parameter
/// </summary>
public sealed class EffectParameter : NamedObject
{
    private IEffectResourceLinker resourceLinker;
    private GetMatrixDelegate GetMatrixImpl;
    private CopyMatrixDelegate CopyMatrix;
    private int matrixSize;
    private int offset;

    /// <summary>
    /// Initializes a new instance of the <see cref="EffectParameter"/> class.
    /// </summary>
    public EffectParameter(EffectData.ValueTypeParameter parameterDescription, EffectConstantBuffer buffer)
        : base(parameterDescription.Name)
    {
        BindValue(parameterDescription, buffer);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EffectParameter"/> class.
    /// </summary>
    public EffectParameter(EffectData.ResourceParameter parameterDescription, EffectResourceType resourceType,
        int offset, IEffectResourceLinker resourceLinker)
        : base(parameterDescription.Name)
    {
        BindResource(parameterDescription, resourceType, offset, resourceLinker);
    }

    /// <summary>The declaration this parameter was built from.</summary>
    public EffectData.Parameter ParameterDescription { get; private set; }

    /// <summary>The constant buffer a value parameter lives in; null for a resource.</summary>
    public EffectConstantBuffer Buffer { get; private set; }

    internal void BindValue(EffectData.ValueTypeParameter parameterDescription, EffectConstantBuffer buffer)
    {
        ParameterDescription = parameterDescription;
        Buffer = buffer;
        resourceLinker = null;
        CopyMatrix = null;
        GetMatrixImpl = null;
        matrixSize = 0;

        ResourceType = EffectResourceType.None;
        IsValueType = true;
        ParameterClass = parameterDescription.Class;
        ParameterType = parameterDescription.Type;
        RowCount = parameterDescription.RowCount;
        ColumnCount = parameterDescription.ColumnCount;
        ElementCount = parameterDescription.Count;
        Offset = (int) parameterDescription.Offset;
        Size = parameterDescription.Size;

        // If the expected matrix is column_major, or its size differs from Matrix4x4F, ours has to be remapped into it.
        if (ParameterClass is EffectParameterClass.MatrixRows or EffectParameterClass.MatrixColumns)
        {
            matrixSize = (ParameterClass == EffectParameterClass.MatrixColumns ? ColumnCount : RowCount) * 4 *
                         sizeof(float);

            if (ParameterClass == EffectParameterClass.MatrixRows)
            {
                CopyMatrix = CopyMatrixColumnMajor;
                GetMatrixImpl = GetMatrixColumnMajorFrom;
            }
            else
            {
                CopyMatrix = CopyMatrixDirect;
                GetMatrixImpl = GetMatrixDirectFrom;
            }
               
        }
    }

    internal void BindResource(EffectData.ResourceParameter parameterDescription, EffectResourceType resourceType,
        int offset, IEffectResourceLinker resourceLinker)
    {
        ParameterDescription = parameterDescription;
        Buffer = null;
        this.resourceLinker = resourceLinker;
        CopyMatrix = null;
        GetMatrixImpl = null;
        matrixSize = 0;

        ResourceType = resourceType;
        IsValueType = false;
        ParameterClass = parameterDescription.Class;
        ParameterType = parameterDescription.Type;
        RowCount = ColumnCount = 0;
        ElementCount = parameterDescription.Count;
        Offset = offset;
        SlotIndex = parameterDescription.Slot;
        DescriptorSet = parameterDescription.DescriptorSet;
    }

    /// <summary>
    /// A unique index of this parameter instance inside the <see cref="EffectParameterCollection"/> of an effect. See remarks.
    /// </summary>
    /// <remarks>
    /// This unique index can be used between different instance of the effect with different deferred <see cref="GraphicsDevice"/>.
    /// </remarks>
    public int Index { get; internal set; }

    /// <summary>
    /// Gets the parameter class.
    /// </summary>
    /// <value>The parameter class.</value>
    public EffectParameterClass ParameterClass { get; private set; }

    /// <summary>
    /// Gets the resource type.
    /// </summary>
    public EffectResourceType ResourceType { get; private set; }

    /// <summary>
    /// Gets the type of the parameter.
    /// </summary>
    /// <value>The type of the parameter.</value>
    public EffectParameterType ParameterType { get; private set; }

    /// <summary>
    /// Gets a boolean indicating if this parameter is a value type (true) or a resource type (false).
    /// </summary>
    public bool IsValueType { get; private set; }

    /// <summary>
    /// Number of rows in a matrix. Otherwise a numeric type returns 1, any other type returns 0.
    /// </summary>
    public int RowCount { get; private set; }

    /// <summary>
    /// Number of columns in a matrix. Otherwise a numeric type returns 1, any other type returns 0.
    /// </summary>
    public int ColumnCount { get; private set; }

    /// <summary>
    /// Gets the collection of effect parameters.
    /// </summary>
    public uint ElementCount { get; private set; }

    /// <summary>
    /// Size in bytes of the element, only valid for value types.
    /// </summary>
    public int Size { get; private set; }

    /// <summary>
    /// Offset of this parameter.
    /// </summary>
    /// <remarks>
    /// For a value type, this offset is the offset in bytes inside the constant buffer.
    /// For a resource type, this offset is an index to the resource linker.
    /// </remarks>
    public int Offset
    {
        get => offset;
        internal set => offset = value;
    }

    /// <summary>
    /// Index of the resource to be set inside <see cref="CommonShaderStage"/>
    /// </summary>
    public int SlotIndex { get; internal set; }
        
    public uint DescriptorSet { get; internal set; }

    /// <summary>
    /// Gets a single value to the associated parameter in the constant buffer.
    /// </summary>
    /// <typeparam name="T">The type of the value to read from the buffer.</typeparam>
    /// <returns>The value of this parameter.</returns>
    public T GetValue<T>() where T : struct
    {
        return Buffer.BackingBuffer.Get<T>(offset);
    }

    /// <summary>
    /// Gets a single value to the associated parameter in the constant buffer.
    /// </summary>
    /// <typeparam name="T">The type of the value to read from the buffer.</typeparam>
    /// <param name="index">The index of the value (for value array).</param>
    /// <returns>The value of this parameter.</returns>
    public T GetValue<T>(int index) where T : struct
    {
        AlignedToFloat4<T>(out var size);
        return Buffer.BackingBuffer.Get<T>(offset + index * size);
    }

    /// <summary>
    /// Gets an array of values to the associated parameter in the constant buffer.
    /// </summary>
    /// <typeparam name = "T">The type of the value to read from the buffer.</typeparam>
    /// <returns>The value of this parameter.</returns>
    public T[] GetValueArray<T>(int count) where T : struct
    {
        if (AlignedToFloat4<T>(out var size))
        {
            var values = new T[count];
            int localOffset = offset;
            for (int i = 0; i < values.Length; i++, localOffset += size)
            {
                Buffer.BackingBuffer.Get(localOffset, out values[i]);
            }

            return values;
        }

        return Buffer.BackingBuffer.GetRange<T>(offset, count);
    }

    /// <summary>
    /// Gets a single value to the associated parameter in the constant buffer.
    /// </summary>
    /// <returns>The value of this parameter.</returns>
    public Matrix4x4F GetMatrix()
    {
        return GetMatrixImpl(offset);
    }

    /// <summary>
    /// Gets a single value to the associated parameter in the constant buffer.
    /// </summary>
    /// <returns>The value of this parameter.</returns>
    public Matrix4x4F GetMatrix(int startIndex)
    {
        return GetMatrixImpl(offset + (startIndex * matrixSize));
    }

    /// <summary>
    /// Gets an array of matrices to the associated parameter in the constant buffer.
    /// </summary>
    /// <param name="count">The count.</param>
    /// <returns>Matrix4x4F[][].</returns>
    /// <returns>The value of this parameter.</returns>
    public Matrix4x4F[] GetMatrixArray(int count)
    {
        return GetMatrixArray(0, count);
    }

    /// <summary>
    /// Gets an array of matrices to the associated parameter in the constant buffer.
    /// </summary>
    /// <returns>The value of this parameter.</returns>
    public unsafe Matrix4x4F[] GetMatrixArray(int startIndex, int count)
    {
        var result = new Matrix4x4F[count];
        var localOffset = offset + (startIndex * matrixSize);
        // Fix the whole buffer
        fixed (Matrix4x4F* pMatrix = result)
        {
            for (int i = 0; i < result.Length; i++, localOffset += matrixSize)
                pMatrix[i] = GetMatrixImpl(localOffset);
        }

        return result;
    }

    /// <summary>
    /// Sets a single value to the associated parameter in the constant buffer.
    /// </summary>
    /// <typeparam name = "T">The type of the value to be written to the buffer.</typeparam>
    /// <param name = "value">The value to write to the buffer.</param>
    public void SetValue<T>(ref T value) where T : struct
    {
        Buffer.BackingBuffer.Set(offset, ref value);
    }

    /// <summary>
    /// Sets a single value to the associated parameter in the constant buffer.
    /// </summary>
    /// <typeparam name = "T">The type of the value to be written to the buffer.</typeparam>
    /// <param name = "value">The value to write to the buffer.</param>
    public void SetValue<T>(T value) where T : struct
    {
        Buffer.BackingBuffer.Set(offset, value);
    }

    /// <summary>
    /// Sets a single matrix value to the associated parameter in the constant buffer.
    /// </summary>
    /// <param name = "value">The matrix to write to the buffer.</param>
    public void SetValue(ref Matrix4x4F value)
    {
        CopyMatrix(ref value, offset);
    }

    /// <summary>
    /// Sets a single matrix value to the associated parameter in the constant buffer.
    /// </summary>
    /// <param name = "value">The matrix to write to the buffer.</param>
    public void SetValue(Matrix4x4F value)
    {
        CopyMatrix(ref value, offset);
    }

    /// <summary>
    /// Sets an array of matrices to the associated parameter in the constant buffer.
    /// </summary>
    /// <param name = "values">An array of matrices to be written to the current buffer.</param>
    public unsafe void SetValue(Matrix4x4F[] values)
    {
        // A parameter the reflection gave no matrix layout for has nothing to remap into and no element stride to step
        // by. Its elements sit one matrix apart, which is exactly what the direct copy below writes for a single one -
        // better than dying on the null delegate, which is what an array of matrices used to do.
        if (CopyMatrix == null)
        {
            Buffer.BackingBuffer.Set(offset, values);
            return;
        }

        var localOffset = offset;
        // Fix the whole buffer
        fixed (Matrix4x4F* pMatrix = values)
        {
            for (int i = 0; i < values.Length; i++, localOffset += matrixSize)
                CopyMatrix(ref pMatrix[i], localOffset);
        }
    }

    /// <summary>
    /// Sets a single matrix at the specified index for the associated parameter in the constant buffer.
    /// </summary>
    /// <param name="index">Index of the matrix to write in element count.</param>
    /// <param name = "value">The matrix to write to the buffer.</param>
    public void SetValue(int index, Matrix4x4F value)
    {
        CopyMatrix(ref value, offset + index * matrixSize);
    }

    /// <summary>
    /// Sets an array of matrices to at the specified index for the associated parameter in the constant buffer.
    /// </summary>
    /// <param name="index">Index of the matrix to write in element count.</param>
    /// <param name = "values">An array of matrices to be written to the current buffer.</param>
    public unsafe void SetValue(int index, Matrix4x4F[] values)
    {
        var localOffset = this.offset + (index * matrixSize);
        // Fix the whole buffer
        fixed (Matrix4x4F* pMatrix = values)
        {
            for (int i = 0; i < values.Length; i++, localOffset += matrixSize)
                CopyMatrix(ref pMatrix[i], localOffset);
        }
    }

    /// <summary>
    /// Sets an array of raw values to the associated parameter in the constant buffer.
    /// </summary>
    /// <param name = "values">An array of values to be written to the current buffer.</param>
    public void SetRawValue(byte[] values)
    {
        Buffer.BackingBuffer.Set(offset, values);
    }

    /// <summary>
    /// Sets an array of values to the associated parameter in the constant buffer.
    /// </summary>
    /// <typeparam name = "T">The type of the value to be written to the buffer.</typeparam>
    /// <param name = "values">An array of values to be written to the current buffer.</param>
    public void SetValue<T>(T[] values) where T : struct
    {
        if (AlignedToFloat4<T>(out var size))
        {
            int localOffset = offset;
            for (int i = 0; i < values.Length; i++, localOffset += size)
            {
                Buffer.BackingBuffer.Set(localOffset, ref values[i]);
            }
        }
        else
        {
            Buffer.BackingBuffer.Set(offset, values);
        }
    }

    /// <summary>
    /// Sets a single value at the specified index for the associated parameter in the constant buffer.
    /// </summary>
    /// <typeparam name = "T">The type of the value to be written to the buffer.</typeparam>
    /// <param name="index">Index of the value to write in typeof(T) element count.</param>
    /// <param name = "value">The value to write to the buffer.</param>
    public void SetValue<T>(int index, ref T value) where T : struct
    {
        AlignedToFloat4<T>(out var size);
        Buffer.BackingBuffer.Set(offset + size * index, ref value);
    }

    /// <summary>
    /// Sets a single value at the specified index for the associated parameter in the constant buffer.
    /// </summary>
    /// <typeparam name = "T">The type of the value to be written to the buffer.</typeparam>
    /// <param name="index">Index of the value to write in typeof(T) element count.</param>
    /// <param name = "value">The value to write to the buffer.</param>
    public void SetValue<T>(int index, T value) where T : struct
    {
        AlignedToFloat4<T>(out var size);
        Buffer.BackingBuffer.Set(offset + size * index, ref value);
    }

    /// <summary>
    /// Sets an array of values to at the specified index for the associated parameter in the constant buffer.
    /// </summary>
    /// <typeparam name = "T">The type of the value to be written to the buffer.</typeparam>
    /// <param name="index">Index of the value to write in typeof(T) element count.</param>
    /// <param name = "values">An array of values to be written to the current buffer.</param>
    public void SetValue<T>(int index, T[] values) where T : struct
    {
        if (AlignedToFloat4<T>(out var size))
        {
            int localOffset = offset + size * index;
            for (int i = 0; i < values.Length; i++, localOffset += size)
            {
                Buffer.BackingBuffer.Set(localOffset, ref values[i]);
            }
        }
        else
        {
            Buffer.BackingBuffer.Set(offset + size * index, values);
        }
    }

    /// <summary>
    /// Gets the resource view set for this parameter.
    /// </summary>
    /// <typeparam name = "T">The type of the resource view.</typeparam>
    /// <returns>The resource view.</returns>
    public T GetResource<T>() where T : class
    {
        return resourceLinker.GetResource<T>((EffectData.ResourceParameter) ParameterDescription);
    }

    /// <summary>
    /// Sets a shader resource for the associated parameter.
    /// </summary>
    /// <typeparam name = "T">The type of the resource view.</typeparam>
    /// <param name="resourceView">The resource view.</param>
    public void SetResource<T>(T resourceView) where T : class
    {
        resourceLinker.SetResource((EffectData.ResourceParameter) ParameterDescription, ResourceType, resourceView);
    }

    /// <summary>
    /// Sets a an array of shader resource views for the associated parameter.
    /// </summary>
    /// <typeparam name = "T">The type of the resource view.</typeparam>
    /// <param name="resourceViewArray">The resource view array.</param>
    public void SetResource<T>(params T[] resourceViewArray) where T : class
    {
        resourceLinker.SetResource((EffectData.ResourceParameter) ParameterDescription, ResourceType,
            resourceViewArray);
    }

    /// <summary>
    /// Sets a shader resource at the specified index for the associated parameter.
    /// </summary>
    /// <typeparam name = "T">The type of the resource view.</typeparam>
    /// <param name="index">Index to start to set the resource views</param>
    /// <param name="resourceView">The resource view.</param>
    public void SetResource<T>(int index, T resourceView) where T : class
    {
        resourceLinker.SetResource((EffectData.ResourceParameter) ParameterDescription, ResourceType, resourceView);
    }

    /// <summary>
    /// Sets a an array of shader resource views at the specified index for the associated parameter.
    /// </summary>
    /// <typeparam name = "T">The type of the resource view.</typeparam>
    /// <param name="index">Index to start to set the resource views</param>
    /// <param name="resourceViewArray">The resource view array.</param>
    public void SetResource<T>(int index, params T[] resourceViewArray) where T : class
    {
        resourceLinker.SetResource((EffectData.ResourceParameter) ParameterDescription, ResourceType,
            resourceViewArray);
    }

    internal void SetDefaultValue()
    {
        if (IsValueType)
        {
            var defaultValue = ((EffectData.ValueTypeParameter)ParameterDescription).DefaultValue;
            if (defaultValue != null)
            {
                SetRawValue(defaultValue);
            }
        }
    }

    /// <summary>
    /// Returns a string that represents the current object.
    /// </summary>
    /// <returns>
    /// A string that represents the current object.
    /// </returns>
    /// <filterpriority>2</filterpriority>
    public override string ToString()
    {
        return
            $"[{Index}] {Name} Class: {ParameterClass}, Resource: {ResourceType}, Type: {ParameterType}, IsValue: {IsValueType}, RowCount: {RowCount}, ColumnCount: {ColumnCount}, ElementCount: {ElementCount} Offset: {Offset}";
    }

    /// <summary>
    /// CopyMatrix delegate used to reorder matrix when copying from <see cref="Matrix4x4F"/>.
    /// </summary>
    /// <param name="matrix">The source matrix.</param>
    /// <param name="offset">The offset in bytes to write to</param>
    private delegate void CopyMatrixDelegate(ref Matrix4x4F matrix, int offset);

    /// <summary>
    /// Copy matrix in row major order.
    /// </summary>
    /// <param name="matrix">The source matrix.</param>
    /// <param name="offset">The offset in bytes to write to</param>
    private unsafe void CopyMatrixRowMajor(ref Matrix4x4F matrix, int offset)
    {
        var pDest = (float*) ((byte*) Buffer.BackingBuffer.DataPointer + offset);
        fixed (void* pMatrix = &matrix)
        {
            var pSrc = (float*) pMatrix;
            // If Matrix4x4F is row_major but expecting less columns/rows
            // then copy only necessary columns/rows.
            for (int i = 0; i < RowCount; i++, pSrc += 4, pDest += 4)
            {
                for (int j = 0; j < ColumnCount; j++)
                    pDest[j] = pSrc[j];
            }
        }
    }

    /// <summary>
    /// Copy matrix in column major order.
    /// </summary>
    /// <param name="matrix">The source matrix.</param>
    /// <param name="offset">The offset in bytes to write to</param>
    private unsafe void CopyMatrixColumnMajor(ref Matrix4x4F matrix, int offset)
    {
        var pDest = (float*) ((byte*) Buffer.BackingBuffer.DataPointer + offset);
        fixed (void* pMatrix = &matrix)
        {
            var pSrc = (float*) pMatrix;
            // If Matrix4x4F is column_major, then we need to transpose it
            for (int i = 0; i < ColumnCount; i++, pSrc++, pDest += 4)
            {
                for (int j = 0; j < RowCount; j++)
                    pDest[j] = pSrc[j * 4];
            }
        }
    }

    private static bool AlignedToFloat4<T>(out int size) where T : struct
    {
        size = Utilities.SizeOf<T>();
        var requireAlign = (size & 0xF) != 0;
        if (requireAlign) size = ((size >> 4) + 1) << 4;
        return requireAlign;
    }

    /// <summary>
    /// Straight Matrix4x4F copy, no conversion.
    /// </summary>
    /// <param name="matrix">The source matrix.</param>
    /// <param name="offset">The offset in bytes to write to</param>
    private void CopyMatrixDirect(ref Matrix4x4F matrix, int offset)
    {
        Buffer.BackingBuffer.Set(offset, matrix);
    }

    /// <summary>
    /// CopyMatrix delegate used to reorder matrix when copying from <see cref="Matrix4x4F"/>.
    /// </summary>
    /// <param name="offset">The offset in bytes to write to</param>
    private delegate Matrix4x4F GetMatrixDelegate(int offset);

    /// <summary>
    /// Copy matrix in row major order.
    /// </summary>
    /// <param name="offset">The offset in bytes to write to</param>
    private unsafe Matrix4x4F GetMatrixRowMajorFrom(int offset)
    {
        var result = default(Matrix4x4F);
        var pSrc = (float*) ((byte*) Buffer.BackingBuffer.DataPointer + offset);
        var pDest = (float*) &result;

        // If Matrix4x4F is row_major but expecting less columns/rows
        // then copy only necessary columns/rows.
        for (int i = 0; i < RowCount; i++, pSrc += 4, pDest += 4)
        {
            for (int j = 0; j < ColumnCount; j++)
                pDest[j] = pSrc[j];
        }

        return result;
    }

    /// <summary>
    /// Copy matrix in column major order.
    /// </summary>
    /// <param name="offset">The offset in bytes to write to</param>
    private unsafe Matrix4x4F GetMatrixColumnMajorFrom(int offset)
    {
        var result = default(Matrix4x4F);
        var pSrc = (float*) ((byte*) Buffer.BackingBuffer.DataPointer + offset);
        var pDest = (float*) &result;

        // If Matrix4x4F is column_major, then we need to transpose it
        for (int i = 0; i < ColumnCount; i++, pSrc += 4, pDest++)
        {
            for (int j = 0; j < RowCount; j++)
                pDest[j * 4] = pSrc[j];
        }

        return result;
    }

    /// <summary>
    /// Straight Matrix4x4F copy, no conversion.
    /// </summary>
    /// <param name="offset">The offset in bytes to write to</param>
    private Matrix4x4F GetMatrixDirectFrom(int offset)
    {
        return Buffer.BackingBuffer.Get<Matrix4x4F>(offset);
    }
}
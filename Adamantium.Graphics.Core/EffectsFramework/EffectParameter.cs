using Adamantium.Core;
using Adamantium.EffectsCompiler;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.EffectsFramework;

/// <summary>
/// One parameter of an effect: a value in one of its constant buffers, or a resource its shaders bind. A value is
/// written into the buffer's bytes where the shaders read it; a resource is handed to the effect's resource linker.
/// </summary>
public sealed class EffectParameter : NamedObject
{
    private IEffectResourceLinker resourceLinker;

    /// <summary>A value of <paramref name="buffer"/>, as <paramref name="description"/> declares it.</summary>
    public EffectParameter(EffectData.ValueTypeParameter description, EffectConstantBuffer buffer)
        : base(description.Name)
    {
        BindValue(description, buffer);
    }

    /// <summary>A resource, as <paramref name="description"/> declares it, at <paramref name="offset"/> among the slots of
    /// <paramref name="resourceLinker"/>.</summary>
    public EffectParameter(EffectData.ResourceParameter description, int offset, IEffectResourceLinker resourceLinker)
        : base(description.Name)
    {
        BindResource(description, offset, resourceLinker);
    }

    /// <summary>The declaration this parameter was made from.</summary>
    public EffectData.Parameter ParameterDescription { get; private set; }

    /// <summary>The constant buffer a value lives in; null for a resource.</summary>
    public EffectConstantBuffer Buffer { get; private set; }

    /// <summary>What the parameter is: a scalar, vector, matrix, struct or pointer value, or a resource.</summary>
    public EffectParameterClass ParameterClass { get; private set; }

    /// <summary>The scalar type of a value, or the kind of descriptor a resource is bound by.</summary>
    public EffectParameterType ParameterType { get; private set; }

    /// <summary>What the shaders do with a resource: read it, write it or both.</summary>
    public EffectResourceAccess Access { get; private set; }

    /// <summary>Whether this is a value in a constant buffer rather than a resource.</summary>
    public bool IsValueType { get; private set; }

    /// <summary>The rows of a matrix, the components of a vector, 1 for a scalar and 0 for a resource.</summary>
    public int RowCount { get; private set; }

    /// <summary>The columns of a matrix, 1 for another value and 0 for a resource.</summary>
    public int ColumnCount { get; private set; }

    /// <summary>How many elements an array has, as declared; 0 for a resource array of no length.</summary>
    public uint ElementCount { get; private set; }

    /// <summary>How many bytes a value takes in its buffer, all its elements together.</summary>
    public int Size { get; private set; }

    /// <summary>Where a value starts in its buffer, in bytes; where a resource's slots start in the resource linker.
    /// </summary>
    public int Offset { get; internal set; }

    /// <summary>The descriptor set a resource is bound in.</summary>
    public uint Set { get; private set; }

    /// <summary>A resource's binding in its set.</summary>
    public uint Binding { get; private set; }

    private int Stride => ElementCount > 1 ? Size / (int)ElementCount : Size;

    internal void BindValue(EffectData.ValueTypeParameter description, EffectConstantBuffer buffer)
    {
        ParameterDescription = description;
        Buffer = buffer;
        resourceLinker = null;
        IsValueType = true;
        ParameterClass = description.Class;
        ParameterType = description.Type;
        RowCount = description.RowCount;
        ColumnCount = description.ColumnCount;
        ElementCount = description.Count;
        Size = description.Size;
        Offset = (int)description.Offset;
    }

    internal void BindResource(EffectData.ResourceParameter description, int offset, IEffectResourceLinker linker)
    {
        ParameterDescription = description;
        Buffer = null;
        resourceLinker = linker;
        IsValueType = false;
        ParameterClass = description.Class;
        ParameterType = description.Type;
        RowCount = ColumnCount = 0;
        ElementCount = description.Count;
        Size = 0;
        Offset = offset;
        Set = description.Set;
        Binding = description.Binding;
        Access = description.Access;
    }

    /// <summary>The value, or the first element of an array.</summary>
    public T GetValue<T>() where T : struct => Buffer.BackingBuffer.Get<T>(Offset);

    /// <summary>The element at <paramref name="index"/> of an array.</summary>
    public T GetValue<T>(int index) where T : struct => Buffer.BackingBuffer.Get<T>(Offset + index * Stride);

    /// <summary>Sets the value, or the first element of an array.</summary>
    public void SetValue<T>(ref T value) where T : struct => Buffer.BackingBuffer.Set(Offset, ref value);

    /// <summary>Sets the value, or the first element of an array.</summary>
    public void SetValue<T>(T value) where T : struct => Buffer.BackingBuffer.Set(Offset, ref value);

    /// <summary>Sets the element at <paramref name="index"/> of an array.</summary>
    public void SetValue<T>(int index, ref T value) where T : struct =>
        Buffer.BackingBuffer.Set(Offset + index * Stride, ref value);

    /// <summary>Sets the element at <paramref name="index"/> of an array.</summary>
    public void SetValue<T>(int index, T value) where T : struct =>
        Buffer.BackingBuffer.Set(Offset + index * Stride, ref value);

    /// <summary>Sets the elements of an array from the first on.</summary>
    public void SetValue<T>(T[] values) where T : struct => SetValue(0, values);

    /// <summary>Sets the elements of an array from <paramref name="index"/> on, each where the shaders read it - the
    /// stride of an array may be larger than its element.</summary>
    public void SetValue<T>(int index, T[] values) where T : struct
    {
        var stride = Stride;
        if (ElementCount <= 1 || stride == Utilities.SizeOf<T>())
        {
            Buffer.BackingBuffer.Set(Offset + index * stride, values);
            return;
        }

        for (var i = 0; i < values.Length; i++)
        {
            Buffer.BackingBuffer.Set(Offset + (index + i) * stride, ref values[i]);
        }
    }

    /// <summary>Sets a matrix, laid out as the shaders read it.</summary>
    public void SetValue(ref Matrix4x4F value) => WriteMatrix(Offset, value);

    /// <summary>Sets a matrix, laid out as the shaders read it.</summary>
    public void SetValue(Matrix4x4F value) => WriteMatrix(Offset, value);

    /// <summary>Sets the matrix at <paramref name="index"/> of an array.</summary>
    public void SetValue(int index, Matrix4x4F value) => WriteMatrix(Offset + index * Stride, value);

    /// <summary>Sets the matrices of an array from the first on.</summary>
    public void SetValue(Matrix4x4F[] values) => SetValue(0, values);

    /// <summary>Sets the matrices of an array from <paramref name="index"/> on.</summary>
    public void SetValue(int index, Matrix4x4F[] values)
    {
        var stride = Stride;
        for (var i = 0; i < values.Length; i++)
        {
            WriteMatrix(Offset + (index + i) * stride, values[i]);
        }
    }

    /// <summary>The resource bound to this parameter.</summary>
    public T GetResource<T>() where T : class =>
        resourceLinker.GetResource<T>((EffectData.ResourceParameter)ParameterDescription);

    /// <summary>Binds <paramref name="resource"/> to this parameter.</summary>
    public void SetResource<T>(T resource) where T : class =>
        resourceLinker.SetResource((EffectData.ResourceParameter)ParameterDescription, resource);

    /// <summary>Binds <paramref name="resources"/> to the elements of an array from the first on.</summary>
    public void SetResource<T>(params T[] resources) where T : class =>
        resourceLinker.SetResource((EffectData.ResourceParameter)ParameterDescription, resources);

    /// <summary>Binds <paramref name="resource"/> to the element at <paramref name="index"/> of an array.</summary>
    public void SetResource<T>(int index, T resource) where T : class =>
        resourceLinker.SetResource((EffectData.ResourceParameter)ParameterDescription, index, resource);

    /// <summary>Binds <paramref name="resources"/> to the elements of an array from <paramref name="index"/> on.</summary>
    public void SetResource<T>(int index, params T[] resources) where T : class =>
        resourceLinker.SetResource((EffectData.ResourceParameter)ParameterDescription, index, resources);

    public override string ToString() =>
        $"{Name}: {ParameterClass} {ParameterType}, {(IsValueType ? $"{Size} bytes at {Offset}" : $"slot {Offset}")}" +
        (ElementCount > 1 ? $", {ElementCount} elements" : string.Empty);

    private unsafe void WriteMatrix(int at, Matrix4x4F value)
    {
        if (ParameterClass != EffectParameterClass.MatrixRows)
        {
            Buffer.BackingBuffer.Set(at, ref value);
            return;
        }

        var target = (float*)((byte*)Buffer.BackingBuffer.DataPointer + at);
        var source = (float*)&value;
        for (var column = 0; column < ColumnCount; column++, target += 4)
        {
            for (var row = 0; row < RowCount; row++)
            {
                target[row] = source[row * 4 + column];
            }
        }
    }
}

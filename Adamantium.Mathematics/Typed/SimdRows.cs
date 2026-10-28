namespace Adamantium.Mathematics.Typed;

/// <summary>A 4x4 matrix seen as four SIMD rows - <c>Vector128&lt;float&gt;</c>, <c>Vector256&lt;double&gt;</c> or
/// <see cref="DoubleRowHalves"/> - over the same bytes, so the whole matrix is loaded into registers at once.</summary>
internal struct SimdRows<TVector> where TVector : struct
{
    /// <summary>The first row.</summary>
    public TVector Row1;

    /// <summary>The second row.</summary>
    public TVector Row2;

    /// <summary>The third row.</summary>
    public TVector Row3;

    /// <summary>The fourth row.</summary>
    public TVector Row4;
}

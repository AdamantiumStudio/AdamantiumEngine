namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>The number of bits the specification stores an unsigned value in (GetNumberOfBitsUsedToStoreUnsignedInteger):
/// one, and one more for each doubling past one that the value needs.</summary>
internal static class PrcBitWidths
{
    internal static int GetUnsignedIntegerWidth(int value)
    {
        var width = 1;
        for (var limit = 1L; value > limit; limit *= 2)
        {
            width++;
        }

        return width;
    }
}

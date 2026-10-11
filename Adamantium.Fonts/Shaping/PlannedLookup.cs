namespace Adamantium.Fonts.Shaping;

internal struct PlannedLookup
{
    public int Index;
    public ILookupTable Table;
    public uint Mask;
    public bool AutoZwnj;
    public bool AutoZwj;
    public ulong[] Coverage;
}

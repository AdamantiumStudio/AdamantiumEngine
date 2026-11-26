namespace Adamantium.Pulsar.Decoding;

/// <summary>The side information of one channel of a granule: ISO/IEC 11172-3 2.4.1.7, 13818-3 2.4.1.7.</summary>
internal struct Mp3Granule
{
    public int Part23Length;
    public int BigValues;
    public int GlobalGain;
    public int ScalefacCompress;
    public bool WindowSwitching;
    public int BlockType;
    public bool Mixed;
    public int Table0;
    public int Table1;
    public int Table2;
    public int SubblockGain0;
    public int SubblockGain1;
    public int SubblockGain2;
    public int Region0Count;
    public int Region1Count;
    public bool Preflag;
    public bool ScalefacScale;
    public bool Count1TableB;

    public readonly bool IsShort => WindowSwitching && BlockType == 2;

    public readonly int TableOf(int region) => region switch { 0 => Table0, 1 => Table1, _ => Table2 };

    public readonly int SubblockGainOf(int window) => window switch { 0 => SubblockGain0, 1 => SubblockGain1, _ => SubblockGain2 };
}

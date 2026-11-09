namespace Adamantium.Fonts.Parsers.Type1;

/// <summary>Where a charstring resumes after a subroutine returns.</summary>
internal readonly struct Type1CallFrame
{
    public Type1CallFrame(byte[] code, int position)
    {
        Code = code;
        Position = position;
    }

    public byte[] Code { get; }

    public int Position { get; }
}

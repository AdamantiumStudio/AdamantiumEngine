namespace Adamantium.Engine.CompilerTests;

/// <summary>A schema entry: the entity type and its tokens.</summary>
internal sealed class PrcTestSchema
{
    public PrcTestSchema(uint type, params uint[] tokens)
    {
        Type = type;
        Tokens = tokens;
    }

    public uint Type { get; }

    public uint[] Tokens { get; }
}

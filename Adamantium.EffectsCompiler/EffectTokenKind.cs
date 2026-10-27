namespace Adamantium.EffectsCompiler;

/// <summary>What a token of an effect file is.</summary>
internal enum EffectTokenKind
{
    /// <summary>A name: a letter or an underscore, then letters, digits and underscores.</summary>
    Word,

    /// <summary>A number, and whatever letters run on from it.</summary>
    Number,

    /// <summary>One character that is none of the others: a brace, an equals sign, a semicolon.</summary>
    Mark,

    /// <summary>A string or a character literal.</summary>
    Quoted,

    /// <summary>A preprocessor directive: a <c>#</c> first on its line, to the end of the line.</summary>
    Directive,

    /// <summary>The end of the file.</summary>
    End
}

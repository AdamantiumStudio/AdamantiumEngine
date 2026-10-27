using System;
using System.Collections.Generic;

namespace Adamantium.EffectsCompiler;

/// <summary>Splits an effect file into tokens, each with where it stands. Spaces and comments between tokens are passed
/// over. A line ends at LF, CR LF or a lone CR, as for Slang; a backslash just before a line end carries a line
/// comment, a string or a directive on to the next line.</summary>
internal sealed class EffectLexer
{
    private readonly string _text;
    private readonly string _filePath;
    private readonly List<int> _lineStarts = [0];
    private int _position;
    private bool _atLineStart = true;

    public EffectLexer(string text, string filePath)
    {
        _text = text;
        _filePath = filePath;
        for (var i = 0; i < _text.Length; i++)
        {
            if (IsLineEnd(i))
            {
                _lineStarts.Add(i + 1);
            }
        }
    }

    /// <summary>Reads the next token; at the end of the file, an <see cref="EffectTokenKind.End"/> token, again and
    /// again.</summary>
    public EffectToken ReadToken()
    {
        SkipSpacesAndComments();
        var start = _position;
        if (start >= _text.Length)
        {
            return new EffectToken(EffectTokenKind.End, string.Empty, start, GetLocation(start));
        }

        var kind = ReadKind(_text[start]);
        _atLineStart = false;
        return new EffectToken(kind, _text.Substring(start, _position - start), start, GetLocation(start));
    }

    /// <summary>Goes back or on to <paramref name="position"/>, which is not the start of a line, and reads on from
    /// there.</summary>
    public void MoveTo(int position)
    {
        _position = position;
        _atLineStart = false;
    }

    private EffectTokenKind ReadKind(char first)
    {
        if (first == '#' && _atLineStart)
        {
            SkipDirective();
            return EffectTokenKind.Directive;
        }

        if (first is '"' or '\'')
        {
            SkipQuoted(first);
            return EffectTokenKind.Quoted;
        }

        if (IsWordStart(first) || char.IsDigit(first))
        {
            SkipWordParts();
            return IsWordStart(first) ? EffectTokenKind.Word : EffectTokenKind.Number;
        }

        _position++;
        return EffectTokenKind.Mark;
    }

    private void SkipSpacesAndComments()
    {
        while (_position < _text.Length)
        {
            if (char.IsWhiteSpace(_text[_position]))
            {
                _atLineStart |= IsLineEnd(_position);
                _position++;
            }
            else if (!SkipComment())
            {
                return;
            }
        }
    }

    private bool SkipComment()
    {
        if (_position + 1 >= _text.Length || _text[_position] != '/')
        {
            return false;
        }

        if (_text[_position + 1] == '/')
        {
            SkipToLineEnd();
            return true;
        }

        if (_text[_position + 1] == '*')
        {
            var end = _text.IndexOf("*/", _position + 2, StringComparison.Ordinal);
            _position = end < 0 ? _text.Length : end + 2;
            return true;
        }

        return false;
    }

    private void SkipDirective()
    {
        while (_position < _text.Length && !IsUncontinuedLineEnd(_position))
        {
            if (SkipComment())
            {
                continue;
            }

            if (_text[_position] is '"' or '\'')
            {
                SkipQuoted(_text[_position]);
            }
            else
            {
                _position++;
            }
        }
    }

    private void SkipQuoted(char quote)
    {
        _position++;
        while (_position < _text.Length && _text[_position] != quote && !IsUncontinuedLineEnd(_position))
        {
            var escapes = _text[_position] == '\\' && _position + 1 < _text.Length && _text[_position + 1] != '\r';
            _position += escapes ? 2 : 1;
        }

        if (_position < _text.Length && _text[_position] == quote)
        {
            _position++;
        }
    }

    private void SkipToLineEnd()
    {
        while (_position < _text.Length && !IsUncontinuedLineEnd(_position))
        {
            _position++;
        }
    }

    private void SkipWordParts()
    {
        while (_position < _text.Length && IsWordPart(_text[_position]))
        {
            _position++;
        }
    }

    private bool IsUncontinuedLineEnd(int position) => IsLineEnd(position) && !IsContinued(position);

    private bool IsLineEnd(int position) =>
        _text[position] == '\n' || (_text[position] == '\r' && (position + 1 == _text.Length || _text[position + 1] != '\n'));

    private bool IsContinued(int lineEnd)
    {
        var before = lineEnd - 1;
        if (before >= 0 && _text[before] == '\r')
        {
            before--;
        }

        return before >= 0 && _text[before] == '\\';
    }

    private SourceLocation GetLocation(int position)
    {
        var line = _lineStarts.BinarySearch(position);
        if (line < 0)
        {
            line = ~line - 1;
        }

        return new SourceLocation(_filePath, line + 1, position - _lineStarts[line] + 1);
    }

    private static bool IsWordStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsWordPart(char c) => char.IsLetterOrDigit(c) || c == '_';
}

using System;
using System.Collections.Generic;
using Adamantium.Pdf.Content;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Fonts;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;

namespace Adamantium.Pdf.Text;

/// <summary>Reads the glyphs a page shows (ISO 32000-2, 9.3 and 9.4): its content and the forms it draws, walked on
/// an explicit stack - a form already being drawn, or one past <see cref="MaxFormDepth"/> deep, is not entered
/// again, and no more than <see cref="MaxFormDraws"/> forms are drawn in all, each decoded once. Each glyph gets its
/// text, from the font or from a marked span's <c>/ActualText</c>, and its box on the page.</summary>
internal sealed class PdfTextReader
{
    public const int MaxFormDepth = 32;
    public const int MaxFormDraws = 10_000;
    public const int MaxGlyphs = 2_000_000;

    private readonly PdfDocument _document;
    private readonly List<PdfTextGlyph> _glyphs = [];
    private readonly Dictionary<PdfFont, (double Ascent, double Descent)> _extents =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PdfStream, ReadOnlyMemory<byte>?> _forms = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<bool> _marked = new();
    private readonly Stack<PdfActualText> _actualTexts = new();
    private PdfMatrix _textMatrix = PdfMatrix.Identity;
    private PdfMatrix _lineMatrix = PdfMatrix.Identity;
    private PdfTextState _state = new() { Ctm = PdfMatrix.Identity, Scale = 1 };
    private int _formDraws;

    private PdfTextReader(PdfDocument document)
    {
        _document = document;
    }

    public static List<PdfTextGlyph> Read(PdfPage page, PdfDocument document)
    {
        var reader = new PdfTextReader(document);
        reader.Walk(page.GetContents(), new PdfResources(page.Resources, document));
        return reader._glyphs;
    }

    private void Walk(ReadOnlyMemory<byte> contents, PdfResources resources)
    {
        var states = new Stack<PdfTextState>();
        var onPath = new HashSet<PdfStream>(ReferenceEqualityComparer.Instance);
        var frames = new Stack<PdfTextFrame>();
        frames.Push(new PdfTextFrame(PdfContentReader.Read(contents).GetEnumerator(), resources, 0, null));
        while (frames.Count > 0 && _glyphs.Count < MaxGlyphs)
        {
            var frame = frames.Peek();
            if (!Advance(frame.Operations))
            {
                frames.Pop();
                frame.Operations.Dispose();
                while (states.Count > frame.Floor - (frame.Form != null ? 1 : 0))
                {
                    _state = states.Pop();
                }

                if (frame.Form != null)
                {
                    onPath.Remove(frame.Form);
                }

                continue;
            }

            var operation = frame.Operations.Current;
            switch (operation.Operator)
            {
                case "q":
                    states.Push(_state);
                    break;
                case "Q":
                    if (states.Count > frame.Floor)
                    {
                        _state = states.Pop();
                    }

                    break;
                case "cm":
                    _state.Ctm = Matrix(operation).Then(_state.Ctm);
                    break;
                case "Do":
                    if (Name(operation, 0) is { } name && frame.Resources.XObject(name) is { } form &&
                        _document.Resolve(form.Dictionary[PdfKeys.Subtype]) is PdfName { Value: PdfKeys.Form } &&
                        frames.Count <= MaxFormDepth && _formDraws < MaxFormDraws && !onPath.Contains(form) &&
                        FormData(form) is { } data)
                    {
                        _formDraws++;
                        onPath.Add(form);
                        states.Push(_state);
                        _state.Ctm = PdfMatrix.From(PdfValues.Numbers(form.Dictionary[PdfKeys.Matrix], _document))
                            .Then(_state.Ctm);
                        var formResources = _document.Resolve(form.Dictionary[PdfKeys.Resources]) is PdfDictionary own
                            ? new PdfResources(own, _document)
                            : frame.Resources;
                        frames.Push(new PdfTextFrame(PdfContentReader.Read(data).GetEnumerator(), formResources,
                            states.Count, form));
                    }

                    break;
                default:
                    Text(operation, frame.Resources);
                    break;
            }
        }

        foreach (var frame in frames)
        {
            frame.Operations.Dispose();
        }
    }

    private ReadOnlyMemory<byte>? FormData(PdfStream form)
    {
        if (!_forms.TryGetValue(form, out var data))
        {
            try
            {
                data = _document.Decode(form).Data;
            }
            catch (PdfException)
            {
                data = null;
            }

            _forms[form] = data;
        }

        return data;
    }

    private static bool Advance(IEnumerator<PdfContentOperation> operations)
    {
        try
        {
            return operations.MoveNext();
        }
        catch (PdfException)
        {
            return false;
        }
    }

    private void Text(PdfContentOperation operation, PdfResources resources)
    {
        switch (operation.Operator)
        {
            case "BT":
                _textMatrix = _lineMatrix = PdfMatrix.Identity;
                break;
            case "Tc":
                _state.CharacterSpacing = operation.Number(0);
                break;
            case "Tw":
                _state.WordSpacing = operation.Number(0);
                break;
            case "Tz":
                _state.Scale = operation.Number(0, 100) / 100;
                break;
            case "TL":
                _state.Leading = operation.Number(0);
                break;
            case "Ts":
                _state.Rise = operation.Number(0);
                break;
            case "Tf":
                _state.Font = Name(operation, 0) is { } fontName
                    ? PdfFonts.Create(resources.Entry(PdfKeys.Font, fontName), _document)
                    : null;
                _state.Size = operation.Number(1);
                break;
            case "Td":
                MoveLine(operation.Number(0), operation.Number(1));
                break;
            case "TD":
                _state.Leading = -operation.Number(1);
                MoveLine(operation.Number(0), operation.Number(1));
                break;
            case "Tm":
                _textMatrix = _lineMatrix = Matrix(operation);
                break;
            case "T*":
                MoveLine(0, -_state.Leading);
                break;
            case "Tj":
                Show(operation, 0);
                break;
            case "'":
                MoveLine(0, -_state.Leading);
                Show(operation, 0);
                break;
            case "\"":
                _state.WordSpacing = operation.Number(0);
                _state.CharacterSpacing = operation.Number(1);
                MoveLine(0, -_state.Leading);
                Show(operation, 2);
                break;
            case "TJ":
                if (operation.Operands.Count > 0 && operation.Operands[0] is PdfArray items)
                {
                    foreach (var item in items)
                    {
                        if (item is PdfString text)
                        {
                            Show(text.Bytes);
                        }
                        else if (item is PdfInteger or PdfReal)
                        {
                            Adjust(PdfValues.Number(item, _document));
                        }
                    }
                }

                break;
            case "BMC":
                _marked.Push(false);
                break;
            case "BDC":
                var actual = Actual(operation, resources);
                _marked.Push(actual != null);
                if (actual != null)
                {
                    _actualTexts.Push(actual);
                }

                break;
            case "EMC":
                if (_marked.Count > 0 && _marked.Pop())
                {
                    _actualTexts.Pop();
                }

                break;
        }
    }

    private PdfActualText Actual(PdfContentOperation operation, PdfResources resources)
    {
        var properties = operation.Operands.Count > 1 ? operation.Operands[1] : null;
        var dictionary = properties is PdfName name ? resources.Properties(name.Value)
            : _document.Resolve(properties) as PdfDictionary;
        return _document.Resolve(dictionary?["ActualText"]) is PdfString text ? new PdfActualText(text.ToText()) : null;
    }

    private void MoveLine(double x, double y)
    {
        _lineMatrix = new PdfMatrix(1, 0, 0, 1, x, y).Then(_lineMatrix);
        _textMatrix = _lineMatrix;
    }

    private void Adjust(double thousandths)
    {
        if (_state.Font is { IsVertical: true })
        {
            _textMatrix = new PdfMatrix(1, 0, 0, 1, 0, -thousandths / 1000 * _state.Size).Then(_textMatrix);
        }
        else
        {
            _textMatrix = new PdfMatrix(1, 0, 0, 1, -thousandths / 1000 * _state.Size * _state.Scale, 0)
                .Then(_textMatrix);
        }
    }

    private void Show(PdfContentOperation operation, int index)
    {
        if (operation.Operands.Count > index && operation.Operands[index] is PdfString text)
        {
            Show(text.Bytes);
        }
    }

    private void Show(byte[] bytes)
    {
        var font = _state.Font;
        if (font == null || bytes == null)
        {
            return;
        }

        var (ascent, descent) = ExtentOf(font);
        var vertical = font.IsVertical;
        for (var position = 0; position < bytes.Length && _glyphs.Count < MaxGlyphs;)
        {
            var character = font.ReadCharacter(bytes, position);
            if (character.Length <= 0)
            {
                break;
            }

            position += character.Length;
            var advance = font.WidthOf(character);
            if (!double.IsFinite(advance))
            {
                advance = 0;
            }

            var rendering = new PdfMatrix(_state.Size * _state.Scale, 0, 0, _state.Size, 0, _state.Rise)
                .Then(_textMatrix).Then(_state.Ctm);
            var spacing = _state.CharacterSpacing + (character.IsWordSpace ? _state.WordSpacing : 0);
            PdfQuad quad;
            PdfPoint origin, end;
            if (vertical)
            {
                quad = new PdfQuad(rendering.Transform(-0.5, -advance), rendering.Transform(0.5, -advance),
                    rendering.Transform(0.5, 0), rendering.Transform(-0.5, 0));
                (origin, end) = (rendering.Transform(0, 0), rendering.Transform(0, -advance));
                _textMatrix = new PdfMatrix(1, 0, 0, 1, 0, -advance * _state.Size + spacing).Then(_textMatrix);
            }
            else
            {
                quad = new PdfQuad(rendering.Transform(0, descent), rendering.Transform(advance, descent),
                    rendering.Transform(advance, ascent), rendering.Transform(0, ascent));
                (origin, end) = (rendering.Transform(0, 0), rendering.Transform(advance, 0));
                _textMatrix = new PdfMatrix(1, 0, 0, 1, (advance * _state.Size + spacing) * _state.Scale, 0)
                    .Then(_textMatrix);
            }

            var size = Math.Sqrt(rendering.C * rendering.C + rendering.D * rendering.D);
            _glyphs.Add(new PdfTextGlyph(TextOf(font, character), quad, origin, end, size, font.Name, character.Code,
                vertical));
        }
    }

    private string TextOf(PdfFont font, PdfCharacter character)
    {
        if (_actualTexts.Count == 0)
        {
            return font.TextOf(character);
        }

        var actual = _actualTexts.Peek();
        if (actual.Used)
        {
            return "";
        }

        actual.Used = true;
        return actual.Text;
    }

    private (double Ascent, double Descent) ExtentOf(PdfFont font)
    {
        if (_extents.TryGetValue(font, out var known))
        {
            return known;
        }

        double ascent, descent;
        if (font is PdfType3Font type3)
        {
            var box = PdfRectangle.From(type3.Dictionary[PdfFontKeys.FontBBox], _document);
            var scale = type3.FontMatrix[3];
            (ascent, descent) = box is { } glyphs && glyphs.Height > 0
                ? (Math.Max(glyphs.Top, glyphs.Bottom) * scale, Math.Min(glyphs.Top, glyphs.Bottom) * scale)
                : (0.8, -0.2);
        }
        else
        {
            var descriptor = font.Descriptor;
            ascent = (descriptor?.Ascent ?? 0) / 1000;
            descent = (descriptor?.Descent ?? 0) / 1000;
            if (ascent <= 0.3 && descriptor?.FontBBox is { } box && box.Top > 300)
            {
                ascent = box.Top / 1000;
            }

            if (descent >= 0 && descriptor?.FontBBox is { } bottom && bottom.Bottom < 0)
            {
                descent = bottom.Bottom / 1000;
            }
        }

        ascent = ascent is > 0.3 and < 1.5 ? ascent : 0.8;
        descent = descent is < 0 and > -1 ? descent : -0.2;
        _extents[font] = (ascent, descent);
        return (ascent, descent);
    }

    private static PdfMatrix Matrix(PdfContentOperation operation) =>
        operation.Operands.Count >= 6
            ? PdfMatrix.From([
                operation.Number(0), operation.Number(1), operation.Number(2), operation.Number(3),
                operation.Number(4), operation.Number(5),
            ])
            : PdfMatrix.Identity;

    private static string Name(PdfContentOperation operation, int index) =>
        operation.Operands.Count > index && operation.Operands[index] is PdfName name ? name.Value : null;
}

using System;
using System.Globalization;
using System.IO;
using System.Text;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Security;

namespace Adamantium.Pdf.Writing;

/// <summary>Writes objects in PDF syntax (ISO 32000-2, 7.3): names with their delimiters escaped, strings literal or
/// hexadecimal, reals without exponents, streams with their <c>/Length</c>. An indirect object of an encrypted file has
/// its strings and stream encrypted with the key of its number.</summary>
internal sealed class PdfObjectWriter
{
    private const int MaxDepth = 256;
    private const string Delimiters = "#()<>[]{}/%";

    private readonly Stream _output;
    private readonly PdfDocument _document;
    private readonly PdfStandardSecurityHandler _security;

    /// <summary>A writer of objects into <paramref name="document"/>, whose encryption they take; null for a file
    /// of none.</summary>
    public PdfObjectWriter(Stream output, PdfDocument document)
    {
        _output = output;
        _document = document;
        _security = document is { IsEncrypted: true } ? document.Security : null;
    }

    /// <summary>Writes <paramref name="value"/> as the indirect object <paramref name="id"/>, encrypted unless
    /// <paramref name="plain"/>.</summary>
    public void WriteIndirect(PdfObjectId id, PdfObject value, bool plain)
    {
        Write($"{id.Number} {id.Generation} obj\n");
        var encryptAs = plain || _security == null ? (PdfObjectId?)null : id;
        if (value is PdfStream stream)
        {
            WriteStream(stream, encryptAs);
        }
        else
        {
            Write(value, encryptAs, 0);
        }

        Write("\nendobj\n");
    }

    public void Write(string text) => _output.Write(Encoding.ASCII.GetBytes(text));

    /// <summary>Writes a direct object, not encrypted - a trailer.</summary>
    public void WritePlain(PdfObject value) => Write(value, null, 0);

    private void WriteStream(PdfStream stream, PdfObjectId? encryptAs)
    {
        byte[] bytes;
        var fromFile = stream.Id != default;
        if (encryptAs is not { } id)
        {
            bytes = (fromFile && _document != null ? _document.Decrypt(stream) : stream.Data).ToArray();
        }
        else if (fromFile && stream.Id == id)
        {
            bytes = stream.Data.ToArray();
        }
        else
        {
            var method = _document.StreamMethodOf(stream.Dictionary);
            var plain = fromFile ? _document.Decrypt(stream).Span : stream.Data.Span;
            bytes = method == PdfCryptMethod.None ? plain.ToArray() : _security.Encrypt(id, plain, method);
        }
        var dictionary = new PdfDictionary();
        foreach (var (key, item) in stream.Dictionary)
        {
            dictionary.Set(key, item);
        }

        dictionary.Set(PdfKeys.Length, new PdfInteger(bytes.Length));
        Write(dictionary, encryptAs, 0);
        Write("\nstream\n");
        _output.Write(bytes);
        Write("\nendstream");
    }

    private void Write(PdfObject value, PdfObjectId? encryptAs, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new PdfException("An object to write nests deeper than any file needs.");
        }

        switch (value)
        {
            case null or PdfNull:
                Write("null");
                break;
            case PdfBoolean boolean:
                Write(boolean.Value ? "true" : "false");
                break;
            case PdfInteger integer:
                Write(integer.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case PdfReal real:
                Write(Number(real.Value));
                break;
            case PdfName name:
                WriteName(name.Value);
                break;
            case PdfString text:
                WriteString(encryptAs is { } id ? _security.Encrypt(id, text.Bytes, _security.StringMethod) : text.Bytes,
                    text.IsHex || encryptAs != null);
                break;
            case PdfReference reference:
                Write($"{reference.Id.Number} {reference.Id.Generation} R");
                break;
            case PdfArray array:
                Write("[");
                for (var i = 0; i < array.Count; i++)
                {
                    if (i > 0)
                    {
                        Write(" ");
                    }

                    Write(array[i], encryptAs, depth + 1);
                }

                Write("]");
                break;
            case PdfDictionary dictionary:
                Write("<<");
                foreach (var (key, item) in dictionary)
                {
                    WriteName(key);
                    Write(" ");
                    Write(item, encryptAs, depth + 1);
                }

                Write(">>");
                break;
            case PdfStream:
                throw new PdfException("A stream can only be written as an indirect object.");
            default:
                throw new PdfException("An object of a kind PDF has no syntax for cannot be written.");
        }
    }

    /// <summary>A real as PDF writes it: no exponent, at most six decimals, no negative zero.</summary>
    public static string Number(double value)
    {
        if (!double.IsFinite(value))
        {
            return "0";
        }

        var text = Math.Round(value, 6).ToString("0.######", CultureInfo.InvariantCulture);
        return text == "-0" ? "0" : text;
    }

    private void WriteName(string value)
    {
        var text = new StringBuilder("/");
        foreach (var b in NameBytes(value))
        {
            if (b == 0)
            {
                throw new PdfException("A name cannot hold a null character (ISO 32000-2, 7.3.5).");
            }

            if (b < 0x21 || b > 0x7E || Delimiters.Contains((char)b))
            {
                text.Append('#').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                text.Append((char)b);
            }
        }

        Write(text.ToString());
    }

    private static byte[] NameBytes(string value)
    {
        var latin = true;
        foreach (var c in value)
        {
            latin &= c <= 0xFF;
        }

        if (latin)
        {
            var bytes = Encoding.Latin1.GetBytes(value);
            if (!IsUtf8(bytes))
            {
                return bytes;
            }
        }

        return Encoding.UTF8.GetBytes(value);
    }

    private static bool IsUtf8(byte[] bytes)
    {
        try
        {
            new UTF8Encoding(false, true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private void WriteString(byte[] bytes, bool hex)
    {
        var printable = !hex;
        foreach (var b in bytes)
        {
            printable &= b is >= 0x20 and < 0x7F or (byte)'\n' or (byte)'\r' or (byte)'\t';
        }

        var text = new StringBuilder(bytes.Length + 2);
        if (!printable)
        {
            text.Append('<');
            foreach (var b in bytes)
            {
                text.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }

            Write(text.Append('>').ToString());
            return;
        }

        text.Append('(');
        foreach (var b in bytes)
        {
            text.Append(b switch
            {
                (byte)'(' => "\\(",
                (byte)')' => "\\)",
                (byte)'\\' => "\\\\",
                (byte)'\n' => "\\n",
                (byte)'\r' => "\\r",
                (byte)'\t' => "\\t",
                _ => ((char)b).ToString(),
            });
        }

        Write(text.Append(')').ToString());
    }
}

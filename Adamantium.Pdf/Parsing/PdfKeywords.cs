using System;

namespace Adamantium.Pdf.Parsing;

/// <summary>The keywords of PDF syntax (ISO 32000-2, 7.3 and 7.5) and of inline images in content (8.9.7).</summary>
internal static class PdfKeywords
{
    public const string Obj = "obj";
    public const string EndObj = "endobj";
    public const string Stream = "stream";
    public const string EndStream = "endstream";
    public const string Xref = "xref";
    public const string Trailer = "trailer";
    public const string StartXref = "startxref";
    public const string Reference = "R";
    public const string True = "true";
    public const string False = "false";
    public const string Null = "null";

    /// <summary>A cross-reference table entry in use.</summary>
    public const string InUse = "n";

    /// <summary>A free cross-reference table entry.</summary>
    public const string Free = "f";

    public const string BeginImage = "BI";
    public const string ImageData = "ID";
    public const string EndImage = "EI";

    /// <summary>The comment the file header starts with, before the version.</summary>
    public static ReadOnlySpan<byte> FileHeaderBytes => "%PDF-"u8;

    public static ReadOnlySpan<byte> ObjBytes => "obj"u8;
    public static ReadOnlySpan<byte> EndObjBytes => "endobj"u8;
    public static ReadOnlySpan<byte> StreamBytes => "stream"u8;
    public static ReadOnlySpan<byte> EndStreamBytes => "endstream"u8;
    public static ReadOnlySpan<byte> TrailerBytes => "trailer"u8;
    public static ReadOnlySpan<byte> StartXrefBytes => "startxref"u8;
    public static ReadOnlySpan<byte> EndImageBytes => "EI"u8;
}

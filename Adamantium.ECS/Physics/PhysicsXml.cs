using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Adamantium.Core;

namespace Adamantium.ECS.Physics;

/// <summary>How the physics' own files are read and written: XML of one serializer, no DTD, indented UTF-8 without a
/// mark or namespaces, written whole or not at all.</summary>
internal static class PhysicsXml
{
    /// <summary>The file in <paramref name="stream"/>; <see cref="PhysicsFileException"/> naming <paramref name="what"/>
    /// for one the serializer cannot read: damaged XML or another file's is not of its kind; a value that is no number,
    /// or a choice there is none of, is a value not read.</summary>
    public static T Read<T>(XmlSerializer serializer, Stream stream, string what)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = XmlReader.Create(stream,
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        try
        {
            // Another file's root - or XML too damaged to have one - is not of this kind.
            if (!serializer.CanDeserialize(reader))
                throw PhysicsFileException.NotThisKind(what, null);
        }
        catch (XmlException exception)
        {
            throw PhysicsFileException.NotThisKind(what, exception);
        }

        try
        {
            return (T)serializer.Deserialize(reader);
        }
        catch (InvalidOperationException exception)
        {
            // Past the root, damaged XML is still another file; anything else is a value not turned into its property.
            throw exception.InnerException is XmlException
                ? PhysicsFileException.NotThisKind(what, exception)
                : PhysicsFileException.ValueNotRead(what, exception);
        }
    }

    /// <summary>Writes <paramref name="file"/> to <paramref name="path"/>, whole or not at all.</summary>
    public static void Write(XmlSerializer serializer, object file, string path) =>
        AtomicFile.Write(path, stream => Write(serializer, file, stream));

    /// <summary>Writes <paramref name="file"/> to <paramref name="stream"/>.</summary>
    public static void Write(XmlSerializer serializer, object file, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) });
        serializer.Serialize(writer, file, new XmlSerializerNamespaces([XmlQualifiedName.Empty]));
    }
}

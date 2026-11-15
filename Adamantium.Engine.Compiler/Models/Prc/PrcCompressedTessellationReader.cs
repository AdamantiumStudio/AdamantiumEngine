using System.Linq;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Reads a highly compressed tessellation (PRC_TYPE_TESS_3D_Compressed), its type code read: its fields as
/// the specification's table of data written to the file gives them, with the lengths the arrays do not store -
/// one flag a triangle of normal_is_reversed, one a face of is_face_planar, one in all of the per-face flags of
/// colours, line attributes and textures when the tessellation has no faces - and the compression of the point
/// references implied by their number.</summary>
internal static class PrcCompressedTessellationReader
{
    private const int EdgeStatusBits = 2;
    private const int ColorBits = 8;
    private const int LineAttributeBits = 16;
    private const int BehaviorBits = 8;
    private const int ImpliedCompressionAbove = 3;

    internal static PrcCompressedTessellationData Read(PrcEntityReader reader)
    {
        var bits = reader.Bits;
        var data = new PrcCompressedTessellationData
        {
            IsCalculated = bits.ReadBoolean(),
            HasFaces = bits.ReadBoolean(),
            Tolerance = bits.ReadDouble()
        };
        data.Origin = new Vector3<double>(bits.ReadFloatAsBytes(), bits.ReadFloatAsBytes(), bits.ReadFloatAsBytes());
        data.Points = bits.ReadCompressedIntegerArray("point coordinates of a compressed tessellation");
        data.EdgeStatus = bits.ReadCharacterArray(EdgeStatusBits, false, "edge statuses of a compressed tessellation");
        data.TriangleFaces = bits.ReadCompressedIndiceArray("triangle faces of a compressed tessellation");
        var referenceCount = bits.ReadCount(1, "point reference flags of a compressed tessellation");
        data.PointIsReference = bits.ReadBooleans(referenceCount, "point reference flags of a compressed tessellation");
        var references = data.PointIsReference.Count(flag => flag);
        data.PointReferences = bits.ReadCompressedIndiceArray(references > ImpliedCompressionAbove,
            "point references of a compressed tessellation");
        var triangles = data.TriangleFaces.Length;
        var faces = data.FaceCount;
        var perFaceFlags = data.HasFaces ? faces : 1;
        data.MustRecalculateNormals = bits.ReadBoolean();
        if (data.MustRecalculateNormals)
        {
            data.NormalIsReversed = bits.ReadBooleans(triangles, "normal reversals of a compressed tessellation");
            data.CreaseAngle = bits.ReadDouble();
            bits.ReadCharacter();
        }
        else
        {
            data.NormalAngleBits = bits.ReadCharacter();
            var normalBits = bits.ReadCount(1, "normal data of a compressed tessellation");
            data.NormalBinaryData = bits.ReadBooleans(normalBits, "normal data of a compressed tessellation");
            data.NormalAngles = bits.ReadShortArray(data.NormalAngleBits, "normal angles of a compressed tessellation");
            data.IsFacePlanar = bits.ReadBooleans(faces, "planar faces of a compressed tessellation");
        }

        if (bits.ReadBoolean())
        {
            data.IsPointColorOnFace = bits.ReadBooleans(perFaceFlags, "point coloured faces of a compressed tessellation");
            data.PointColors = bits.ReadCharacterArray(ColorBits, false, "point colours of a compressed tessellation");
        }

        if (bits.ReadBoolean())
        {
            data.IsMultipleLineAttributeOnFace = bits.ReadBooleans(perFaceFlags,
                "faces of several line attributes of a compressed tessellation");
        }

        data.LineAttributes = bits.ReadShortArray(LineAttributeBits, "line attributes of a compressed tessellation");
        if (!bits.ReadBoolean())
        {
            data.Texture = ReadTexture(bits);
            if (!bits.ReadBoolean())
            {
                data.FaceHasTexture = bits.ReadBooleans(perFaceFlags, "textured faces of a compressed tessellation");
            }
            else
            {
                data.FaceHasTexture = Enumerable.Repeat(true, perFaceFlags).ToArray();
            }
        }

        if (bits.ReadBoolean())
        {
            data.Behaviors = bits.ReadCharacterArray(BehaviorBits, false, "behaviours of a compressed tessellation");
        }

        reader.EndOf(PrcTypes.Tessellation3DCompressed);
        return data;
    }

    private static PrcCompressedTexture ReadTexture(PrcBitReader bits)
    {
        var texture = new PrcCompressedTexture();
        var words = bits.ReadCount(32, "texture reference bits of a compressed tessellation");
        var flags = new bool[words * 32];
        for (var word = 0; word < words; word++)
        {
            for (var part = 0; part < 4; part++)
            {
                var character = bits.ReadCharacter();
                for (var bit = 0; bit < 8; bit++)
                {
                    flags[word * 32 + part * 8 + bit] = ((character >> bit) & 1) != 0;
                }
            }
        }

        var usedInLastWord = (int)bits.ReadUnsignedInteger();
        if (usedInLastWord > 32)
        {
            throw bits.Corrupt($"the texture reference bits of a compressed tessellation use {usedInLastWord} bits of their last word");
        }

        texture.ReferenceFlags = words == 0 ? [] : flags.Take((words - 1) * 32 + usedInLastWord).ToArray();
        var references = bits.ReadCount(5, "texture references of a compressed tessellation");
        texture.References = new int[references];
        for (var index = 0; index < references; index++)
        {
            texture.References[index] = (int)bits.ReadNumberOfBitsThenUnsignedInteger();
        }

        bits.ReadDouble();
        var parameters = bits.ReadCount(32, "texture parameters of a compressed tessellation");
        texture.Parameters = new float[parameters];
        for (var index = 0; index < parameters; index++)
        {
            texture.Parameters[index] = bits.ReadFloatAsBytes();
        }

        return texture;
    }
}

/// <summary>The fields of a highly compressed tessellation as the file holds them, before its mesh is rebuilt.</summary>
internal sealed class PrcCompressedTessellationData
{
    internal bool IsCalculated { get; init; }

    internal bool HasFaces { get; init; }

    /// <summary>The step the point coordinates are integer multiples of.</summary>
    internal double Tolerance { get; init; }

    /// <summary>The point the first point's coordinates are measured from.</summary>
    internal Vector3<double> Origin { get; set; }

    /// <summary>Three integers for each point stored, in the coordinate system the traversal builds for it.</summary>
    internal int[] Points { get; set; } = [];

    /// <summary>Two bits a triangle, in traversal order: 1 when it has a right neighbour, 2 a left one.</summary>
    internal int[] EdgeStatus { get; set; } = [];

    /// <summary>The face of each triangle, in traversal order.</summary>
    internal int[] TriangleFaces { get; set; } = [];

    /// <summary>For each vertex the traversal meets, whether it is a point met before.</summary>
    internal bool[] PointIsReference { get; set; } = [];

    /// <summary>For each vertex met before, the index of its point, points numbered as they are stored.</summary>
    internal int[] PointReferences { get; set; } = [];

    internal bool MustRecalculateNormals { get; set; }

    /// <summary>One flag a triangle when normals are recalculated: whether its winding is the traversal's.</summary>
    internal bool[] NormalIsReversed { get; set; } = [];

    internal double CreaseAngle { get; set; }

    internal int NormalAngleBits { get; set; }

    internal bool[] NormalBinaryData { get; set; } = [];

    internal int[] NormalAngles { get; set; } = [];

    internal bool[] IsFacePlanar { get; set; } = [];

    /// <summary>For each face, whether its vertices have colours; null when no face has.</summary>
    internal bool[] IsPointColorOnFace { get; set; }

    /// <summary>Five characters a coloured vertex: whether it has alpha, then red, green, blue and alpha.</summary>
    internal int[] PointColors { get; set; } = [];

    /// <summary>For each face, whether each of its triangles has a line attribute of its own; null when no face has.</summary>
    internal bool[] IsMultipleLineAttributeOnFace { get; set; }

    /// <summary>Styles of the globals, plus one: one a face, or one a triangle of a face of several.</summary>
    internal int[] LineAttributes { get; set; } = [];

    /// <summary>The texture coordinates; null for none.</summary>
    internal PrcCompressedTexture Texture { get; set; }

    internal bool[] FaceHasTexture { get; set; }

    internal int[] Behaviors { get; set; } = [];

    /// <summary>One more than the highest face a triangle names.</summary>
    internal int FaceCount => TriangleFaces.Length == 0 ? 0 : TriangleFaces.Max() + 1;
}

/// <summary>The texture coordinates of a highly compressed tessellation (CompressedTextureParameter).</summary>
internal sealed class PrcCompressedTexture
{
    /// <summary>For each textured vertex the traversal meets, whether its coordinates were stored before.</summary>
    internal bool[] ReferenceFlags { get; set; } = [];

    internal int[] References { get; set; } = [];

    /// <summary>U and v of each stored texture coordinate, in turn.</summary>
    internal float[] Parameters { get; set; } = [];
}

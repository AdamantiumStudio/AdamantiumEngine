using System.Linq;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Reads the fields of a highly compressed tessellation (PRC_TYPE_TESS_3D_Compressed) after its type
/// code.</summary>
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
            data.IsPointColorOnFace = bits.ReadBooleans(perFaceFlags, "point colored faces of a compressed tessellation");
            data.PointColors = bits.ReadCharacterArray(ColorBits, false, "point colors of a compressed tessellation");
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
            data.Behaviors = bits.ReadCharacterArray(BehaviorBits, false, "behaviors of a compressed tessellation");
        }

        reader.SkipNewData(PrcTypes.Tessellation3DCompressed);
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
            throw bits.CreateCorruptException($"the texture reference bits of a compressed tessellation use {usedInLastWord} bits of their last word");
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

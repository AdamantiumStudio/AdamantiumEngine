using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Engine.Compiler.Models.U3d;
using Adamantium.Mathematics;

namespace Adamantium.Engine.CompilerTests;

/// <summary>A parent of a test node and where the node stands relative to it.</summary>
internal sealed class U3dTestParent
{
    public string Name { get; init; } = string.Empty;

    public Matrix4x4F Transform { get; init; } = Matrix4x4F.Identity;
}

/// <summary>A face of a test mesh: indices into each pool at its three corners.</summary>
internal sealed class U3dTestFace
{
    public int ShadingId { get; init; }

    public int[] Positions { get; init; }

    public int[] Normals { get; init; }

    public int[] DiffuseColors { get; init; }

    public int[] TextureCoordinates { get; init; }
}

/// <summary>A CLOD mesh for the tests, written whole as its base mesh.</summary>
internal sealed class U3dTestMesh
{
    public string Name { get; init; }

    public List<Vector3F> Positions { get; init; } = [];

    public List<Vector3F> Normals { get; init; } = [];

    public List<Vector4F> DiffuseColors { get; init; } = [];

    public List<Vector4F> TextureCoordinates { get; init; } = [];

    public List<U3dTestFace> Faces { get; init; } = [];

    /// <summary>One entry a shading ID: its attributes, 0x1 diffuse colours.</summary>
    public List<uint> ShadingAttributes { get; init; } = [0];

    /// <summary>One entry a shading ID: how many texture layers it has.</summary>
    public List<int> ShadingLayers { get; init; } = [0];

    public bool ExcludeNormals { get; init; }
}

/// <summary>The blocks of ECMA-363 clause 9 the tests write, compressed as the file they go in is.</summary>
internal static class U3dTestBlocks
{
    private static readonly U3dContext Shading = U3dContext.Dynamic("cShading");

    public static void GroupNode(U3dTestFile file, string name, IEnumerable<U3dTestParent> parents, byte[] metaData = null)
    {
        var data = file.Data();
        data.WriteString(name);
        WriteParents(data, parents);
        file.Declare(U3dBlockTypes.GroupNode, data, metaData);
    }

    public static byte[] ModelNode(U3dTestFile file, string name, IEnumerable<U3dTestParent> parents, string resource,
        uint visibility = 1)
    {
        var data = file.Data();
        data.WriteString(name);
        WriteParents(data, parents);
        data.WriteString(resource);
        data.WriteU32(visibility);
        return U3dTestFile.Block(U3dBlockTypes.ModelNode, data.ToArray());
    }

    public static void LightNode(U3dTestFile file, string name, IEnumerable<U3dTestParent> parents, string resource)
    {
        var data = file.Data();
        data.WriteString(name);
        WriteParents(data, parents);
        data.WriteString(resource);
        file.Declare(U3dBlockTypes.LightNode, data);
    }

    public static void ViewNode(U3dTestFile file, string name, IEnumerable<U3dTestParent> parents, string resource,
        float fieldOfView, float nearClip, float farClip, float width, float height)
    {
        var data = file.Data();
        data.WriteString(name);
        WriteParents(data, parents);
        data.WriteString(resource);
        data.WriteU32(0);
        data.WriteF32(nearClip);
        data.WriteF32(farClip);
        data.WriteF32(fieldOfView);
        data.WriteF32(width);
        data.WriteF32(height);
        data.WriteF32(0);
        data.WriteF32(0);
        data.WriteU32(0);
        data.WriteU32(0);
        file.Declare(U3dBlockTypes.ViewNode, data);
    }

    public static byte[] ShadingModifier(U3dTestFile file, string name, params string[] shaderPerShadingId)
    {
        var data = file.Data();
        data.WriteString(name);
        data.WriteU32(1);
        data.WriteU32(0x7);
        data.WriteU32((uint)shaderPerShadingId.Length);
        foreach (var shader in shaderPerShadingId)
        {
            data.WriteU32(1);
            data.WriteString(shader);
        }

        return U3dTestFile.Block(U3dBlockTypes.ShadingModifier, data.ToArray());
    }

    public static void Mesh(U3dTestFile file, U3dTestMesh mesh)
    {
        var declaration = file.Data();
        declaration.WriteString(mesh.Name);
        declaration.WriteU32(0);
        declaration.WriteU32(mesh.ExcludeNormals ? 1u : 0u);
        declaration.WriteU32((uint)mesh.Faces.Count);
        declaration.WriteU32((uint)mesh.Positions.Count);
        declaration.WriteU32((uint)mesh.Normals.Count);
        declaration.WriteU32((uint)mesh.DiffuseColors.Count);
        declaration.WriteU32(0);
        declaration.WriteU32((uint)mesh.TextureCoordinates.Count);
        WriteShadings(declaration, mesh.ShadingAttributes, mesh.ShadingLayers);
        declaration.WriteU32((uint)mesh.Positions.Count);
        declaration.WriteU32((uint)mesh.Positions.Count);
        WriteQuality(declaration);
        declaration.WriteF32(0.9f);
        declaration.WriteF32(0.5f);
        declaration.WriteF32(0.985f);
        declaration.WriteU32(0);
        file.Declare(U3dBlockTypes.MeshDeclaration, declaration);

        var data = file.Data();
        data.WriteString(mesh.Name);
        data.WriteU32(0);
        data.WriteU32((uint)mesh.Faces.Count);
        data.WriteU32((uint)mesh.Positions.Count);
        data.WriteU32((uint)mesh.Normals.Count);
        data.WriteU32((uint)mesh.DiffuseColors.Count);
        data.WriteU32(0);
        data.WriteU32((uint)mesh.TextureCoordinates.Count);
        foreach (var position in mesh.Positions)
        {
            WriteVector(data, position);
        }

        foreach (var normal in mesh.Normals)
        {
            WriteVector(data, normal);
        }

        foreach (var color in mesh.DiffuseColors)
        {
            WriteVector(data, color);
        }

        foreach (var coordinate in mesh.TextureCoordinates)
        {
            WriteVector(data, coordinate);
        }

        foreach (var face in mesh.Faces)
        {
            data.WriteCompressedU32(Shading, (uint)face.ShadingId);
            for (var corner = 0; corner < 3; corner++)
            {
                data.WriteCompressedU32(U3dContext.Static((uint)mesh.Positions.Count), (uint)face.Positions[corner]);
                if (!mesh.ExcludeNormals)
                {
                    data.WriteCompressedU32(U3dContext.Static((uint)mesh.Normals.Count), (uint)face.Normals[corner]);
                }

                if ((mesh.ShadingAttributes[face.ShadingId] & 0x1) != 0)
                {
                    data.WriteCompressedU32(U3dContext.Static((uint)mesh.DiffuseColors.Count), (uint)face.DiffuseColors[corner]);
                }

                for (var layer = 0; layer < mesh.ShadingLayers[face.ShadingId]; layer++)
                {
                    data.WriteCompressedU32(U3dContext.Static((uint)mesh.TextureCoordinates.Count),
                        (uint)face.TextureCoordinates[corner]);
                }
            }
        }

        file.Continue(U3dBlockTypes.MeshBaseContinuation, data);
    }

    /// <summary>A point set of one point at each position, each with a normal and a diffuse colour, each position
    /// split off the one before it. Returns the positions as the quantization leaves them.</summary>
    public static List<Vector3F> PointSet(U3dTestFile file, string name, IReadOnlyList<Vector3F> positions,
        IReadOnlyList<Vector3F> normals, IReadOnlyList<Vector4F> colors, float inverseQuant)
    {
        WriteSetDeclaration(file, U3dBlockTypes.PointSetDeclaration, name, positions.Count, 0x1, inverseQuant);
        var data = file.Data();
        data.WriteString(name);
        data.WriteU32(0);
        data.WriteU32(0);
        data.WriteU32((uint)positions.Count);
        var reconstructed = new List<Vector3F>();
        var reconstructedNormals = new List<Vector3F>();
        var reconstructedColors = new List<Vector4F>();
        for (var position = 0; position < positions.Count; position++)
        {
            var split = Math.Max(0, position - 1);
            WriteNewPosition(data, positions[position], position, reconstructed, inverseQuant);
            var predictedNormal = position == 0 ? Vector3F.Zero : Vector3F.Normalize(reconstructedNormals[split]);
            data.WriteCompressedU32(U3dContext.Dynamic("cNormlCnt"), 1);
            reconstructedNormals.Add(WriteDifference3(data, predictedNormal, normals[position], inverseQuant, "cDiffNormalSign",
                "cDiffNormalX", "cDiffNormalY", "cDiffNormalZ"));
            data.WriteCompressedU32(U3dContext.Dynamic("cPointCnt"), 1);
            data.WriteCompressedU32(Shading, 0);
            data.WriteCompressedU32(U3dContext.Dynamic("cNormlIdx"), 0);
            data.WriteCompressedU8(U3dContext.Dynamic("cDiffDup"), 0);
            var predictedColor = position == 0 ? Vector4F.Zero : reconstructedColors[split];
            reconstructedColors.Add(WriteColor(data, predictedColor, colors[position], inverseQuant));
        }

        file.Continue(U3dBlockTypes.PointSetContinuation, data);
        return reconstructed;
    }

    /// <summary>A polyline: a line from each position to the next, a normal at each position. Returns the positions
    /// as the quantization leaves them.</summary>
    public static List<Vector3F> LineSet(U3dTestFile file, string name, IReadOnlyList<Vector3F> positions,
        IReadOnlyList<Vector3F> normals, float inverseQuant)
    {
        WriteSetDeclaration(file, U3dBlockTypes.LineSetDeclaration, name, positions.Count - 1, 0, inverseQuant);
        var data = file.Data();
        data.WriteString(name);
        data.WriteU32(0);
        data.WriteU32(0);
        data.WriteU32((uint)positions.Count);
        var reconstructed = new List<Vector3F>();
        var reconstructedNormals = new List<Vector3F>();
        for (var position = 0; position < positions.Count; position++)
        {
            WriteNewPosition(data, positions[position], position, reconstructed, inverseQuant);
            var predictedNormal = position >= 2 ? reconstructedNormals[position - 1] : Vector3F.Zero;
            data.WriteCompressedU32(U3dContext.Dynamic("cNormlCnt"), 1);
            reconstructedNormals.Add(WriteDifference3(data, predictedNormal, normals[position], inverseQuant, "cDiffNormalSign",
                "cDiffNormalX", "cDiffNormalY", "cDiffNormalZ"));
            data.WriteCompressedU32(U3dContext.Dynamic("cLineCnt"), position == 0 ? 0u : 1u);
            if (position == 0)
            {
                continue;
            }

            data.WriteCompressedU32(Shading, 0);
            data.WriteCompressedU32(U3dContext.Static((uint)position), (uint)(position - 1));
            data.WriteCompressedU32(U3dContext.Dynamic("cNormlIdx"), 0);
            data.WriteCompressedU32(U3dContext.Dynamic("cNormlIdx"), 0);
        }

        file.Continue(U3dBlockTypes.LineSetContinuation, data);
        return reconstructed;
    }

    public static void Material(U3dTestFile file, string name, Vector3F diffuse, float opacity, float reflectivity)
    {
        var data = file.Data();
        data.WriteString(name);
        data.WriteU32(0x3F);
        WriteVector(data, new Vector3F(0.1f, 0.1f, 0.1f));
        WriteVector(data, diffuse);
        WriteVector(data, new Vector3F(0.2f, 0.3f, 0.4f));
        WriteVector(data, new Vector3F(0.05f, 0.0f, 0.0f));
        data.WriteF32(reflectivity);
        data.WriteF32(opacity);
        file.Declare(U3dBlockTypes.MaterialResource, data);
    }

    public static void Shader(U3dTestFile file, string name, string material, string texture = null)
    {
        var data = file.Data();
        data.WriteString(name);
        data.WriteU32(0x1);
        data.WriteF32(0);
        data.WriteU32(0x617);
        data.WriteU32(0x606);
        data.WriteU32(1);
        data.WriteU32(texture == null ? 0u : 1u);
        data.WriteU32(0);
        data.WriteString(material);
        if (texture != null)
        {
            data.WriteString(texture);
            data.WriteF32(1);
            data.WriteU8(0);
            data.WriteU8(0);
            data.WriteF32(1);
            data.WriteU8(0);
            WriteMatrix(data, Matrix4x4F.Identity);
            WriteMatrix(data, Matrix4x4F.Identity);
            data.WriteU8(0x3);
        }

        file.Declare(U3dBlockTypes.LitTextureShader, data);
    }

    public static void Texture(U3dTestFile file, string name, byte[] png)
    {
        var declaration = file.Data();
        declaration.WriteString(name);
        declaration.WriteU32(2);
        declaration.WriteU32(2);
        declaration.WriteU8(0x0E);
        declaration.WriteU32(1);
        declaration.WriteU8(0x02);
        declaration.WriteU8(0x0E);
        declaration.WriteU16(0);
        declaration.WriteU32((uint)png.Length);
        file.Declare(U3dBlockTypes.TextureDeclaration, declaration);

        var half = png.Length / 2;
        foreach (var part in new[] { png[..half], png[half..] })
        {
            var data = file.Data();
            data.WriteString(name);
            data.WriteU32(0);
            foreach (var value in part)
            {
                data.WriteU8(value);
            }

            file.Continue(U3dBlockTypes.TextureContinuation, data);
        }
    }

    public static void Light(U3dTestFile file, string name, byte type, Vector3F color, float spotAngle, float intensity)
    {
        var data = file.Data();
        data.WriteString(name);
        data.WriteU32(0x1);
        data.WriteU8(type);
        WriteVector(data, color);
        data.WriteF32(1);
        data.WriteF32(1);
        data.WriteF32(0.5f);
        data.WriteF32(0.25f);
        data.WriteF32(spotAngle);
        data.WriteF32(intensity);
        file.Declare(U3dBlockTypes.LightResource, data);
    }

    public static void View(U3dTestFile file, string name)
    {
        var data = file.Data();
        data.WriteString(name);
        data.WriteU32(1);
        data.WriteString(string.Empty);
        data.WriteU32(0);
        data.WriteU32(0);
        WriteVector(data, new Vector4F(0, 0, 0, 1));
        data.WriteF32(1);
        data.WriteF32(100);
        file.Declare(U3dBlockTypes.ViewResource, data);
    }

    public static void WriteParents(U3dTestBitWriter data, IEnumerable<U3dTestParent> parents)
    {
        var list = parents.ToList();
        data.WriteU32((uint)list.Count);
        foreach (var parent in list)
        {
            data.WriteString(parent.Name);
            WriteMatrix(data, parent.Transform);
        }
    }

    private static void WriteSetDeclaration(U3dTestFile file, uint type, string name, int primitives, uint shadingAttributes,
        float inverseQuant)
    {
        var data = file.Data();
        data.WriteString(name);
        data.WriteU32(0);
        data.WriteU32(0);
        data.WriteU32((uint)primitives);
        for (var count = 0; count < 5; count++)
        {
            data.WriteU32(0);
        }

        WriteShadings(data, [shadingAttributes], [0]);
        data.WriteU32(1000);
        data.WriteU32(1000);
        data.WriteU32(1000);
        for (var factor = 0; factor < 5; factor++)
        {
            data.WriteF32(inverseQuant);
        }

        data.WriteU32(0);
        data.WriteU32(0);
        data.WriteU32(0);
        data.WriteU32(0);
        file.Declare(type, data);
    }

    private static void WriteShadings(U3dTestBitWriter data, List<uint> attributes, List<int> layers)
    {
        data.WriteU32((uint)attributes.Count);
        for (var shading = 0; shading < attributes.Count; shading++)
        {
            data.WriteU32(attributes[shading]);
            data.WriteU32((uint)layers[shading]);
            for (var layer = 0; layer < layers[shading]; layer++)
            {
                data.WriteU32(2);
            }

            data.WriteU32((uint)shading);
        }
    }

    private static void WriteQuality(U3dTestBitWriter data)
    {
        data.WriteU32(1000);
        data.WriteU32(1000);
        data.WriteU32(1000);
        for (var factor = 0; factor < 5; factor++)
        {
            data.WriteF32(0.001f);
        }
    }

    private static void WriteNewPosition(U3dTestBitWriter data, Vector3F actual, int current, List<Vector3F> reconstructed,
        float inverseQuant)
    {
        data.WriteCompressedU32(U3dContext.Static(current == 0 ? 1u : (uint)current), (uint)Math.Max(0, current - 1));
        var predicted = current == 0 ? Vector3F.Zero : reconstructed[current - 1];
        reconstructed.Add(WriteDifference3(data, predicted, actual, inverseQuant, "cPosDiffSign", "cPosDiffX", "cPosDiffY",
            "cPosDiffZ"));
    }

    private static Vector3F WriteDifference3(U3dTestBitWriter data, Vector3F predicted, Vector3F actual, float inverseQuant,
        string signs, string xContext, string yContext, string zContext)
    {
        var first = Quantize(predicted.X, actual.X, inverseQuant);
        var second = Quantize(predicted.Y, actual.Y, inverseQuant);
        var third = Quantize(predicted.Z, actual.Z, inverseQuant);
        data.WriteCompressedU8(U3dContext.Dynamic(signs), (byte)(first.SignBit | (second.SignBit << 1) | (third.SignBit << 2)));
        data.WriteCompressedU32(U3dContext.Dynamic(xContext), first.Difference);
        data.WriteCompressedU32(U3dContext.Dynamic(yContext), second.Difference);
        data.WriteCompressedU32(U3dContext.Dynamic(zContext), third.Difference);
        return new Vector3F(first.Reconstructed, second.Reconstructed, third.Reconstructed);
    }

    private static Vector4F WriteColor(U3dTestBitWriter data, Vector4F predicted, Vector4F actual, float inverseQuant)
    {
        var red = Quantize(predicted.X, actual.X, inverseQuant);
        var green = Quantize(predicted.Y, actual.Y, inverseQuant);
        var blue = Quantize(predicted.Z, actual.Z, inverseQuant);
        var alpha = Quantize(predicted.W, actual.W, inverseQuant);
        data.WriteCompressedU8(U3dContext.Dynamic("cDiffuseColorSign"),
            (byte)(red.SignBit | (green.SignBit << 1) | (blue.SignBit << 2) | (alpha.SignBit << 3)));
        data.WriteCompressedU32(U3dContext.Dynamic("cColorDiffR"), red.Difference);
        data.WriteCompressedU32(U3dContext.Dynamic("cColorDiffG"), green.Difference);
        data.WriteCompressedU32(U3dContext.Dynamic("cColorDiffB"), blue.Difference);
        data.WriteCompressedU32(U3dContext.Dynamic("cColorDiffA"), alpha.Difference);
        return new Vector4F(red.Reconstructed, green.Reconstructed, blue.Reconstructed, alpha.Reconstructed);
    }

    private static U3dTestQuantized Quantize(float predicted, float actual, float inverseQuant)
    {
        var negative = predicted > actual;
        var difference = (uint)(MathF.Abs(actual - predicted) / inverseQuant + 0.5f);
        return new U3dTestQuantized
        {
            SignBit = negative ? 1 : 0,
            Difference = difference,
            Reconstructed = predicted + (negative ? -1f : 1f) * difference * inverseQuant
        };
    }

    private static void WriteVector(U3dTestBitWriter data, Vector3F vector)
    {
        data.WriteF32(vector.X);
        data.WriteF32(vector.Y);
        data.WriteF32(vector.Z);
    }

    private static void WriteVector(U3dTestBitWriter data, Vector4F vector)
    {
        data.WriteF32(vector.X);
        data.WriteF32(vector.Y);
        data.WriteF32(vector.Z);
        data.WriteF32(vector.W);
    }

    private static void WriteMatrix(U3dTestBitWriter data, Matrix4x4F matrix)
    {
        foreach (var element in matrix.ToArray())
        {
            data.WriteF32(element);
        }
    }
}

/// <summary>A value quantized against its prediction: the sign, the difference and what a reader rebuilds.</summary>
internal sealed class U3dTestQuantized
{
    public int SignBit { get; init; }

    public uint Difference { get; init; }

    public float Reconstructed { get; init; }
}

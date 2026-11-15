using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Engine.Compiler.Models.U3d;

namespace Adamantium.Engine.CompilerTests;

/// <summary>Writes U3D files for the tests, block by block as ECMA-363 9 lays them out, compressed or in the no
/// compression profile: the file header, the declaration blocks, then the continuation blocks, each padded to four
/// bytes, the header's declaration and file sizes worked out from them.</summary>
internal sealed class U3dTestFile
{
    private readonly List<byte[]> declarations = [];
    private readonly List<byte[]> continuations = [];

    public U3dTestFile(bool compressed, double? unitsScalingFactor = null)
    {
        Compressed = compressed;
        UnitsScalingFactor = unitsScalingFactor;
    }

    public bool Compressed { get; }

    public double? UnitsScalingFactor { get; }

    /// <summary>A writer for one block's data, compressing as the file does.</summary>
    public U3dTestBitWriter Data() => new(!Compressed);

    public void Declare(uint type, U3dTestBitWriter data, byte[] metaData = null) =>
        declarations.Add(Block(type, data.ToArray(), metaData));

    public void DeclareBlock(byte[] block) => declarations.Add(block);

    public void Continue(uint type, U3dTestBitWriter data) => continuations.Add(Block(type, data.ToArray()));

    public void ContinueBlock(byte[] block) => continuations.Add(block);

    /// <summary>A whole block: type, sizes, data, padding, meta data, padding.</summary>
    public static byte[] Block(uint type, byte[] data, byte[] metaData = null)
    {
        metaData ??= [];
        var block = new List<byte>();
        block.AddRange(BitConverter.GetBytes(type));
        block.AddRange(BitConverter.GetBytes((uint)data.Length));
        block.AddRange(BitConverter.GetBytes((uint)metaData.Length));
        block.AddRange(data);
        Pad(block);
        block.AddRange(metaData);
        Pad(block);
        return block.ToArray();
    }

    /// <summary>A meta data section of <paramref name="entries"/>.</summary>
    public static byte[] MetaData(IEnumerable<U3dMetaDataEntry> entries)
    {
        var list = entries.ToList();
        var writer = new U3dTestBitWriter(true);
        writer.WriteU32((uint)list.Count);
        foreach (var entry in list)
        {
            writer.WriteU32(entry.Attributes);
            writer.WriteString(entry.Key);
            if (entry.IsBinary)
            {
                writer.WriteU32((uint)entry.BinaryValue.Length);
                foreach (var value in entry.BinaryValue)
                {
                    writer.WriteU8(value);
                }
            }
            else
            {
                writer.WriteString(entry.Value);
            }
        }

        return writer.ToArray();
    }

    /// <summary>A Modifier Chain block holding <paramref name="modifiers"/>, whole blocks each.</summary>
    public static byte[] ModifierChain(string name, uint chainType, params byte[][] modifiers)
    {
        var header = new U3dTestBitWriter(true);
        header.WriteString(name);
        header.WriteU32(chainType);
        header.WriteU32(0);
        var data = new List<byte>(header.ToArray());
        Pad(data);
        data.AddRange(BitConverter.GetBytes((uint)modifiers.Length));
        foreach (var modifier in modifiers)
        {
            data.AddRange(modifier);
        }

        return Block(U3dBlockTypes.ModifierChain, data.ToArray());
    }

    public byte[] ToArray()
    {
        var profile = (Compressed ? 0u : 0x4u) | (UnitsScalingFactor.HasValue ? 0x8u : 0u);
        var headerSize = 12 + (UnitsScalingFactor.HasValue ? 32 : 24);
        var declarationSize = headerSize + declarations.Sum(block => block.Length);
        var fileSize = declarationSize + continuations.Sum(block => block.Length);
        var header = new U3dTestBitWriter(true);
        header.WriteI16(0);
        header.WriteI16(0);
        header.WriteU32(profile);
        header.WriteU32((uint)declarationSize);
        header.WriteU64((ulong)fileSize);
        header.WriteU32(106);
        if (UnitsScalingFactor is { } units)
        {
            header.WriteF64(units);
        }

        var file = new List<byte>(Block(U3dBlockTypes.FileHeader, header.ToArray()));
        foreach (var block in declarations.Concat(continuations))
        {
            file.AddRange(block);
        }

        return file.ToArray();
    }

    private static void Pad(List<byte> bytes)
    {
        while (bytes.Count % 4 != 0)
        {
            bytes.Add(0);
        }
    }
}

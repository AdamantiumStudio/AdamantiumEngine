using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Parsers.CFF;
using Adamantium.Fonts.Tables.CFF;

namespace Adamantium.Fonts.Extensions
{
    internal static partial class FontStreamExtensions
    {
        public static CFFIndex ReadCffIndex(this FontStreamReader reader, CFFVersion version = CFFVersion.CFF)
        {
            var cffIndex = new CFFIndex();

            if (version == CFFVersion.CFF)
            {
                cffIndex.Count = reader.ReadUInt16();
            }
            else
            {
                cffIndex.Count = reader.ReadUInt32();
            }

            cffIndex.Offsets = new List<uint>();
            cffIndex.DataByOffset = new List<byte[]>();
            if (cffIndex.Count == 0)
            {
                return cffIndex;
            }

            cffIndex.OffsetSize = reader.ReadByte();
            if (cffIndex.OffsetSize is < 1 or > 4 ||
                (cffIndex.Count + 1L) * cffIndex.OffsetSize > reader.Length - reader.Position)
            {
                throw new InvalidDataException("A CFF INDEX is malformed.");
            }

            for (long i = 0; i <= cffIndex.Count; ++i)
            {
                uint offset = 0;
                for (var b = 0; b < cffIndex.OffsetSize; b++)
                {
                    offset = (offset << 8) | reader.ReadByte();
                }

                if (i == 0 ? offset != 1 : offset < cffIndex.Offsets[^1])
                {
                    throw new InvalidDataException("A CFF INDEX has offsets out of order.");
                }

                cffIndex.Offsets.Add(offset);
            }

            var data = reader.ReadBytes(cffIndex.Offsets[^1] - 1L, true);
            for (var i = 1; i < cffIndex.Offsets.Count; ++i)
            {
                var start = (int)cffIndex.Offsets[i - 1] - 1;
                var end = (int)cffIndex.Offsets[i] - 1;
                cffIndex.DataByOffset.Add(data.AsSpan(start, end - start).ToArray());
            }

            return cffIndex;
        }

        public static List<FontDict> ReadFDArray(this FontStreamReader otfTtfReader, long cffOffset, uint fdArrayOffset, CFFFont font)
        {
            otfTtfReader.Position = cffOffset + fdArrayOffset;

            var fontDictIndex = otfTtfReader.ReadCffIndex(font.CffVersion);
            var fontDicts = new List<FontDict>();
            
            for (int i = 0; i < fontDictIndex.Count; i++)
            {
                var dictParser = new DictOperandParser(fontDictIndex.DataByOffset[i], font);
                var result = dictParser.GetAllAvailableOperands();

                int name = 0;
                int offset = 0;
                int size = 0;

                foreach (var operandResult in result.Results)
                {
                    switch (operandResult.Key)
                    {
                        case DictOperatorsType.FontName:
                            name = operandResult.Value.AsInt();
                            break;
                        case DictOperatorsType.Private: // private DICT
                            var lst = operandResult.Value.AsList();
                            size = (int) lst[0];
                            offset = (int) lst[1];
                            break;
                    }
                }

                var fontDict = new FontDict(size, offset);
                fontDict.FontName = name;
                fontDicts.Add(fontDict);
            }

            foreach (var fontDict in fontDicts)
            {
                otfTtfReader.Position = cffOffset + fontDict.PrivateDictOffset;
                var bytes = otfTtfReader.ReadBytes(fontDict.PrivateDictSize, true);
                var dictParser = new DictOperandParser(bytes, font);
                var result = dictParser.GetAllAvailableOperands();

                foreach (var operandResult in result.Results)
                {
                    switch (operandResult.Key)
                    {
                        case DictOperatorsType.Subrs:
                            var localSubrsOffset = operandResult.Value.AsInt();
                            otfTtfReader.Position = cffOffset + fontDict.PrivateDictOffset + localSubrsOffset;
                            var offsets = otfTtfReader.ReadCffIndex(font.CffVersion);
                            fontDict.LocalSubr = offsets.DataByOffset;
                            break;
                        case DictOperatorsType.vsindex:
                            fontDict.VariationStoreIndex = operandResult.Value.AsInt();
                            break;
                    }
                }
            }

            return fontDicts;
        }
        
        public static void ReadFDSelect(this FontStreamReader reader, CFFFont font, int charStringCount)
        {
            byte format = reader.ReadByte();
            font.CIDFontInfo.FdSelectFormat = format;
            switch (format)
            {
                case 0:
                    font.CIDFontInfo.FdRanges0 = reader.ReadBytes(charStringCount, true);
                    break;
                case 3:
                case 4:
                    uint rangesCount = 0;
                    rangesCount = format == 3 ? reader.ReadUInt16() : reader.ReadUInt32();
                    if (rangesCount * (format == 3 ? 3L : 6L) > reader.Length - reader.Position)
                    {
                        throw new InvalidDataException("The FDSelect ranges run past the end of the font.");
                    }

                    font.CIDFontInfo.FdRanges = new FDRange[rangesCount+1];
                    for (int i = 0; i < rangesCount; i++)
                    {
                        var range = format == 3
                            ? new FDRange(reader.ReadUInt16(), reader.ReadByte())
                            : new FDRange(reader.ReadUInt32(), reader.ReadUInt16());
                        font.CIDFontInfo.FdRanges[i] = range;
                    }

                    uint first = format == 3 ? reader.ReadUInt16() : reader.ReadUInt32();
                    // sentinel
                    font.CIDFontInfo.FdRanges[rangesCount] = new FDRange(first, 0);
                    break;
                default:
                    throw new InvalidDataException($"FDSelect has format {format}, which the CFF formats do not define.");
            }
        }
    }
}
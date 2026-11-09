using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Tables.CFF;
using Adamantium.Fonts.Tables.CMAP;

namespace Adamantium.Fonts.Extensions
{
    internal static partial class FontStreamExtensions
    {
        public static CharacterMapFormat0 ReadCharacterMapFormat0(this FontStreamReader reader)
                {
                    var format0 = new CharacterMapFormat0
                    {
                        Length = reader.ReadUInt16(),
                        Language = reader.ReadUInt16(),
                        GlyphIdArray = reader.ReadBytes(256, true)
                    };
                    return format0;
                }

                public static CharacterMapFormat2 ReadCharacterMapFormat2(this FontStreamReader reader)
                {
                    var format2 = new CharacterMapFormat2
                    {
                        Length = reader.ReadUInt16(),
                        Language = reader.ReadUInt16(),
                    };
                    var highestKey = 0;
                    for (var i = 0; i < format2.SubheaderKeys.Length; i++)
                    {
                        format2.SubheaderKeys[i] = reader.ReadUInt16();
                        highestKey = Math.Max(highestKey, format2.SubheaderKeys[i]);
                    }

                    var subHeaderCount = highestKey / 8 + 1;
                    reader.EnsureAvailable(subHeaderCount, 8);
                    format2.SubHeaders = new CharacterMapSubHeader[subHeaderCount];
                    for (var i = 0; i < subHeaderCount; i++)
                    {
                        format2.SubHeaders[i] = new CharacterMapSubHeader
                        {
                            FirstCode = reader.ReadUInt16(),
                            EntryCount = reader.ReadUInt16(),
                            IdDelta = reader.ReadInt16(),
                            IdRangeOffset = reader.ReadUInt16(),
                        };
                    }

                    var glyphBytes = format2.Length - (6 + 2 * 256 + 8 * subHeaderCount);
                    if (glyphBytes < 0)
                    {
                        throw new InvalidDataException("A cmap format 2 subtable is shorter than its subheaders.");
                    }

                    format2.GlyphIdArray = reader.ReadUInt16Array(glyphBytes / 2);
                    return format2;
                }

                public static CharacterMapFormat4 ReadCharacterMapFormat4(this FontStreamReader reader)
                {
                    var format4 = new CharacterMapFormat4();
                    format4.Length = reader.ReadUInt16();

                    var tableEndAt = reader.Position - 4 + format4.Length;
        
                    format4.Language = reader.ReadUInt16();
                    format4.SegCountX2 = reader.ReadUInt16();
                    format4.SegCount = (UInt16) (format4.SegCountX2 / 2);
                    format4.SearchRange = reader.ReadUInt16();
                    format4.EntrySelector = reader.ReadUInt16();
                    format4.RangeShift = reader.ReadUInt16();
        
                    format4.EndCode = reader.ReadUInt16Array(format4.SegCount);
                    format4.ReservedPad = reader.ReadUInt16();
                    format4.StartCode = reader.ReadUInt16Array(format4.SegCount);
                    format4.IdDelta = reader.ReadInt16Array(format4.SegCount);
                    format4.IdRangeOffsets = reader.ReadUInt16Array(format4.SegCount);
        
                    var remainingLength = tableEndAt - reader.Position;
                    if (remainingLength < 0)
                    {
                        throw new InvalidDataException("A cmap format 4 subtable is shorter than its segments.");
                    }

                    format4.GlyphIdArray = reader.ReadUInt16Array((int)(remainingLength / 2));
        
                    return format4;
                }
        
                public static CharacterMapFormat6 ReadCharacterMapFormat6(this FontStreamReader reader)
                {
                    var format6 = new CharacterMapFormat6
                    {
                        Length = reader.ReadUInt16(),
                        Language = reader.ReadUInt16(),
                        FirstCode = reader.ReadUInt16(),
                        EntryCount = reader.ReadUInt16()
                    };
                    format6.GlyphIdArray = reader.ReadUInt16Array(format6.EntryCount);
        
                    return format6;
                }

                public static CharacterMapFormat8 ReadCharacterMapFormat8(this FontStreamReader reader)
                {
                    var format8 = new CharacterMapFormat8
                    {
                        Reserved = reader.ReadUInt16(),
                        Length = reader.ReadUInt32(),
                        Language = reader.ReadUInt32(),
                    };
                    reader.ReadBytes(format8.Is32.Length, true).CopyTo(format8.Is32, 0);
                    format8.NumGroups = reader.ReadUInt32();
                    reader.EnsureAvailable(format8.NumGroups, 12);
                    var groups = new SequentialMapGroup[format8.NumGroups];
                    for (var i = 0; i < groups.Length; ++i)
                    {
                        groups[i].StartCharCode = reader.ReadUInt32();
                        groups[i].EndCharCode = reader.ReadUInt32();
                        groups[i].StartGlyphId = reader.ReadUInt32();
                    }

                    format8.Groups = groups.OrderBy(g => g.StartCharCode).ToArray();
                    return format8;
                }

                public static CharacterMapFormat10 ReadCharacterMapFormat10(this FontStreamReader reader)
                {
                    var format10 = new CharacterMapFormat10
                    {
                        Reserved = reader.ReadUInt16(),
                        Length = reader.ReadUInt32(),
                        Language = reader.ReadUInt32(),
                        StartCharCode = reader.ReadUInt32(),
                        NumChars = reader.ReadUInt32(),
                    };
                    format10.GlyphIdArray = reader.ReadUInt16Array((int)Math.Min(format10.NumChars, int.MaxValue));
                    return format10;
                }

                public static CharacterMapFormat12 ReadCharacterMapFormat12(this FontStreamReader reader)
                {
                    var format12 = new CharacterMapFormat12();
                    format12.Reserved = reader.ReadUInt16();
                    format12.Length = reader.ReadUInt32();
                    format12.Language = reader.ReadUInt32();
                    format12.NumGroups = reader.ReadUInt32();
                    reader.EnsureAvailable(format12.NumGroups, 12);
                    format12.Groups = new SequentialMapGroup[format12.NumGroups];
        
                    for (int i = 0; i < format12.Groups.Length; ++i)
                    {
                        format12.Groups[i].StartCharCode = reader.ReadUInt32();
                        format12.Groups[i].EndCharCode = reader.ReadUInt32();
                        format12.Groups[i].StartGlyphId = reader.ReadUInt32();
                    }
        
                    format12.StartCharCodes = format12.Groups.Select(x => x.StartCharCode).ToArray();
                    format12.EndCharCodes = format12.Groups.Select(x => x.EndCharCode).ToArray();
                    format12.StartGlyphIds = format12.Groups.Select(x => x.StartGlyphId).ToArray();
        
                    return format12;
                }
        
                public static CharacterMapFormat13 ReadCharacterMapFormat13(this FontStreamReader reader)
                {
                    var format13 = new CharacterMapFormat13();
                    format13.Reserved = reader.ReadUInt16();
                    format13.Length = reader.ReadUInt32();
                    format13.Language = reader.ReadUInt32();
                    format13.NumGroups = reader.ReadUInt32();
                    reader.EnsureAvailable(format13.NumGroups, 12);
                    format13.Groups = new ConstantMapGroup[format13.NumGroups];
        
                    for (int i = 0; i < format13.Groups.Length; ++i)
                    {
                        format13.Groups[i].StartCharCode = reader.ReadUInt32();
                        format13.Groups[i].EndCharCode = reader.ReadUInt32();
                        format13.Groups[i].GlyphId = reader.ReadUInt32();
                    }
        
                    return format13;
                }
        
                public static CharacterMapFormat14 ReadCharacterMapFormat14(this FontStreamReader reader)
                {
                    var startPos = reader.Position - 2;
                    var format14 = new CharacterMapFormat14();
                    format14.Length = reader.ReadUInt32();
                    format14.NumVarSelectorRecords = reader.ReadUInt32();
                    reader.EnsureAvailable(format14.NumVarSelectorRecords, 11);
                    var records = new VariationSelectorRecord[format14.NumVarSelectorRecords];
        
                    for (int i = 0; i < format14.NumVarSelectorRecords; ++i)
                    {
                        records[i].VarSelector = reader.ReadUInt24();
                        records[i].DefaultUVSOffset = reader.ReadUInt32();
                        records[i].NonDefaultUVSOffset = reader.ReadUInt32();
                    }
        
                    for (int i = 0; i < format14.NumVarSelectorRecords; ++i)
                    {
                        var selector = new VariationSelector();
                        var record = records[i];
                        if (record.DefaultUVSOffset != 0)
                        {
                            reader.Position = startPos + record.DefaultUVSOffset;
                            uint numUnicodeValueRanges = reader.ReadUInt32();
                            for (int x = 0; x < numUnicodeValueRanges; ++x)
                            {
                                var startCode = reader.ReadUInt24();
                                selector.DefaultStartCodes.Add(startCode);
                                selector.DefaultEndCodes.Add(startCode + reader.ReadByte());
                            }
                        }
        
                        if (record.NonDefaultUVSOffset != 0)
                        {
                            reader.Position = startPos + record.NonDefaultUVSOffset;
                            uint numUVSMappings = reader.ReadUInt32();
                            for (int x = 0; x < numUVSMappings; ++x)
                            {
                                var unicodeValue = reader.ReadUInt24();
                                var glyphId = reader.ReadUInt16();
                                selector.UVSMappings[unicodeValue] = glyphId;
                            }
                        }
        
                        format14.VarSelectors[record.VarSelector] = selector;
                    }
        
                    return format14;
                }
    }
}
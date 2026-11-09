using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Extensions;
using Adamantium.Fonts.Tables.CFF;

namespace Adamantium.Fonts.Parsers.CFF
{
    internal class CFFParser : ICFFParser
    {
        private FontStreamReader otfTtfReader;
        private long cffOffset;

        private CFFFontSet fontSet;
        
        // TABLES
        private CFFHeader cffHeader;
        private CFFIndex cffNameIndex;
        private CFFIndex cffTopDictIndex;
        private CFFIndex cffStringIndex;

        public CFFParser(long cffOffset, FontStreamReader ttfReader)
        {
            this.cffOffset = cffOffset;
            otfTtfReader = ttfReader;
            fontSet = new CFFFontSet();
        }

        public CFFIndex GlobalSubroutineIndex { get; private set; }
        public int GlobalSubrBias { get; private set; }
        CFFFont ICFFParser.Parse()
        {
            ReadCFFHeader();
            ReadNameIndex();
            ReadTopDictIndex();
            ReadStringIndex();
            ReadGlobalSubroutineIndex();
            foreach (var font in fontSet.Fonts)
            {
                ReadFDSelect(font);
                ReadFDArray(font);
                ReadLocalSubroutineIndex(font);
                ReadCharStringsIndex(font);
                ReadCharsets(font);
                ReadEncoding(font);
            }

            if (fontSet.Fonts.Count > 1)
            {
                Debug.WriteLine($"Warning! File has more than one font");
            }

            return fontSet.Fonts.FirstOrDefault();
        }

        
        private void ReadCFFHeader()
        {
            cffHeader = new CFFHeader();
            otfTtfReader.Position = cffOffset;

            cffHeader.Major = otfTtfReader.ReadByte();
            cffHeader.Minor = otfTtfReader.ReadByte();
            cffHeader.HeaderSize = otfTtfReader.ReadByte();
            cffHeader.OffsetSize = otfTtfReader.ReadByte();

            // if there are 4 'magic' bytes - fill header size as 4 bytes
            if (cffHeader.Major == 0x5F &&
                cffHeader.Minor == 0x0F &&
                cffHeader.HeaderSize == 0x3C &&
                cffHeader.OffsetSize == 0xF5)
            {
                cffHeader.HeaderSize = 4;
            }
        }

        private void ReadNameIndex()
        {
            // set position to the start of Name Index (it is start of CFF data + size of CFF header)
            otfTtfReader.Position = cffOffset;
            otfTtfReader.Position += cffHeader.HeaderSize;

            cffNameIndex = otfTtfReader.ReadCffIndex();

            for (int i = 0; i < cffNameIndex.Count; ++i)
            {
                var font = new CFFFont(fontSet, CFFVersion.CFF);
                var name = Encoding.UTF8.GetString(cffNameIndex.DataByOffset[i]);
                font.Name = name;
                fontSet.AddFont(font);
            }
        }

        private void ReadTopDictIndex()
        {
            // set position to the start of Top Dict Index (it is start of CFF data + size of CFF header + data in Name Index)
            otfTtfReader.Position = cffOffset;
            otfTtfReader.Position += cffHeader.HeaderSize;
            otfTtfReader.Position += cffNameIndex.GetLength();

            cffTopDictIndex = otfTtfReader.ReadCffIndex();

            for (int i = 0; i < cffTopDictIndex.Count; ++i)
            {
                fontSet.Fonts[i].ParseTopDict(cffTopDictIndex.DataByOffset[i]);
            }
        }

        private void ReadStringIndex()
        {
            // set position to the start of String Dict Index (it is start of CFF data + size of CFF header + data in Name Index + data in Top Dict Index)
            otfTtfReader.Position = cffOffset;
            otfTtfReader.Position += cffHeader.HeaderSize;
            otfTtfReader.Position += cffNameIndex.GetLength();
            otfTtfReader.Position += cffTopDictIndex.GetLength();

            cffStringIndex = otfTtfReader.ReadCffIndex();

            var uniqueStrings = new List<string>();

            for (int i = 0; i < cffStringIndex.DataByOffset.Count; ++i)
            {
                uniqueStrings.Add(Encoding.UTF8.GetString(cffStringIndex.DataByOffset[i]));
            }

            fontSet.SetUniqueStrings(uniqueStrings);
        }

        private void ReadGlobalSubroutineIndex()
        {
            // set position to the start of String Dict Index (it is start of CFF data + size of CFF header + data in Name Index + data in Top Dict Index + data in String Index)
            otfTtfReader.Position = cffOffset;
            otfTtfReader.Position += cffHeader.HeaderSize;
            otfTtfReader.Position += cffNameIndex.GetLength();
            otfTtfReader.Position += cffTopDictIndex.GetLength();
            otfTtfReader.Position += cffStringIndex.GetLength();

            GlobalSubroutineIndex = otfTtfReader.ReadCffIndex();

            GlobalSubrBias = this.CalculateSubrBias(GlobalSubroutineIndex.Count);
        }

        /// <summary>
        /// The FDSelect associates an FD(Font DICT) with a glyph by
        /// specifying an FD index for that glyph. The FD index is used to
        /// access one of the Font DICTs stored in the Font DICT INDEX.
        /// </summary>
        /// <param name="font"></param>
        private void ReadFDSelect(CFFFont font)
        {
            if (font.CIDFontInfo.FDSelect == 0) return;

            var charStringCount = ReadCharStringIndexCount(font);

            otfTtfReader.Position = cffOffset + font.CIDFontInfo.FDSelect;
            otfTtfReader.ReadFDSelect(font, charStringCount);
        }

        private void ReadFDArray(CFFFont font)
        {
            if (font.CIDFontInfo.FDArray == 0) return;

            otfTtfReader.Position = cffOffset + font.CIDFontInfo.FDArray;

            font.CIDFontDicts = otfTtfReader.ReadFDArray(cffOffset, (uint)font.CIDFontInfo.FDArray, font);
        }

        private void ReadLocalSubroutineIndex(CFFFont font)
        {
            // set position to the start of Private DICT data (it is start of CFF data + offset to Private DICT data from Top DICT)
            otfTtfReader.Position = cffOffset;
            var topDictPrivateEntry = font.GetTopDictOperatorValue(DictOperatorsType.Private).AsNumberNumber();
            otfTtfReader.Position += (int) topDictPrivateEntry.Number2;

            // save the beginning of Private DICT data start for later use (e.g for local Subrs offset)
            var privateDictDataStart = otfTtfReader.Position;

            List<byte> privateDictRawData = new List<byte>();

            // read the whole Private DICT data
            privateDictRawData.AddRange(otfTtfReader.ReadBytes((int) topDictPrivateEntry.Number1));
            privateDictRawData.Reverse();

            font.ParsePrivateDict(privateDictRawData.ToArray());

            if (!font.IsLocalSubroutineAvailable)
                return; // there is no local subroutines for this font

            // get the offset to local subroutines
            var localSubrOffset = font.GetPrivateDictOperatorValue(DictOperatorsType.Subrs).AsInt();

            // fill local subrs index structure
            otfTtfReader.Position = privateDictDataStart + localSubrOffset;

            font.LocalSubroutineIndex = otfTtfReader.ReadCffIndex();

            font.LocalSubrBias = this.CalculateSubrBias(font.LocalSubroutineIndex.Count);
        }

        private ushort ReadCharStringIndexCount(CFFFont font)
        {
            otfTtfReader.Position = cffOffset;
            otfTtfReader.Position += font.GetTopDictOperatorValue(DictOperatorsType.CharStrings).AsInt();
            var count = otfTtfReader.ReadUInt16();

            return count;
        }

        private void ReadCharStringsIndex(CFFFont font)
        {
            // set position to the start of CharStrings (it is start of CFF data + offset to CharStrings from Top DICT)
            otfTtfReader.Position = cffOffset;
            otfTtfReader.Position += font.GetTopDictOperatorValue(DictOperatorsType.CharStrings).AsInt();
            
            font.CharStringsIndex = otfTtfReader.ReadCffIndex();

            var count = font.CharStringsIndex.DataByOffset.Count;
            if (count == 0)
            {
                throw new InvalidDataException("The CFF font has no glyphs, not even .notdef.");
            }

            var glyphs = new Glyph[count];
            var fontDicts = new FontDict[count];
            var source = new CFFGlyphOutlineSource(this, font, fontDicts, otfTtfReader.Length);
            var fdArraySelector = new FontDictArraySelector(font.CIDFontInfo);

            for (var i = 0; i < count; ++i)
            {
                var glyph = Glyph.Create((uint)i, OutlineType.CompactFontFormat);
                glyphs[i] = glyph;
                try
                {
                    if (font.IsCIDFont)
                    {
                        fontDicts[i] = font.CIDFontDicts[fdArraySelector.SelectFontDictArray((uint)i)];
                    }

                    glyph.SetOutlineSource(source);
                }
                catch (Exception)
                {
                    glyph.IsInvalid = true;
                }
            }

            font.SetGlyphs(glyphs);
        }

        private void ReadCharsets(CFFFont font)
        {
            var charset = font.GetTopDictOperatorValue(DictOperatorsType.charset).AsInt();
            font.GetGlyphByIndex(0).Name = fontSet.GetStringBySid(0);
            if (charset < 0 || cffOffset + charset >= otfTtfReader.Length)
            {
                return;
            }

            if (charset <= 2)
            {
                var predefined = charset switch
                {
                    1 => CffPredefinedSets.ExpertCharset.Length,
                    2 => CffPredefinedSets.ExpertSubsetCharset.Length,
                    _ => 229,
                };
                for (var index = 1; index < font.Glyphs.Count && index < predefined; index++)
                {
                    var glyph = font.GetGlyphByIndex((uint)index);
                    glyph.SID = charset switch
                    {
                        1 => CffPredefinedSets.ExpertCharset[index],
                        2 => CffPredefinedSets.ExpertSubsetCharset[index],
                        _ => (uint)index,
                    };
                    glyph.Name = fontSet.GetStringBySid(glyph.SID);
                }

                return;
            }

            otfTtfReader.Position = cffOffset + charset;
            var format = otfTtfReader.ReadByte();
            switch (format)
            {
                case 0:
                    ReadCharsetFormat0(font);
                    break;
                case 1:
                    ReadCharsetFormat1Or2(font, true);
                    break;
                case 2:
                    ReadCharsetFormat1Or2(font, false);
                    break;
            }
        }

        private void ReadCharsetFormat0(CFFFont font)
        {
            for (var index = 1; index < font.Glyphs.Count && otfTtfReader.Position + 2 <= otfTtfReader.Length; index++)
            {
                var glyph = font.GetGlyphByIndex((uint)index);
                glyph.SID = otfTtfReader.ReadUInt16();
                glyph.Name = font.IsCIDFont ? null : fontSet.GetStringBySid(glyph.SID);
            }
        }
        
        private void ReadCharsetFormat1Or2(CFFFont font, bool isFormat1)
        {
            for (var glyphIndex = 1; glyphIndex < font.Glyphs.Count;)
            {
                if (otfTtfReader.Position + (isFormat1 ? 3 : 4) > otfTtfReader.Length)
                {
                    return;
                }

                uint sid = otfTtfReader.ReadUInt16();
                int count = (isFormat1 ? otfTtfReader.ReadByte() : otfTtfReader.ReadUInt16()) + 1; // +1 because first glyph is not included in count, but we need to store it below anyway

                while (count-- > 0 && glyphIndex < font.Glyphs.Count)
                {
                    var glyph = font.GetGlyphByIndex((uint)glyphIndex++);
                    glyph.SID = sid++; // each SID range in formats 1 and 2 are just sequential SIDs, so just increment initial SID 'count' times, no need to read anything from font file till next range
                    glyph.Name = font.IsCIDFont ? null : fontSet.GetStringBySid(glyph.SID);
                }
            }
        }

        private void ReadEncoding(CFFFont font)
        {
            var codes = new int[256];
            Array.Fill(codes, -1);
            font.Encoding = codes;
            if (font.IsCIDFont)
            {
                return;
            }

            var encoding = font.GetTopDictOperatorValue(DictOperatorsType.Encoding).AsInt();
            if (encoding == 0)
            {
                StandardCodes(font, codes);
                return;
            }

            if (encoding == 1)
            {
                var bySid = GlyphsBySid(font);
                for (var code = 0; code < codes.Length; code++)
                {
                    var sid = CffPredefinedSets.ExpertEncoding[code];
                    if (sid != 0 && bySid.TryGetValue(sid, out var index))
                    {
                        codes[code] = index;
                    }
                }

                return;
            }

            if (encoding < 0 || cffOffset + encoding + 2 > otfTtfReader.Length)
            {
                return;
            }

            otfTtfReader.Position = cffOffset + encoding;

            var format = otfTtfReader.ReadByte();
            var glyphIndex = 1;
            if ((format & 0x7F) == 0)
            {
                var count = otfTtfReader.ReadByte();
                for (var i = 0; i < count && otfTtfReader.Position < otfTtfReader.Length; i++, glyphIndex++)
                {
                    var code = otfTtfReader.ReadByte();
                    if (glyphIndex < font.Glyphs.Count)
                    {
                        codes[code] = glyphIndex;
                    }
                }
            }
            else if ((format & 0x7F) == 1)
            {
                var ranges = otfTtfReader.ReadByte();
                for (var r = 0; r < ranges && otfTtfReader.Position + 2 <= otfTtfReader.Length; r++)
                {
                    int first = otfTtfReader.ReadByte();
                    int left = otfTtfReader.ReadByte();
                    for (var code = first; code <= first + left && code < 256 && glyphIndex < font.Glyphs.Count;
                         code++, glyphIndex++)
                    {
                        codes[code] = glyphIndex;
                    }
                }
            }

            if ((format & 0x80) != 0 && otfTtfReader.Position < otfTtfReader.Length)
            {
                ReadSupplements(font, codes);
            }
        }

        private static void StandardCodes(CFFFont font, int[] codes)
        {
            var byName = new Dictionary<string, int>();
            foreach (var glyph in font.Glyphs)
            {
                if (glyph.Name != null)
                {
                    byName.TryAdd(glyph.Name, (int)glyph.Index);
                }
            }

            for (var code = 0; code < 256; code++)
            {
                var name = Encodings.StandardEncoding.NameOf(code);
                if (name != null && byName.TryGetValue(name, out var index))
                {
                    codes[code] = index;
                }
            }
        }

        private static Dictionary<uint, int> GlyphsBySid(CFFFont font)
        {
            var bySid = new Dictionary<uint, int>();
            foreach (var glyph in font.Glyphs)
            {
                bySid.TryAdd(glyph.SID, (int)glyph.Index);
            }

            return bySid;
        }

        private void ReadSupplements(CFFFont font, int[] codes)
        {
            var bySid = GlyphsBySid(font);
            var supplements = otfTtfReader.ReadByte();
            for (var s = 0; s < supplements && otfTtfReader.Position + 3 <= otfTtfReader.Length; s++)
            {
                var code = otfTtfReader.ReadByte();
                var sid = otfTtfReader.ReadUInt16();
                if (bySid.TryGetValue(sid, out var index))
                {
                    codes[code] = index;
                }
            }
        }
    }
}
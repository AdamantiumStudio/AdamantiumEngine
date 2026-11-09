using System;
using System.Collections.Generic;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Extensions;
using Adamantium.Fonts.Tables.CFF;

namespace Adamantium.Fonts.Parsers.CFF
{
    internal class CFF2Parser : ICFFParser
    {
        private FontStreamReader otfTtfReader;
        private long cffOffset;
        
        private CFFHeader cffHeader;
        private CFFFontSet fontSet;

        public CFF2Parser(long cffOffset, FontStreamReader ttfReader)
        {
            this.cffOffset = cffOffset;
            otfTtfReader = ttfReader;
            fontSet = new CFFFontSet();
        }

        public IReadOnlyCollection<Glyph> Glyphs { get; }

        public CFFIndex GlobalSubroutineIndex { get; private set; }
        public int GlobalSubrBias { get; private set; }

        private UInt32 charstringOffset;
        private UInt32 fdArrayOffset;
        private UInt32 fdSelectOffset;
        private UInt32 variationStoreOffset;
        private CFFFont cffFont;

        public CFFFont Parse()
        {
            cffFont = new CFFFont(fontSet, CFFVersion.CFF2) {IsLocalSubroutineAvailable = false};
            ReadHeader();
            ReadTopDict();
            ReadGlobalSubrIndex();
            ReadVariationStore();
            ReadFDArray();
            ReadFDSelect();
            ReadCharstringIndex();

            return cffFont;
        }

        protected virtual void ReadHeader()
        {
            cffHeader = new CFFHeader();
            otfTtfReader.Position = cffOffset;

            cffHeader.Major = otfTtfReader.ReadByte();
            cffHeader.Minor = otfTtfReader.ReadByte();
            cffHeader.HeaderSize = otfTtfReader.ReadByte();
            cffHeader.TopDictLength = otfTtfReader.ReadUInt16();
        }

        protected virtual void ReadTopDict()
        {
            var data = otfTtfReader.ReadBytes(cffHeader.TopDictLength, true);
            var operandParser = new DictOperandParser(data, cffFont);
            var result = operandParser.GetAllAvailableOperands();

            foreach (var operandResult in result.Results)
            {
                switch (operandResult.Key)
                {
                    case DictOperatorsType.CharStrings:
                        charstringOffset = operandResult.Value.AsUInt();
                        break;
                    case DictOperatorsType.vstore:
                        variationStoreOffset = operandResult.Value.AsUInt();
                        break;
                    case DictOperatorsType.FDArray:
                        fdArrayOffset = operandResult.Value.AsUInt();
                        break;
                    case DictOperatorsType.FDSelect:
                        fdSelectOffset = operandResult.Value.AsUInt();
                        break;
                }
            }
        }

        private void ReadGlobalSubrIndex()
        {
            otfTtfReader.Position = cffOffset + cffHeader.HeaderSize + cffHeader.TopDictLength;
            GlobalSubroutineIndex = otfTtfReader.ReadCffIndex(CFFVersion.CFF2);
            
            if (GlobalSubroutineIndex.Count == 0) return;

            GlobalSubrBias = this.CalculateSubrBias(GlobalSubroutineIndex.Count);
        }

        private void ReadVariationStore()
        {
            if (variationStoreOffset == 0) return;

            cffFont.VariationStore = otfTtfReader.ReadItemVariationStore(cffOffset + variationStoreOffset + 2);
        }

        private void ReadFDArray()
        {
            if (fdArrayOffset == 0) return;
            
            otfTtfReader.Position = cffOffset + fdArrayOffset;
            cffFont.CIDFontDicts = otfTtfReader.ReadFDArray(cffOffset, fdArrayOffset, cffFont);
        }

        private void ReadFDSelect()
        {
            if (cffFont.CIDFontDicts.Count <= 1 && fdSelectOffset == 0) return;

            var charStringCount = ReadCharStringIndexCount();
            otfTtfReader.Position = cffOffset + fdSelectOffset;
            otfTtfReader.ReadFDSelect(cffFont, (int)charStringCount);
        }

        private UInt32 ReadCharStringIndexCount()
        {
            otfTtfReader.Position = cffOffset + charstringOffset;
            
            var count = otfTtfReader.ReadUInt32();
            return count;
        }

        private void ReadCharstringIndex()
        {
            otfTtfReader.Position = cffOffset + charstringOffset;

            var charstringIndex = otfTtfReader.ReadCffIndex(CFFVersion.CFF2);
            cffFont.CharStringsIndex = charstringIndex;
            
            var count = cffFont.CharStringsIndex.DataByOffset.Count;
            var glyphs = new Glyph[count];
            var fontDicts = new FontDict[count];
            var source = new CFFGlyphOutlineSource(this, cffFont, fontDicts, otfTtfReader.Length);
            cffFont.OutlineSource = source;
            var fdArraySelector = new FontDictArraySelector(cffFont.CIDFontInfo);

            for (var i = 0; i < count; ++i)
            {
                var glyph = Glyph.Create((uint)i, OutlineType.CompactFontFormat);
                glyphs[i] = glyph;
                try
                {
                    if (cffFont.IsCIDFont)
                    {
                        fontDicts[i] = cffFont.CIDFontDicts[fdArraySelector.SelectFontDictArray((uint)i)];
                    }
                    else if (cffFont.CIDFontDicts.Count == 1)
                    {
                        fontDicts[i] = cffFont.CIDFontDicts[0];
                    }

                    glyph.SetOutlineSource(source);
                }
                catch (Exception)
                {
                    glyph.IsInvalid = true;
                }
            }

            cffFont.SetGlyphs(glyphs);
        }
    }
}
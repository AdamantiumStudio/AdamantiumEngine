using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Exceptions;
using Adamantium.Fonts.Extensions;
using Adamantium.Fonts.Parsers.CFF;
using Adamantium.Fonts.Tables;
using Adamantium.Fonts.Tables.CFF;
using Adamantium.Fonts.Tables.GPOS;
using Adamantium.Fonts.Tables.GSUB;
using Adamantium.Fonts.Tables.Layout;

namespace Adamantium.Fonts.Parsers
{
    internal class OpenTypeParser : SfntParser
    {
        private TTCHeader ttcHeader;

        // list of common mandatory tables
        private static ReadOnlyCollection<string> commonMandatoryTables;

        // list of TrueType outlines mandatory tables
        private static ReadOnlyCollection<string> trueTypeMandatoryTables;

        // list of CFF outlines mandatory tables
        private static Dictionary<string, string> cffMandatoryTables;

        // CFF table version (null if not OTF outlines format)
        private CFFVersion? CFFVersion;

        private ICFFParser cffParser;

        private CFFFont cffFont;

        private byte[] bitmapLocations;

        private byte[] bitmapImages;

        private Font bitmapFont;

        static OpenTypeParser()
        {
            commonMandatoryTables = new ReadOnlyCollection<string>(new List<string>
            {
                "cmap",
                "head",
                "hhea",
                "hmtx",
                "maxp",
                "name",
                "OS/2",
                "post"
            });

            trueTypeMandatoryTables = new ReadOnlyCollection<string>(new List<string>
            {
                "glyf",
                "loca"
            });

            cffMandatoryTables = new Dictionary<string, string>()
            {
                {"CFF", "CFF "},
                {"CFF2", "CFF2"}
            };
        }

        protected internal OpenTypeParser()
        {

        }

        protected internal OpenTypeParser(string filePath, byte resolution = 1) : base(filePath, resolution)
        {
        }

        protected internal OpenTypeParser(FontStreamReader fontStreamReader, byte resolution = 0, params TableDirectory[] tableDirectories)
            : base(fontStreamReader, resolution, tableDirectories)
        {
        }

        /// <summary>A font program embedded in a document (PDF FontFile2): only head and the outline tables must be
        /// there, since the document maps characters and gives names and metrics itself.</summary>
        internal bool IsEmbeddedProgram { get; init; }

        protected override bool ReadsTable(string name) => !IsEmbeddedProgram || name is TableNames.head or
            TableNames.maxp or TableNames.loca or TableNames.glyf or TableNames.hhea or TableNames.hmtx or
            TableNames.cmap or TableNames.post;

        protected override bool CanDoWithout(string name) => IsEmbeddedProgram && name is TableNames.hhea or
            TableNames.hmtx or TableNames.cmap or TableNames.post;

        public override void Parse()
        {
            // 1st step - check if this is a single font or collection
            IsOTFCollection();

            // reset stream position
            FontReader.Position = 0;

            // 2nd step - read offset table (if single font)
            if (!IsFontCollection)
            {
                ReadTableDirectory();
            }
            else // read TTC Header
            {
                ReadTTCHeader();
                ReadTableDirectories();
            }

            ReadFontCollection();
        }
        
        public override void ReadFontName()
        {
            IsOTFCollection();
            FontReader.Position = 0;
            if (!IsFontCollection)
            {
                ReadTableDirectory();
            }
            else // read TTC Header
            {
                ReadTTCHeader();
                ReadTableDirectories();
            }
            var font = new Font(Typeface);
            Typeface.AddFont(font);
            CurrentFont = font;
            var nameTable =
                TableDirectories.SelectMany(x=>x.Tables).FirstOrDefault(x => x.Name == TableNames.name);
            if (nameTable != null)
            {
                ReadNameTable(nameTable);
            }
        }

        protected override void ReadTable(TableEntry entry)
        {
            switch (entry.Name)
            {
                case TableNames.GPOS:
                    ReadGlyphPositioningTable(entry);
                    break;
                case TableNames.GSUB:
                    ReadGlyphSubstitutionTable(entry);
                    break;
                case TableNames.fvar:
                    ReadFvarTable(entry);
                    break;
                case TableNames.STAT:
                    ReadStyleAttributesTable(entry);
                    break;
                case TableNames.MVAR:
                    CurrentFont.FontMetricsVariations = MetricsVariationTable.Read(FontReader, entry.Offset);
                    break;
                case TableNames.avar:
                    CurrentFont.AxisVariations = AxisVariationTable.Read(FontReader, entry.Offset);
                    break;
                case TableNames.gvar:
                    CurrentFont.GlyphVariations = GlyphVariationTable.Read(FontReader, entry.Offset);
                    break;
                case TableNames.HVAR:
                    CurrentFont.MetricsVariations = HorizontalMetricsVariationTable.Read(FontReader, entry.Offset);
                    break;
                case TableNames.COLR:
                    CurrentFont.ColorLayers = ColorLayerTable.Read(FontReader, entry.Offset);
                    CurrentFont.ColorPaints = ColorPaintTable.Read(FontReader, entry.Offset, entry.Length);
                    break;
                case TableNames.CPAL:
                    CurrentFont.ColorPaletteTable = ColorPaletteTable.Read(FontReader, entry.Offset);
                    break;
                case TableNames.CBLC:
                    ForgetOtherFontsBitmaps();
                    bitmapLocations = ReadTableBytes(entry);
                    CurrentFont.ColorBitmaps ??= EmbeddedColorBitmapTable.Create(bitmapLocations, bitmapImages);
                    break;
                case TableNames.CBDT:
                    ForgetOtherFontsBitmaps();
                    bitmapImages = ReadTableBytes(entry);
                    CurrentFont.ColorBitmaps ??= EmbeddedColorBitmapTable.Create(bitmapLocations, bitmapImages);
                    break;
                case TableNames.SVG:
                    CurrentFont.SvgDocuments = SvgDocumentTable.Create(ReadTableBytes(entry));
                    break;
                case TableNames.sbix:
                    CurrentFont.ColorBitmaps ??= StandardBitmapTable.Create(ReadTableBytes(entry), (int)Typeface.GlyphCount);
                    break;
                case TableNames.CFF:
                case TableNames.CFF2:
                    DetermineCFFVersion(CurrentTableDirectory);
                    ParseCFF(entry);
                    break;
            }
        }

        private void ForgetOtherFontsBitmaps()
        {
            if (ReferenceEquals(bitmapFont, CurrentFont))
            {
                return;
            }

            bitmapFont = CurrentFont;
            bitmapLocations = null;
            bitmapImages = null;
        }

        private byte[] ReadTableBytes(TableEntry entry)
        {
            var data = new byte[entry.Length];
            FontReader.Position = entry.Offset;
            var read = 0;
            while (read < data.Length)
            {
                var chunk = FontReader.Read(data, read, data.Length - read);
                if (chunk == 0)
                {
                    return null;
                }

                read += chunk;
            }

            return data;
        }

        private void IsOTFCollection()
        {
            FontReader.Position = 0;
            IsFontCollection = (FontReader.ReadString(4) == "ttcf");
        }

        private void ReadTTCHeader()
        {
            FontReader.Position = 0;
            ttcHeader = new TTCHeader();
            ttcHeader.Tag = FontReader.ReadString(4);
            ttcHeader.MajorVersion = FontReader.ReadUInt16();
            ttcHeader.MinorVersion = FontReader.ReadUInt16();
            ttcHeader.NumFonts = FontReader.ReadUInt32();
            ttcHeader.TableDirectoryOffsets = new UInt32[ttcHeader.NumFonts];
            for (int i = 0; i < ttcHeader.NumFonts; ++i)
            {
                ttcHeader.TableDirectoryOffsets[i] = FontReader.ReadUInt32();
            }
        }

        private void ReadTableDirectories()
        {
            foreach (var offset in ttcHeader.TableDirectoryOffsets)
            {
                FontReader.Position = offset;
                ReadTableDirectory();
            }
        }

        private void ReadTableDirectory()
        {
            var tableDirectory = new TableDirectory();
            TableDirectories.Add(tableDirectory);

            tableDirectory.SfntVersion = FontReader.ReadUInt32();
            tableDirectory.NumTables = FontReader.ReadUInt16();

            tableDirectory.OutlineType = tableDirectory.SfntVersion is 0x00010000 or 0x74727565
                ? OutlineType.TrueType
                : OutlineType.CompactFontFormat;

            // skip other fields
            FontReader.Position += 6;

            // 3rd step - read all table records for current table directory
            ReadTableRecords(tableDirectory);
        }

        private void ReadTableRecords(TableDirectory tableDirectory)
        {
            tableDirectory.Tables = new TableEntry[tableDirectory.NumTables];

            for (int i = 0; i < tableDirectory.NumTables; ++i)
            {
                var table = new TableEntry
                {
                    Name = FontReader.ReadString(4),
                    CheckSum = FontReader.ReadUInt32(),
                    Offset = FontReader.ReadUInt32(),
                    Length = FontReader.ReadUInt32()
                };

                tableDirectory.TablesOffsets[table.Name] = table.Offset;
                tableDirectory.Tables[i] = table;
            }

            CheckMandatoryTables(tableDirectory);
        }

        private void CheckMandatoryTables(TableDirectory tableDirectory)
        {
            foreach (var table in commonMandatoryTables)
            {
                if (IsEmbeddedProgram && table != TableNames.head)
                {
                    continue;
                }

                if (!tableDirectory.TablesOffsets.ContainsKey(table))
                {
                    throw new ParserException($"Table {table} is not present in {FilePath}");
                }
            }

            switch (tableDirectory.OutlineType)
            {
                case OutlineType.TrueType when !HasColorBitmaps(tableDirectory):
                    foreach (var table in trueTypeMandatoryTables)
                    {
                        if (!tableDirectory.TablesOffsets.ContainsKey(table))
                        {
                            throw new ParserException($"Table {table} is not present in {FilePath}");
                        }
                    }

                    break;
                case OutlineType.CompactFontFormat:
                    if (!tableDirectory.TablesOffsets.ContainsKey(cffMandatoryTables["CFF"]) &&
                        !tableDirectory.TablesOffsets.ContainsKey(cffMandatoryTables["CFF2"]))
                    {
                        throw new ParserException(
                            $"Table either {cffMandatoryTables["CFF"]} or {cffMandatoryTables["CFF2"]} is not present in {FilePath}");
                    }

                    break;
            }
        }

        private static bool HasColorBitmaps(TableDirectory tableDirectory)
        {
            return tableDirectory.TablesOffsets.ContainsKey(TableNames.CBDT) ||
                   tableDirectory.TablesOffsets.ContainsKey(TableNames.sbix);
        }

        private void DetermineCFFVersion(TableDirectory tableDirectory)
        {
            if (tableDirectory.OutlineType != OutlineType.CompactFontFormat) return;

            if (tableDirectory.TablesOffsets.ContainsKey(cffMandatoryTables["CFF"]))
            {
                CFFVersion = Tables.CFF.CFFVersion.CFF;
            }
            else
            {
                CFFVersion = Tables.CFF.CFFVersion.CFF2;
            }
        }

        private void ParseCFF(TableEntry entry)
        {
            var offset = entry.Offset;

            cffParser = CFFVersion switch
            {
                Tables.CFF.CFFVersion.CFF => new CFFParser(offset, FontReader),
                Tables.CFF.CFFVersion.CFF2 => new CFF2Parser(offset, FontReader),
                _ => cffParser
            };

            cffFont = cffParser.Parse();
            Typeface.SetGlyphs(cffFont.Glyphs);
            Typeface.OutlineSource = cffFont.OutlineSource;
            CurrentFont.VariationData = cffFont.VariationStore;
        }

        protected virtual void ReadFvarTable(TableEntry entry)
        {
            FontReader.Position = entry.Offset;

            var majorVersion = FontReader.ReadUInt16();
            var minorVersion = FontReader.ReadUInt16();
            var axesArrayOffset = FontReader.ReadUInt16();
            var reserved = FontReader.ReadUInt16();
            var axisCount = FontReader.ReadUInt16();
            var axisSize = FontReader.ReadUInt16();
            var instanceCount = FontReader.ReadUInt16();
            var instanceSize = FontReader.ReadUInt16();

            FontReader.Position = entry.Offset + axesArrayOffset;
            var currentOffset = FontReader.Position;

            var axes = new List<VariationAxisRecord>();

            for (var i = 0; i < axisCount; ++i)
            {
                var axis = new VariationAxisRecord();

                axis.AxisTag = FontReader.ReadString(4);
                axis.MinValue = FontReader.ReadInt32().FromF16Dot16();
                axis.DefaultValue = FontReader.ReadInt32().FromF16Dot16();
                axis.MaxValue = FontReader.ReadInt32().FromF16Dot16();
                axis.Flags = FontReader.ReadUInt16();
                axis.AxisNameID = FontReader.ReadUInt16();

                axes.Add(axis);

                currentOffset += axisSize;
                FontReader.Position = currentOffset;
            }

            var instances = new List<FontNamedInstance>();
            for (var j = 0; j < instanceCount; ++j)
            {
                var nameId = FontReader.ReadUInt16();
                FontReader.ReadUInt16();
                var variations = new FontVariation[axisCount];
                for (var k = 0; k < axisCount; ++k)
                {
                    variations[k] = new FontVariation(axes[k].AxisTag, FontReader.ReadInt32().FromF16Dot16());
                }

                instances.Add(new FontNamedInstance(CurrentFont.GetName(nameId), variations));
                currentOffset += instanceSize;
                FontReader.Position = currentOffset;
            }

            CurrentFont.NamedInstances = instances;
            CurrentFont.Axes = axes
                .Select(a => new FontAxis(a.AxisTag, (float)a.MinValue, (float)a.DefaultValue, (float)a.MaxValue,
                    CurrentFont.GetName(a.AxisNameID), (a.Flags & HiddenAxis) != 0))
                .ToArray();
        }

        private const ushort HiddenAxis = 0x0001;

        protected virtual void ReadStyleAttributesTable(TableEntry entry)
        {
            var (values, elidedFallbackName) = StyleAttributesTable.Read(FontReader, entry.Offset, CurrentFont.GetName);            CurrentFont.AxisValues = values;
            CurrentFont.ElidedFallbackName = elidedFallbackName;
        }

        protected virtual void ReadGlyphPositioningTable(TableEntry entry)
        {
            FontReader.Position = entry.Offset;

            var gpos = new GlyphPositioningTable();
            gpos.MajorVersion = FontReader.ReadUInt16();
            gpos.MinorVersion = FontReader.ReadUInt16();

            var scriptListOffset = FontReader.ReadUInt16() + entry.Offset;
            var featureListOffset = FontReader.ReadUInt16() + entry.Offset;
            var lookupListOffset = FontReader.ReadUInt16() + entry.Offset;

            if (gpos.MinorVersion == 1)
            {
                gpos.FeatureVariationsOffset = FontReader.ReadUInt32();
            }

            gpos.ScriptList = FontReader.ReadScriptList(scriptListOffset);

            gpos.FeatureList = FontReader.ReadFeatureList(featureListOffset);
            if (gpos.FeatureVariationsOffset != 0)
            {
                gpos.FeatureVariations = FeatureVariationsTable.Read(FontReader, entry.Offset + gpos.FeatureVariationsOffset,
                    gpos.FeatureList);
            }

            gpos.LookupList = FontReader.ReadGPOSLookupListTable(lookupListOffset);
            CurrentFont.Layout.Gpos = gpos;

            ProcessFeatures(gpos, FeatureKind.GPOS);

        }

        protected virtual void ReadGlyphSubstitutionTable(TableEntry entry)
        {
            FontReader.Position = entry.Offset;

            var gsub = new GlyphSubstitutionTable();
            gsub.MajorVersion = FontReader.ReadUInt16();
            gsub.MinorVersion = FontReader.ReadUInt16();

            var scriptListOffset = FontReader.ReadUInt16() + entry.Offset;
            var featureListOffset = FontReader.ReadUInt16() + entry.Offset;
            var lookupListOffset = FontReader.ReadUInt16() + entry.Offset;

            if (gsub.MinorVersion == 1)
            {
                gsub.FeatureVariationsOffset = FontReader.ReadUInt32();
            }

            gsub.ScriptList = FontReader.ReadScriptList(scriptListOffset);

            gsub.FeatureList = FontReader.ReadFeatureList(featureListOffset);
            if (gsub.FeatureVariationsOffset != 0)
            {
                gsub.FeatureVariations = FeatureVariationsTable.Read(FontReader, entry.Offset + gsub.FeatureVariationsOffset,
                    gsub.FeatureList);
            }

            gsub.LookupList = FontReader.ReadGSUBLookupListTable(lookupListOffset);
            CurrentFont.Layout.Gsub = gsub;

            ProcessFeatures(gsub, FeatureKind.GSUB);
            foreach (var feature in CurrentFont.FeatureCatalog.GSUBFeatures)
            {
                feature.ValueCount = Math.Max(1, feature.Lookups
                    .Where(index => index < gsub.LookupList.Length)
                    .SelectMany(index => gsub.LookupList[index].SubTables)
                    .OfType<AlternateSubstitutionSubTable>()
                    .SelectMany(table => table.AlternateSetTables)
                    .Select(set => set.AlternateGlyphIDs.Length)
                    .DefaultIfEmpty(0)
                    .Max());
            }
        }

        private void ProcessFeatures(IFontLayout layout, FeatureKind featureKind)
        {
            var catalog = CurrentFont.FeatureCatalog;
            foreach (var scriptTable in layout.ScriptList)
            {
                var script = catalog.GetOrAddScript(scriptTable.Name);
                if (scriptTable.DefaultLang != null)
                {
                    AddLanguageSystem(layout, featureKind, script.GetOrAddLanguage(scriptTable.DefaultLang.Name, true),
                        scriptTable.DefaultLang);
                }

                foreach (var langSysTable in scriptTable.LangSysTables)
                {
                    AddLanguageSystem(layout, featureKind, script.GetOrAddLanguage(langSysTable.Name, false),
                        langSysTable);
                }
            }
        }

        private void AddLanguageSystem(IFontLayout layout, FeatureKind featureKind, FontLanguage language,
            LangSysTable langSysTable)
        {
            var catalog = CurrentFont.FeatureCatalog;
            foreach (var index in langSysTable.FeatureIndices)
            {
                if (index >= layout.FeatureList.Length)
                {
                    continue;
                }

                language.AddFeature(catalog.GetOrAddFeature(layout.FeatureList[index], featureKind, CurrentFont.GetName));
            }

            if (!langSysTable.HasRequireFeature || langSysTable.RequiredFeatureIndex >= layout.FeatureList.Length)
            {
                return;
            }

            var feature = catalog.GetOrAddFeature(layout.FeatureList[langSysTable.RequiredFeatureIndex], featureKind,
                CurrentFont.GetName);
            if (featureKind == FeatureKind.GSUB)
            {
                language.RequiredGSUBFeature = feature;
            }
            else
            {
                language.RequiredGPOSFeature = feature;
            }
        }
    }
}

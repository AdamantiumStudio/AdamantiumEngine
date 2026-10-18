using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Extensions;
using Adamantium.Fonts.Tables;
using Adamantium.Fonts.Tables.CMAP;
using Adamantium.Mathematics;

namespace Adamantium.Fonts.Parsers
{
    internal abstract class SfntParser : IFontParser
    {
        // TTF font data - main output of parser class
        //public TTFFont FontData { get; }

        public Typeface Typeface { get; protected set; }

        // is font collection
        protected bool IsFontCollection { get; set; }

        // "table name-table entry" mapping
        //private Dictionary<string, TableEntry> tableMap;

        // tables
        private HeadTable head;
        private MaximumProfileTable maxp;
        private TTFIndexToLocationTable loca;
        private HorizontalHeaderTable hhea;
        private HorizontalMetricsTable hmtx;
        private VerticalHeaderTable vhea;
        private VerticalMetricsTable vmtx;

        protected NameTable Name { get; set; }

        protected List<TableDirectory> TableDirectories { get; set; }

        // TTF font file path (all data in Big-Endian)
        protected string FilePath { get; set; }

        // TTF byte FontReader
        protected FontStreamReader FontReader { get; set; }
        
        protected Font CurrentFont { get; set; }
        
        protected TableDirectory CurrentTableDirectory { get; private set; }

        protected static List<string> StandardGlyphNames { get; }

        protected static Dictionary<string, int> SortingTable { get; }
        
        // resolution of bezier curves
        protected byte Resolution { get; set; }
        
        protected HashSet<long> ReadTables { get; private set; }

        static SfntParser()
        {
            StandardGlyphNames = new List<string>()
            {
                ".notdef",
                ".null",
                "nonmarkingreturn",
                "space",
                "exclam",
                "quotedbl",
                "numbersign",
                "dollar",
                "percent",
                "ampersand",
                "quotesingle",
                "parenleft",
                "parenright",
                "asterisk",
                "plus",
                "comma",
                "hyphen",
                "period",
                "slash",
                "zero",
                "one",
                "two",
                "three",
                "four",
                "five",
                "six",
                "seven",
                "eight",
                "nine",
                "colon",
                "semicolon",
                "less",
                "equal",
                "greater",
                "question",
                "at",
                "A",
                "B",
                "C",
                "D",
                "E",
                "F",
                "G",
                "H",
                "I",
                "J",
                "K",
                "L",
                "M",
                "N",
                "O",
                "P",
                "Q",
                "R",
                "S",
                "T",
                "U",
                "V",
                "W",
                "X",
                "Y",
                "Z",
                "bracketleft",
                "backslash",
                "bracketright",
                "asciicircum",
                "underscore",
                "grave",
                "a",
                "b",
                "c",
                "d",
                "e",
                "f",
                "g",
                "h",
                "i",
                "j",
                "k",
                "l",
                "m",
                "n",
                "o",
                "p",
                "q",
                "r",
                "s",
                "t",
                "u",
                "v",
                "w",
                "x",
                "y",
                "z",
                "braceleft",
                "bar",
                "braceright",
                "asciitilde",
                "Adieresis",
                "Aring",
                "Ccedilla",
                "Eacute",
                "Ntilde",
                "Odieresis",
                "Udieresis",
                "aacute",
                "agrave",
                "acircumflex",
                "adieresis",
                "atilde",
                "aring",
                "ccedilla",
                "eacute",
                "egrave",
                "ecircumflex",
                "edieresis",
                "iacute",
                "igrave",
                "icircumflex",
                "idieresis",
                "ntilde",
                "oacute",
                "ograve",
                "ocircumflex",
                "odieresis",
                "otilde",
                "uacute",
                "ugrave",
                "ucircumflex",
                "udieresis",
                "dagger",
                "degree",
                "cent",
                "sterling",
                "section",
                "bullet",
                "paragraph",
                "germandbls",
                "registered",
                "copyright",
                "trademark",
                "acute",
                "dieresis",
                "notequal",
                "AE",
                "Oslash",
                "infinity",
                "plusminus",
                "lessequal",
                "greaterequal",
                "yen",
                "mu",
                "partialdiff",
                "summation",
                "product",
                "pi",
                "integral",
                "ordfeminine",
                "ordmasculine",
                "Omega",
                "ae",
                "oslash",
                "questiondown",
                "exclamdown",
                "logicalnot",
                "radical",
                "florin",
                "approxequal",
                "Delta",
                "guillemotleft",
                "guillemotright",
                "ellipsis",
                "nonbreakingspace",
                "Agrave",
                "Atilde",
                "Otilde",
                "OE",
                "oe",
                "endash",
                "emdash",
                "quotedblleft",
                "quotedblright",
                "quoteleft",
                "quoteright",
                "divide",
                "lozenge",
                "ydieresis",
                "Ydieresis",
                "fraction",
                "currency",
                "guilsinglleft",
                "guilsinglright",
                "fi",
                "fl",
                "daggerdbl",
                "periodcentered",
                "quotesinglbase",
                "quotedblbase",
                "perthousand",
                "Acircumflex",
                "Ecircumflex",
                "Aacute",
                "Edieresis",
                "Egrave",
                "Iacute",
                "Icircumflex",
                "Idieresis",
                "Igrave",
                "Oacute",
                "Ocircumflex",
                "apple",
                "Ograve",
                "Uacute",
                "Ucircumflex",
                "Ugrave",
                "dotlessi",
                "circumflex",
                "tilde",
                "macron",
                "breve",
                "dotaccent",
                "ring",
                "cedilla",
                "hungarumlaut",
                "ogonek",
                "caron",
                "Lslash",
                "lslash",
                "Scaron",
                "scaron",
                "Zcaron",
                "zcaron",
                "brokenbar",
                "Eth",
                "eth",
                "Yacute",
                "yacute",
                "Thorn",
                "thorn",
                "minus",
                "multiply",
                "onesuperior",
                "twosuperior",
                "threesuperior",
                "onehalf",
                "onequarter",
                "threequarters",
                "franc",
                "Gbreve",
                "gbreve",
                "Idotaccent",
                "Scedilla",
                "scedilla",
                "Cacute",
                "cacute",
                "Ccaron",
                "ccaron",
                "dcroat",
            };
            
            SortingTable = new Dictionary<string, int>()
            {
                { "head",    0      },
                { "maxp",    10     },
                { "name",    20     },
                { "loca",    30     },
                { "glyf",    40     },
                { "CFF ",    50     },
                { "CFF2",    60     },
                { "cmap",    70     },
                { "hhea",    80     },
                { "vhea",    85     },
                { "hmtx",    90     },
                { "vmtx",    91     },
                { "fvar",    92	    },
                { "OS/2",    120    },
                { "post",    130    },
                { "BASE",    140    },
                { "GDEF",    150	},
                { "GPOS",    160	},
                { "GSUB",    170	},
                { "EBSC",    180	},
                { "JSTF",    190	},
                { "MATH",    200	},
                { "CBDT",    210	},
                { "CBLC",    220	},
                { "COLR",    230	},
                { "CPAL",    240	},
                { "SVG ",    250	},
                { "cvt ",    260    },    
                { "fpgm",    270    },
                { "prep",    280    },
                { "VORG",    290    },
                { "EBDT",    300    },
                { "EBLC",    310    },
                { "gasp",    320    },
                { "hdmx",    330    },
                { "kern",    340    },
                { "LTSH",    350	},
                { "PCLT",    360	},
                { "VDMX",    370    },
                { "sbix",    380	},
                { "acnt",    390	},
                { "avar",    400	},
                { "bdat",    410	},
                { "bloc",    420	},
                { "bsln",    430	},
                { "cvar",    440	},
                { "fdsc",    450	},
                { "feat",    460	},
                { "fmtx",    470	},
                { "gvar",    490	},
                { "HVAR",    495	},
                { "MVAR",    496	},
                { "STAT",    497	},
                { "hsty",    500	},
                { "just",    510	},
                { "lcar",    520	},
                { "mort",    530	},
                { "morx",    540	},
                { "opbd",    550	},
                { "prop",    560	},
                { "trak",    570	},
                { "Zapf",    580	},
                { "Silf",    590	},
                { "Glat",    600	},
                { "Gloc",    610	},
                { "Feat",    620	},
                { "Sill",    630	},
                { "DSIG",    640	},
            };
        }

        protected internal SfntParser()
        {
            
        }

        protected internal SfntParser(string filePath, byte resolution)
        {
            Initialize(filePath, resolution);
        }
        
        protected internal SfntParser(FontStreamReader fontReader, byte resolution, params TableDirectory[] tableDirectories)
        {
            Initialize(fontReader, resolution, tableDirectories);
        }

        protected void InitializeBase(string filePath, byte resolution)
        {
            Typeface = new Typeface();
            Typeface.Parser = this;
            FilePath = filePath;
            Resolution = resolution > 0 ? resolution : (byte)1;
            ReadTables = new HashSet<long>();
        }

        protected virtual void Initialize(string filePath, byte resolution)
        {
            InitializeBase(filePath, resolution);
            TableDirectories = new List<TableDirectory>();
            FontReader = FilePath.LoadIntoStream();
        }

        protected virtual void Initialize(FontStreamReader fontReader, byte resolution, params TableDirectory[] tableDirectories)
        {
            InitializeBase(fontReader.FilePath, resolution);
            TableDirectories = new List<TableDirectory>(tableDirectories);
            FontReader = fontReader;
        }

        public abstract void Parse();

        public abstract void ReadFontName();

        public byte[] GetFontBytes()
        {
            return FontReader.GetBuffer();
        }

        private void SortTables()
        {
            foreach (var tableDirectory in TableDirectories)
            {
                var dict = new Dictionary<int, TableEntry>();
                foreach (var table in tableDirectory.Tables)
                {
                    if (!SortingTable.ContainsKey(table.Name)) continue;

                    dict[SortingTable[table.Name]] = table;
                }
                var sortedTables = dict.OrderBy(x => x.Key).Select(x => x.Value).ToArray();
                tableDirectory.Tables = sortedTables;
            }
        }

        protected virtual void ReadFontCollection()
        {
            SortTables();
            
            foreach (var tableDirectory in TableDirectories)
            {
                CurrentTableDirectory = tableDirectory;
                ReadTableDirectory(tableDirectory);
            }
            
            Typeface.UpdateGlyphNames();
        }

        protected virtual void ReadTableDirectory(TableDirectory tableDirectory)
        {
            var font = new Font(Typeface);
            Typeface.AddFont(font);
            CurrentFont = font;

            foreach (var tableEntry in tableDirectory.Tables)
            {
                if (!ReadTables.Add(tableEntry.Offset) && IsSharedGlyphData(tableEntry.Name))
                {
                    continue;
                }

                switch (tableEntry.Name)
                {
                    case TableNames.head:
                        ReadHeadTable(tableEntry);
                        break;
                    case TableNames.maxp:
                        ReadMaximumProfileTable(tableEntry);
                        if (!HasOutlines(tableDirectory) && Typeface.GlyphCount == 0)
                        {
                            CreateGlyphsWithoutOutlines();
                        }

                        break;
                    case TableNames.name:
                        ReadNameTable(tableEntry);
                        break;
                    case TableNames.loca:
                        ReadGlyphIndexToLocationTable(tableEntry);
                        break;
                    case TableNames.cmap:
                        ReadCharToGlyphIndexTable(tableEntry);
                        break;
                    case TableNames.hhea:
                        ReadHorizontalHeaderTable(tableEntry);
                        break;
                    case TableNames.vhea:
                        ReadVerticalHeaderTable(tableEntry);
                        break;
                    case TableNames.hmtx:
                        ReadHorizontalMetricsTable(tableEntry);
                        break;
                    case TableNames.vmtx:
                        ReadVerticalMetricsTable(tableEntry);
                        break;
                    case TableNames.VORG:
                        ReadVerticalOriginTable(tableEntry);
                        break;
                    case TableNames.JSTF:
                        ReadJustificationTable(tableEntry);
                        break;
                    case TableNames.OS2:
                        ReadOS2Table(tableEntry);
                        break;
                    case TableNames.glyf:
                        ReadTTFGlyphs(tableEntry);
                        break;
                    case TableNames.post:
                        ReadPostTable(tableEntry);
                        break;
                    case TableNames.kern:
                        ReadKerningTable(tableEntry);
                        break;
                    case TableNames.GDEF:
                        ReadGDEFTable(tableEntry);
                        break;
                    default:
                        ReadTable(tableEntry);
                        break;
                }
            }
        }

        protected virtual void ReadTable(TableEntry entry)
        {
            
        }
        
        protected virtual void ReadPostTable(TableEntry entry)
        {
            FontReader.Position = entry.Offset;

            var version = FontReader.ReadUInt32();
            var italicAngle = FontReader.ReadUInt32();
            var underlinePosition = FontReader.ReadInt16();
            var underlineThickness = FontReader.ReadInt16();
            CurrentFont.UnderlinePosition = underlinePosition;
            CurrentFont.UnderlineThickness = underlineThickness;
            var isFixedPitch = FontReader.ReadUInt32(); // 0 is proportionally spaced, non-zero - font is monospaced

            // skip next 4 fields
            FontReader.Position += 16;

            var post = new PostTable();

            switch (version)
            {
                case 0x00010000:
                    post.Version = 1;
                    break;
                case 0x00030000:
                    post.Version = 3;
                    break;
                case 0x00020000:
                    post.Version = 2;
                    CurrentFont.IsGlyphNamesProvided = true;
                    var glyphsNumber = FontReader.ReadUInt16();
                    var glyphNameIndices = FontReader.ReadUInt16Array(glyphsNumber);

                    var glyphNames = post.GlyphNames;
                    for (uint i = 0; i < glyphsNumber; ++i)
                    {
                        var glyphNameIndex = glyphNameIndices[i];
                        if (glyphNameIndex < 258)
                        {
                            glyphNames[i] = StandardGlyphNames[glyphNameIndex];
                        }
                        else
                        {
                            var length = FontReader.ReadByte();
                            var glyphName = FontReader.ReadString(length, Encoding.UTF8);
                            glyphNames[i] = glyphName;
                        }

                        if (Typeface.GetGlyphByIndex(i, out var glyph))
                        {
                            glyph.Name = glyphNames[i];
                        }
                    }
                    break;
            }
        }

        protected virtual void ReadHeadTable(TableEntry entry)
        {
            head = new HeadTable();
            
            FontReader.Position = entry.Offset;

            head.MajorVersion = FontReader.ReadUInt16();
            head.MinorVersion = FontReader.ReadUInt16();
            head.FontRevisionMaj = FontReader.ReadUInt16();
            head.FontRevisionMin = FontReader.ReadUInt16();
            head.CheckSumAdjustment = FontReader.ReadUInt32();
            head.MagicNumber = FontReader.ReadUInt32();
            head.Flags = FontReader.ReadUInt16();
            head.UnitsPerEm = FontReader.ReadUInt16();
            head.CreatedDate = FontReader.ReadInt64();
            head.ModifiedDate = FontReader.ReadInt64();
            head.XMin = FontReader.ReadInt16();
            head.YMin = FontReader.ReadInt16();
            head.XMax = FontReader.ReadInt16();
            head.YMax = FontReader.ReadInt16();
            head.MacStyle = FontReader.ReadUInt16();
            head.LowestRecPPEM = FontReader.ReadUInt16();
            head.FontDirectionHint = FontReader.ReadInt16();
            head.IndexToLocFormat = FontReader.ReadInt16();
            head.GlyphDataFormat = FontReader.ReadInt16();

            CurrentFont.UnitsPerEm = head.UnitsPerEm;
            CurrentFont.LowestRecPPEM = head.LowestRecPPEM;
            var originalDate = new DateTime(1904, 1, 1);
            var delta = new TimeSpan(head.CreatedDate);
            CurrentFont.Created = originalDate + delta;
            delta = new TimeSpan(head.ModifiedDate);
            CurrentFont.Modified = originalDate + delta;
        }

        protected virtual void ReadMaximumProfileTable(TableEntry entry)
        {
            maxp = new MaximumProfileTable();
            
            FontReader.Position = entry.Offset;

            maxp.MajorVersion = FontReader.ReadUInt16();
            maxp.MinorVersion = FontReader.ReadUInt16();
            maxp.NumGlyphs = FontReader.ReadUInt16();
            maxp.MaxPoints = FontReader.ReadUInt16();
            maxp.MaxContours = FontReader.ReadUInt16();
            maxp.MaxCompositePoints = FontReader.ReadUInt16();
            maxp.MaxCompositeContours = FontReader.ReadUInt16();
            maxp.MaxZones = FontReader.ReadUInt16();
            maxp.MaxTwilightPoints = FontReader.ReadUInt16();
            maxp.MaxStorage = FontReader.ReadUInt16();
            maxp.MaxFunctionDefs = FontReader.ReadUInt16();
            maxp.MaxInstructionDefs = FontReader.ReadUInt16();
            maxp.MaxStackElements = FontReader.ReadUInt16();
            maxp.MaxSizeOfInstructions = FontReader.ReadUInt16();
            maxp.MaxComponentElements = FontReader.ReadUInt16();
            maxp.MaxComponentDepth = FontReader.ReadUInt16();
        }
        
        protected virtual void ReadNameTable(TableEntry entry)
        {
            FontReader.Position = entry.Offset;
            var nameTable = new NameTable();
            nameTable.Version = FontReader.ReadUInt16();
            nameTable.Count = FontReader.ReadUInt16();
            nameTable.StorageOffset = FontReader.ReadUInt16();
            nameTable.NameRecords = new NameRecord[nameTable.Count];
            
            for (int i = 0; i < nameTable.Count; ++i)
            {
                var record = new NameRecord();
                record.PlatformId = FontReader.ReadUInt16();
                record.EncodingId = FontReader.ReadUInt16();
                record.LanguageId = FontReader.ReadUInt16();
                record.NameId = FontReader.ReadUInt16();
                record.Length = FontReader.ReadUInt16();
                record.StringOffset = FontReader.ReadUInt16();
                nameTable.NameRecords[i] = record;
            }

            if (nameTable.Version == 1)
            {
                nameTable.LangTagCount = FontReader.ReadUInt16();
                nameTable.LangTagRecords = new LangTagRecord[nameTable.LangTagCount];

                for (int i = 0; i < nameTable.LangTagCount; ++i)
                {
                    var langRecord = new LangTagRecord();
                    langRecord.Length = FontReader.ReadUInt16();
                    langRecord.LangTagOffset = FontReader.ReadUInt16();
                }
            }

            var ranks = new Dictionary<ushort, int>();
            foreach (var nameRecord in nameTable.NameRecords)
            {
                var rank = NameRecordRank(nameRecord);
                if (rank < 0 || (ranks.TryGetValue(nameRecord.NameId, out var best) && best >= rank))
                {
                    continue;
                }

                ranks[nameRecord.NameId] = rank;
                FontReader.Position = entry.Offset + nameTable.StorageOffset + nameRecord.StringOffset;
                var encoding = nameRecord.PlatformId == 1
                    ? Encoding.GetEncoding("ISO-8859-1")
                    : Encoding.BigEndianUnicode;

                var str = FontReader.ReadString(nameRecord.Length, encoding);
                CurrentFont.SetName(nameRecord.NameId, str);

                switch ((NameIdKind)nameRecord.NameId)
                {
                    case NameIdKind.Copyright:
                        CurrentFont.Copyright = str;
                        break;
                    case NameIdKind.FontFamily:
                        CurrentFont.FontFamily = str;
                        break;
                    case NameIdKind.FontSubfamily:
                        CurrentFont.FontSubfamily = str;
                        break;
                    case NameIdKind.UniqueFontId:
                        CurrentFont.UniqueId = str;
                        break;
                    case NameIdKind.FullFontName:
                        CurrentFont.FullName = str;
                        break;
                    case NameIdKind.VersionString:
                        CurrentFont.Version = str;
                        break;
                    case NameIdKind.Trademark:
                        CurrentFont.Trademark = str;
                        break;
                    case NameIdKind.Manufacturer:
                        CurrentFont.Manufacturer = str;
                        break;
                    case NameIdKind.Designer:
                        CurrentFont.Designer = str;
                        break;
                    case NameIdKind.Description:
                        CurrentFont.Description = str;
                        break;
                    case NameIdKind.VendorUrl:
                        CurrentFont.VendorUrl = str;
                        break;
                    case NameIdKind.DesignerUrl:
                        CurrentFont.DesignerUrl = str;
                        break;
                    case NameIdKind.LicenseDescription:
                        CurrentFont.LicenseDescription = str;
                        break;
                    case NameIdKind.LicenseInfoUrl:
                        CurrentFont.LicenseInfoUrl = str;
                        break;
                    case NameIdKind.TypographicFamilyName:
                        CurrentFont.TypographicFamilyName = str;
                        break;
                    case NameIdKind.TypographicSubfamilyName:
                        CurrentFont.TypographicSubfamilyName = str;
                        break;
                    case NameIdKind.WwsFamilyName:
                        CurrentFont.WwsFamilyName = str;
                        break;
                    case NameIdKind.WwsSubfamilyName:
                        CurrentFont.WwsSubfamilyName = str;
                        break;
                    case NameIdKind.LightBackgroundPalette:
                        CurrentFont.LightBackgroundPalette = str;
                        break;
                    case NameIdKind.DarkBackgroundPalette:
                        CurrentFont.DarkBackgroundPalette = str;
                        break;
                }
            }

            Name = nameTable;
        }

        private static bool IsSharedGlyphData(string table)
        {
            return table is TableNames.glyf or TableNames.loca or TableNames.CFF or TableNames.CFF2;
        }

        private static int NameRecordRank(NameRecord record)
        {
            return record.PlatformId switch
            {
                3 when record.EncodingId is 1 or 10 => record.LanguageId == 0x0409 ? 4 : 3,
                0 => 2,
                1 when record.EncodingId == 0 => record.LanguageId == 0 ? 1 : 0,
                _ => -1,
            };
        }

        protected virtual void ReadGlyphIndexToLocationTable(TableEntry entry)
        {
            loca = new TTFIndexToLocationTable();
            
            FontReader.Position = entry.Offset;

            loca.GlyphOffsets = new UInt32[maxp.NumGlyphs + 1]; // + 'end-of-glyphs' entry

            if (head.IndexToLocFormat == 0)
            {
                for (var i = 0; i < maxp.NumGlyphs + 1; ++i)
                {
                    var glyphIndex = FontReader.ReadUInt16();
                    loca.GlyphOffsets[i] = glyphIndex * 2u;
                }
            }
            else
            {
                for (var i = 0; i < maxp.NumGlyphs + 1; ++i)
                {
                    loca.GlyphOffsets[i] = FontReader.ReadUInt32();
                }
            }
        }

        // Cmap
        protected virtual void ReadCharToGlyphIndexTable(TableEntry entry)
        {
            CharacterToGlyphTable cmap = new CharacterToGlyphTable();
            FontReader.Position = entry.Offset;
            cmap.Version = FontReader.ReadUInt16();
            var numTables = FontReader.ReadUInt16();
            var encodings = new EncodingRecord[numTables];
            for (var index = 0; index < encodings.Length; index++)
            {
                encodings[index] = ReadEncodingRecord();
            }

            cmap.CharacterMaps = new CharacterMap[numTables];

            for (var index = 0; index < encodings.Length; index++)
            {
                var encoding = encodings[index];
                FontReader.Position = entry.Offset + encoding.SubtableOffset;
                var format = FontReader.ReadUInt16();
                CharacterMap map = ReadCharacterMap(format);
                map.PlatformId = encoding.PlatformId;
                map.EncodingId = encoding.EncodingId;

                cmap.CharacterMaps[index] = map;
                if (map is CharacterMapFormat14 sequences)
                {
                    CurrentFont.VariationSequences = sequences;
                }
            }
            
            cmap.CollectUnicodeToGlyphMappings();

            var glyphsList = new List<Glyph>();

            foreach (var glyphPair in cmap.GlyphToUnicode)
            {
                if (!Typeface.GetGlyphByIndex(glyphPair.Key, out var glyph))
                {
                    Typeface.AddErrorMessage($"[ERR] cmap maps a character to glyph {glyphPair.Key}, the font has {Typeface.GlyphCount}");
                    continue;
                }

                glyph.SetUnicodes(glyphPair.Value);
                glyphsList.Add(glyph);
            }
            
            CurrentFont.SetGlyphs(glyphsList);
            var font = CurrentFont as IFont;
            font.SetGlyphUnicodes(cmap.GlyphToUnicode);
        }

        private CharacterMap ReadCharacterMap(ushort format)
        {
            switch (format)
            {
                case 0:
                    return FontReader.ReadCharacterMapFormat0();
                case 2:
                    return FontReader.ReadCharacterMapFormat2();
                case 4:
                    return FontReader.ReadCharacterMapFormat4();
                case 6:
                    return FontReader.ReadCharacterMapFormat6();
                case 8:
                    return FontReader.ReadCharacterMapFormat8();
                case 10:
                    return FontReader.ReadCharacterMapFormat10();
                case 12:
                    return FontReader.ReadCharacterMapFormat12();
                case 13:
                    return FontReader.ReadCharacterMapFormat13();
                case 14:
                    return FontReader.ReadCharacterMapFormat14();
                default:
                    throw new NotSupportedException($"Format {format} is not supported");
            }
        }

        private EncodingRecord ReadEncodingRecord()
        {
            var record = new EncodingRecord();
            record.PlatformId = FontReader.ReadUInt16();
            record.EncodingId = FontReader.ReadUInt16();
            record.SubtableOffset = FontReader.ReadUInt32();
            return record;
        }

        protected virtual void ReadHorizontalHeaderTable(TableEntry entry)
        {
            hhea = new HorizontalHeaderTable();

            FontReader.Position = entry.Offset;

            hhea.MajorVersion = FontReader.ReadUInt16();
            hhea.MinorVersion = FontReader.ReadUInt16();
            hhea.Ascender = FontReader.ReadInt16();
            hhea.Descender = FontReader.ReadInt16();
            hhea.LineGap = FontReader.ReadInt16();
            hhea.AdvanceWidthMax = FontReader.ReadUInt16();
            hhea.MinLeftSideBearing = FontReader.ReadInt16();
            hhea.MinRightSideBearing = FontReader.ReadInt16();
            hhea.XMaxExtent = FontReader.ReadInt16();
            hhea.CaretSlopeRise = FontReader.ReadInt16();
            hhea.CaretSlopeRun = FontReader.ReadInt16();
            hhea.CaretOffset = FontReader.ReadInt16();

            // skip 4 reserved int16 fields
            FontReader.Position += sizeof(Int16) * 4;

            hhea.MetricDataFormat = FontReader.ReadInt16();
            hhea.NumberOfHMetrics = FontReader.ReadUInt16();

            CurrentFont.LineAscent = hhea.Ascender;
            CurrentFont.LineDescent = (Int16)Math.Abs(hhea.Descender);
            CurrentFont.LineGap = hhea.LineGap;
            CurrentFont.LineSpacingMultiplier = (hhea.Ascender - hhea.Descender + hhea.LineGap)/(float)CurrentFont.UnitsPerEm;
        }

        protected virtual void ReadHorizontalMetricsTable(TableEntry entry)
        {
            hmtx = new HorizontalMetricsTable();
            
            FontReader.Position = entry.Offset;

            ushort lastAdvanceWidth = 0;

            hmtx.AdvanceWidths = new ushort[maxp.NumGlyphs];
            hmtx.LeftSideBearings = new short[maxp.NumGlyphs];

            for (var i = 0; i < maxp.NumGlyphs; ++i)
            {
                if (i < hhea.NumberOfHMetrics)
                {
                    var advanceWidth = FontReader.ReadUInt16();
                    hmtx.AdvanceWidths[i] = advanceWidth;

                    lastAdvanceWidth = advanceWidth; // last advanceWidth entry is propagated for all glyph indexes beyond 'numberOfHMetrics' count
                }
                else
                {
                    hmtx.AdvanceWidths[i] = lastAdvanceWidth;
                }

                var leftSideBearing = FontReader.ReadInt16();
                hmtx.LeftSideBearings[i] = leftSideBearing;
                if (ReferenceEquals(CurrentFont, Typeface.Fonts[0]) && Typeface.GetGlyphByIndex((uint)i, out var glyph))
                {
                    glyph.AdvanceWidth = lastAdvanceWidth;
                    glyph.LeftSideBearing = leftSideBearing;
                }
            }

            CurrentFont.SetHorizontalMetrics(hmtx.AdvanceWidths, hmtx.LeftSideBearings);
        }
        
        protected virtual void ReadVerticalHeaderTable(TableEntry entry)
        {
            vhea = new VerticalHeaderTable();

            FontReader.Position = entry.Offset;

            vhea.MajorVersion = FontReader.ReadUInt16();
            vhea.MinorVersion = FontReader.ReadUInt16();
            vhea.Ascender = FontReader.ReadInt16();
            vhea.Descender = FontReader.ReadInt16();
            vhea.LineGap = FontReader.ReadInt16();
            vhea.AdvanceHeightMax = FontReader.ReadUInt16();
            vhea.MinTopSideBearing = FontReader.ReadInt16();
            vhea.MinBottomSideBearing = FontReader.ReadInt16();
            vhea.YMaxExtent = FontReader.ReadInt16();
            vhea.CaretSlopeRise = FontReader.ReadInt16();
            vhea.CaretSlopeRun = FontReader.ReadInt16();
            vhea.CaretOffset = FontReader.ReadInt16();

            // skip 4 reserved int16 fields
            FontReader.Position += sizeof(Int16) * 4;

            vhea.MetricDataFormat = FontReader.ReadInt16();
            vhea.NumberOfYMetrics = FontReader.ReadUInt16();
        }
        
        protected virtual void ReadVerticalMetricsTable(TableEntry entry)
        {
            if (vhea == null || vhea.NumberOfYMetrics == 0)
            {
                return;
            }

            vmtx = new VerticalMetricsTable();

            FontReader.Position = entry.Offset;

            ushort lastAdvanceWidth = 0;

            vmtx.AdvanceHeights = new ushort[maxp.NumGlyphs];
            vmtx.TopSideBearings = new short[maxp.NumGlyphs];
            CurrentFont.SetVerticalMetrics(vmtx.AdvanceHeights, vmtx.TopSideBearings);

            for (var i = 0; i < maxp.NumGlyphs; ++i)
            {
                if (i < vhea.NumberOfYMetrics)
                {
                    var advanceWidth = FontReader.ReadUInt16();
                    vmtx.AdvanceHeights[i] = advanceWidth;

                    lastAdvanceWidth = advanceWidth; // last advanceWidth entry is propagated for all glyph indexes beyond 'numberOfHMetrics' count
                }
                else
                {
                    vmtx.AdvanceHeights[i] = lastAdvanceWidth;
                }

                var topSideBearing = FontReader.ReadInt16();
                vmtx.TopSideBearings[i] = topSideBearing;
                Typeface.GetGlyphByIndex((uint)i, out  var glyph);
                glyph.AdvanceHeight = lastAdvanceWidth;
                glyph.TopSideBearing = topSideBearing;
            }
        }

        protected virtual void ReadVerticalOriginTable(TableEntry entry)
        {
            FontReader.Position = entry.Offset;
            FontReader.ReadUInt16();
            FontReader.ReadUInt16();
            var defaultY = FontReader.ReadInt16();
            var count = FontReader.ReadUInt16();
            var origins = new Dictionary<uint, short>(count);
            for (var i = 0; i < count; i++)
            {
                var glyph = FontReader.ReadUInt16();
                origins[glyph] = FontReader.ReadInt16();
            }

            CurrentFont.SetVerticalOrigins(defaultY, origins);
        }

        protected virtual void ReadJustificationTable(TableEntry entry)
        {
            FontReader.Position = entry.Offset;
            FontReader.ReadUInt16();
            FontReader.ReadUInt16();
            var scriptCount = FontReader.ReadUInt16();
            var scripts = new List<(string Tag, ushort Offset)>(scriptCount);
            for (var i = 0; i < scriptCount; i++)
            {
                scripts.Add((FontReader.ReadString(4), FontReader.ReadUInt16()));
            }

            var extenders = new Dictionary<string, uint[]>();
            foreach (var (tag, offset) in scripts)
            {
                if (offset == 0 || offset + 2 > entry.Length)
                {
                    continue;
                }

                FontReader.Position = entry.Offset + offset;
                var extenderOffset = FontReader.ReadUInt16();
                if (extenderOffset == 0 || offset + extenderOffset + 2 > entry.Length)
                {
                    continue;
                }

                FontReader.Position = entry.Offset + offset + extenderOffset;
                var count = FontReader.ReadUInt16();
                if (offset + extenderOffset + 2 + count * 2 > entry.Length)
                {
                    continue;
                }

                var glyphs = new uint[count];
                for (var i = 0; i < glyphs.Length; i++)
                {
                    glyphs[i] = FontReader.ReadUInt16();
                }

                extenders[tag] = glyphs;
            }

            CurrentFont.SetJustificationExtenders(extenders);
        }

        protected virtual void ReadOS2Table(TableEntry entry)
        {
            var os2 = new OS2Table();
            FontReader.Position = entry.Offset;

            os2.version = FontReader.ReadUInt16();
            os2.xAvgCharWidth = FontReader.ReadInt16();
            os2.usWeightClass = FontReader.ReadUInt16();
            os2.usWidthClass = FontReader.ReadUInt16();
            os2.fsType = FontReader.ReadUInt16();
            os2.ySubscriptXSize = FontReader.ReadInt16();
            os2.ySubscriptYSize = FontReader.ReadInt16();
            os2.ySubscriptXOffset = FontReader.ReadInt16();
            os2.ySubscriptYOffset = FontReader.ReadInt16();
            os2.ySuperscriptXSize = FontReader.ReadInt16();
            os2.ySuperscriptYSize = FontReader.ReadInt16();
            os2.ySuperscriptXOffset = FontReader.ReadInt16();
            os2.ySuperscriptYOffset = FontReader.ReadInt16();
            os2.yStrikeoutSize = FontReader.ReadInt16();
            os2.yStrikeoutPosition = FontReader.ReadInt16();
            CurrentFont.StrikeoutSize = os2.yStrikeoutSize;
            CurrentFont.SuperscriptYOffset = os2.ySuperscriptYOffset;
            CurrentFont.SubscriptYOffset = os2.ySubscriptYOffset;
            CurrentFont.StrikeoutPosition = os2.yStrikeoutPosition;
            os2.sFamilyClass = FontReader.ReadInt16();
            os2.panose = FontReader.ReadBytes(10, true); // array of 10 bytes
            os2.ulUnicodeRange1 = FontReader.ReadUInt32();
            os2.ulUnicodeRange2 = FontReader.ReadUInt32();
            os2.ulUnicodeRange3 = FontReader.ReadUInt32();
            os2.ulUnicodeRange4 = FontReader.ReadUInt32();
            os2.achVendID = FontReader.ReadString(4); // 32 bytes (4 items, each - 8 bytes)
            os2.fsSelection = FontReader.ReadUInt16();
            os2.usFirstCharIndex = FontReader.ReadUInt16();
            os2.usLastCharIndex = FontReader.ReadUInt16();
            os2.sTypoAscender = FontReader.ReadInt16();
            os2.sTypoDescender = FontReader.ReadInt16();
            os2.sTypoLineGap = FontReader.ReadInt16();
            os2.usWinAscent = FontReader.ReadUInt16();
            os2.usWinDescent = FontReader.ReadUInt16();
            os2.ulCodePageRange1 = FontReader.ReadUInt32();
            os2.ulCodePageRange2 = FontReader.ReadUInt32();
            os2.sxHeight = FontReader.ReadInt16();
            os2.sCapHeight = FontReader.ReadInt16();
            os2.usDefaultChar = FontReader.ReadUInt16();
            os2.usBreakChar = FontReader.ReadUInt16();
            os2.usMaxContext = FontReader.ReadUInt16();
            if (os2.version == 5)
            {
                os2.usLowerOpticalPointSize = FontReader.ReadUInt16();
                os2.usUpperOpticalPointSize = FontReader.ReadUInt16();
            }

            CurrentFont.Ascender = os2.sTypoAscender;
            CurrentFont.Descender = os2.sTypoDescender;
            CurrentFont.CapsHeight = os2.sCapHeight;
            var weightClass = os2.usWeightClass is > 0 and < 10 ? os2.usWeightClass * 100 : os2.usWeightClass;
            CurrentFont.Weight = new FontWeight(Math.Max(1, Math.Min(1000, (int)weightClass)));
            CurrentFont.Stretch = FontStretch.FromWidthClass(os2.usWidthClass);
            CurrentFont.Style = (os2.fsSelection & (1 << 9)) != 0 ? FontStyle.Oblique
                : (os2.fsSelection & 1) != 0 ? FontStyle.Italic
                : FontStyle.Normal;

            const UInt16 useTypoMetrics = 1 << 7;
            if ((os2.fsSelection & useTypoMetrics) != 0 || CurrentFont.LineAscent + CurrentFont.LineDescent == 0)
            {
                CurrentFont.LineAscent = os2.sTypoAscender;
                CurrentFont.LineDescent = (Int16)Math.Abs(os2.sTypoDescender);
                CurrentFont.LineGap = os2.sTypoLineGap;
            }

            if (CurrentFont.LineAscent + CurrentFont.LineDescent == 0)
            {
                CurrentFont.LineAscent = (Int16)os2.usWinAscent;
                CurrentFont.LineDescent = (Int16)os2.usWinDescent;
                CurrentFont.LineGap = 0;
                CurrentFont.LineMetricsFromWindows = true;
            }
        }

        protected virtual void ReadTTFGlyphs(TableEntry entry)
        {
            CurrentFont.MarkTrueTypeOutlines();
            var glyphs = new Glyph[maxp.NumGlyphs];
            var source = new TTFGlyphOutlineSource(this, FontReader.GetBuffer(), entry.Offset, loca.GlyphOffsets, glyphs);
            Typeface.OutlineSource = source;
            for (uint i = 0; i < glyphs.Length; ++i)
            {
                var glyph = new Glyph(i, OutlineType.TrueType);
                glyphs[i] = glyph;
                if (HasGlyphData(glyph))
                {
                    glyph.SetOutlineSource(source);
                }
            }

            Typeface.SetGlyphs(glyphs);
        }

        private static bool HasOutlines(TableDirectory tableDirectory)
        {
            return tableDirectory.TablesOffsets.ContainsKey(TableNames.glyf) ||
                   tableDirectory.TablesOffsets.ContainsKey(TableNames.CFF) ||
                   tableDirectory.TablesOffsets.ContainsKey(TableNames.CFF2);
        }

        private void CreateGlyphsWithoutOutlines()
        {
            var glyphs = new Glyph[maxp.NumGlyphs];
            for (uint i = 0; i < glyphs.Length; ++i)
            {
                glyphs[i] = new Glyph(i, OutlineType.TrueType);
            }

            Typeface.SetGlyphs(glyphs);
        }

        private bool HasGlyphData(Glyph glyph)
        {
            var glyphIndex = glyph.Index;
            if (loca.GlyphOffsets[glyphIndex] == loca.GlyphOffsets[glyphIndex + 1])
            {
                Typeface.AddErrorMessage($"[WARN] Offsets for indices {glyphIndex} and {glyphIndex + 1} are equal - current glyph has no outline");
                return false;
            }

            if (loca.GlyphOffsets[glyphIndex] == loca.GlyphOffsets[maxp.NumGlyphs])
            {
                Typeface.AddErrorMessage($"[ERR] Offset for index {glyphIndex} is equal to 'end-of-table'");
                glyph.IsInvalid = true;
                return false;
            }

            return true;
        }

        internal void ReadGlyphOutlines(FontStreamReader reader, Glyph glyph, Glyph[] fontGlyphs)
        {
            if (ReadGlyphData(reader, glyph).NumberOfContours < 0)
            {
                glyph.AddComponentOutlines(index => fontGlyphs[index]);
            }
        }

        internal GlyphHeader ReadGlyphData(FontStreamReader reader, Glyph glyph)
        {
            var glyphHeader = ReadTTFGlyphHeader(reader);
            glyph.BoundingRectangle =
                Rectangle.FromCorners(glyphHeader.XMin, glyphHeader.YMin, glyphHeader.XMax, glyphHeader.YMax);

            if (glyphHeader.NumberOfContours >= 0)
            {
                ReadSimpleGlyphComponentData(reader, glyph, glyphHeader.NumberOfContours);
            }
            else
            {
                ReadTTFCompositeGlyphComponentData(reader, glyph);
            }

            return glyphHeader;
        }

        private void ReadSimpleGlyphComponentData(FontStreamReader reader, Glyph glyph, Int32 numberOfContours)
        {
            // get the 'contour ends' array
            UInt16[] endPtsOfContours = new ushort[numberOfContours];

            for (var j = 0; j < numberOfContours; ++j)
            {
                endPtsOfContours[j] = reader.ReadUInt16();
                // calc num of points for current contour
                var numberOfPoints = (ushort)(endPtsOfContours[j] - (j != 0 ? endPtsOfContours[j - 1] : -1));
                glyph.AddOutline(new Outline() {NumberOfPoints = numberOfPoints});
            }

            // skip instructions
            var instructionLength = reader.ReadUInt16();
            glyph.SetInstructions(reader.ReadBytes(instructionLength, true));

            // calc number of points for entire glyph component
            var numberOfPointsInGlyph = (ushort)(endPtsOfContours[numberOfContours - 1] + 1);

            // read all flags
            TTFGlyphPointFlag[] flags = ReadGlyphComponentFlags(reader, numberOfPointsInGlyph);

            // read all 'x' coordinates
            Int16[] xCoords = new short[numberOfPointsInGlyph];
            for (var j = 0; j < numberOfPointsInGlyph; ++j)
            {
                if (flags[j].XShort)
                {
                    byte shortXCoord = reader.ReadByte();
                    xCoords[j] = shortXCoord;

                    if (!flags[j].XMultipurpose)
                    {
                        xCoords[j] *= -1;
                    }

                    // apply delta to obtain absolute coordinates(coordinates of current point stored as deltas to previous point, or to(0, 0) if this is the first point)
                    if (j > 0)
                    {
                        xCoords[j] += xCoords[j - 1];
                    }
                }
                else
                {
                    if (flags[j].XMultipurpose)
                    {
                        // current absolute coordinate is the same as previously calculated one, so we do not add delta here, instead we just store the same value
                        xCoords[j] = (short)(j != 0 ? xCoords[j - 1] : 0);
                    }
                    else
                    {
                        xCoords[j] = reader.ReadInt16();

                        // apply delta to obtain absolute coordinates(coordinates of current point stored as deltas to previous point, or to(0, 0) if this is the first point)
                        if (j > 0)
                        {
                            xCoords[j] += xCoords[j - 1];
                        }
                    }
                }
            }

            // read all 'y' coordinates
            Int16[] yCoords = new short[numberOfPointsInGlyph];
            for (var j = 0; j < numberOfPointsInGlyph; ++j)
            {
                if (flags[j].YShort)
                {
                    byte shortYCoord = reader.ReadByte();
                    yCoords[j] = shortYCoord;

                    if (!flags[j].YMultipurpose)
                    {
                        yCoords[j] *= -1;
                    }

                    // apply delta to obtain absolute coordinates(coordinates of current point stored as deltas to previous point, or to(0, 0) if this is the first point)
                    if (j > 0)
                    {
                        yCoords[j] += yCoords[j - 1];
                    }
                }
                else
                {
                    if (flags[j].YMultipurpose)
                    {
                        // current absolute coordinate is the same as previously calculated one, so we do not add delta here, instead we just store the same value
                        yCoords[j] = (short)(j != 0 ? yCoords[j - 1] : 0);
                    }
                    else
                    {
                        yCoords[j] = reader.ReadInt16();

                        // apply delta to obtain absolute coordinates(coordinates of current point stored as deltas to previous point, or to(0, 0) if this is the first point)
                        if (j > 0)
                        {
                            yCoords[j] += yCoords[j - 1];
                        }
                    }
                }
            }

            int currentPointIndex = 0;
            foreach (var outline in glyph.Outlines)
            {
                for (int i = 0; i < outline.NumberOfPoints; ++i)
                {
                    var outlinePoint = new OutlinePoint(xCoords[currentPointIndex], yCoords[currentPointIndex],
                        !flags[currentPointIndex].OnCurve);
                    outline.Points.Add(outlinePoint);
                    currentPointIndex++;
                }
            }
        }

        protected bool ReadTTFCompositeGlyphComponentData(FontStreamReader reader, Glyph glyph, bool readInstructions = true)
        {
            TTFGlyphCompositeFlag compositeFlag;
            glyph.IsComposite = true;

            do
            {
                var compositeGlyphComponent = new CompositeGlyphComponent();

                compositeFlag = ReadGlyphCompositeFlag(reader);
                var glyphIndexInComposite = reader.ReadUInt16();

                compositeGlyphComponent.SimpleGlyphIndex = glyphIndexInComposite;
                compositeGlyphComponent.UseMyMetrics = compositeFlag.UseMyMetrics;
                var matrix = compositeGlyphComponent.TransformMatrix;

                Int16 arg1Word = 0;
                Int16 arg2Word = 0;
                sbyte arg1Byte = 0;
                sbyte arg2Byte = 0;

                if (compositeFlag.Arg1And2AreWords)
                {
                    arg1Word = reader.ReadInt16();
                    arg2Word = reader.ReadInt16();
                }
                else
                {
                    arg1Byte = (sbyte)reader.ReadByte();
                    arg2Byte = (sbyte)reader.ReadByte();
                }

                if (compositeFlag.WeHaveAScale)
                {
                    Int16 xyScale = reader.ReadInt16();

                    matrix.M11 = xyScale.FromF2Dot14();
                    matrix.M22 = xyScale.FromF2Dot14();
                }
                else if (compositeFlag.WeHaveAnXAndYScale)
                {
                    Int16 xScale = reader.ReadInt16();
                    Int16 yScale = reader.ReadInt16();

                    matrix.M11 = xScale.FromF2Dot14();
                    matrix.M22 = yScale.FromF2Dot14();
                }
                else if (compositeFlag.WeHaveATwoByTwo)
                {
                    Int16 xScale = reader.ReadInt16();
                    Int16 scale01 = reader.ReadInt16();
                    Int16 scale10 = reader.ReadInt16();
                    Int16 yScale = reader.ReadInt16();

                    matrix.M11 = xScale.FromF2Dot14();
                    matrix.M12 = scale01.FromF2Dot14();
                    matrix.M21 = scale10.FromF2Dot14();
                    matrix.M22 = yScale.FromF2Dot14();
                }

                if (compositeFlag.ArgsAreXYValues) // matched points == false
                {
                    matrix.M31 = compositeFlag.Arg1And2AreWords ? arg1Word : arg1Byte;
                    matrix.M32 = compositeFlag.Arg1And2AreWords ? arg2Word : arg2Byte;

                    if (compositeFlag.ScaledComponentOffset) // x and y offset values are considered to be in the component glyph’s coordinate system, and the scale transformation is applied to both values
                    {
                        matrix.M31 = compositeFlag.Arg1And2AreWords ? arg1Word : arg1Byte;
                        matrix.M32 = compositeFlag.Arg1And2AreWords ? arg2Word : arg2Byte;
                    }
                }

                compositeGlyphComponent.TransformMatrix = matrix;

                // skip instructions in case we reconstructing glyph table from WOFF2 format
                if (compositeFlag.WeHaveInstructions && readInstructions)
                {
                    ushort numberOfInstructions = reader.ReadUInt16();
                    glyph.SetInstructions(reader.ReadBytes(numberOfInstructions, true));
                }

                if (!compositeFlag.ArgsAreXYValues)
                {
                    compositeGlyphComponent.IsAnchored = true;
                    compositeGlyphComponent.ParentPoint = compositeFlag.Arg1And2AreWords ? (ushort)arg1Word : (byte)arg1Byte;
                    compositeGlyphComponent.ChildPoint = compositeFlag.Arg1And2AreWords ? (ushort)arg2Word : (byte)arg2Byte;
                }

                glyph.CompositeGlyphComponents.Add(compositeGlyphComponent);
            } while (compositeFlag.MoreComponents);

            return compositeFlag.WeHaveInstructions;
        }

        protected TTFGlyphCompositeFlag ReadGlyphCompositeFlag(FontStreamReader reader)
        {
            ushort rawFlag = reader.ReadUInt16();
            return ParseRawGlyphCompositeFlag(rawFlag);
        }

        private TTFGlyphCompositeFlag ParseRawGlyphCompositeFlag(ushort rawFlag)
        {
            TTFGlyphCompositeFlag flag = new TTFGlyphCompositeFlag
            {
                Arg1And2AreWords = Convert.ToBoolean(rawFlag & 0x0001),
                ArgsAreXYValues = Convert.ToBoolean(rawFlag & 0x0002),
                WeHaveAScale = Convert.ToBoolean(rawFlag & 0x0008),
                MoreComponents = Convert.ToBoolean(rawFlag & 0x0020),
                WeHaveAnXAndYScale = Convert.ToBoolean(rawFlag & 0x0040),
                WeHaveATwoByTwo = Convert.ToBoolean(rawFlag & 0x0080),
                WeHaveInstructions = Convert.ToBoolean(rawFlag & 0x0100),
                UseMyMetrics = Convert.ToBoolean(rawFlag & 0x0200),
                ScaledComponentOffset = Convert.ToBoolean(rawFlag & 0x0800)
            };


            return flag;
        }

        private TTFGlyphPointFlag[] ReadGlyphComponentFlags(FontStreamReader reader, ushort numberOfPoints)
        {
            byte numberOfRepeats = 0;

            var flags = new TTFGlyphPointFlag[numberOfPoints];

            for (ushort i = 0; i < numberOfPoints; ++i)
            {
                if (numberOfRepeats == 0)
                {
                    var rawFlag = reader.ReadByte();
                    flags[i] = ParseRawGlyphPointFlag(rawFlag);

                    if (flags[i].Repeat)
                    {
                        numberOfRepeats = reader.ReadByte();
                    }
                }
                else
                {
                    flags[i] = flags[i - 1];
                    --numberOfRepeats;
                }
            }

            return flags;
        }

        private TTFGlyphPointFlag ParseRawGlyphPointFlag(byte rawFlag)
        {
            var flag = new TTFGlyphPointFlag();
            flag.OnCurve = Convert.ToBoolean(rawFlag & 0x01);
            flag.XShort = Convert.ToBoolean(rawFlag & 0x02);
            flag.YShort = Convert.ToBoolean(rawFlag & 0x04);
            flag.Repeat = Convert.ToBoolean(rawFlag & 0x08);
            flag.XMultipurpose = Convert.ToBoolean(rawFlag & 0x10);
            flag.YMultipurpose = Convert.ToBoolean(rawFlag & 0x20);

            return flag;
        }

        private GlyphHeader ReadTTFGlyphHeader(FontStreamReader reader)
        {
            GlyphHeader glyphHeader = new GlyphHeader();

            glyphHeader.NumberOfContours = reader.ReadInt16();
            glyphHeader.XMin = reader.ReadInt16();
            glyphHeader.YMin = reader.ReadInt16();
            glyphHeader.XMax = reader.ReadInt16();
            glyphHeader.YMax = reader.ReadInt16();

            return glyphHeader;
        }

        // Microsoft's 'kern' (version 0) and Apple's (version 1.0), as HarfBuzz reads them: each subtable to its length,
        // the last one to the end of the table.
        protected virtual void ReadKerningTable(TableEntry entry)
        {
            var table = new byte[entry.Length];
            FontReader.Position = entry.Offset;
            var read = 0;
            while (read < table.Length)
            {
                var chunk = FontReader.Read(table, read, table.Length - read);
                if (chunk == 0)
                {
                    break;
                }

                read += chunk;
            }

            if (read < 4)
            {
                return;
            }

            var apple = BigEndian16(table, 0) == 1;
            if (!apple && BigEndian16(table, 0) != 0)
            {
                return;
            }

            var count = apple ? (read >= 8 ? BigEndian32(table, 4) : 0) : BigEndian16(table, 2);
            var subtables = new List<KerningSubtable>();
            var start = apple ? 8 : 4;
            for (var i = 0; i < count && start + (apple ? 8 : 6) <= read; i++)
            {
                var subtable = new KerningSubtable { Apple = apple };
                int length;
                if (apple)
                {
                    length = (int)Math.Min(BigEndian32(table, start), int.MaxValue);
                    var coverage = table[start + 4];
                    subtable.Horizontal = (coverage & 0x80) == 0;
                    subtable.CrossStream = (coverage & 0x40) != 0;
                    subtable.Variation = (coverage & 0x20) != 0;
                    subtable.Format = table[start + 5];
                }
                else
                {
                    subtable.Version = BigEndian16(table, start);
                    length = BigEndian16(table, start + 2);
                    ParseTTFCoverage(BigEndian16(table, start + 4), subtable);
                }

                subtable.Length = (ushort)Math.Min(length, ushort.MaxValue);
                var end = i == count - 1 ? read : (int)Math.Min((long)start + length, read);
                if (end <= start)
                {
                    break;
                }

                subtable.Data = new byte[end - start];
                Array.Copy(table, start, subtable.Data, 0, end - start);
                if (subtable.Format == 0)
                {
                    ReadKerningPairs(subtable);
                }

                CurrentFont.Layout.HasCrossKerning |= subtable.CrossStream;
                CurrentFont.Layout.HasMachineKerning |= subtable.Format == 1;
                subtables.Add(subtable);
                start = end;
            }

            CurrentFont.Layout.KernSubtables = subtables.ToArray();
            if (subtables.Exists(subtable => subtable.Format == 0))
            {
                CurrentFont.KerningData = CurrentFont.Layout.KernSubtables;
            }
        }

        private static void ReadKerningPairs(KerningSubtable subtable)
        {
            var data = subtable.Data;
            var at = subtable.HeaderSize;
            if (at + 8 > data.Length)
            {
                return;
            }

            subtable.NumberOfPairs = BigEndian16(data, at);
            subtable.SearchRange = BigEndian16(data, at + 2);
            subtable.EntrySelector = BigEndian16(data, at + 4);
            subtable.RangeShift = BigEndian16(data, at + 6);
            for (int j = 0, pair = at + 8; j < subtable.NumberOfPairs && pair + 6 <= data.Length; j++, pair += 6)
            {
                var key = GenerateKerningKey(BigEndian16(data, pair), BigEndian16(data, pair + 2));
                subtable.KerningValues[key] = (short)BigEndian16(data, pair + 4);
            }
        }

        private static ushort BigEndian16(byte[] data, int at) => (ushort)((data[at] << 8) | data[at + 1]);

        private static uint BigEndian32(byte[] data, int at) => ((uint)BigEndian16(data, at) << 16) | BigEndian16(data, at + 2);

        protected virtual void ReadGDEFTable(TableEntry entry)
        {
            var gdef = FontReader.ReadGDEFTable(entry.Offset);
            gdef.FillData(Typeface);
            CurrentFont.Layout.Gdef = gdef;
        }

        private void ParseTTFCoverage(ushort rawCoverage, KerningSubtable kerningSubtable)
        {
            kerningSubtable.Horizontal = Convert.ToBoolean(rawCoverage & 0x0001);
            kerningSubtable.Minimum = Convert.ToBoolean(rawCoverage & 0x0002);
            kerningSubtable.CrossStream = Convert.ToBoolean(rawCoverage & 0x0004);
            kerningSubtable.Override = Convert.ToBoolean(rawCoverage & 0x0008);
            ushort format = (ushort)(rawCoverage & 0xFF00);
            format >>= 8; // fit 8-15 bits of ushort into byte
            kerningSubtable.Format = (byte)format;
        }

        public static UInt32 GenerateKerningKey(ushort leftIndex, ushort rightIndex)
        {
            UInt32 left = leftIndex;
            UInt32 right = rightIndex;

            UInt32 key = left;
            key <<= 16;
            key |= right; 
            return key;
        }
    };
}

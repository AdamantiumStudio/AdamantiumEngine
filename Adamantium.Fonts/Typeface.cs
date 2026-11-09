using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Parsers;

namespace Adamantium.Fonts
{
    public class Typeface
    {
        private static int lastId;

        private readonly List<IFont> fonts;
        private List<Glyph> glyphs;
        private Glyph[] madeGlyphs;
        private Func<uint, Glyph> glyphFactory;
        private List<UInt32> unicodes;
        private readonly List<string> errorMessages;
        private Dictionary<string, Glyph> glyphsByName;
        private Dictionary<uint, Glyph> glyphsByCid;
        internal IFontParser Parser { get; set; }

        internal IGlyphOutlineSource OutlineSource { get; set; }

        public IFont CurrentFont { get; private set; }

        public Typeface()
        {
            Id = System.Threading.Interlocked.Increment(ref lastId);
            OutlinesId = Id;
            fonts = new List<IFont>();
            glyphs = new List<Glyph>();
            unicodes = new List<uint>();

            errorMessages = new List<string>();
        }

        /// <summary>Unique among the typefaces of the process: with a glyph index, it names a glyph in a shared atlas.</summary>
        public int Id { get; }

        /// <summary>The <see cref="Id"/> of the typeface whose glyphs this one draws with the same outlines: its own, or
        /// that of the font it is an instance of when the instance varies only metrics and colors.</summary>
        public int OutlinesId { get; internal set; }

        public IReadOnlyList<IFont> Fonts => fonts.AsReadOnly();

        public uint GlyphCount => glyphFactory != null ? (uint)madeGlyphs.Length : (uint)glyphs.Count;

        public IReadOnlyCollection<Glyph> Glyphs => glyphFactory != null ? AllMadeGlyphs() : glyphs.AsReadOnly();
        public IReadOnlyCollection<string> ErrorMessages => errorMessages.AsReadOnly();

        internal void AddFont(IFont font)
        {
            fonts.Add(font);
        }

        public IFont GetFont(uint index)
        {
            return fonts[(int)index];
        }

        public IFont GetFont(string fullName)
        {
            return fonts.FirstOrDefault(x => x.FullName == fullName);
        }

        public void UpdateGlyphNames()
        {
            foreach (var font in fonts)
            {
                font.UpdateGlyphNamesCache();
            }
        }

        internal void SetDefaultFont()
        {
            CurrentFont = fonts[0];
        }

        internal void SetCurrentFont(IFont font)
        {
            if (!fonts.Contains(font)) return;

            CurrentFont = font;
        }
        
        public bool GetGlyphByIndex(uint index, out Glyph glyph)
        {
            glyph = null;

            if (index >= GlyphCount)
            {
                return false;
            }

            glyph = glyphFactory != null ? MadeGlyph(index) : glyphs[(int)index];

            return true;
        }

        internal void SetGlyphs(IEnumerable<Glyph> glyphsArray)
        {
            glyphs.Clear();
            glyphs.AddRange(glyphsArray);
            glyphsByName = null;
            glyphsByCid = null;
        }

        /// <summary>The font's own encoding, for a Type 1 or bare CFF font: the glyph of each of 256 codes, -1 where a
        /// code has none. Null for fonts that map characters by cmap instead.</summary>
        public IReadOnlyList<int> BuiltInEncoding { get; internal set; }

        /// <summary>Whether glyphs are named by CID (a CID-keyed CFF font) rather than by glyph name.</summary>
        public bool IsCidKeyed { get; internal set; }

        /// <summary>The glyph named <paramref name="name"/> in the font's charset or CharStrings.</summary>
        public bool GetGlyphByName(string name, out Glyph glyph)
        {
            var byName = System.Threading.Volatile.Read(ref glyphsByName);
            if (byName == null)
            {
                byName = new Dictionary<string, Glyph>(StringComparer.Ordinal);
                foreach (var each in Glyphs)
                {
                    if (each.Name != null)
                    {
                        byName.TryAdd(each.Name, each);
                    }
                }

                System.Threading.Volatile.Write(ref glyphsByName, byName);
            }

            glyph = null;
            return name != null && byName.TryGetValue(name, out glyph);
        }

        /// <summary>The glyph of <paramref name="cid"/>: by the charset of a CFF font - its CIDs in a CID-keyed one,
        /// its string ids in any other, as PDF's CIDFontType0 reads them (ISO 32000-2, 9.7.4.2); in any other font, the
        /// glyph of that index.</summary>
        public bool GetGlyphByCid(uint cid, out Glyph glyph)
        {
            if (!IsCidKeyed && Parser is not Parsers.CFF.BareCffParser)
            {
                return GetGlyphByIndex(cid, out glyph);
            }

            var byCid = System.Threading.Volatile.Read(ref glyphsByCid);
            if (byCid == null)
            {
                byCid = new Dictionary<uint, Glyph>();
                foreach (var each in Glyphs)
                {
                    byCid.TryAdd(each.Index == 0 ? 0 : each.SID, each);
                }

                System.Threading.Volatile.Write(ref glyphsByCid, byCid);
            }

            return byCid.TryGetValue(cid, out glyph);
        }

        /// <summary>A bare CFF font program, as PDF embeds one (<c>FontFile3 /Type1C</c> or <c>/CIDFontType0C</c>).
        /// </summary>
        /// <exception cref="InvalidDataException">The data is not a CFF font.</exception>
        public static Typeface LoadCff(byte[] data)
        {
            var parser = new Parsers.CFF.BareCffParser(data);
            parser.Parse();
            return parser.Typeface;
        }

        /// <summary>A Type 1 font program: PFA, PFB, or the clear text and the encrypted part as PDF embeds them
        /// (<c>FontFile</c>).</summary>
        /// <exception cref="InvalidDataException">The data is not a Type 1 font.</exception>
        public static Typeface LoadType1(byte[] data)
        {
            var parser = new Parsers.Type1.Type1Parser(data);
            parser.Parse();
            return parser.Typeface;
        }

        /// <summary>A TrueType font program as PDF embeds one (<c>FontFile2</c>): often a subset without cmap, name,
        /// OS/2, post or even maxp and hhea, since the document maps its characters and gives their widths.</summary>
        /// <exception cref="InvalidDataException">The data is not a TrueType font.</exception>
        public static Typeface LoadTrueTypeProgram(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            var stream = new FontStreamReader(data);
            var reader = new FontTypeReader(stream, Encoding.UTF8, true);
            var fontType = reader.GetFontType();
            reader.Close();
            if (fontType != FontType.Ttf)
            {
                throw new InvalidDataException("The data is not a TrueType font.");
            }

            stream.Position = 0;
            var parser = new OpenTypeParser(stream, 3) { IsEmbeddedProgram = true };
            try
            {
                parser.Parse();
            }
            catch (Exception exception) when (exception is EndOfStreamException or Exceptions.ParserException)
            {
                throw new InvalidDataException("The TrueType font is malformed: " + exception.Message, exception);
            }

            if (parser.Typeface.Fonts.Count == 0 || parser.Typeface.GlyphCount == 0)
            {
                throw new InvalidDataException("The TrueType font has no glyphs.");
            }

            parser.Typeface.SetDefaultFont();
            return parser.Typeface;
        }

        internal void SetGlyphFactory(int count, Func<uint, Glyph> factory)
        {
            madeGlyphs = new Glyph[count];
            glyphFactory = factory;
        }

        private Glyph MadeGlyph(uint index)
        {
            var glyph = System.Threading.Volatile.Read(ref madeGlyphs[index]);
            if (glyph != null)
            {
                return glyph;
            }

            var made = glyphFactory(index);
            return System.Threading.Interlocked.CompareExchange(ref madeGlyphs[index], made, null) ?? made;
        }

        private Glyph[] AllMadeGlyphs()
        {
            for (var index = 0u; index < madeGlyphs.Length; index++)
            {
                MadeGlyph(index);
            }

            return madeGlyphs;
        }

        internal void AddErrorMessage(string message)
        {
            lock (errorMessages)
            {
                errorMessages.Add(message);
            }
        }

        public byte[] GetFontAsBytesArray()
        {
            return Parser.GetFontBytes();
        }

        /// <summary>The system font of a family ("Segoe UI"), an older family name ("Segoe UI Semibold") or a full
        /// name ("Segoe UI Bold"), in its regular face when the name is a family; null when there is none. The typeface
        /// of a file is parsed once and shared (<see cref="TypefaceStore"/>).</summary>
        public static Typeface LoadSystemFont(string fontName)
        {
            var face = FontCollection.System.Match(fontName);
            return face == null ? null : TypefaceStore.GetTypeface(face.Path);
        }
        
        public static Typeface GetFontName(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(nameof(path));

            var reader = new FontTypeReader(path);
            var fontType = reader.GetFontType();
            reader.Close();
            IFontParser parser = null; 
            
            switch (fontType)
            {
                case FontType.Ttf:
                case FontType.Otf:
                    parser = new OpenTypeParser(path, 0);
                    break;
                case FontType.Woff:
                    parser = new WoffParser(path, 0);
                    break;
                case FontType.Woff2:
                    parser = new Woff2Parser(path, 0);
                    break;
                default:
                    return null;
            }

            parser.ReadFontName();

            return parser.Typeface;
        }

        public static Typeface LoadFont(string path, byte sampleResolution = 3)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(nameof(path));

            var reader = new FontTypeReader(path);
            var fontType = reader.GetFontType();
            reader.Close();
            IFontParser parser = null; 
            
            switch (fontType)
            {
                case FontType.Ttf:
                case FontType.Otf:
                    parser = new OpenTypeParser(path, sampleResolution);
                    break;
                case FontType.Woff:
                    parser = new WoffParser(path, sampleResolution);
                    break;
                case FontType.Woff2:
                    parser = new Woff2Parser(path, sampleResolution);
                    break;
                default:
                    throw new NotSupportedException("This font type is not supported");
            }

            parser.Parse();

            return parser.Typeface;
        }

        public static async Task<Typeface> LoadFontAsync(string path, byte sampleResolution)
        {
            return await Task.Run(()=> LoadFont(path, sampleResolution));
        }

        public static Typeface LoadFont(byte[] fontData, byte sampleResolution)
        {
            var fontStream = new FontStreamReader(fontData);
            return LoadFont(fontStream, sampleResolution);
        }

        public static async Task<Typeface> LoadFontAsync(byte[] fontData, byte sampleResolution)
        {
            return await Task.Run(() => LoadFont(fontData, sampleResolution));
        }

        public static Typeface LoadFont(FontStreamReader fontStream, byte sampleResolution)
        {
            var reader = new FontTypeReader(fontStream, Encoding.UTF8, true);
            var fontType = reader.GetFontType();
            reader.Close();
            fontStream.Position = 0;
            IFontParser parser = null;

            switch (fontType)
            {
                case FontType.Ttf:
                case FontType.Otf:
                    parser = new OpenTypeParser(fontStream, sampleResolution);
                    break;
                case FontType.Woff:
                    parser = new WoffParser(fontStream, sampleResolution);
                    break;
                case FontType.Woff2:
                    parser = new Woff2Parser(fontStream, sampleResolution);
                    break;
                default:
                    throw new NotSupportedException("This font type is not supported");
            }

            parser.Parse();

            return parser.Typeface;
        }

        public static async Task<Typeface> LoadFontAsync(FontStreamReader fontStream, byte sampleResolution)
        {
            return await Task.Run(() => LoadFont(fontStream, sampleResolution));
        }
    }
}
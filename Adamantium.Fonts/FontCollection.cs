using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Adamantium.Core.Fonts;

namespace Adamantium.Fonts;

/// <summary>
/// The fonts of some folders grouped into families, each face known by its headers alone (name, weight, slant, width),
/// so a family is found and a face picked without parsing a single glyph. <see cref="System"/> holds the operating
/// system's fonts; an index of them is kept on disk, and only files changed since are read again.
/// </summary>
public sealed class FontCollection
{
    private const string CacheHeader = "adamantium-font-index 1";
    private static readonly string[] Extensions = [".ttf", ".otf", ".ttc", ".otc"];
    private static readonly Lazy<FontCollection> SystemFonts = new(CreateSystem);

    private readonly object _gate = new();
    private readonly string _cachePath;
    private readonly Dictionary<string, CachedFile> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CachedFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<FontFace>> _families = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<FontFace>> _legacyFamilies = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<FontFace>> _fullNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>An empty collection; with <paramref name="cachePath"/>, the index of what is added is kept in that
    /// file.</summary>
    public FontCollection(string cachePath = null)
    {
        _cachePath = cachePath;
        LoadCache();
    }

    /// <summary>The operating system's fonts, indexed on first use.</summary>
    public static FontCollection System => SystemFonts.Value;

    /// <summary>The families, by their typographic names.</summary>
    public IReadOnlyList<string> Families
    {
        get
        {
            lock (_gate)
            {
                return _families.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
    }

    /// <summary>Adds the fonts of a folder and its subfolders.</summary>
    public void AddFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        lock (_gate)
        {
            var changed = false;
            foreach (var file in files)
            {
                changed |= AddFileLocked(file);
            }

            if (changed)
            {
                SaveCache();
            }
        }
    }

    /// <summary>Adds the faces of one font file.</summary>
    public void AddFile(string path)
    {
        lock (_gate)
        {
            if (AddFileLocked(path))
            {
                SaveCache();
            }
        }
    }

    /// <summary>The faces of a family, found by its typographic name ("Segoe UI"), its older name ("Segoe UI
    /// Semibold") or a face's full name ("Segoe UI Bold").</summary>
    public IReadOnlyList<FontFace> FacesOf(string family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return [];
        }

        lock (_gate)
        {
            if (_families.TryGetValue(family, out var faces) || _legacyFamilies.TryGetValue(family, out faces)
                || _fullNames.TryGetValue(family, out faces))
            {
                return faces.ToList();
            }

            return [];
        }
    }

    /// <summary>The face of <paramref name="family"/> that best fits the weight, slant and width asked for, picked as
    /// CSS Fonts 4 and the browsers pick it: the width first, then the slant, then the weight. Null when the family is
    /// not in the collection.</summary>
    public FontFace Match(string family, FontWeight weight = default, FontStyle style = FontStyle.Normal,
        FontStretch stretch = default)
    {
        return FontMatcher.Match(FacesOf(family), weight, style, stretch);
    }

    /// <summary>Loads a face: its typeface is parsed once and shared.</summary>
    public static IFont Load(FontFace face)
    {
        return FontOf(TypefaceStore.GetTypeface(face.Path), face);
    }

    /// <summary>Loads a face at a weight, width and style: a variable face sets its 'wght' and 'wdth' axes to them, and
    /// its 'ital' and 'slnt' to the style (clamped to its ranges; see <see cref="FontVariation.For(FontWeight, FontStretch, FontStyle, IFont)"/>);
    /// any other face is loaded as it is.</summary>
    public static IFont Load(FontFace face, FontWeight weight, FontStretch stretch, FontStyle style = FontStyle.Normal)
    {
        return Vary(Load(face), face, weight, stretch, style);
    }

    /// <summary>The face at a weight and width, as <see cref="Load(FontFace, FontWeight, FontStretch)"/> gives it, when
    /// its file is already parsed; otherwise false, and the file is parsed on a worker
    /// (<see cref="TypefaceStore.LoadInBackground"/>), which raises <see cref="TypefaceStore.Loaded"/> when done. Never
    /// waits.</summary>
    public static bool TryLoad(FontFace face, FontWeight weight, FontStretch stretch, out IFont font) =>
        TryLoad(face, weight, stretch, FontStyle.Normal, out font);

    /// <summary>The face at a weight, width and style, as <see cref="Load(FontFace, FontWeight, FontStretch, FontStyle)"/>
    /// gives it, when its file is already parsed; otherwise false, as <see cref="TryLoad(FontFace, FontWeight, FontStretch, out IFont)"/>.</summary>
    public static bool TryLoad(FontFace face, FontWeight weight, FontStretch stretch, FontStyle style, out IFont font)
    {
        if (!TypefaceStore.TryGetTypeface(face.Path, out var typeface))
        {
            TypefaceStore.LoadInBackground(face.Path);
            font = null;
            return false;
        }

        font = Vary(FontOf(typeface, face), face, weight, stretch, style);
        return true;
    }

    private static IFont FontOf(Typeface typeface, FontFace face)
    {
        var index = Math.Min(face.CollectionIndex, typeface.Fonts.Count - 1);
        return typeface.Fonts[index];
    }

    private static IFont Vary(IFont font, FontFace face, FontWeight weight, FontStretch stretch, FontStyle style)
    {
        return face.IsVariable ? font.GetInstance(FontVariation.For(weight, stretch, style, font)) : font;
    }

    private static FontCollection CreateSystem()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var collection = new FontCollection(Path.Combine(local, "Adamantium", "font-index.txt"));
        foreach (var folder in SystemFolders(local))
        {
            collection.AddFolder(folder);
        }

        return collection;
    }

    private static IEnumerable<string> SystemFolders(string local)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            yield return Path.Combine(local, "Microsoft", "Windows", "Fonts");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "/System/Library/Fonts";
            yield return "/Library/Fonts";
            yield return Path.Combine(home, "Library", "Fonts");
        }
        else
        {
            yield return "/usr/share/fonts";
            yield return "/usr/local/share/fonts";
            yield return Path.Combine(home, ".local", "share", "fonts");
            yield return Path.Combine(home, ".fonts");
        }
    }

    private bool AddFileLocked(string path)
    {
        if (_files.ContainsKey(path))
        {
            return false;
        }

        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists)
            {
                return false;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }

        var changed = false;
        if (!_cache.TryGetValue(path, out var file)
            || file.Ticks != info.LastWriteTimeUtc.Ticks || file.Length != info.Length)
        {
            List<FontFace> faces;
            try
            {
                faces = FontFaceReader.Read(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or EndOfStreamException
                                          or ArgumentException)
            {
                faces = [];
            }

            file = new CachedFile(info.LastWriteTimeUtc.Ticks, info.Length, faces);
            _cache[path] = file;
            changed = true;
        }

        _files[path] = file;
        foreach (var face in file.Faces)
        {
            Index(_families, face.Family, face);
            if (!string.Equals(face.LegacyFamily, face.Family, StringComparison.OrdinalIgnoreCase))
            {
                Index(_legacyFamilies, face.LegacyFamily, face);
            }

            if (!string.IsNullOrEmpty(face.FullName))
            {
                Index(_fullNames, face.FullName, face);
            }
        }

        return changed;
    }

    private static void Index(Dictionary<string, List<FontFace>> map, string key, FontFace face)
    {
        if (!map.TryGetValue(key, out var faces))
        {
            faces = [];
            map[key] = faces;
        }

        faces.Add(face);
    }

    private void LoadCache()
    {
        if (_cachePath == null || !File.Exists(_cachePath))
        {
            return;
        }

        try
        {
            var lines = File.ReadAllLines(_cachePath, Encoding.UTF8);
            if (lines.Length == 0 || lines[0] != CacheHeader)
            {
                return;
            }

            string path = null;
            List<FontFace> faces = null;
            foreach (var line in lines.Skip(1))
            {
                var fields = line.Split('\t');
                if (fields[0] == "file" && fields.Length == 4)
                {
                    path = fields[1];
                    faces = [];
                    _cache[path] = new CachedFile(long.Parse(fields[2], CultureInfo.InvariantCulture),
                        long.Parse(fields[3], CultureInfo.InvariantCulture), faces);
                }
                else if (fields[0] == "face" && fields.Length == 14 && faces != null)
                {
                    faces.Add(new FontFace(fields[2], fields[3], fields[4], fields[5],
                        FontWeight.Parse(fields[6]), (FontStyle)Enum.Parse(typeof(FontStyle), fields[7]),
                        FontStretch.Parse(fields[8]), path, int.Parse(fields[1], CultureInfo.InvariantCulture),
                        FontWeight.Parse(fields[9]), FontWeight.Parse(fields[10]),
                        FontStretch.Parse(fields[11]), FontStretch.Parse(fields[12])));
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException
                                      or ArgumentException or OverflowException)
        {
            _cache.Clear();
        }
    }

    private void SaveCache()
    {
        if (_cachePath == null)
        {
            return;
        }

        var text = new StringBuilder();
        text.AppendLine(CacheHeader);
        foreach (var pair in _cache.Where(p => _files.ContainsKey(p.Key) || File.Exists(p.Key)))
        {
            text.Append("file\t").Append(pair.Key).Append('\t')
                .Append(pair.Value.Ticks.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(pair.Value.Length.ToString(CultureInfo.InvariantCulture)).AppendLine();
            foreach (var face in pair.Value.Faces)
            {
                text.Append("face\t").Append(face.CollectionIndex.ToString(CultureInfo.InvariantCulture))
                    .Append('\t').Append(Clean(face.Family))
                    .Append('\t').Append(Clean(face.LegacyFamily))
                    .Append('\t').Append(Clean(face.FaceName))
                    .Append('\t').Append(Clean(face.FullName))
                    .Append('\t').Append(face.Weight.Value.ToString(CultureInfo.InvariantCulture))
                    .Append('\t').Append(face.Style)
                    .Append('\t').Append(face.Stretch.Percent.ToString(CultureInfo.InvariantCulture))
                    .Append('\t').Append(face.MinWeight.Value.ToString(CultureInfo.InvariantCulture))
                    .Append('\t').Append(face.MaxWeight.Value.ToString(CultureInfo.InvariantCulture))
                    .Append('\t').Append(face.MinStretch.Percent.ToString(CultureInfo.InvariantCulture))
                    .Append('\t').Append(face.MaxStretch.Percent.ToString(CultureInfo.InvariantCulture))
                    .Append("\tend").AppendLine();
            }
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath));
            var temporary = _cachePath + ".tmp";
            File.WriteAllText(temporary, text.ToString(), Encoding.UTF8);
            if (File.Exists(_cachePath))
            {
                File.Delete(_cachePath);
            }

            File.Move(temporary, _cachePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string Clean(string text) =>
        (text ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private sealed class CachedFile
    {
        public CachedFile(long ticks, long length, List<FontFace> faces)
        {
            Ticks = ticks;
            Length = length;
            Faces = faces;
        }

        public long Ticks { get; }

        public long Length { get; }

        public List<FontFace> Faces { get; }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Adamantium.Graphics.Core;
using Serilog;

namespace Adamantium.Graphics;

/// <summary>What happened to each effect the last time this machine tried to compile it - attempts, successes,
/// timings, and whether building it took the process down. Lives beside the shader binaries, so a driver update
/// starts a fresh folder and a fresh verdict, and names the card and driver in full because the folder is a hash.
/// <para>Written through a temporary file: unlike the in-flight note, a structure caught half-written is worthless.</para></summary>
public sealed class ShaderCompileStats
{
    public const string FileName = "compile-stats.xml";

    // Deaths after which an effect is no longer retried (until RetryAfter passes).
    private const int DeathsBeforeGivingUp = 3;

    // Long enough that a repeatable fault costs no child process per launch, short enough that a one-off recovers.
    private static readonly TimeSpan RetryAfter = TimeSpan.FromDays(7);

    // The render path asks per shader, so it cannot read the file each time. Refresh brings a background child's
    // success back in without a restart.
    private static ShaderCompileStats _current;

    private readonly string _path;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private DeviceInfo _device;

    private ShaderCompileStats(string path) => _path = path;

    public static ShaderCompileStats ForDevice(IGraphicsDevice device) => _current ??= Load(device);

    /// <summary>Bumped when the verdicts are re-read, so anything that gave up on an effect can tell, for the price
    /// of an int compare, that it is worth asking again.</summary>
    public static int Generation { get; private set; }

    public static void Refresh(IGraphicsDevice device)
    {
        _current = Load(device);
        Generation++;
    }

    /// <summary>The full type name this shader belongs to: the render path sees only the short one, and a retry has
    /// to name the type.</summary>
    public string EffectOfShader(string shaderName)
    {
        if (string.IsNullOrEmpty(shaderName)) return null;

        var dot = shaderName.IndexOf('.');
        var effect = dot < 0 ? shaderName : shaderName[..dot];

        foreach (var name in _entries.Keys)
        {
            if (name == effect || name.EndsWith("." + effect, StringComparison.Ordinal)) return name;
        }

        return null;
    }

    /// <summary>Whether an effect is still worth another child process: a death that keeps repeating is settled.</summary>
    public bool CanRetry(string name) =>
        !_entries.TryGetValue(name, out var e) || e.Deaths < DeathsBeforeGivingUp;

    private sealed class Entry
    {
        public int Attempts;
        public int Successes;
        public int Deaths;
        public int LastCompileMs;
        public string LastOutcome;
        public string DiedAt;
        public DateTime? LastAttemptUtc;
        public DateTime? PoisonedSinceUtc;
    }

    private readonly record struct DeviceInfo(string Name, uint Vendor, uint Device, uint DriverRaw, string CacheKey)
    {
        public string DriverText => Vendor switch
        {
            // The packing is the VENDOR's business, not Vulkan's: the same integer means different things on
            // different cards, so a number decoded by the wrong rule is worse than the raw one.
            0x10DE => $"{DriverRaw >> 22 & 0x3FF}.{DriverRaw >> 14 & 0xFF}.{DriverRaw >> 6 & 0xFF}.{DriverRaw & 0x3F}",
            0x8086 => $"{DriverRaw >> 14}.{DriverRaw & 0x3FFF}",
            _ => $"{DriverRaw >> 22}.{DriverRaw >> 12 & 0x3FF}.{DriverRaw & 0xFFF}"
        };
    }

    public static ShaderCompileStats Load(IGraphicsDevice device)
    {
        var folder = SafeFolder(device);
        var stats = new ShaderCompileStats(folder == null ? null : Path.Combine(folder, FileName));

        try
        {
            var props = device.MainDevice.GraphicsAdapter.AdapterProperties;
            stats._device = new DeviceInfo(props.DeviceName, props.VendorID, props.DeviceID, props.DriverVersion,
                folder == null ? "" : Path.GetFileName(folder));
        }
        catch
        {
            // Without the adapter the statistics are still usable; only the header is poorer.
        }

        if (stats._path == null || !File.Exists(stats._path)) return stats;

        try
        {
            foreach (var e in XDocument.Load(stats._path).Root?.Elements("effect") ?? Enumerable.Empty<XElement>())
            {
                var name = (string)e.Attribute("name");
                if (string.IsNullOrEmpty(name)) continue;

                stats._entries[name] = new Entry
                {
                    Attempts = (int?)e.Attribute("attempts") ?? 0,
                    Successes = (int?)e.Attribute("successes") ?? 0,
                    Deaths = (int?)e.Attribute("deaths") ?? 0,
                    LastCompileMs = (int?)e.Attribute("compileMs") ?? 0,
                    LastOutcome = (string)e.Attribute("lastOutcome"),
                    DiedAt = (string)e.Attribute("diedAt"),
                    LastAttemptUtc = (DateTime?)e.Attribute("lastAttempt"),
                    PoisonedSinceUtc = (DateTime?)e.Attribute("poisonedSince")
                };
            }
        }
        catch (Exception ex)
        {
            // A damaged file is not worth a launch. Starting over loses history, not correctness.
            Log.Logger.Warning($"Shader compile statistics unreadable ({ex.GetType().Name}) - starting a fresh record");
            stats._entries.Clear();
        }

        return stats;
    }

    /// <summary>True while an effect stays out of the compile pass: either it has died enough times to be called
    /// settled, or its last death is recent enough that trying again would just repeat it.</summary>
    public bool ShouldSkip(string name) =>
        _entries.TryGetValue(name, out var e) && IsOut(e);

    /// <summary>The same verdict reached from a shader's name, which carries the effect's SHORT name -
    /// "MaterialEffect.Material.Run.Fragment" - while the statistics are keyed by the full one.</summary>
    public bool ShouldSkipShader(string shaderName)
    {
        if (string.IsNullOrEmpty(shaderName)) return false;

        var dot = shaderName.IndexOf('.');
        var effect = dot < 0 ? shaderName : shaderName[..dot];

        foreach (var (name, entry) in _entries)
        {
            if (!IsOut(entry)) continue;
            if (name == effect || name.EndsWith("." + effect, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static bool IsOut(Entry e) =>
        e.PoisonedSinceUtc is { } since && (e.Deaths >= DeathsBeforeGivingUp || DateTime.UtcNow - since < RetryAfter);

    public void RecordAttempt(string name)
    {
        var e = Get(name);
        e.Attempts++;
        e.LastAttemptUtc = DateTime.UtcNow;
    }

    public void RecordSuccess(string name, int milliseconds)
    {
        var e = Get(name);
        e.Successes++;
        e.LastCompileMs = milliseconds;
        e.LastOutcome = "Compiled";
        // A success clears the sentence: whatever went wrong before is no longer what happens.
        e.PoisonedSinceUtc = null;
    }

    public void RecordSkipped(string name, string reason)
    {
        var e = Get(name);
        e.LastOutcome = "Skipped: " + reason;
    }

    /// <param name="stage">The shader being created when the process died; an effect holds many.</param>
    public void RecordDeath(string name, string stage = null)
    {
        var e = Get(name);
        e.Deaths++;
        e.LastOutcome = "ProcessDied";
        e.PoisonedSinceUtc = DateTime.UtcNow;
        if (!string.IsNullOrEmpty(stage)) e.DiedAt = stage;
    }

    private Entry Get(string name) =>
        _entries.TryGetValue(name, out var e) ? e : _entries[name] = new Entry();

    public void Save()
    {
        if (_path == null) return;

        try
        {
            var root = new XElement("shaderCompileStats",
                new XAttribute("updated", DateTime.UtcNow.ToString("O")),
                new XElement("device",
                    new XAttribute("name", _device.Name ?? "unknown"),
                    new XAttribute("vendor", $"{_device.Vendor:X4}"),
                    new XAttribute("device", $"{_device.Device:X4}"),
                    new XAttribute("driver", _device.DriverText),
                    new XAttribute("driverRaw", $"{_device.DriverRaw:X}"),
                    new XAttribute("cacheKey", _device.CacheKey ?? "")),
                _entries.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Effect(p.Key, p.Value)));

            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var temp = _path + ".tmp";
            new XDocument(root).Save(temp);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            // Bookkeeping. A pass that cannot write its notes still compiles shaders.
            Log.Logger.Warning($"Shader compile statistics not written: {ex.GetType().Name}");
        }
    }

    private static XElement Effect(string name, Entry e)
    {
        var element = new XElement("effect",
            new XAttribute("name", name),
            new XAttribute("attempts", e.Attempts),
            new XAttribute("successes", e.Successes));

        if (e.Deaths > 0) element.Add(new XAttribute("deaths", e.Deaths));
        if (e.LastCompileMs > 0) element.Add(new XAttribute("compileMs", e.LastCompileMs));
        if (e.LastOutcome != null) element.Add(new XAttribute("lastOutcome", e.LastOutcome));
        if (e.DiedAt != null) element.Add(new XAttribute("diedAt", e.DiedAt));
        if (e.LastAttemptUtc is { } at) element.Add(new XAttribute("lastAttempt", at.ToString("O")));
        if (e.PoisonedSinceUtc is { } since) element.Add(new XAttribute("poisonedSince", since.ToString("O")));

        return element;
    }

    private static string SafeFolder(IGraphicsDevice device)
    {
        try
        {
            return ShaderBinaryCache.DirectoryFor(device);
        }
        catch
        {
            return null;
        }
    }
}

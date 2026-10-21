using System.Runtime.CompilerServices;

namespace Adamantium.Wayland.Generator;

/// <summary>
/// Generates Adamantium.Wayland/Generated from the protocol XML files in Protocols/: wayland.xml, the core protocol,
/// and the extensions from wayland-protocols. A protocol is added by putting its XML there and running this again.
/// </summary>
public static class Program
{
    public static void Main()
    {
        var source = SourceDirectory();
        var output = Path.GetFullPath(Path.Combine(source, "..", "Adamantium.Wayland", "Generated"));

        var protocols = Directory.GetFiles(Path.Combine(source, "Protocols"), "*.xml")
            .Order(StringComparer.Ordinal)
            .Select(ProtocolReader.Read)
            .ToList();

        var emitter = new CSharpEmitter(protocols);
        var files = protocols.ToDictionary(p => Naming.FileName(p.FileName), emitter.Protocol);
        files["WaylandInterfaces.cs"] = emitter.Interfaces();

        // Everything is generated before anything is written, so a protocol that fails leaves the old output whole.
        if (Directory.Exists(output))
        {
            Directory.Delete(output, true);
        }

        Directory.CreateDirectory(output);
        foreach (var (name, text) in files)
        {
            File.WriteAllText(Path.Combine(output, name), text);
        }

        Console.WriteLine($"{protocols.Count} protocols, {protocols.Sum(p => p.Interfaces.Count)} interfaces -> {output}");
    }

    private static string SourceDirectory([CallerFilePath] string path = "")
    {
        return Path.GetDirectoryName(path)!;
    }
}

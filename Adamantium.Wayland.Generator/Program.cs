namespace Adamantium.Wayland.Generator;

public static class Program
{
    public static void Main()
    {
        var generator = new WaylandBindingGenerator();
        generator.Run();

        var protocols = generator.Protocols;
        Console.WriteLine($"{protocols.Count} protocols, {protocols.Sum(p => p.Interfaces.Count)} interfaces -> {generator.OutputPath}");
    }
}

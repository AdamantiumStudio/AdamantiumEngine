namespace Adamantium.MVVM.Tests;

/// <summary>A class with a generated command and no notifying of its own: its other properties are still reached.</summary>
public partial class CommandOnlyHost
{
    public string Name { get; set; }

    public int Runs;

    [Command]
    private void Run() => Runs++;
}

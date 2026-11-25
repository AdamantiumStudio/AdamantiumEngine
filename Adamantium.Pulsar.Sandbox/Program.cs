using System.Globalization;
using System.Runtime;
using Adamantium.Pulsar;
using Adamantium.Pulsar.Sandbox;

// Pulsar.Sandbox tone [seconds]                      a quiet tone, to hear
// Pulsar.Sandbox measure [seconds] [buffers, ms] [loads]   silence, and how often it broke off
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var command = args.Length > 0 ? args[0] : "tone";
var seconds = args.Length > 1 ? int.Parse(args[1]) : 5;
if (command == "tone")
{
    using var output = AudioOutput.CreateDefault(TimeSpan.FromMilliseconds(40));
    output.Start(new ToneRenderer(440, 0.05f));
    Thread.Sleep(TimeSpan.FromSeconds(seconds));
    Console.WriteLine($"{output.State} {output.DeviceProblem}");
    Console.WriteLine(output.Statistics);
    return;
}

var buffers = (args.Length > 2 ? args[2] : "20,30,40,60,100").Split(',').Select(int.Parse).ToList();
var loads = args.Length > 3
    ? args[3].Split(',').Select(name => Enum.Parse<GarbageLoadKind>(name, ignoreCase: true)).ToArray()
    : Enum.GetValues<GarbageLoadKind>();
Console.WriteLine($"server GC {GCSettings.IsServerGC}, {GCSettings.LatencyMode}, {Environment.ProcessorCount} cores, {seconds} s each");
Console.WriteLine("load             asked  given ms   gaps  between fills ms  GC pause ms  gen0/1/2     heap MB");
foreach (var kind in loads)
{
    using var load = new GarbageLoad(kind);
    foreach (var buffer in buffers)
    {
        using var output = AudioOutput.CreateDefault(TimeSpan.FromMilliseconds(buffer));
        output.Start(new ToneRenderer(440, 0));
        using var pauses = new PauseWatch();
        Thread.Sleep(TimeSpan.FromSeconds(seconds));
        // What the clock did says nothing of a device.
        if (output.State != AudioOutputState.Running)
        {
            Console.WriteLine($"{kind,-16} {buffer,5} {output.State}: {output.DeviceProblem}");
            continue;
        }

        var statistics = output.Statistics;
        Console.WriteLine($"{kind,-16} {buffer,5} {output.Buffer.TotalMilliseconds,9:F1} {statistics.Gaps,6} "
            + $"{statistics.LongestBetweenFills.TotalMilliseconds,17:F1} {pauses.Longest.TotalMilliseconds,12:F1}  "
            + $"{pauses.CollectionsOf(0)}/{pauses.CollectionsOf(1)}/{pauses.CollectionsOf(2)}   {GC.GetTotalMemory(false) >> 20,10}");
    }
}

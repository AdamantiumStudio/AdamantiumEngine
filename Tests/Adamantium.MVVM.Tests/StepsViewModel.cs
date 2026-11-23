using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.MVVM;

namespace Adamantium.MVVM.Tests;

/// <summary>Commands that allow concurrent runs: each run waits for a gate of its own, so a test ends them in any
/// order.</summary>
public partial class StepsViewModel
{
    public readonly List<TaskCompletionSource<bool>> Gates = new();
    public int Taken;
    public readonly List<string> Named = new();

    [Command(AllowsConcurrentRuns = true)]
    private async Task Take(CancellationToken token)
    {
        var gate = new TaskCompletionSource<bool>();
        Gates.Add(gate);
        await gate.Task.WaitAsync(token);
        Taken++;
    }

    [Command(AllowsConcurrentRuns = true)]
    private async Task Name(string name, CancellationToken token)
    {
        var gate = new TaskCompletionSource<bool>();
        Gates.Add(gate);
        await gate.Task.WaitAsync(token);
        Named.Add(name);
    }

    // Ignored on a void method: it never runs on.
    [Command(AllowsConcurrentRuns = true)]
    private void Count() => Taken++;
}

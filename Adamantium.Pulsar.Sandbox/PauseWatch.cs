namespace Adamantium.Pulsar.Sandbox;

/// <summary>The garbage collector's pauses while it runs, read after each collection.</summary>
internal sealed class PauseWatch : IDisposable
{
    private readonly ManualResetEvent _stopping = new(false);
    private readonly Thread _thread;
    private readonly int[] _collections = new int[3];

    public PauseWatch()
    {
        for (var generation = 0; generation < _collections.Length; generation++)
        {
            _collections[generation] = GC.CollectionCount(generation);
        }

        _thread = new Thread(Watch) { IsBackground = true };
        _thread.Start();
    }

    public TimeSpan Longest { get; private set; }

    public int CollectionsOf(int generation) => GC.CollectionCount(generation) - _collections[generation];

    public void Dispose()
    {
        _stopping.Set();
        _thread.Join();
        _stopping.Dispose();
    }

    private void Watch()
    {
        // The last one before this is none of its own.
        var seen = GC.GetGCMemoryInfo(GCKind.Any).Index;
        while (!_stopping.WaitOne(1))
        {
            var info = GC.GetGCMemoryInfo(GCKind.Any);
            if (info.Index == seen)
            {
                continue;
            }

            seen = info.Index;
            foreach (var pause in info.PauseDurations)
            {
                if (pause > Longest)
                {
                    Longest = pause;
                }
            }
        }
    }
}

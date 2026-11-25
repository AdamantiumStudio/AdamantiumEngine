namespace Adamantium.Pulsar.Sandbox;

/// <summary>Threads that keep the garbage collector busy, for measuring what its pauses do to an output.</summary>
internal sealed class GarbageLoad : IDisposable
{
    private const int Threads = 4;
    private const int NodesPerThread = 2_000_000;
    private static readonly TimeSpan BetweenFullCollections = TimeSpan.FromSeconds(2);

    private readonly List<Thread> _threads = [];
    private readonly ManualResetEvent _stopping = new(false);

    public GarbageLoad(GarbageLoadKind kind)
    {
        if (kind == GarbageLoadKind.None)
        {
            return;
        }

        using var ready = new CountdownEvent(Threads);
        for (var index = 0; index < Threads; index++)
        {
            var seed = index;
            Run(() => Allocate(kind, seed, ready));
        }

        ready.Wait();
        if (kind == GarbageLoadKind.FullCollections)
        {
            Run(CollectNowAndThen);
        }
    }

    public void Dispose()
    {
        _stopping.Set();
        foreach (var thread in _threads)
        {
            thread.Join();
        }

        _stopping.Dispose();
    }

    private void Run(ThreadStart work)
    {
        var thread = new Thread(work) { IsBackground = true };
        _threads.Add(thread);
        thread.Start();
    }

    // Short-lived garbage alone, or nodes of a large live graph replaced at random: what a collection has to walk.
    private void Allocate(GarbageLoadKind kind, int seed, CountdownEvent ready)
    {
        var random = new Random(seed);
        var nodes = new Node[kind == GarbageLoadKind.ShortLived ? 1 : NodesPerThread];
        for (var index = 0; index < nodes.Length; index++)
        {
            nodes[index] = new Node { Next = index > 0 ? nodes[random.Next(index)] : null };
        }

        ready.Signal();
        while (!_stopping.WaitOne(0))
        {
            for (var count = 0; count < 2000; count++)
            {
                var at = random.Next(nodes.Length);
                nodes[at] = new Node { Next = count % 16 == 0 ? nodes[random.Next(nodes.Length)] : null };
            }

            // As fast as a busy game allocates, not as fast as the machine can.
            Thread.Sleep(1);
        }
    }

    private void CollectNowAndThen()
    {
        while (!_stopping.WaitOne(BetweenFullCollections))
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        }
    }

    private sealed class Node
    {
        public Node Next { get; init; }

        // The rest of an ordinary object.
        public decimal First { get; init; }

        public decimal Second { get; init; }
    }
}

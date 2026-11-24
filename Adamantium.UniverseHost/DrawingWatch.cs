namespace Adamantium.UniverseHost;

/// <summary>Whether a drawing where the game stands is under way, and a wait for its end that no thread sits in: at a
/// debugger's stop the thread pool is held, so whoever waits goes on on the drawing's own thread as it ends.</summary>
internal sealed class DrawingWatch
{
    private readonly Lock _gate = new();
    private bool _drawing;
    private TaskCompletionSource _ended;

    /// <summary>A drawing starts.</summary>
    public void Begin()
    {
        lock (_gate)
        {
            _drawing = true;
        }
    }

    /// <summary>The drawing ends: whoever waits for it goes on, on this thread.</summary>
    public void End()
    {
        TaskCompletionSource ended;
        lock (_gate)
        {
            _drawing = false;
            ended = _ended;
            _ended = null;
        }

        ended?.SetResult();
    }

    /// <summary>Done once no drawing is under way - at once if none is; called off by its token.</summary>
    public Task WaitTillEndedAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_drawing)
            {
                return Task.CompletedTask;
            }

            _ended ??= new TaskCompletionSource();
            return _ended.Task.WaitAsync(cancellationToken);
        }
    }
}

using Adamantium.Multiverse;
using Adamantium.Multiverse.Remote;
using Serilog;

namespace Adamantium.UniverseHost;

/// <summary>Draws the world where a debugger holds the game, on a thread of its own (<see cref="UniverseHostThreads.Drawing"/>),
/// as the viewer asks while the game stands: a stop reached, a size given, a surface asked for. The debugger lets it run
/// with the link while every other thread of the game is held, the universe's own among them; before the game is let go
/// on, <see cref="WaitTillItWaitsAsync"/> sees it is done; the universe's drawing lock keeps it from ever drawing beside
/// the game's own drawing.</summary>
internal sealed class WhereItStandsDrawing : IDisposable
{
    private readonly Universe _universe;
    private readonly RemoteUniverseOutput _output;
    private readonly Func<bool> _standing;
    private readonly AutoResetEvent _asked = new(false);
    private readonly DrawingWatch _watch = new();
    private volatile bool _ended;
    private bool _placesWhatMoved;

    public WhereItStandsDrawing(Universe universe, RemoteUniverseOutput output, Func<bool> standing)
    {
        _universe = universe;
        _output = output;
        _standing = standing;
        new Thread(Run) { IsBackground = true, Name = UniverseHostThreads.Drawing }.Start();
    }

    /// <summary>Asks for a drawing; asks made before it starts come to one.</summary>
    public void Ask() => _asked.Set();

    /// <summary>Whether what the game moved so far is drawn where it is - held at a statement of its code, a breakpoint or
    /// a step; else it is drawn as the last frame placed it (see <see cref="Universe.DrawWhereItStands"/>).</summary>
    public bool PlacesWhatMoved
    {
        get => Volatile.Read(ref _placesWhatMoved);
        set => Volatile.Write(ref _placesWhatMoved, value);
    }

    /// <summary>Done once it draws no more - at once while it waits to be asked. No thread waits for it: the link's reading
    /// thread goes on to read what comes, a call called off among it.</summary>
    public Task WaitTillItWaitsAsync(CancellationToken cancellationToken) => _watch.WaitTillEndedAsync(cancellationToken);

    public void Dispose()
    {
        _ended = true;
        _asked.Set();
    }

    private void Run()
    {
        while (true)
        {
            _asked.WaitOne();
            if (_ended)
            {
                return;
            }

            _watch.Begin();
            try
            {
                // Standing when it starts - not let go on meanwhile; a new size or ring reaches the viewer only with the
                // next frame, and no frame of the game's own comes.
                while (_standing() && _universe.DrawWhereItStands(PlacesWhatMoved) && _output.HandsOverWithNextFrame)
                {
                }
            }
            catch (Exception exception)
            {
                Log.Logger.Error(exception, "Drawing the world where it stands failed");
            }
            finally
            {
                _watch.End();
            }
        }
    }
}

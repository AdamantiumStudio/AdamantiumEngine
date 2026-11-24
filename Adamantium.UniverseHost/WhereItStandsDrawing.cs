using Adamantium.Multiverse;
using Adamantium.Multiverse.Remote;
using Serilog;

namespace Adamantium.UniverseHost;

/// <summary>Draws the world where a debugger holds the game, on a thread of its own (<see cref="UniverseHostThreads.Drawing"/>),
/// as the viewer asks while the game stands: a stop reached, a size given, a surface asked for. The debugger lets it run
/// with the link while every other thread of the game is held, the universe's own among them; before the game is let go
/// on, <see cref="WaitTillItWaits"/> sees it is done; the universe's drawing lock keeps it from ever drawing beside the
/// game's own drawing.</summary>
internal sealed class WhereItStandsDrawing : IDisposable
{
    private readonly Universe _universe;
    private readonly RemoteUniverseOutput _output;
    private readonly Func<bool> _standing;
    private readonly AutoResetEvent _asked = new(false);
    // Set while it waits to be asked; reset while it draws.
    private readonly ManualResetEventSlim _waiting = new(true);
    private volatile bool _ended;

    public WhereItStandsDrawing(Universe universe, RemoteUniverseOutput output, Func<bool> standing)
    {
        _universe = universe;
        _output = output;
        _standing = standing;
        new Thread(Run) { IsBackground = true, Name = UniverseHostThreads.Drawing }.Start();
    }

    /// <summary>Asks for a drawing; asks made before it starts come to one.</summary>
    public void Ask() => _asked.Set();

    /// <summary>Waits until it draws no more - at once while it waits to be asked.</summary>
    public void WaitTillItWaits(CancellationToken cancellationToken) => _waiting.Wait(cancellationToken);

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

            _waiting.Reset();
            try
            {
                // Standing when it starts - not let go on meanwhile; a new size or ring reaches the viewer only with the
                // next frame, and no frame of the game's own comes.
                while (_standing() && _universe.DrawWhereItStands() && _output.HandsOverWithNextFrame)
                {
                }
            }
            catch (Exception exception)
            {
                Log.Logger.Error(exception, "Drawing the world where it stands failed");
            }
            finally
            {
                _waiting.Set();
            }
        }
    }
}

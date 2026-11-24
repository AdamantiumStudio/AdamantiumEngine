using Adamantium.Multiverse;
using Adamantium.Multiverse.Remote;
using Serilog;

namespace Adamantium.UniverseHost;

/// <summary>Draws the world where a debugger holds the game, on a thread of its own (<see cref="UniverseHostThreads.Drawing"/>),
/// as the viewer asks: a size given, a surface asked for, a stop reached. The debugger lets the thread run only while it
/// holds the game - every other thread of the game held, the universe's own among them - and holds it again, once it
/// waits, before it lets the game go on: the drawing never meets a step of the game.</summary>
internal sealed class WhereItStandsDrawing : IDisposable
{
    private readonly Universe _universe;
    private readonly AutoResetEvent _asked = new(false);
    private volatile bool _ended;

    public WhereItStandsDrawing(Universe universe)
    {
        _universe = universe;
        new Thread(Run) { IsBackground = true, Name = UniverseHostThreads.Drawing }.Start();
    }

    /// <summary>Asks for a drawing; asks made before it starts come to one.</summary>
    public void Ask() => _asked.Set();

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

            try
            {
                _universe.DrawWhereItStands();
            }
            catch (Exception exception)
            {
                Log.Logger.Error(exception, "Drawing the world where it stands failed");
            }
        }
    }
}

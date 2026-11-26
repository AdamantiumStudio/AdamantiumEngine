namespace Adamantium.Pulsar.Mixing;

/// <summary>Commands from one thread at a time to another, with no lock between and nothing allocated.</summary>
internal sealed class CommandQueue
{
    // More than a game asks for between two blocks; a full one makes the output wait for who empties it.
    private const int Capacity = 4096;

    private readonly Command[] _commands = new Command[Capacity];
    private uint _added;
    private uint _taken;

    public bool TryAdd(in Command command)
    {
        if (_added - Volatile.Read(ref _taken) == Capacity)
        {
            return false;
        }

        _commands[_added % Capacity] = command;
        Volatile.Write(ref _added, _added + 1);
        return true;
    }

    public bool TryTake(out Command command)
    {
        if (_taken == Volatile.Read(ref _added))
        {
            command = default;
            return false;
        }

        command = _commands[_taken % Capacity];
        // A sound removed must not be kept alive by a command long done.
        _commands[_taken % Capacity].Sound = null;
        _commands[_taken % Capacity].Stream = null;
        Volatile.Write(ref _taken, _taken + 1);
        return true;
    }
}

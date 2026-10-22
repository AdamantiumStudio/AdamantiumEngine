namespace Adamantium.Multiverse.Input.Evdev;

/// <summary>Gamepads through the Linux input devices (/dev/input/event*) the user may read - the desktop grants
/// gamepads to the user at the seat. New devices are looked for once a second.</summary>
public sealed class EvdevGamepadBackend : IGamepadBackend
{
    private const string Devices = "/dev/input";
    private const long ScanMilliseconds = 1000;

    private readonly object sync = new();
    private readonly Dictionary<string, EvdevGamepad> open = new();
    private readonly HashSet<string> skipped = [];
    private Gamepad[] connected = [];
    private long nextScan;

    public IReadOnlyList<Gamepad> Gamepads => Volatile.Read(ref connected);

    public void Update()
    {
        lock (sync)
        {
            var changed = false;
            foreach (var (path, gamepad) in open.ToArray())
            {
                if (!gamepad.Poll())
                {
                    gamepad.Close();
                    open.Remove(path);
                    changed = true;
                }
            }

            var now = Environment.TickCount64;
            if (now >= nextScan)
            {
                nextScan = now + ScanMilliseconds;
                changed |= Scan();
            }

            if (changed)
            {
                Volatile.Write(ref connected, open.Values.ToArray<Gamepad>());
            }
        }
    }

    private bool Scan()
    {
        string[] present;
        try
        {
            present = Directory.GetFiles(Devices, "event*");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        skipped.IntersectWith(present);
        var added = false;
        foreach (var path in present)
        {
            if (open.ContainsKey(path) || skipped.Contains(path))
            {
                continue;
            }

            if (EvdevGamepad.Open(path, out var readable) is { } gamepad)
            {
                open[path] = gamepad;
                added = true;
            }
            else if (readable)
            {
                skipped.Add(path);
            }
        }

        return added;
    }
}

using System.Globalization;

namespace Adamantium.GameHost;

/// <summary>What the game host runs: a scene, the folders of its content, the assemblies of its code, the size it draws
/// at; and, for a run with no one watching, how many frames it plays and where the scene is written as the game left it.
/// </summary>
public sealed class GameHostOptions
{
    private const uint DefaultWidth = 1280;
    private const uint DefaultHeight = 720;

    public string ScenePath { get; private set; }

    public List<string> ContentFolders { get; } = [];

    /// <summary>The game's assemblies, loaded by path with the PDB beside each, in the order they depend on each other.
    /// </summary>
    public List<string> CodeAssemblies { get; } = [];

    public uint Width { get; private set; } = DefaultWidth;

    public uint Height { get; private set; } = DefaultHeight;

    /// <summary>The frames played before the host stops by itself; 0 for no end.</summary>
    public int Frames { get; private set; }

    /// <summary>Where the scene is written as the game left it after <see cref="Frames"/> frames; null for nowhere.
    /// </summary>
    public string CapturePath { get; private set; }

    /// <summary>Where the last frame is written, as a PNG, after <see cref="Frames"/> frames; null for nowhere.</summary>
    public string ImagePath { get; private set; }

    /// <summary>The options of <paramref name="arguments"/>:
    /// <c>--scene file --content folder... --code assembly... --size 1280x720 --frames n --capture file --image file</c>.
    /// </summary>
    public static GameHostOptions Parse(IReadOnlyList<string> arguments)
    {
        var options = new GameHostOptions();
        for (var index = 0; index < arguments.Count; index++)
        {
            var name = arguments[index];
            var value = index + 1 < arguments.Count ? arguments[++index] : throw new ArgumentException($"{name} needs a value");
            switch (name)
            {
                case "--scene":
                    options.ScenePath = Path.GetFullPath(value);
                    break;
                case "--content":
                    options.ContentFolders.Add(Path.GetFullPath(value));
                    break;
                case "--code":
                    options.CodeAssemblies.Add(Path.GetFullPath(value));
                    break;
                case "--size":
                    options.ReadSize(value);
                    break;
                case "--frames":
                    options.ReadFrames(value);
                    break;
                case "--capture":
                    options.CapturePath = Path.GetFullPath(value);
                    break;
                case "--image":
                    options.ImagePath = Path.GetFullPath(value);
                    break;
                default:
                    throw new ArgumentException("Unknown option " + name);
            }
        }

        if (options.ScenePath == null)
            throw new ArgumentException("--scene is needed");

        return options;
    }

    private void ReadSize(string value)
    {
        var parts = value.Split('x');
        var culture = CultureInfo.InvariantCulture;
        if (parts.Length != 2 || !uint.TryParse(parts[0], NumberStyles.None, culture, out var width)
            || !uint.TryParse(parts[1], NumberStyles.None, culture, out var height)
            || width == 0 || height == 0)
            throw new ArgumentException("--size is width x height, as 1280x720");

        Width = width;
        Height = height;
    }

    private void ReadFrames(string value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var frames))
            throw new ArgumentException("--frames is a count of frames, 0 or more");

        Frames = frames;
    }
}

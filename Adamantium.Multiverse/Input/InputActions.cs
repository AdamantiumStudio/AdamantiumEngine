using System.Text.Json;
using System.Text.Json.Serialization;

namespace Adamantium.Multiverse.Input;

/// <summary>
/// The actions of a universe: its maps, read once per frame right after the input, before any service runs - from the
/// higher <see cref="InputActionMap.Priority"/> down, a control that drove an action not reaching the maps below.
/// Within a map, a chord held takes the controls of a binding it holds more modifiers over: Ctrl+S is not also S.
/// The user's rebindings travel as JSON: <c>{ "Camera.Move": [ { "up": "Key:W", "down": "Key:S", ... } ] }</c>.
/// </summary>
public sealed class InputActions
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly List<InputActionMap> maps = [];
    private readonly HashSet<InputControl> consumed = [];
    private readonly List<InputControl> used = [];
    private readonly List<InputBinding> heldChords = [];
    private readonly Dictionary<string, InputBinding[]> overrides = new(StringComparer.Ordinal);

    /// <summary>The maps, the highest priority first.</summary>
    public IReadOnlyList<InputActionMap> Maps => maps;

    /// <summary>Adds a map; rebindings loaded before apply to its actions.</summary>
    public InputActionMap Add(InputActionMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (maps.Contains(map))
        {
            return map;
        }

        var index = 0;
        while (index < maps.Count && maps[index].Priority >= map.Priority)
        {
            index++;
        }

        maps.Insert(index, map);
        foreach (var action in map.Actions)
        {
            if (overrides.TryGetValue(action.FullName, out var bindings))
            {
                action.Rebind(bindings);
            }
        }

        return map;
    }

    public void Remove(InputActionMap map)
    {
        maps.Remove(map);
    }

    /// <summary>Reads every enabled map against the routed input.</summary>
    public void Update(InputWormhole keyboard, InputWormhole pointer)
    {
        consumed.Clear();
        foreach (var map in maps)
        {
            if (!map.IsEnabled)
            {
                foreach (var action in map.Actions)
                {
                    action.Clear();
                }

                continue;
            }

            var reader = new InputReader(keyboard, pointer, map.GamepadSlot);
            used.Clear();
            // The chords whose modifiers are held: within the map, each takes the controls of the bindings it names and
            // goes beyond - Ctrl+S over S.
            heldChords.Clear();
            foreach (var action in map.Actions)
            {
                var bindings = action.Bindings;
                for (int i = 0; i < bindings.Count; i++)
                {
                    var binding = bindings[i];
                    if (binding.Modifiers.Count > 0 && !binding.IsBlocked(consumed) && binding.ModifiersHeld(reader))
                    {
                        heldChords.Add(binding);
                    }
                }
            }

            foreach (var action in map.Actions)
            {
                action.Evaluate(reader, consumed, used, heldChords);
            }

            consumed.UnionWith(used);
        }
    }

    /// <summary>The rebound actions of the added maps, as JSON.</summary>
    public string SaveOverrides()
    {
        var data = new SortedDictionary<string, InputBindingData[]>(StringComparer.Ordinal);
        foreach (var map in maps)
        {
            foreach (var action in map.Actions)
            {
                if (action.IsRebound)
                {
                    data[action.FullName] = action.Bindings.Select(InputBindingData.From).ToArray();
                }
            }
        }

        return JsonSerializer.Serialize(data, Json);
    }

    /// <summary>Rebinds the actions the JSON names - the added ones now, the ones of maps added later on adding.</summary>
    /// <exception cref="FormatException">A binding names no control.</exception>
    public void LoadOverrides(string json)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, InputBindingData[]>>(json, Json) ?? new();
        foreach (var (fullName, bindings) in data)
        {
            overrides[fullName] = bindings.Select(binding => binding.ToBinding()).ToArray();
        }

        foreach (var map in maps)
        {
            foreach (var action in map.Actions)
            {
                if (overrides.TryGetValue(action.FullName, out var bindings))
                {
                    action.Rebind(bindings);
                }
            }
        }
    }
}

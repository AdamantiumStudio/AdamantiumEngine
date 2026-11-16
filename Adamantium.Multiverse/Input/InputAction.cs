using Adamantium.Mathematics;

namespace Adamantium.Multiverse.Input;

/// <summary>
/// Something the user does - move, turn, take a screenshot - rather than the key it is on. Code declares it with its
/// default bindings; the user may rebind it (see <see cref="InputActions.LoadOverrides"/>). Its state is refreshed once
/// per frame by the universe's <see cref="InputActions"/>.
/// </summary>
public sealed class InputAction
{
    private const float ButtonPressPoint = 0.5f;

    private readonly InputBinding[] defaultBindings;
    private InputBinding[] bindings;

    public InputAction(string name, InputActionType type, params InputBinding[] defaultBindings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Contains('.'))
        {
            throw new ArgumentException("An action's name must not contain '.': the map's name is joined with one.",
                nameof(name));
        }

        Name = name;
        Type = type;
        this.defaultBindings = defaultBindings ?? [];
        bindings = this.defaultBindings;
    }

    public string Name { get; }

    public InputActionType Type { get; }

    /// <summary>The map the action is in; null until it is added to one.</summary>
    public InputActionMap Map { get; internal set; }

    /// <summary>"Map.Action": the name overrides are saved under.</summary>
    public string FullName => Map == null ? Name : $"{Map.Name}.{Name}";

    public IReadOnlyList<InputBinding> DefaultBindings => defaultBindings;

    public IReadOnlyList<InputBinding> Bindings => bindings;

    /// <summary>Whether the bindings differ from the defaults.</summary>
    public bool IsRebound => !ReferenceEquals(bindings, defaultBindings);

    /// <summary>Held this frame.</summary>
    public bool IsDown { get; private set; }

    /// <summary>Whether the modifiers of one of its bindings are all held this frame - for a drag, the button and keys
    /// it is made with - whether the drag moves or not; for a binding with none, whether it is down. What a gesture
    /// lasts by: its value is zero on the frames the pointer stands still.</summary>
    public bool IsHeld { get; private set; }

    /// <summary>Went down this frame, a tap shorter than a frame included.</summary>
    public bool IsPressed { get; private set; }

    /// <summary>Went up this frame, a tap shorter than a frame included.</summary>
    public bool IsReleased { get; private set; }

    /// <summary>The value along X: -1..1 for bounded controls, 1 or 0 for a button.</summary>
    public float Value { get; private set; }

    /// <summary>The value on both axes, X to the right and Y up.</summary>
    public Vector2F Vector { get; private set; }

    /// <summary>Replaces the bindings; takes effect from the next frame.</summary>
    public void Rebind(params InputBinding[] newBindings)
    {
        bindings = newBindings ?? [];
    }

    public void ResetBindings()
    {
        bindings = defaultBindings;
    }

    internal void Evaluate(in InputReader reader, HashSet<InputControl> consumed, List<InputControl> used,
        List<InputBinding> heldChords)
    {
        var wasDown = IsDown;
        var bounded = Vector2F.Zero;
        var unbounded = Vector2F.Zero;
        var pressed = false;
        var released = false;
        var active = false;
        var held = false;

        foreach (var binding in bindings)
        {
            if (binding.IsBlocked(consumed) || !binding.ModifiersHeld(reader) || IsSuperseded(binding, heldChords))
            {
                continue;
            }

            var value = binding.Read(reader);
            held |= binding.Modifiers.Count > 0 || value != Vector2F.Zero;
            var bindingPressed = binding.Pressed(reader);
            if (binding.IsBounded)
            {
                bounded += value;
            }
            else
            {
                unbounded += value;
            }

            if (value != Vector2F.Zero || bindingPressed)
            {
                active = true;
                binding.CollectControls(used);
            }

            pressed |= bindingPressed;
            released |= binding.Released(reader);
        }

        var vector = new Vector2F(Math.Clamp(bounded.X, -1f, 1f), Math.Clamp(bounded.Y, -1f, 1f)) + unbounded;
        Vector = vector;
        Value = vector.X;
        IsDown = Type == InputActionType.Button
            ? Math.Abs(vector.X) >= ButtonPressPoint || Math.Abs(vector.Y) >= ButtonPressPoint
            : active && vector != Vector2F.Zero;
        if (Type == InputActionType.Button)
        {
            Value = IsDown ? 1 : 0;
        }

        IsPressed = pressed || (IsDown && !wasDown);
        IsReleased = released || (!IsDown && wasDown);
        IsHeld = held || IsDown;
    }

    private static bool IsSuperseded(InputBinding binding, List<InputBinding> heldChords)
    {
        for (int i = 0; i < heldChords.Count; i++)
        {
            if (heldChords[i].Supersedes(binding))
            {
                return true;
            }
        }

        return false;
    }

    internal void Clear()
    {
        IsReleased = IsDown;
        IsHeld = false;
        IsDown = false;
        IsPressed = false;
        Value = 0;
        Vector = Vector2F.Zero;
    }
}

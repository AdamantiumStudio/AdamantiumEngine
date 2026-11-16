using Adamantium.Mathematics;

namespace Adamantium.Multiverse.Input;

/// <summary>
/// One way to drive an action: a control, two controls making an axis, four making a vector or two axes making a
/// stick - optionally only while <see cref="Modifiers"/> are held, as Control for Ctrl+B.
/// </summary>
public sealed class InputBinding
{
    private readonly InputControl[] controls;
    private readonly InputControl[] modifiers;

    private InputBinding(InputBindingKind kind, InputControl[] controls, InputControl[] modifiers, float scaleX,
        float scaleY)
    {
        Kind = kind;
        this.controls = controls;
        this.modifiers = modifiers ?? [];
        ScaleX = scaleX;
        ScaleY = scaleY;
    }

    public InputBindingKind Kind { get; }

    /// <summary>The controls in the order the kind takes them: the one control; negative, positive; up, down, left,
    /// right; X, Y.</summary>
    public IReadOnlyList<InputControl> Controls => controls;

    /// <summary>Controls that must be held for the binding to count.</summary>
    public IReadOnlyList<InputControl> Modifiers => modifiers;

    public float ScaleX { get; }

    public float ScaleY { get; }

    /// <summary>Whether the value stays within -1..1 on each axis.</summary>
    public bool IsBounded => Array.TrueForAll(controls, control => control.IsBounded);

    public static InputBinding Control(InputControl control, float scale = 1, params InputControl[] modifiers)
    {
        return new InputBinding(InputBindingKind.Control, [control], modifiers, scale, 1);
    }

    public static InputBinding Axis(InputControl negative, InputControl positive, params InputControl[] modifiers)
    {
        return new InputBinding(InputBindingKind.Axis, [negative, positive], modifiers, 1, 1);
    }

    public static InputBinding Vector(InputControl up, InputControl down, InputControl left, InputControl right,
        params InputControl[] modifiers)
    {
        return new InputBinding(InputBindingKind.Vector, [up, down, left, right], modifiers, 1, 1);
    }

    public static InputBinding Stick(InputControl x, InputControl y, float scaleX = 1, float scaleY = 1,
        params InputControl[] modifiers)
    {
        return new InputBinding(InputBindingKind.Stick, [x, y], modifiers, scaleX, scaleY);
    }

    /// <summary>Whether it names <paramref name="control"/>, as a part or as a modifier.</summary>
    public bool Uses(InputControl control)
    {
        return Array.IndexOf(controls, control) >= 0 || Array.IndexOf(modifiers, control) >= 0;
    }

    public override string ToString()
    {
        var parts = string.Join(", ", controls);
        return modifiers.Length == 0 ? $"{Kind}({parts})" : $"{Kind}({parts}) + {string.Join(" + ", modifiers)}";
    }

    internal bool ModifiersHeld(in InputReader reader)
    {
        foreach (var modifier in modifiers)
        {
            if (reader.Value(modifier) == 0)
            {
                return false;
            }
        }

        return true;
    }

    internal Vector2F Read(in InputReader reader)
    {
        return Kind switch
        {
            InputBindingKind.Control => new Vector2F(reader.Value(controls[0]) * ScaleX, 0),
            InputBindingKind.Axis => new Vector2F((reader.Value(controls[1]) - reader.Value(controls[0])) * ScaleX, 0),
            InputBindingKind.Vector => new Vector2F(reader.Value(controls[3]) - reader.Value(controls[2]),
                reader.Value(controls[0]) - reader.Value(controls[1])),
            InputBindingKind.Stick => new Vector2F(reader.Value(controls[0]) * ScaleX,
                reader.Value(controls[1]) * ScaleY),
            _ => Vector2F.Zero
        };
    }

    internal bool Pressed(in InputReader reader)
    {
        return Kind == InputBindingKind.Control && reader.Pressed(controls[0]);
    }

    internal bool Released(in InputReader reader)
    {
        return Kind == InputBindingKind.Control && reader.Released(controls[0]);
    }

    /// <summary>Whether this binding, its modifiers held, takes the controls of <paramref name="other"/> in the same map:
    /// it names every control of the other and more modifiers - Ctrl+S over S, the right button's drag with Alt over
    /// the drag alone - so a shortcut is not also the key it is made with.</summary>
    internal bool Supersedes(InputBinding other)
    {
        if (ReferenceEquals(this, other) || modifiers.Length <= other.modifiers.Length)
        {
            return false;
        }

        foreach (var control in other.controls)
        {
            if (!Uses(control))
            {
                return false;
            }
        }

        foreach (var modifier in other.modifiers)
        {
            if (!Uses(modifier))
            {
                return false;
            }
        }

        return true;
    }

    internal bool IsBlocked(HashSet<InputControl> consumed)
    {
        if (consumed.Count == 0)
        {
            return false;
        }

        foreach (var control in controls)
        {
            if (consumed.Contains(control))
            {
                return true;
            }
        }

        foreach (var modifier in modifiers)
        {
            if (consumed.Contains(modifier))
            {
                return true;
            }
        }

        return false;
    }

    internal void CollectControls(List<InputControl> used)
    {
        used.AddRange(controls);
        used.AddRange(modifiers);
    }
}

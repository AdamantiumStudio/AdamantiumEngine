namespace Adamantium.Wayland;

/// <summary>A request or an event as the generated tables describe it.</summary>
public readonly unsafe struct MessageDescription
{
    public MessageDescription(string name, string signature, WlInterface*[] types)
    {
        Name = name;
        Signature = signature;
        Types = types;
    }

    public string Name { get; }

    public string Signature { get; }

    public WlInterface*[] Types { get; }
}

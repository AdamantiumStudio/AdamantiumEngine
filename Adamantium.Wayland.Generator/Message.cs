namespace Adamantium.Wayland.Generator;

public sealed record Message(string Name, bool IsDestructor, int Since, string Summary, IReadOnlyList<Argument> Arguments)
{
    /// <summary>The untyped new_id of wl_registry.bind: the caller names the interface, which goes on the wire as a
    /// string and a version ahead of the id.</summary>
    public bool HasUntypedNewId => Arguments.Any(a => a.Type == ArgumentType.NewId && a.Interface == null);

    public Argument NewId => Arguments.FirstOrDefault(a => a.Type == ArgumentType.NewId);
}

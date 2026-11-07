namespace Adamantium.Wayland.Generator.CodeGeneration;

/// <summary>The C# names of an interface's requests and events: one that meets a member of WaylandProxy, the class or
/// one of its enums takes Request or Event after it.</summary>
internal sealed class MemberNames
{
    private static readonly HashSet<string> BaseMembers =
    [
        "Handle", "Version", "Id", "DestroyProxy", "Interface", "InterfaceName", "InterfaceVersion", "Create",
        "Adopt", "Find", "Marshal", "MarshalDestroy", "DispatchEvent", "Equals", "GetHashCode", "ToString",
        "GetType", "MemberwiseClone", "Finalize",
    ];

    public MemberNames(string className, Interface i)
    {
        var taken = new HashSet<string>(BaseMembers) { className };
        foreach (var e in i.Enums)
        {
            taken.Add(Naming.Pascal(e.Name));
        }

        Requests = i.Requests.Select(r => Claim(taken, Naming.Pascal(r.Name), "Request")).ToList();
        Events = i.Events.Select(e => Claim(taken, Naming.Pascal(e.Name), "Event")).ToList();

        foreach (var e in Events)
        {
            if (!taken.Add(e + "Handler"))
            {
                throw new InvalidDataException($"{i.Name}: {e}Handler is taken");
            }
        }
    }

    public IReadOnlyList<string> Requests { get; }

    public IReadOnlyList<string> Events { get; }

    private static string Claim(HashSet<string> taken, string name, string suffix)
    {
        var claimed = taken.Contains(name) ? name + suffix : name;
        if (!taken.Add(claimed))
        {
            throw new InvalidDataException($"Member name '{claimed}' is taken twice");
        }

        return claimed;
    }
}

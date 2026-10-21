using System.Runtime.InteropServices;

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

/// <summary>
/// Builds the wl_interface tables in native memory, where libwayland reads them. Every interface is allocated before
/// any is filled, since their messages point at each other. They live as long as the process: a proxy's interface is
/// read for as long as the proxy is.
/// </summary>
public static unsafe class WaylandInterfaceBuilder
{
    public static WlInterface* Allocate() => (WlInterface*)NativeMemory.AllocZeroed((nuint)sizeof(WlInterface));

    public static void Fill(WlInterface* target, string name, int version, MessageDescription[] requests,
        MessageDescription[] events)
    {
        target->name = Utf8(name);
        target->version = version;
        target->method_count = requests.Length;
        target->methods = Messages(requests);
        target->event_count = events.Length;
        target->events = Messages(events);
    }

    private static WlMessage* Messages(MessageDescription[] descriptions)
    {
        if (descriptions.Length == 0)
        {
            return null;
        }

        var messages = (WlMessage*)NativeMemory.AllocZeroed((nuint)descriptions.Length, (nuint)sizeof(WlMessage));
        for (var i = 0; i < descriptions.Length; i++)
        {
            var description = descriptions[i];
            messages[i].name = Utf8(description.Name);
            messages[i].signature = Utf8(description.Signature);

            // At least one slot, so that no message has a null table even without arguments.
            var count = Math.Max(description.Types.Length, 1);
            var types = (WlInterface**)NativeMemory.AllocZeroed((nuint)count, (nuint)sizeof(WlInterface*));
            for (var t = 0; t < description.Types.Length; t++)
            {
                types[t] = description.Types[t];
            }

            messages[i].types = types;
        }

        return messages;
    }

    private static byte* Utf8(string value) => (byte*)Marshal.StringToCoTaskMemUTF8(value);
}

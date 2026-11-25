using System;
using System.Runtime.InteropServices;

namespace Adamantium.Win32.Audio;

/// <summary>The audio devices of Windows (<c>IMMDeviceEnumerator</c>). Called on a thread with COM initialized.</summary>
public static unsafe partial class AudioEndpoints
{
    /// <summary><c>E_NOTFOUND</c>: there is no such device.</summary>
    public const int NotFound = unchecked((int)0x80070490);

    private const int Render = 0;
    private const int Console = 0;
    private const uint AllContexts = 0x17;

    private static readonly Guid EnumeratorClass = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid EnumeratorInterface = new("A95664D2-9614-4F35-A746-DE8DB63617E6");

    /// <summary>A client of the default output device.</summary>
    public static int ActivateDefaultOutput(out AudioClient client)
    {
        client = default;
        void* enumerator = null;
        void* device = null;
        var classId = EnumeratorClass;
        var interfaceId = EnumeratorInterface;
        var result = CoCreateInstance(&classId, IntPtr.Zero, AllContexts, &interfaceId, &enumerator);
        if (result < 0)
        {
            return result;
        }

        result = ((delegate* unmanaged<void*, int, int, void**, int>)TableOf(enumerator)[4])(enumerator, Render, Console,
            &device);
        Marshal.Release((IntPtr)enumerator);
        if (result < 0)
        {
            return result;
        }

        void* activated = null;
        var clientId = AudioClient.InterfaceId;
        result = ((delegate* unmanaged<void*, Guid*, uint, void*, void**, int>)TableOf(device)[3])(device, &clientId,
            AllContexts, null, &activated);
        Marshal.Release((IntPtr)device);
        client = new AudioClient(activated);
        return result;
    }

    private static void** TableOf(void* instance) => *(void***)instance;

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* classId, IntPtr outer, uint context, Guid* interfaceId, void** instance);
}

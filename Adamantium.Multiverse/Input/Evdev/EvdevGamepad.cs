using System.Runtime.InteropServices;
using System.Text;
using Adamantium.Core;
using Adamantium.Mathematics;

namespace Adamantium.Multiverse.Input.Evdev;

/// <summary>A gamepad read from its evdev device, its state taken in at each SYN_REPORT.</summary>
public sealed unsafe class EvdevGamepad : Gamepad
{
    private const int AxisCount = 0x40;
    private const int KeyBytes = 0x300 / 8;

    private readonly object sync = new();
    private readonly int fd;
    private readonly string name;
    private readonly (int Minimum, int Maximum)[] ranges;
    private readonly int[] axes = new int[AxisCount];
    private GamepadButton buttons;
    private bool leftTriggerDown;
    private bool rightTriggerDown;
    private GamepadState state = new() { IsConnected = true };

    /// <summary>A gamepad with these axis ranges, fed by <see cref="Take"/>: for reading recorded events.</summary>
    public EvdevGamepad(string name, (int Minimum, int Maximum)[] ranges) : this(-1, name, ranges)
    {
    }

    private EvdevGamepad(int fd, string name, (int Minimum, int Maximum)[] ranges)
    {
        this.fd = fd;
        this.name = name ?? string.Empty;
        this.ranges = ranges;
        for (var axis = 0; axis < AxisCount; axis++)
        {
            axes[axis] = axis is EvdevMapping.AxisZ or EvdevMapping.AxisRz or EvdevMapping.AxisGas or EvdevMapping.AxisBrake
                ? ranges[axis].Minimum
                : (ranges[axis].Minimum + ranges[axis].Maximum) / 2;
        }
    }

    public override string Name => name;

    public override GamepadFace Face => EvdevMapping.Face(name);

    public override GamepadState GetState()
    {
        lock (sync)
        {
            return state;
        }
    }

    /// <summary>Opens the device at <paramref name="path"/> if it is a gamepad; null when it is not, or may not be read -
    /// <paramref name="readable"/> tells the two apart, as the user may be let in a moment after a device arrives.</summary>
    public static EvdevGamepad Open(string path, out bool readable)
    {
        var fd = PosixInterop.Open(path, PosixInterop.OpenReadOnly | PosixInterop.OpenNonBlock | PosixInterop.OpenCloseOnExec);
        readable = fd >= 0;
        if (fd < 0)
        {
            return null;
        }

        var keys = stackalloc byte[KeyBytes];
        if (PosixInterop.Ioctl(fd, Read(0x20 + EvdevMapping.EventKey, KeyBytes), keys) < 0 ||
            (keys[EvdevMapping.ButtonSouth / 8] & (1 << (EvdevMapping.ButtonSouth % 8))) == 0)
        {
            PosixInterop.Close(fd);
            return null;
        }

        var ranges = new (int, int)[AxisCount];
        var info = stackalloc int[6];
        for (var axis = 0; axis < AxisCount; axis++)
        {
            if (PosixInterop.Ioctl(fd, Read(0x40 + axis, 24), info) == 0)
            {
                ranges[axis] = (info[1], info[2]);
            }
        }

        var text = stackalloc byte[256];
        var length = PosixInterop.Ioctl(fd, Read(0x06, 256), text);
        var deviceName = length > 0 ? Encoding.UTF8.GetString(text, length).TrimEnd('\0') : string.Empty;
        var gamepad = new EvdevGamepad(fd, deviceName, ranges);
        gamepad.Resync();
        return gamepad;
    }

    /// <summary>Takes in one event; a SYN_REPORT makes what came before it the state.</summary>
    public void Take(ushort type, ushort code, int value)
    {
        lock (sync)
        {
            switch (type)
            {
                case EvdevMapping.EventKey when code == EvdevMapping.ButtonTriggerLeft2:
                    leftTriggerDown = value != 0;
                    break;
                case EvdevMapping.EventKey when code == EvdevMapping.ButtonTriggerRight2:
                    rightTriggerDown = value != 0;
                    break;
                case EvdevMapping.EventKey:
                    var button = EvdevMapping.Button(code);
                    buttons = value != 0 ? buttons | button : buttons & ~button;
                    break;
                case EvdevMapping.EventAbsolute when code < AxisCount:
                    axes[code] = value;
                    break;
                case EvdevMapping.EventSync when code == EvdevMapping.SyncReport:
                    state = Compose();
                    break;
            }
        }
    }

    /// <summary>Reads what the device has sent; false once it is gone.</summary>
    internal bool Poll()
    {
        var events = stackalloc byte[24 * 64];
        while (true)
        {
            var count = (long)PosixInterop.Read(fd, events, 24 * 64);
            if (count < 0)
            {
                var error = Marshal.GetLastPInvokeError();
                if (error == PosixInterop.ErrorInterrupted)
                {
                    continue;
                }

                return error == PosixInterop.ErrorAgain;
            }

            if (count == 0)
            {
                return false;
            }

            for (var at = 0; at + 24 <= count; at += 24)
            {
                var type = *(ushort*)(events + at + 16);
                var code = *(ushort*)(events + at + 18);
                var value = *(int*)(events + at + 20);
                if (type == EvdevMapping.EventSync && code == EvdevMapping.SyncDropped)
                {
                    Resync();
                    continue;
                }

                Take(type, code, value);
            }
        }
    }

    internal void Close() => PosixInterop.Close(fd);

    private GamepadState Compose()
    {
        var hatX = axes[EvdevMapping.AxisHatX];
        var hatY = axes[EvdevMapping.AxisHatY];
        var pressed = buttons
                      | (hatY < 0 ? GamepadButton.DpadUp : 0) | (hatY > 0 ? GamepadButton.DpadDown : 0)
                      | (hatX < 0 ? GamepadButton.DpadLeft : 0) | (hatX > 0 ? GamepadButton.DpadRight : 0);
        return new GamepadState
        {
            IsConnected = true,
            Buttons = pressed,
            LeftThumb = new Vector2F(Stick(EvdevMapping.AxisX), -Stick(EvdevMapping.AxisY)),
            RightThumb = new Vector2F(Stick(EvdevMapping.AxisRx), -Stick(EvdevMapping.AxisRy)),
            LeftTrigger = Trigger(EvdevMapping.AxisZ, EvdevMapping.AxisBrake, leftTriggerDown),
            RightTrigger = Trigger(EvdevMapping.AxisRz, EvdevMapping.AxisGas, rightTriggerDown),
        };
    }

    private float Stick(ushort axis) => EvdevMapping.Stick(axes[axis], ranges[axis].Minimum, ranges[axis].Maximum);

    private float Trigger(ushort axis, ushort alternative, bool digital)
    {
        var used = ranges[axis].Maximum > ranges[axis].Minimum ? axis : alternative;
        var (minimum, maximum) = ranges[used];
        return maximum > minimum ? EvdevMapping.Trigger(axes[used], minimum, maximum) : digital ? 1 : 0;
    }

    private void Resync()
    {
        var keys = stackalloc byte[KeyBytes];
        var info = stackalloc int[6];
        lock (sync)
        {
            if (PosixInterop.Ioctl(fd, Read(0x18, KeyBytes), keys) >= 0)
            {
                buttons = GamepadButton.None;
                leftTriggerDown = rightTriggerDown = false;
                for (var code = 0; code < KeyBytes * 8; code++)
                {
                    if ((keys[code / 8] & (1 << (code % 8))) != 0)
                    {
                        Take(EvdevMapping.EventKey, (ushort)code, 1);
                    }
                }
            }

            for (var axis = 0; axis < AxisCount; axis++)
            {
                if (ranges[axis].Maximum > ranges[axis].Minimum && PosixInterop.Ioctl(fd, Read(0x40 + axis, 24), info) == 0)
                {
                    axes[axis] = info[0];
                }
            }

            state = Compose();
        }
    }

    private static nuint Read(int number, int size) => (nuint)((2u << 30) | ((uint)size << 16) | ((uint)'E' << 8) | (uint)number);
}

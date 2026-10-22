using System;
using Adamantium.Wayland;
using NUnit.Framework;

namespace Adamantium.EngineTests;

public class XkbKeyboardTests
{
    private const uint DeadAcuteKey = 32;
    private const uint EKey = 18;

    private const string Keymap = """
        xkb_keymap {
            xkb_keycodes { minimum = 8; maximum = 255; <AC03> = 40; <AD03> = 26; };
            xkb_types { include "complete" };
            xkb_compat { include "complete" };
            xkb_symbols { key <AC03> { [ dead_acute ] }; key <AD03> { [ e, E ] }; };
        };
        """;

    [Test]
    public void ADeadKey_ThenALetter_ComposeOneCharacter()
    {
        using var keyboard = Compile();

        var first = keyboard.Compose(DeadAcuteKey, out var pending);
        var second = keyboard.Compose(EKey, out var composed);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(XkbComposeStatus.Composing));
            Assert.That(pending, Is.Empty);
            Assert.That(second, Is.EqualTo(XkbComposeStatus.Composed));
            Assert.That(composed, Is.EqualTo("é"));
        });
    }

    [Test]
    public void ALetter_OutsideASequence_TypesItsOwnText()
    {
        using var keyboard = Compile();

        Assert.That(keyboard.Compose(EKey, out _), Is.EqualTo(XkbComposeStatus.None));
        Assert.That(keyboard.Text(EKey), Is.EqualTo("e"));
    }

    private static XkbKeyboard Compile()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("libxkbcommon is a Linux library.");
        }

        var locale = Environment.GetEnvironmentVariable("LC_ALL");
        Environment.SetEnvironmentVariable("LC_ALL", "en_US.UTF-8");
        try
        {
            XkbKeyboard keyboard;
            try
            {
                keyboard = XkbKeyboard.FromText(Keymap);
            }
            catch (DllNotFoundException)
            {
                Assert.Ignore("libxkbcommon is not installed.");
                return null;
            }

            Assert.That(keyboard, Is.Not.Null, "the keymap compiles");
            return keyboard;
        }
        finally
        {
            Environment.SetEnvironmentVariable("LC_ALL", locale);
        }
    }
}

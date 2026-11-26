### Added

- `Adamantium.MacOS`: `NSPasteboard`, `NSMenu`, `NSStatusItem`, `NSSavePanel`, `NSPopUpButton`, `NSTextInputContext`, `NSDragging`, `NSSpellChecker`, `GCController`,
  `NSData`, `NSURL`, `NSUserDefaults`, `NSLocale`, `NSFileManager`, making an `NSArray`, notifications of every
  application, the user's times of clicks and repeated keys, the live state of a key, the languages of the keyboard
  layouts, the window at a point, and Core Haptics for the motors of a controller.
- A gamepad the engine takes in buzzes for half a second, so whoever holds it knows it is connected; its coming and
  going is logged.
- `GamepadVibration`, a component of the gamepads' vibration - each motor's strength and sharpness, the triggers', how
  long - played by `GamepadVibrationService`, which stops it at a breakpoint and when it is let go of; `Gamepad.SetSharpness`, where the gamepad can tell (Core Haptics);
  `IUniversePlatform.Gamepads`.

### Fixed

- Text under a rounded clip: the clip's edge is a pixel wide, no longer fading the first row inside a straight side.
- A pan of fingers with Alt kept down after the camera was flown orbits about what has come under the pointer.

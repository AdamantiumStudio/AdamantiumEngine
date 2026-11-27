### Fixed

- `Adamantium.Engine.Generators`: Slang is looked for by the process's architecture, not the machine's: an x64 build on
  an Arm machine (Windows on Arm) found no `win-arm64` libraries and compiled no shaders without the Vulkan SDK.

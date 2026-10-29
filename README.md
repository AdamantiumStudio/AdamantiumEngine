# Adamantium Engine

A graphics engine for .NET on its own Vulkan bindings: a Vulkan renderer, effects compiled to SPIR-V at build time,
fonts with MSDF atlases, image codecs, 3D model import and an entity-component system. It is the foundation of
[Adamantium UI](https://github.com/AdamantiumStudio/AdamantiumUI), where a 3D scene and the interface around it are
drawn by one renderer in one process.

> **Alpha.** Windows only for now, and the API will change between releases.

## Packages

| Package | What it is |
|---|---|
| `Adamantium.Core` | Disposable and named objects, observable collections, commands, events, data buffers, dependency injection, type parsing |
| `Adamantium.Mathematics` | Vectors, matrices, quaternions, colors, bounding volumes, polygon triangulation and fill rules |
| `Adamantium.Fonts` | TrueType, OpenType and CFF: outlines, metrics, kerning, OpenType features; MSDF glyph atlases |
| `Adamantium.Imaging` | PNG and APNG, JPEG, GIF, BMP, TGA, TIFF, ICO, DDS; mipmaps, format conversion, palette quantizers |
| `Adamantium.Graphics.Core` | Devices, presenters, buffers, textures, vertex layouts, meshes, the effects framework |
| `Adamantium.Graphics` | The Vulkan renderer: device, swap chains, memory allocation, pipelines, effects, textures, text |
| `Adamantium.EffectsCompiler` | Compiles effects (`.fx`) to SPIR-V with Slang, with the reflection data the engine binds them by |
| `Adamantium.FX` | Built-in effects: basic, font, sprite, Forward+ lighting, full-screen quad |
| `Adamantium.Engine.Compiler` | Imports 3D models: Collada, Wavefront OBJ, 3DS |
| `Adamantium.ECS` | Entities and components: cameras, colliders, animation, meshes |
| `Adamantium.Multiverse` | Universes, the outputs they render to, windowing platforms, input |
| `Adamantium.Engine` | Forward+ rendering, scene managers, cameras, selection, editing tools |
| `Adamantium.ProceduralGeometry` | Rectangles, ellipses, arcs, polygons, lines, cubes, spheres, cylinders, cones, capsules, grids |
| `Adamantium.MVVM` | View models with validation, sync and async commands |
| `Adamantium.Win32` | Windows, messages, system metrics, cursors, Shell dialogs |
| `Adamantium.MacOS` | Application, windows and cursors through a native app wrapper (not yet verified, see below) |
| `Adamantium.XInput` | XInput gamepads |
| `Adamantium.GameInput` | Microsoft GameInput gamepads, with the native shim they need |
| `Adamantium.Engine.Generators` | Source generator: a typed class for every `.fx` in the project |
| `Adamantium.MVVM.Generators` | Source generator for `Adamantium.MVVM`: bindable properties and commands from attributes |
| `Adamantium.Content.Build` | The content project: a library of assets, cooked at build and copied to every project that references it |
| `Adamantium.Engine.Templates` | `dotnet new` templates: `adamantium-content`, a content project |

All packages share one version and are released together.

## Requirements

- **.NET 10.**
- **Windows 10 or 11, x64.** macOS and Linux are planned; the macOS code exists but has not been built and run
  recently, so it is not claimed.
- **A GPU and driver with Vulkan 1.4** and the `shaderInt64`, `shaderDrawParameters` and `samplerAnisotropy` features,
  buffer device addresses and descriptor indexing - for descriptor sets, update after bind of sampled images. Neither of
  the newer extensions is needed from the driver: without `VK_EXT_descriptor_heap` the engine binds through descriptor
  sets, and `VK_EXT_shader_object` comes with the
  [`Adamantium.Vulkan.ShaderObjectLayer`](https://www.nuget.org/packages/Adamantium.Vulkan.ShaderObjectLayer) package -
  Khronos' shader object layer - where the driver lacks it. By the vendors' driver notes:
  - **NVIDIA:** Turing or newer - GeForce GTX 16 and RTX 20 series and later, Quadro RTX and T series, RTX A, Ada and
    Blackwell.
  - **AMD:** RDNA or newer - Radeon RX 5000 series and later, from Adrenalin 25.1.1. The RX 5000 and RX 6000 series
    now get their own driver from AMD.
  - **Intel:** Arc and Core Ultra graphics whose driver reports Vulkan 1.4 - `vulkaninfo` says which.

  The engine is developed and tested on an NVIDIA Quadro RTX 4000 with NVIDIA's
  [Vulkan developer driver](https://developer.nvidia.com/vulkan-driver), with the descriptor heap and with descriptor
  sets; other GPUs have not been tested.
- **No Vulkan SDK.** The Slang shader compiler ships in the
  [`Adamantium.Vulkan.Slang`](https://www.nuget.org/packages/Adamantium.Vulkan.Slang) package.

## Getting started

```
dotnet add package Adamantium.Engine --prerelease
```

`Adamantium.Engine` brings in most of the others as dependencies. Add the input and platform packages you need
beside it.

### Your own effects

Every `.fx` file in a project becomes a class at build time: `Effects/Glow.fx` in a project with the root namespace
`MyApp` gives `MyApp.Effects.Glow`, derived from `Effect`. The generator needs to see the files and to know where the
Slang compiler is:

```xml
<ItemGroup>
  <PackageReference Include="Adamantium.Engine.Generators" Version="0.1.0-alpha" PrivateAssets="all" />
  <PackageReference Include="Adamantium.Vulkan.Slang" Version="1.0.11" GeneratePathProperty="true" PrivateAssets="all" />
  <AdditionalFiles Include="**\*.fx;**\*.fxh" />
  <CompilerVisibleProperty Include="RootNamespace;ProjectDir;AdamantiumSlangPath" />
</ItemGroup>

<PropertyGroup>
  <AdamantiumSlangPath>$(PKGAdamantium_Vulkan_Slang)</AdamantiumSlangPath>
</PropertyGroup>
```

A shader that does not compile fails the build with the compiler's message.

### Your own content

Assets live in a content project of their own, as in XNA: a library whose files are its assets. Every project that
references it, directly or through another, gets them in its output.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Adamantium.Content.Build" Version="0.1.0-alpha" />
  </ItemGroup>
</Project>
```

- **What is content.** Every file of the project but its code, its build files and hidden files. Leave one out with
  `<AdamantiumContent Remove="..." />`; bring one in from elsewhere with
  `<AdamantiumContent Include="..\Shared\logo.png" Link="Textures\logo.png" />`.
- **The build.** Models (Collada, OBJ, 3DS) are cooked into the engine's own format, with their textures beside them;
  the rest is copied as it is. Only what changed is built again.
- **Where it goes.** To `Content/{Name}/` of the application's folder, where `{Name}` is the last segment of the
  project's name (`MyGame.ShipContent` gives `ShipContent`), or `AdamantiumContentName`. Two content projects an
  application references need different names.
- **Finding it.** The class `{Name}Assets` has a constant for each file: its path from the application's folder.
  `ContentManager` loads by it, taking the cooked model when there is one:

```csharp
var scene = await universe.Content.LoadAsync<SceneData>(ShipContentAssets.Models.Ship.Ship_dae);
```

`dotnet new adamantium-content` (from `Adamantium.Engine.Templates`) creates such a project; Rider and Visual Studio
list it in New Project.

## Building from source

```
git clone https://github.com/AdamantiumStudio/AdamantiumEngine.git
cd AdamantiumEngine
dotnet build AdamantiumEngine.sln -c Debug
```

Everything the build needs comes from nuget.org. The tests are in `AdamantiumEngine.Tests.sln`;
`Adamantium.Engine.GraphicsTests` needs a Vulkan device that meets the requirements above.

## Related repositories

- [AdamantiumUI](https://github.com/AdamantiumStudio/AdamantiumUI): the UI framework built on this engine.
- [AdamantiumVulkan](https://github.com/AdamantiumStudio/AdamantiumVulkan): the Vulkan, Slang and SPIR-V bindings.
- [QuantumBinding](https://github.com/AdamantiumStudio/QuantumBinding): the generator those bindings are made with.

## License

[Apache-2.0](https://github.com/AdamantiumStudio/AdamantiumEngine/blob/master/LICENSE). Parts of the engine are
derived from third-party work under MIT and ISC; their notices are in the file headers and in
[THIRD-PARTY-NOTICES.md](https://github.com/AdamantiumStudio/AdamantiumEngine/blob/master/THIRD-PARTY-NOTICES.md).

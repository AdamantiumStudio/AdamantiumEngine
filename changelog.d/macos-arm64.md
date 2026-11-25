### Added

- macOS on Apple Silicon draws: on Apple's platforms the engine loads KosmicKrisp itself - its manifest beside the
  application (`vulkan/icd.d`), in the Vulkan SDK, `/usr/local` or Homebrew - and hands it to the Vulkan loader as its
  only driver (`VK_LUNARG_direct_driver_loading`), so another driver installed beside it (MoltenVK) takes no part. The
  effects generator compiles shaders on the Mac.
- `Adamantium.MacOS` calls AppKit from C# through the Objective-C runtime: the application and its events, windows,
  views, screens, cursors, images and `CAMetalLayer`.

### Changed

- The shaders of a pass with more than one stage are created linked (`VK_SHADER_CREATE_LINK_STAGE_BIT_EXT`), in one
  call; the shader binary cache gives such a group all of its shaders or none. A failed `vkCreateShadersEXT` throws
  instead of handing out empty shaders.
- Vertex buffers are bound with their stride (`vkCmdBindVertexBuffers2`), not only through the vertex input: where
  shader objects come from Khronos' layer over KosmicKrisp, every instance drew with the first one's data.
- `IGraphicsDevice.SetVertexBuffers` takes `params ReadOnlySpan<IBuffer>` instead of an array and allocates nothing.
- Takes Adamantium.Vulkan 1.4.0, Slang 1.2.0, Spirv 2.1.0, Loader 1.2.0 and ShaderObjectLayer 1.0.3.

### Removed

- `MacOSAppWrapper.framework` (an Objective-C build for Intel only) and the empty `Adamantium.OSXNative` project.

### Fixed

- A viewer waited forever for a universe host serving on a long name on macOS, where a pipe is a socket whose path may
  have 104 bytes: the pipes' names are short whatever the name.
- A font with no OS/2 table - Apple's own, such as macOS's Courier - would not load: OS/2 is not required, and such a
  font is bold and italic by its head table's `macStyle`.
- A PDF font not embedded and drawn by a system font from a collection (`.ttc`) - Times on macOS - had every width 0:
  the widths are the face's own.
- A font collection whose faces each have glyphs of their own (macOS's Helvetica.ttc and other system collections) drew
  every face with the glyphs of the face read last - text came out as other letters: each such face has a typeface of
  its own, and faces sharing their glyphs still share one.

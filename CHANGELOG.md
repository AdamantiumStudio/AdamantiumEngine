# Changelog

Notable changes to the Adamantium Engine packages, in the form of [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
All packages share one version.

## Unreleased

### Added

- `Adamantium.Mathematics.Typed`: one generic type for each number type in place of float and double pairs -
  `Vector2<T>`, `Vector3<T>`, `Vector4<T>` (integers too), `Quaternion<T>`, `Matrix3x2<T>`, `Matrix3x3<T>`,
  `Matrix4x4<T>` - made with static factories as in System.Numerics (`Vector3.Create`, `Matrix4x4.CreateLookAtLH`,
  `Quaternion.CreateFromYawPitchRoll`...) and converted between number types only explicitly, with `As<T>()`. What
  needs fractions - length, normalizing, blending - is there only for fractional types. Equality is exact, a NaN
  equal to a NaN for dictionaries; `NearEqual` compares within a tolerance. Matrix products and transforms use SIMD
  where the processor has it - 128 bits for float, 256 or two halves of 128 for double - and give the same bits on
  every processor and without SIMD. The types the engine uses today stay until they are moved over; the new ones
  answer as they do, apart from mistakes of theirs set right: triangular and echelon forms, a skew, an isometric
  projection, a field of view in degrees, collinearity, spherical blends of perpendicular rotations, the angles of a
  rotation looking straight up or down, a mirror's decomposition, the inverse of a matrix whose elements lie far from
  one or far apart - a far translation in float, a small 2D scale.
- `Adamantium.Mathematics.Typed` colors and vertex values: `Color` (R8G8B8A8), `ColorBGRA` (B8G8R8A8), `Color3F`,
  `Color4F` and `Bool4` (four 32-bit truths). A fraction becomes a byte as UNORM does - clamped, rounded to the
  nearest - so a byte goes through a fraction and back unchanged; each packing is named by its byte order from the
  lowest up, as Vulkan names its formats, and each `From` is the inverse of its `To`: the number 0xAARRGGBB, which
  System.Drawing calls ARGB, is `FromBgra` here, and `FromArgb` takes alpha in the lowest byte.
- `Adamantium.Mathematics.Typed` geometry, generic in the same way: `Size<T>`, `Rectangle<T>` (X, Y, Width, Height),
  `Viewport<T>`, `Ray<T>`, `Ray2<T>`, `Plane<T>`, `BoundingBox<T>`, `BoundingSphere<T>` and the tests between them in
  `Collision`; `Ray.CreateFromViewport` for the ray through a point of the screen, `Matrix4x4.CreateReflection` and
  `CreateShadow` (and the 3x3 ones) for a plane. Double boxes and spheres far from the origin keep what float loses.
  A whole-number rectangle takes a point as a cell, as a pixel is: the cells on its right and bottom edges are
  outside. The helpers are set by what they work on: `MathHelper.Scalar` for single numbers, `MathHelper.Angle` for
  angles between directions (from the arctangent, exact near 0 and π), `MathHelper.Polyline` for length, even
  resampling, winding and convexity, `MathHelper.Curves` for Bézier spans of any degree, quadratic B-splines, NURBS and
  arcs, appended to a list the caller keeps and sampled at their parameter's exact steps, the NURBS to its end.
  `BoundingFrustum<T>` is a value now, its planes and corners kept inline; an endless far plane keeps everything ahead
  inside, its corners at infinity along the edges, and the tests by corners - support point, plane, frustum - take them
  as such. `OrientedBoundingBox<T>` answers in its own axes as an axis-aligned box does - touching is meeting, a NaN
  is outside, a box turned inside out holds nothing - and meets other boxes, segments and frustums by their separating
  axes and planes.
  Mistakes of the old types are set right: a ray's distances to spheres and
  points with a direction not of length one, segments and planes at a small angle taken as parallel, short directions
  and normals taken as none, crossing rays missed in float, a sheared sphere's radius, the nearest point of a sphere to
  its center, a NaN counted as a hit or as inside a frustum, a frustum's planes lost to overflow on large matrices, an
  oriented box's turn composed the wrong way round and its scale laid along its own axes, a sheared box no longer
  holding what it held, a box and frustum apart by the frustum's plane taken as crossing,
  and equality, exact now - a frustum's taking in whether it checks its far plane.

- Tessellated passes draw. `IGraphicsDevice.PatchControlPoints`: how many control points make a patch of a
  `PatchList`, set by the caller; `IGraphicsDevice.TessellationDomainOrigin`: the corner of the domain at (0, 0), upper
  left unless set. A tessellated draw of another topology, or of patches of no points or more than the device's
  `maxTessellationPatchSize`, or of another number of points than the pass's hull shader reads a patch of
  (`EffectData.Shader.InputPatchSize`), and patches drawn by a pass that does not tessellate, are refused before they
  reach the device.
- Mesh passes draw: `IGraphicsDevice.DrawMeshTasks` launches workgroups of the pass's task shader, or of its mesh shader
  when it has none - a mesh shader without a task shader is made as one. More workgroups than the device launches for
  that stage are refused, and so are mesh tasks of a pass without a mesh shader and vertices of a pass with one.
  `GraphicsAdapter.MeshShaderProperties`: the device's limits for mesh and task shaders.
- `SharedSurface.CanExport` and `CanImport`: whether the device exports, or imports, the timeline semaphores a surface
  shared through handles of a type signals its frames with (`GraphicsAdapter.GetTimelineSemaphoreFeatures`). A device
  that cannot is refused with that said, not left to fail in Vulkan; the memory is still checked as it is made.
- `EffectSource`: the effect file reader is public, for tools that read effect files as the compiler does - its
  techniques, passes and hit groups with each stage's entry point, place and the stage Slang compiles it for
  (`EffectStageSource.SlangStage`), what breaks the rules (`Errors`, in file order) and the code with the techniques
  blanked out. A technique that breaks the rules is blanked as far as it goes - its closing brace, a brace it did not
  open or the next technique - and the file read on, so the code after it keeps its place and later techniques are
  read; a lone CR ends a line, as for Slang. `EffectCompiler.SpirvProfile`: the SPIR-V version effects compile to.
- `IGraphicsDevice.UploadFrameData`: copies data into the frame's upload pages and gives its device address, for a
  shader to read through a pointer while the frame lasts; Forward+ uploads its lights, instances and palettes so. Data
  larger than a page gets a buffer of its own in system memory, not in the small window of video memory the CPU can
  write, a power of two in size: the next frame of the same slot takes it again for data of its size or half of it,
  and disposes of one it leaves unused.
- `BitmapLoader.MaxPixels`: the most pixels a picture whose data does not bound its size - a run-length BMP - is read
  at, 2^28 unless set. `GraphicsAdapter.SupportsImageCubeArray`: whether the device takes an array of cubes.
  `ITexture.ReadbackToImage` reads any mip level back, not only the first. `IDescriptorManager.FallbackTextureIndex`:
  where the stand-in for a disposed texture is in a shader's texture array of no length.
- `GraphicsSettings.TextEdges` (`TextEdgeSettings`): how text's edges blend - the gammas of light ink, of small and
  of large dark ink, the sizes dark ink goes from the small curve to the large one between, and how wide a crisp edge
  ramps. Read on every text draw, so the application may tune them while it runs; a value the shader cannot take - a
  gamma or a width not above zero, a size below zero - is refused.
- `TiffReader`: a TIFF raster too large to hold whole, read a tile or a window at a time - opening reads only the
  file's structure. `Levels` are the full-resolution image and its reduced-resolution copies (from the directory chain
  or SubIFDs), largest first; `ReadTile` reads all bands or one (only that band's plane when the file keeps bands
  apart), `ReadWindow` any rectangle, decoding only the rows of a tile inside the level and the tiles under it on the
  thread pool, as many at once as 64 MB of decoding buffers allow; `HasTile` tells a tile the file leaves out - it reads as `NoData` when the samples can
  hold that value, else as zeros - and `TiffLevel.TileArea` the part of an edge tile inside the level - the rest reads
  as zeros. Samples
  come as the file holds them, not as colors (`TiffLevel`: bands, bits, `TiffSampleFormat`, `TiffPhotometric`, the
  color map, the alpha band, the orientation). Opened on a path it reads at offsets from any number of threads at once;
  on a seekable stream reads take turns. A compression it cannot read refuses the file when it is opened.
- GeoTIFF (OGC GeoTIFF 1.1) through `TiffReader.GeoReference`: the raster-to-model transformation from a tie point
  and pixel scale or a matrix, half a pixel off for `PixelIsPoint`, control points as they are; the model type, the
  projected, geographic and vertical systems by EPSG code, and every key by number with its short, double or text
  values (`TiffGeoKey`); `Resampled`/`GeoReferenceOf` for a reduced-resolution copy. GDAL's no-data value
  (`NoData`, as the samples hold it - rounded to a float for float samples; infinities and NaN as C runtimes print
  them, MSVC's too) and metadata - per band scale, offset and description (`Bands`), and the rest (`Metadata`).
  Checked against GDAL 3.12 on Natural Earth, USGS 3DEP, Landsat 8 and GDAL's own test files.
- TIFF, read and written, written from the specifications (TIFF 6.0, its Technical Notes, BigTIFF, ITU-T T.4/T.6).
  `Image.Load` and `BitmapLoader` read any common TIFF: strips or tiles, classic or BigTIFF, either byte order;
  uncompressed, PackBits, LZW (also the TIFF 5.0 kind), Deflate, CCITT run lengths, Group 3 and Group 4 fax, JPEG;
  the horizontal and floating-point predictors; 1 to 64 bits, unsigned, signed or floats of 16, 24, 32 and 64 bits;
  gray, RGB, palette, CMYK, YCbCr (subsampled too), CIELab; alpha, associated or not; samples in planes; the
  Orientation tag; every page of the size and format of the first as a frame. Colors come out RGBA8 (up to 8 bits),
  RGBA16 (up to 16) or RGBA32 float; signed and wider integers keep their values. `.tif`/`.tiff` are known by name
  and BigTIFF by its header. `TiffEncoder` writes gray, RGB, RGBA, BGRA, 16-bit, float and integer data, a page per
  frame, in strips or tiles (`TiffSaveOptions`), uncompressed, PackBits, LZW or Deflate with a predictor - Deflate by
  default - and BigTIFF when the file would pass 4 GB; `Image.Save` writes TIFF. A broken or hostile file fails with
  `InvalidDataException` or `NotSupportedException`: lengths are bounded by the file, a picture no array holds is
  refused before it is decoded, and only the rows of a tile inside the image are decoded; a compressed strip or tile
  is not read past 16 times its decoded size and 64 KB, more than any codec needs; a field that only describes
  the image (resolution, color profile, geotags, GDAL's) whose values would run past the file's end is left out, as if
  the file did not have it. A broken link after the first
  page leaves the pages before it. LERC-compressed files are refused: the
  format has no specification.
- `JpegDecoder.ComponentsAreRgb`: three components taken as RGB as they are, as TIFF stores JPEG of RGB data.
- `JpegImage(width, height, format, pixels)`: the picture made from its pixels.
- `ColorQuantizer.Quantize(rgba, width, height, maxColors, dither)`: RGBA pixels in a palette of up to 256 colors
  (`QuantizedImage`: the palette, an index a pixel, the transparent index). Few enough colors are kept exactly; more
  are cut into boxes one cut at a time - the cut, across a channel at a value, that lowers the squared error most -
  the boxes' means refined by k-means over the colors, and pixels mapped to the nearest with Floyd–Steinberg error
  diffusion if asked. On photographs of 800 to 3840 pixels it comes within 46.5 to 47.9 dB of them undithered and
  41.7 to 44.8 dithered, where Pillow's median cut comes within 42.8 to 44.6 and ImageMagick 7 within 43.3 to 44.0.
  Its time goes with the colors a picture has: written as GIF, a 3840×1920 photograph takes 0.6 s undithered and
  1.0 dithered, a 3840×2160 one of a million colors 1.4 and 2.6 - Pillow 1.7 and 2.7, ImageMagick 3.1 and 3.9.
- `ImageShrink.FitWithin`: pixels shrunk to fit a size, the aspect kept - each the average of the area it covers,
  colors weighted by alpha so transparent pixels lend them none, sRGB ones averaged as light - for 8- and 16-bit
  gray, RGB and RGBA in either order; read a row at a time in bands on every core, nothing the source's size made.
- `GifImage.Read`, `GifImage.Frames` (`GifFrame`: place, delay, `GifDisposal`) and `GifImage.Comments`;
  `GifEncoder.Encode(image, output, dither)` is static.
- `PngImage` (a PNG or APNG read: `Read`, `Frames`, `BitDepth`, `Metadata`, `ReleaseDecodedFrames`) and `PngEncoder`
  are public, with `PngMetadata`, `PngText`, `PngChromaticities`, `PngRenderingIntent`, `PngFrame`, `PngDispose` and
  `PngBlend`.
- `RawBitmapExtension.GetRgba8Pixels`: a frame as RGBA of 8 bits a channel, for what takes nothing else - BGRA
  swapped, 16 bits by their high bytes, RGB and gray given alpha; the ICO reader and color glyphs read PNG through
  it, so 16-bit PNG works there too.
- `JpegEncoder.Encode(image, output, quality)`: a picture written as JPEG at a quality of 1 to 100, from gray, RGB,
  BGR, RGBA and BGRA pixels of 8 or 16 bits; `BitmapLoader` writes at 100.
- `JpegDecoder.Size`: the picture's size from its frame header, decoding nothing - what the preview is an eighth of.
- `GraphicsAdapter.SupportsDescriptorHeap`: whether the device has the descriptor heap, extension and feature - what an
  application asks before it offers the heap as a choice, and what the device goes by.
- The engine runs on GPUs without `VK_EXT_descriptor_heap`: there shaders reach their resources through descriptor sets.
  A pass pushes its descriptors on every Apply (push descriptors, Vulkan 1.4) and a texture array of no length reads
  one update-after-bind set every pass shares, at the same index the heap gives the texture. That set starts with
  `GraphicsSettings.TextureArrayCapacity` textures (1024) and doubles when full, up to what the device's update-after-bind
  limits allow (`DescriptorSetManager.MaxTextureArrayCapacity`). The same SPIR-V serves both; `DescriptorSetManager`,
  `HeapEffectPass` / `SetsEffectPass` and `HeapEffectResourceLinker` / `SetsEffectResourceLinker` are the two paths.
  A pass that needs more push descriptors or a bigger uniform buffer than the GPU gives, or binds two resources at one
  binding, is refused with its name rather than drawn wrong. `GraphicsAdapter.MaxPushDescriptors`.
- `GraphicsSettings`: what the application asks of Vulkan - instance and device extensions, required and optional,
  layers (`VK_LAYER_KHRONOS_shader_object` where present, validation in debug), folders the loader looks for layers in
  (by default `vulkan/layers/<rid>` beside the application, where `Adamantium.Vulkan.ShaderObjectLayer` puts the layer),
  the descriptor model (`DescriptorModel.Auto` / `Heap` / `Sets`) and the frames in flight. `GraphicsSettings.
  CreateDefault()` gives what the engine used before; `MainGraphicsDevice.Create`, `VulkanInstance.Create` and
  `GraphicsDeviceService` take it. `MainGraphicsDevice.DescriptorModel` tells which path the device took.
- `IDescriptorManager`: the heap or the descriptor sets behind one interface - `TextureIndex`, `BindTo`.
- `Texture.GetLevelTexture(level)`: one mip level as a texture of its own, for a shader that reads a level while
  another level of the same image is drawn into.
- `PosixInterop.SetEnvironment` / `GetEnvironment` (`setenv` / `getenv`): variables native code in this process reads.
- `EffectPass.DynamicBufferPool.UploadRange`: where a constant buffer's copy is, as a page and an offset.
- `ShellDialog.PickFolders` (`FOS_PICKFOLDERS`): the shell's open dialog chooses a folder.
- `Win32Interop.QueryDisplayConfig`, `GetDisplayConfigBufferSizes`, `DisplayConfigGetDeviceInfo` and their structs: the
  display paths, with each monitor's exact refresh rate.
  `Win32Interop.TimeBeginPeriod` / `TimeEndPeriod`: waits finer than the default 15.6 ms.
- `SharedSurfaceDescriptor.Submitted` / `NoteSubmitted` and `SharedSurface.AwaitsFrame`: a consumer in the producer's
  process tells that a frame is on its way before the GPU has finished it, so it can look again rather than wait.
- `UniverseOutput.DisplayContent` is virtual: an output can tell, once its frame is submitted, that the frame is there.
- `Adamantium.DBus` serves objects of its own: `DBusConnection.Serve` answers the calls that come to the connection
  with what its handler gives - a reply (`DBusMessage.MethodReturn`), an error, or nothing for a method not served -
  one call at a time, in the order they came, off the thread calls are read on, so a handler may wait for its own
  calls; one that throws is answered with an error naming the method, not what went wrong, and
  `org.freedesktop.DBus.Peer.Ping` is always answered. `DBusMessage.Signal` makes a signal to send.
- Wayland's `xdg_toplevel_icon_manager_v1` (staging `xdg-toplevel-icon-v1`): `XdgToplevelIconManagerV1.CreateIcon`
  makes an icon - a name in the icon theme (`XdgToplevelIconV1.SetName`), pictures (`AddBuffer`) - and `SetIcon` gives it
  to a toplevel.
- ICO files are written: `IcoHelper.SaveToStream` writes every level of an RGBA picture, up to 256 pixels a side, as a
  PNG image of the file.
- Wayland's `xdg_wm_dialog_v1` (staging `xdg-dialog-v1`): `XdgWmDialogV1.GetXdgDialog` makes a toplevel a dialog of
  its parent, and `XdgDialogV1.SetModal` / `UnsetModal` tell the compositor it is modal.
- `TextLayout.BalanceLines`: text wrapped by words takes the narrowest width that keeps its lines - `MaxLines` of
  them, or as many as wrapping at the full width gives - so they come out about equally long, as CSS's
  `text-wrap: balance`; the lines are still aligned in the full width. A width counts only when every word fits its
  line whole and the lines fit the area's height, so balancing never trims or hyphenates what it would not otherwise.
  Horizontal text of up to 10 lines in one area without a drop cap. With `MaxLines = 2` a label of two or more words
  goes on two lines, broken where the wider line is narrowest.
- `Win32Interop.EnableWindow`: lets a window take input or refuses it all, as the windows behind a modal one do.
  `Win32Interop.EnumThreadWindows`: walks the top-level windows a thread owns.
- Word's text effects, drawn from each glyph's own distance field: `GlyphOutline`, `GlyphShadow`, `GlyphGlow` and
  `GlyphReflection` on a stretch (`TextAttributes.Outline`, `Shadow`, `Glow`, `Reflection`) or as the layout's defaults
  (`TextLayout.GlyphOutline` and the rest, which move `Revision` on). Each is a copy of the glyphs under all of the
  text - reflections, then shadows, glows and outlines - grown and softened no further than the field reaches past the
  outline, about a tenth of an em; the shared atlas is as it was. The glyph shader takes a soft edge
  (`FontItem.Synthesis.w`) and a reflection's fade (`FontItem.Paint.zw`).

- Gamepads on Linux: `EvdevGamepadBackend` reads the input devices (`/dev/input/event*`) the user may read, looking for
  new ones once a second, and `EvdevGamepad` takes the Linux gamepad layout - buttons by their place, the sticks with
  up positive, the triggers, the hat as the d-pad - into `GamepadState` at each report, catching up after dropped
  events. Its face is guessed from the device's name. No vibration yet. `PosixInterop.Open` and `Ioctl` for it.
- `DropCap.Length` and `DropCap.CanStart`: how many characters a drop cap takes from a text, and whether one can start
  it at all - not a text starting with a blank, a control character or an object; `DropCap.CharactersOf`, how many
  characters a drop cap of given letters counts, an opening quote not counted. `TextLayout.DropCapLength`: the
  characters the last layout set as its drop cap, 0 where it drew none - for an editor to show and keep room only for a
  drop cap that is drawn.
- `Adamantium.DBus`: a D-Bus client of our own. `DBusConnection.ConnectSession` connects to the session bus over its
  unix socket (EXTERNAL authentication), `CallAsync` sends a method call and gives its reply - an error reply thrown
  as a `DBusException` - and `Subscribe` raises a handler for the signals a match rule lets through. Messages are
  marshalled by hand: `DBusWriter` and `DBusReader` write and read every basic and container type, `ReadValue` any
  signature as lists, dictionaries and object arrays. `PosixInterop.GetUid` for the authentication.
- `Adamantium.Wayland`: Wayland client bindings. The protocols are generated into C# from their XML by
  `Adamantium.Wayland.Generator` - the core `wayland.xml`, and xdg-shell, viewporter, fractional-scale, cursor-shape,
  xdg-decoration, relative-pointer, pointer-constraints, text-input-v3, tablet-v2, xdg-toplevel-drag, xdg-activation,
  primary-selection, xdg-output, pointer-gestures, xdg-foreign and xdg-session-management from wayland-protocols - with no
  C in between: a class per interface (`WlSurface`, `XdgToplevel`), its requests as methods, its events as C# events,
  its enums nested in it, and the `wl_interface` tables libwayland marshals by. Of libwayland-client only the wire is
  called: `wl_proxy_marshal_array_flags` for every request, one dispatcher (`wl_proxy_add_dispatcher`) for every event.
  `WaylandDisplay` connects and dispatches, `WlRegistry.Bind<T>` binds a global by its type, and an event's handler
  that throws has its exception rethrown from the dispatching call. `XkbKeyboard` reads the keymap `wl_keyboard`
  sends through xkbcommon: keysyms, text and repeat of evdev key codes under the current modifiers and layout, and
  compose sequences - dead keys, the Compose key - by the user's locale (`Compose`, `XkbComposeStatus`).
- A Vulkan surface on Wayland: `PresentationParameters.DisplayHandle` carries the `wl_display` beside the `wl_surface`
  as `OutputHandle` (and `UniverseOutputDescription.DisplayHandle` passes it on), and `VulkanInstance` makes a
  `VK_KHR_wayland_surface` from them on Linux. The extension is optional there, so a machine without Wayland still
  renders off-screen; a window asked for without it is refused when its surface is made.
- `UniverseOutput.DrawTimeMs`: what a frame of one output costs on the CPU, after the wait for its frame slot,
  smoothed over its recent frames, which `RenderingService` counts - for statistics of each view where a universe
  draws several.
- `UniverseOutput.OwnCamera`: the camera the universe made for an output when it came - the output's own, not a
  camera of the scene.
- Recorded draws: `IGraphicsDevice.BeginRecording` sends the draws that follow into a secondary command buffer for the
  frame slot - recorded over in place on later frames - and `ExecuteRecorded` runs it inside the render pass.
  `BeginFingerprint`/`EndFingerprint` take the draws as a fingerprint only, issuing nothing - shaders, constants by
  content, heap indices, buffers, dynamic state, scissors and draw arguments - and `EndRecording` gives the same
  fingerprint for what it recorded, so a caller can tell a recording still stands for its draws. Needs
  `VK_KHR_maintenance7` (`SupportsRecordedCommands`), enabled when the device has it; a render pass then takes inline
  draws and secondary command buffers both. A shader hot reload or a change of target formats retires a recording
  (`IRecordedCommands.IsReusable`). With it: `GraphicsAdapter.SupportsMaintenance7`, `ShaderHotReload.ReloadCount` and
  `DescriptorHeapManager.HeapBindInfos`.
- A universe hosted by another platform runs on a thread of its own (`IUniverse.StartHostedLoop`), paced by its host:
  it draws a frame each time the host asks for one through the shared surface (`SharedSurfaceDescriptor.Demand`,
  `SharedSurfaceDemand`), so it draws no more often than it is shown and the host's frames never wait for it.
  `SuspendHostedLoop`/`ResumeHostedLoop` hold it between two frames while its devices are made again; its `Fps` counts
  the frames it drew. A frame that throws is logged and the loop goes on. `UniverseOutput.WaitUntilShown` gives whether
  the output has room; a frame drawn without it is dropped, and the output asks for another the next time its host
  asks for one (`SharedSurfaceDemand.WhenBeyond`). `SharedSurface.CreateExportable` takes how many surfaces the producer rotates through
  (`BufferCount`, `SurfaceAt`); the consumer copies the newest and gives the older ones back with it.
- `IUniverse.FrameMode`: `OnDemand` draws only when something asks, and a still scene draws nothing - the loop sleeps
  until a message reaches the universe's channel. `IUniverse.Invalidate()` asks a frame of every output: the world
  changed. `UniverseOutput.Invalidate` and `IUniverse.Invalidate(UniverseOutput)` ask one of that output alone - its
  input, its size; the others are not drawn (`UniverseOutput.IsDrawnThisFrame`). `TransformService` asks of every
  output when a node of the world moved in the frame, and only of the outputs looking through a camera when just that
  camera moved. `IUniverse.Post` runs an action on the universe's thread before its next frame, then draws every
  output: a change from another thread with no race against a frame. `Continuous` draws every output at its host's
  pace. A paused universe sleeps between messages, runs what is posted and keeps the frames asked for its resuming.
- GPU time of a frame: `IGraphicsDevice.LastGpuFrameMs`, from timestamps at the start and the end of the frame's
  commands, and `Universe.GpuTimeMs`, averaged over a second. `ForwardPlusRenderingProcessor.TrianglesDrawn`,
  `TrianglesInScene` and `DrawCalls`: the triangles of the meshes the last frame drew, of all it would draw were they in
  view, and the calls it took. A mesh out of view is left out by its box in the `SceneIndex` - a skinned one too, which
  was always drawn - and by its collider where there is no index.
- The Forward+ processor draws meshes instanced: the frame's visible meshes are gathered by geometry, pass and render
  state, each copy's world, color, texture and joint palette go into per-frame buffers the passes read by address
  (`MeshInstance`, `MeshDrawKey`, `MeshDrawGroup`), and every group is one call. The parts of a model bent by one
  skeleton share its palette. Sixteen copies of an animated model of 64 parts: 1020 calls a frame, now 135. The lit
  passes take `InstancesAddress`, `PalettesAddress` and `InstanceBase` in place of `world`, `albedo` and the `Skinning`
  buffer, and sample a copy's own texture from `meshTextures`.
- `MeshGeometryCache` finds a mesh's geometry by its content: meshes with the same vertices and indices - a model loaded
  twice, or duplicated - share one `RenderGeometry`, its buffers made once, so their copies draw as one call. A mesh is
  hashed again only when its version moves; a geometry no mesh uses any more is let go after the frames in flight.
- Textures by index: a texture array of no length in an effect, `Texture2D textures[];`, is the resource heap seen as
  an array of textures, indexed by `IDescriptorHeapManager.TextureIndex` - copies with different textures draw in one
  call. Image descriptors take whole slots of the heap for it, and the device enables `runtimeDescriptorArray` and
  non-uniform indexing of sampled images.
- `DynamicAabbTree<T>`: a bounding volume hierarchy of moving boxes - leaves grown by a margin, inserted where they add
  the least surface, kept balanced by rotations - with a ray query. `SceneIndex`, kept by `TransformService` frame by
  frame, holds every mesh entity's box in the world; `Picking.PickMeshes` asks it for the meshes a ray passes near
  instead of walking every entity, and tests them nearest first, stopping once the next box is past the nearest hit.
  A skinned part's box comes from its joints alone (`Skin.WorldBounds`): each point within the reach it had of its
  joints in the bind pose.
- The effect parser passes `#if`, `#ifdef`, `#ifndef`, `#elif`, `#else`, `#endif`, `#undef`, `#pragma`, `#error` and
  `#warning` to Slang as it does `#include` and `#define`, each to the end of its line; a `#define` no longer leaves its
  value to the grammar.
- `Colors.TryParse`: a color as markup writes it - a name in any case, `#RGB`, `#ARGB`, `#RRGGBB` or `#AARRGGBB`,
  spaces around it allowed - one reader for the framework's type parser, a color picker's hex box and the markup
  language server.
- `Css`: CSS values as SVG glyphs and HTML styles write them - `Css.Declarations(style)`, a style's declarations
  (`CssDeclaration`: name, value, `!important`) cut at semicolons outside quotes and brackets, and
  `Css.TryParseColor`: `#rgb`, `#rgba`, `#rrggbb`, `#rrggbbaa`, `rgb()`/`rgba()`, `transparent` and the named colors.
  SVG glyphs read their styles and colors through it.
- `FontVariation.ParseList("wght=650, wdth=87.5, opsz=auto")`: axis values as markup and documents write them; a tag
  is four characters, a shorter one written with the spaces OpenType pads it with (`TS  =5`) - a three-letter typo
  such as `wgt=650` is refused - and blank entries are passed over.
- `Adamantium.MVVM.PropertyAccessors`: reads and writes the properties of objects by name - what bindings and data paths
  do - through code the MVVM generator writes, so nothing is generated at run time and no reflection invokes it (iOS,
  NativeAOT). `PropertyAccessors.GetValue(item, "Name")` and `TryGetValue` read a value at once; `Find` gives a
  `PropertyAccessor` to keep - `GetValue`, `SetValue`, `CanRead`, `CanWrite`, `ValueType`. The generator writes a class
  for each type it describes, with a method to read and one to write each property (`PersonProperties.GetName`), so a
  debugger steps into them by name. It describes the non-generic classes with `INotifyPropertyChanged` or
  `[ViewModel]` - their hand-written public properties and the `[Bindable]` properties and `[Command]` commands it
  generates - and every non-generic class under a base marked `[DescribeProperties]`. What no description holds -
  another type, a generic one, a property another generator adds, a struct's hand-written member, an interface's
  member, a public field, and a property whose use would warn or fail in the generated code (`[Obsolete]` that is an
  error or has its own diagnostic id, `[Experimental]`, `[RequiresUnreferencedCode]`, `[RequiresDynamicCode]`) - is
  reached through reflection, and `PropertyAccessors.ReachedThroughReflection` tells it once per type. `Find` looks at
  the class, its bases, its interfaces and then its public fields; `FindDeclared` only at the class and its bases, so
  an interface's default implementation does not count. A getter or setter that is not public is not used; an `init`
  setter is, as reflection writes it - the generated code reaches it through `[UnsafeAccessor]`.
- `BidiParagraph.DirectionOf(text)`: which way a paragraph runs when its text decides, as `TextDirection.Auto` lays it
  out - by its first strong character outside isolates - for an editor that sets each paragraph's margins by the side
  its lines start from before laying it out.
- A drop cap in a paragraph running right to left: it stands at the right, past the margin there, and the lines beside
  it end as far before its ink as they start past it at the left; a caret and a click at it go as at any right-to-left
  letter. Down vertical lines a right-to-left paragraph still takes none. A drop cap of characters running both ways -
  "(1" opening a Hebrew paragraph - is shaped run by run and set as its line shows them, a bracket mirrored.
- The keyboard layouts a user switches between: `Win32Interop.GetKeyboardLayoutList`, and on Wayland
  `XkbKeyboard.LayoutCount` and `XkbKeyboard.Character(code, layout)` - what a key types in each layout of the keymap.
- `TextComposition`: how the lines of a text are composed - hyphens and their limit, line breaking, optical margins,
  word and letter spacing, glyph scaling, justification alternates, kashidas, CJK spacing trim and hanging punctuation,
  where a justified last line and a line of one word stand - as one value compared by value, which refuses a negative
  hyphen limit and glyphs scaled to nothing (`TextComposition.ScalesGlyphs` tells the latter for a control's own
  check). `TextLayout.Composition` takes it at once; the layout's own properties of these settings read and write it.
  `DropCap.LeastLines` is the fewest lines a drop cap spans.
- `FeatureSamples.Of(font, features, count)`: what shows each of a font's substitution features - its own sample text,
  or the characters it turns into other glyphs: the ones a character variant names first, then Latin letters and digits,
  letters of one script, one form of a letter, signs only where no letter changes - for a gallery of stylistic sets or a
  list of the font's features.
- `Skeleton`: the joints and inverse bind matrices a skinned mesh bends with, one for all the parts of a mesh that
  share them - `Skin.Skeleton`. An imported model's parts share one, and so do a copy's parts.
- `ReadOnlyTrackingCollection.AsSpan`, and `Transform.CalculateFinalTransform(camera, parentWorld, cameraWorld)` for a
  walk that has worked out the camera's world position once.
- A tool can do its own thing with a click and a frame instead of selecting: `ToolProcessor.Clicked` and
  `SelectTool.Framed` are virtual, `ToolProcessor.PressedOn` lets a press on an entity start a gesture,
  `SelectTool.ClicksOnRelease` clicks on the release and draws a frame when the pointer moves away first, and
  `ToolProcessor.ShowsLightHandles` leaves a selected light's handles out of a tool that drags nothing - an editor's
  delete tool is built on `SelectTool` so.
- glTF import opens meshopt-compressed assets, `EXT_meshopt_compression` and `KHR_meshopt_compression`, with a decoder
  of our own (`GltfMeshopt`): vertex data of versions 0 and 1, triangles and index sequences, and the octahedral,
  quaternion, exponential and color filters. Compressed buffer views are unpacked as the file is read; a buffer no
  view reads - the placeholder or fallback behind compressed views - is not loaded.
- glTF import opens Draco-compressed meshes, `KHR_draco_mesh_compression`, with a decoder of our own
  (`DracoDecoder`, Draco bitstream 2.2): sequential and edgebreaker connectivity (standard and valence traversal,
  topology splits, attribute seams), rANS-coded symbols, quantized, octahedral-normal, integer and plain attributes,
  and the parallelogram, constrained multi-parallelogram, texture-coordinate and geometric-normal predictions. A
  compressed primitive's accessors take the decoded data in place of their buffer views
  (`GltfFile.UnpackedAccessor`). A Draco mesh larger than its primitive's accessors say is refused before anything is
  allocated for it.
- glTF import keeps everything the file says, used or not yet:
  - every object's `extras` and extensions as JSON (`ExtraData`, the `Extra` of the scene, its asset, scenes, nodes,
    meshes and their parts, materials, images, textures, samplers, skins, cameras, lights, animations and channels);
  - `SceneData.Asset` (version, generator, copyright), `ExtensionsUsed` and `ExtensionsRequired`, every scene
    (`Scenes`, `DefaultScene`), and the nodes outside the scene shown under `OtherNodes`, with the meshes no node
    shows; cameras and skins no node uses by keys of their own (`camera0`, `skin0`); clips are made only of what the
    shown scene moves;
  - each animation as the file has it (`AnimationSources`: targets, pointers of `KHR_animation_pointer`,
    interpolation, times and values, cubic tangents too) beside the clips made of it;
  - `skin.skeleton` (`Controller.SkeletonRoot`), an orthographic camera's `XMag`/`YMag`, a light's `Intensity`,
    `Range` and `InnerConeAngle`;
  - the file's tangents, in the engine's axes, used instead of computed ones; any vertex attribute a `Mesh` has no
    array for, by its name (`Mesh.Attributes`, `VertexAttribute`): `TEXCOORD_4`, `COLOR_1`, `JOINTS_1`, `_ID`;
  - morph targets (`Mesh.MorphTargets`, `MorphTarget`) with their names, the mesh's weights (`Mesh.MorphWeights`) and
    the node's (`Model.MorphWeights`);
  - samplers and textures (`SceneData.Samplers`, `Textures`, with images in KTX2, WebP or AVIF as `Texture.Sources`);
    each material slot's texture with its texture coordinates and transform (`TextureReference`); the emissive factor
    and strength apart; every `KHR_materials_*` - ior, dispersion, unlit, and as layers of the material
    (`ClearcoatLayer`, `SheenLayer`, `TransmissionLayer`, `DiffuseTransmissionLayer`, `VolumeLayer`, `SpecularLayer`,
    `IridescenceLayer`, `AnisotropyLayer`, `SpecularGlossiness`) clearcoat, sheen, transmission, diffuse transmission,
    volume, specular, iridescence, anisotropy, specular-glossiness; the metallic-roughness part's own
    data (`Material.PbrExtra`); material variants (`SceneData.MaterialVariants`, `Mesh.VariantMaterials` by the
    variant's index);
  - `KHR_mesh_quantization`, now opened; copies of a mesh (`EXT_mesh_gpu_instancing`: `Model.Instances`,
    `InstanceAttributes`); metadata packets (`KHR_xmp_json_ld`: `SceneData.XmpPackets`).
  All of it is written to `.aemf` and read back.
- glTF 2.0 import, `.gltf` and `.glb` (`GltfConverter`, through `ModelConverter` and the content project's model
  importer): the scene's node tree, placed by TRS or matrix; each primitive of a mesh as a mesh of its own with its
  material - triangles, strips and fans as lists of triangles, lines and points as they are - with normals, four sets
  of texture coordinates and vertex colors; buffers in files beside the asset, in data URIs or in a `.glb`'s binary
  chunk; accessors of every component type, normalized, interleaved by their stride, matrices by their columns, and
  sparse. An extension the asset requires and the reader does not know stops the import with its name; one it only uses
  is told (`UnsupportedFeatures`), and so is anything else not read - morph targets, a second scene.
  `KHR_texture_transform` on the base color texture is laid on the coordinates it reads. The node tree is walked
  without recursion, so a chain however deep imports, and a node reached twice is told. A glTF model's front, +Z,
  comes in facing -Z, as the engine's models face (`UpAxis.Y_UP_RH_FRONT_Z`, changed by (-x, -y, -z) with the
  points' order reversed).
- glTF skins, animation, cameras and lights. A skin is its joint nodes with their inverse bind matrices
  (`SceneData.Controller.JointsAreNodes`), each vertex's first four joints and weights read; every animation is a clip
  of its own in `SceneData.Clips`, by name, each node's translation, rotation and scale channels brought to one list of
  keys - a key wherever any has one, thirty a second along a cubic spline (at most a minute's worth between two keys),
  and a stepped channel beside a blended one holding up to each step; a camera (perspective or orthographic) and a
  `KHR_lights_punctual` light where their nodes stand. A skin with more joints than are drawn
  (`SceneData.Controller.MaxDrawnJoints`, 128) is told.
- GPU skinning: `Skin` holds a skinned mesh's joint entities and inverse bind matrices and fills the palette its draw
  bends it with - each joint where it stands now, against the mesh - and the forward renderer draws a skinned mesh lit
  and textured with it (`ForwardPlusEffect`'s `SkinnedColored` and `SkinnedTextured` passes, up to
  `ForwardPlusRenderingProcessor.MaxJoints`, 128). A skinned mesh was drawn through a pass that did not exist. The
  palette is a constant buffer of its own (`Includes/Skinning.fxh`), uploaded by skinned passes only; a skinned mesh is
  not culled by its bind pose's box; the editor's selection outline and ghost follow the skin (`BasicEffect`'s
  `SkinnedLitInstanced`, `SkinnedFlatInstanced` and `SkinnedOutlineInstanced` passes). A weight below zero, which
  glTF forbids, is taken as none and a joint index below zero as the first joint, on the GPU and off it.
- A mesh drawn skinned (`Skin.DrawnOn`) is clicked, pointed at and framed where the skin puts it now, not where it was
  bound. `Skin.PosePoints` gives its points so, posed again only when a joint, the mesh or its node has moved;
  `Skin.PosedBounds` a box round them found without posing them, which a ray is tried against first; `Skin.Intersect`
  looks through a triangle tree of the skin's own that `TriangleTree.Refit` lays on the posed points.
- `Entity.Duplicate` gives the copy's skin its own copied joints and its clips their copied nodes
  (`ICloneableComponent.CloneReferences`); an entity outside what was copied is kept as it is.
- Animation of nodes: `AnimationComponent.Clips`, `CurrentClip` (bindable, choosing one starts it), `ClipNames`,
  `Duration`, and `Targets` - the entities a clip poses by their node's key; a clip with targets poses them each frame
  (`Pose`, `Sample`), a joint and a wheel alike, through `Transform.SetPose`, which stands an entity as a node is posed
  whatever its pivot. `EntityImportTemplate` gives a model with clips one, its targets found among its nodes; a format
  with a single clip - Collada - has it in `Clips`, under the model's name, and no longer in `SceneData.Animation`,
  which only a scene saved before keeps. `RestPoses` keep how each target stands with no clip on it: choosing another
  clip, or none (`null`, an unknown name - which stops the play), stands them there first
  (`Rest`). `TimeAt` plays a clip by every `CalculateOptions`, back-and-forth ones included.
- `SceneData.Material` and `Material` keep glTF's PBR: metallic and roughness factors and their map, normal map and its
  scale, occlusion map and its strength, emissive map (`KHR_materials_emissive_strength` taken), `AlphaMode`,
  `AlphaCutoff` and `DoubleSided`. The forward renderer still draws the base color, its texture and emission.
- `SceneData.Image.Data` and `MimeType`: an image a model carries inside - a `.glb`'s, a data URI's - as its bytes,
  written to the `.aemf` with the model; `EntityImportTemplate` embeds them in the content manager under the model's
  and the image's key and a hash of the bytes (`robot.glb#image0:1A2B3C…`), the material's image path, and loads the
  texture from there - the model saved again with another picture gets that picture. A picture
  that cannot be read leaves its part untextured instead of failing the whole model.
- `IContentManager.Embed`: makes bytes loadable as an asset of a name, as an image inside a model is; the content
  manager then loads it (`ContentReaderParameters.Data`), keeps it and loads it again for a new device like one read from
  a file. `TextureContentReader` reads such a texture from its bytes.
- `Entity.Source` and `SceneData.Source`: the file an entity was made from, as it was asked for. A model loaded through
  the content manager is named by its file (`SceneDataContentReader`) and keeps the path here; it was named by the
  whole path.
- `Component.IsExpanded` and `Entity.IsExpanded`: whether an inspector shows the object's section open, each object its
  own; open to begin with.
- A camera's projection changes smoothly: setting `CameraBase.ProjectionType` flows from the old projection into the
  new over `ProjectionTransitionTime` milliseconds (300; 0 switches at once), keeping the plane at `FocusDistance` - a
  third-person camera's `Radius` - the same size on screen all the way, as Unity's scene view does. Coming back from a
  zoomed flat view, the camera steps along its view to where the perspective shows that plane as large. A projection
  set before a camera's first frame is taken as it is.
- `CameraBase.IsIsometric`: the camera looks down a cube's diagonal, whatever its projection - orthographic, the
  isometric view of drawings; perspective, the same angle with depth. A free camera turns smoothly to the diagonal
  nearest to where it looks, about the point at its focus distance, and can turn on from there.
- `CameraBase.OrthoScale`: an orthographic view's zoom, scaling its projection along X and Y and never Z,
  so nothing is clipped away however close it comes, as a CAD view zooms. The wheel multiplies it by `ZoomStep` a
  notch; going flat from a perspective sets it to show the focus plane as large, and `JumpToObject` to show the object
  as large. `JumpToObject` also makes its distance the focus distance.
- A camera hung on another entity, or taken off one, travels from where it stood and looked to its new place and view
  over `CameraBase.HostChangeTime` milliseconds (250; 0 jumps), as `WorldPosition` and its view show it.
- `EntityManager.Cameras`: the world's cameras in a group of their own, wherever they hang - an entity with a camera
  joins as its tree joins the world and leaves with it; `EntityGroup.Count`. Every camera of the group moves on each
  frame, not only those an output shows: a third-person camera follows its subject, a hung one travels.
- `EntityIcons` draws a camera's view beside its icon: a pyramid for a perspective view, as wide as its field of view,
  or a box for a flat one, running where the camera looks, with a mark over its top edge
  (`CameraFrustumTemplate`).
- `PlacementTool` with no ghost picks what a click lands on (`Under`), drawing only its `Icon`, if it has one: a pick
  of what to place onto.
- Forward+ clusters follow a flat or flowing projection, and the first one reaches behind the camera, as a flat view
  sees what is there.
- A free camera with no owner turns with its entity: the rotation tool turns its view, and its view turns the entity.
- Leaving an isometric view, a camera turns back to where it looked before it, unless it was turned meanwhile.
- `CameraBase.IsLocked`: the user's mouse and keys leave the camera where it stands, even looked through
  (`Observatory.Controls`).
- The middle mouse button drags a free camera across its screen plane with the pointer, as far as the plane at its
  focus distance moves under it.
- In a flat view the ground grid reaches past all the view shows, fading only off screen.
- `Universe.Fps`: the frames a universe actually runs a second, beside what its rendering could sustain (`RenderFps`);
  0 while it is paused.
- `Transform.StayInWorldUnder(newOwner)`: rewrites an entity's position, rotation and scale for the owner it is about
  to hang on, so it stays where it stands in the world - as an entity dragged into a group in a hierarchy does.
- `Transform.Angles`: the rotation in degrees about X, Y and Z, as an inspector types it; the angles set come back as
  set until something else turns the entity. `QuaternionF.ToYawPitchRoll` takes a rotation apart into the angles
  `RotationYawPitchRoll` makes it from.
- `Selection` holds several entities: `Items`, the current one last; `Select`, `Add`, `Remove`, `Contains`.
  `Current` stays the one chosen last, and setting it selects it alone. Every selected entity is outlined, round their
  joint silhouette (`EditorOverlayProcessor.DrawOutline` takes several).
- A click with Shift adds what it lands on to the selection, with Control takes it out. `SelectTool` draws a frame
  across the screen from a press on nothing, selecting CAD style (`ScreenFrame`): drawn to the right, solid and blue,
  what lies wholly inside; to the left, dashed and green, whatever it touches - with Shift adding, Control taking out.
  It takes what a click would (`ToolsService.PickEntities`, `EditorProcessor.PickEntities`): the scene's parts with a
  form, by their points and triangles on screen, and the icons of lights, cameras and markers.
- The move, rotation and scale tools work several selected entities together, about their common center
  (`ToolProcessor.WorksGroups`).
- `ToolProcessor.PressedOnNothing` and `ContinueGesture`: a tool's own gesture begun from a press on nothing.
- The ground grid's axes are drawn as lines, seen from any side, and so are its lines while the plane is seen edge
  on - the eye in it, or a flat view along it.
- `CameraBase.OrthoNear` and `OrthoFar` (-10 000 and 10 000): an orthographic view's planes, behind the
  camera and ahead, set apart from a perspective's.
- A first-person camera looks where its host faces, turned by its own `Rotation`, and keeps its view when it is freed;
  it stood still.
- Color glyphs of 'SVG': the document's `<style>` sheets style its elements, by tag, `*`, `.class`, `#id` and their
  compounds, as CSS cascades them - an `!important` declaration of the `style` attribute, then of the sheet, then the
  `style` attribute, the sheet by specificity and order, and the presentation attribute last; selectors with
  combinators, pseudo-classes or attributes leave their rule out, and at-rules are skipped.
  `clipPathUnits="objectBoundingBox"` clips in the units of the box of what it clips, as it is drawn, the clip's own
  transform after the box, as browsers place it (a clip of nothing clips all of it); such a clip was left out.
  Strokes, masks, patterns and filters stay unsupported.
- `FontAtlasStore.ColorImageLayers` and `ColorImages`: the layers the images of color glyphs take, against
  `ColorImageLayerBudget`, and how many images they hold.
  For a second after a sweep of the color atlas that let no image go, new images take room past the budget at once:
  each waited for another sweep, which built every text again for nothing.
- Japanese set as JLREQ and CSS Text 4 set it. `TextLayout.SpacingTrim` (`TextSpacingTrim`, as `text-spacing-trim`):
  `Normal` (the default) sets an opening bracket after a closing one, or two closing ones side by side, half-width and
  gives up a closing punctuation's blank half where the line would not fit otherwise; `TrimStart`, `TrimBoth`,
  `TrimAll` trim the start and end of lines as well, `SpaceAll` none. `TextLayout.HangingPunctuation`
  (`HangingPunctuation`, as `hanging-punctuation`): `AllowEnd` (the default) hangs a comma or full stop past the end of
  the line where it would not fit, `ForceEnd` at the end of every line, `None` never; a justified line leaves the hung
  one out. A justified CJK line reaches the edge widened between its letters, where spaces are none.
- `TextLayout.GetLineFrame`: the frame of `Frames` a line stands in. `FrozenGlyphRun.AtlasVersion` and `IsStale`: the
  atlas version a run was built at, and whether glyphs landed since - for holders of runs of one layout, each building
  its own window of it.
- `FontAtlasStore.ColorImageLayerBudget`: the layers (some 5.6 MB each) the images of color glyphs ('sbix', 'CBDT')
  take before the ones no text draws are let go - 16 by default, about 500 of Noto Color Emoji's. A new image that
  finds no room has every text drawn build again and mark its images; a few frames and a quarter of a second later -
  however many windows draw - the unmarked ones give their
  cells, a layer left empty goes to images of any size, and an image let go lands again under its old program when
  text draws it. When the drawn ones take more, the atlas grows past the budget. The atlas kept every image it was
  ever asked for: a thousand of Noto's took some 250 MB.
- `ComposerItem.Penalty(width, cost, flagged, after)` and `ComposerItem.After`: what a break there adds to the line
  after it, as TeX's discretionary break - the rest of a word set again by itself. Such a break counts toward
  `ParagraphComposer`'s hyphen limit whatever its width comes to (a hyphen inside a ligature may narrow the line).
- Carets, clicks and selections down vertical lines: `TextLayout.HitTest` takes a point where vertical text is shown
  (a click on a drop cap included), `GetCaretRect` gives the caret there - a dash across a vertical line, a bar across
  a horizontal one - and `HitTestLine` the place along a line, as `GetCaretPoint` gives it, for the caret moving to
  the line before or after and keeping its column.
- `TextLayout.MoveWordVisually`: the caret a word to the right or left on the screen, as Ctrl+arrows move it - to the
  nearest place that way on its line where a word starts, past the last one to the first on the next line the plain
  arrows go to. In mixed text it follows the screen, where `NextWordStop` follows the reading order; both ways stop at
  the same places, a wrapped line once.
- Double-sided ruby: a base takes one annotation over it and one under it (`AttributedText.SetRuby` replaces only the
  rubies on its side, and a null annotation clears both); the two bases may differ, one inside the other. The line
  grows on both sides.
- Drop caps in vertical text: the cap stands upright at the head of the lines it spans, its em as wide as they are
  from the right of the first line's em to the left of the last's, and those lines start below it.
- `TextLayout.LineBounds` (`TextLineBounds`), as WinUI's: `Full` (the default) takes whole lines; `TrimToCapHeight`
  starts the first line at the top of its tallest capitals, `TrimToBaseline` ends the last on its baseline, `Tight`
  does both. The size and a vertical alignment take the trimmed box, so a label centered by its layout has its capitals
  in the middle. Horizontal text outside frames; a text on a baseline grid keeps its first line whole.
- `GroundGrid`, the editor's ground: the plane y = 0 ruled into cells, as editors show it. Cells of 1, 10, 100...
  times `CellSize` are shown by how big they are on screen, the smaller ones fading as the eye moves away, the X axis
  runs red along it and the Z axis blue, and the grid fades out towards its edge; what stands on it hides it. Drawn by
  the editor overlay with its own `GridEffect`, from the eye's place modulo each level's cells, so the lines hold still
  however far the eye has gone. Colors, line width, the cell size and the plane's y (`PlaneY`) are settings.
- `Typeface.OutlinesId`: the typeface whose glyphs it draws with the same outlines. `GlyphTextureData.KeyOf` keys atlas
  cells by it.

- `PlacementTool`, an editor tool that places a new object with the mouse: a see-through ghost of it
  (`EditorOverlayProcessor.DrawGhost`) follows the pointer, resting by its lowest point on the surface under it or, past
  every surface, on the ground grid as far as the grid is drawn. A click raises `Placed`; with Shift held the tool stays,
  else it is done (`Finished`), as it is on Esc; the right button turns the camera.
  `NeedsSomethingUnder` places only on a scene entity, as a camera riding on one is. `Icon` draws an icon with the ghost
  for what the scene shows by an icon.

- `Marker`, a component that shows an entity with nothing else to show - an empty one, a group - by an icon of
  `EntityIcons` (`EmptyIconTemplate`, or `GroupIconTemplate` for an entity with `EntityIconKeys.Group`). A click on it
  selects the entity; with the select tool a drag, once the pointer has moved a few points, moves the entity in the
  plane facing the eye, as the move tool's center does (`ToolProcessor.DragsClicked` names the handles that drag what a press lands on). `EntityIcons.PlaceIcon`
  puts an icon facing a camera at a size on its screen.

- `Picking.Pick(..., withDisabled)`: a disabled entity and what is under it are picked too, as an editor picks what is
  switched off; a hidden one never is. The editor's tools pick so.

- `EntityIconKeys.Group`, the icon key of a group.

- `ForwardPlusRenderingProcessor.Display` (`ViewDisplay`): a view draws its meshes shaded, as wireframes, or shaded
  with the wireframe over them, in `WireColor`. With `FollowsEditorDisplay` on, as in an editor, an entity's
  `EditorDisplay` sets how its subtree is drawn (`EntityDisplay`) - as the view, or in one of the view's own ways in its
  place, either way (`Shaded`, `Wireframe`, `ShadedWireframe`), or only as its collider (`Collider`)
  (`ForwardPlusRenderingProcessor.Drawn`); a part's own setting overrides its owner's. A game leaves it off.
  `ShowColliders` draws every collider, besides those each `Collider.DisplayCollider` shows.
  Over a shaded mesh the wireframe is drawn in the same pass (`ForwardPlusWireEffect`), from where each pixel lies in
  its triangle, smooth and `WireWidth` pixels wide, on a GPU with VK_KHR_fragment_shader_barycentric
  (`MainGraphicsDevice.FragmentBarycentricSupported`); without it, and for a wireframe alone, as lines of one pixel,
  drawn a step nearer than the surface under them so they do not break up in it. A skinned mesh has no wireframe and
  stays shaded.

- `IGraphicsDevice.DepthBiasConstantFactor` and `DepthBiasSlopeFactor`: how far `DepthBiasEnabled` moves what is drawn
  in depth.

- `ProceduralShape`, the parameters a shape's mesh is generated from, kept on its entity to change later:
  `RectangleShape`, `EllipseShape`, `PolygonShape`, `ArcShape`, `LineShape`, `PlaneShape` - flat, sized along two axes -
  and `CubeShape`, `SphereShape`, `CylinderShape`, `ConeShape`, `CapsuleShape`, `TorusShape`, `TubeShape`,
  `TeapotShape` - sized along three - each with its `Tessellation`, passed to the generator as it is, and its own
  parameters (the polygon's sides are its tessellation; the cone's top, the torus' and tube's thickness are fractions).
  A change to a parameter makes the mesh again once, on the next frame's transform pass (`RebuildWhenChanged`), in the
  same `Mesh` (`Mesh.ReplaceWith`) so what draws it uploads it anew rather than holding a new mesh besides, and
  refits the collider; nothing is made while the parameters stay. `ShapeTemplate` builds an entity from a shape.

- Generators sized along each axis: `Shapes.Sphere` (all three kinds, an ellipsoid with its own normals),
  `Shapes.Cone`, `Shapes.Cylinder`, `Shapes.Capsule`, `Shapes.Torus`, `Shapes.Tube` and `Shapes.Teapot` take a
  `Vector3` size and stretch the points - and the normals they write - as they make them, not by a pass after.

- `Observatory.ControlledCamera`: the one camera the user's mouse and camera keys move, as an editor's own camera is;
  any other camera an output shows is only looked through - `InputService` and the orientation cube leave it alone.
  Unset, as before, they move whichever camera the output shows. The wireframe and screenshot keys work whatever the
  camera.

- `TextLayout.HyphenLimit`, as InDesign's hyphen limit: the most lines in a row that may end in a hyphen breaking a
  word (0, the default, sets none); hyphens of the text itself are not counted. Past it a word goes down whole, unless
  it is alone on its line. `ParagraphComposer.Break` and `BreakAnyway` take the limit too.

- `TextShaper.SetsSpace(font, codepoint)`: whether shaping sets a space separator in a font, by its own glyph or by the
  font's space at its width.

- `AttributedText.ClearOverrides(start, length)`, as InDesign's Clear Overrides: the range keeps only its styles; an
  inline object keeps its size and alignment.

- `IFont.GetGlyphAlternates(glyph, feature, codepoint, language)`: the glyphs one feature gives a glyph in text of a
  script and language, from the language system a shaper takes for them.

- `GlyphWordData.Annotates`: whether a glyph is a ruby annotation's.

- Named paragraph and character styles, as InDesign's: `ParagraphStyle(Name, Paragraph, Text, BasedOn)` and
  `CharacterStyle(Name, Attributes, BasedOn)`, set with `AttributedText.SetParagraphStyle` / `SetCharacterStyle` and
  read back with `ParagraphStyleAt` / `CharacterStyleAt`. A range takes, each over the one before, the defaults, its
  paragraph's style, its character style and what was applied to it; a paragraph's own `ParagraphAttributes` set over
  its style field by field (`ParagraphAttributes.With` takes only the fields set). A style based on another sets over
  it; changing a style, or one it is based on, changes the text it is set on the next time it is laid out.

- Ruby, as HTML's `<ruby>`: `AttributedText.SetRuby(start, length, RubyAnnotation(Text, Position, FontSize))` sets an
  annotation - furigana, a reading - over a base, centered, at half the base's size unless told; a base narrower than
  its annotation is spread to its width with the room around each glyph, and stays on one line. The line grows to hold
  the annotation, as CSS's, unless its height is set (`LineHeight` or `BlockLineHeight`). `RubyPosition.Under` puts it
  below the line. In vertical text the annotation stands upright to the right of its base (to the left under), and
  the column widens on that side only.

- Optical kerning, as InDesign's: `TextLayout.Kerning` and `TextAttributes.Kerning` (`TextKerning.Metrics` - the font's
  pairs, the default; `Optical`; `None`). Optically, the space between two letters is measured from their outlines
  (`OpticalKerning.Between`): the white between them, taken into their hollows no deeper than 0.15 em, is made what it
  is between the font's "nn" and "HH", by no more than a quarter em, and never so close that their ink at one height
  comes within 0.02 em. For a font with no pairs, or pairs it left out. Horizontal left-to-right text of the scripts
  whose letters stand apart (`OpticalKerning.Applies`: Latin, Greek, Cyrillic, Armenian, Georgian); right to left,
  vertical text and other scripts keep the font's pairs, and two letters of different sizes are left as they are.
  `Glyph.GetMergedOutlineSegments(rate)` samples and reads the outline under the glyph's lock.

- A baseline grid, as InDesign's: `TextLayout.BaselineGrid` (`BaselineGrid(Step, Offset, FirstLineOnly)`). Every line
  is moved down to the first grid line at or below its baseline, never up, so lines of a larger size and the columns of
  `Frames` keep the grid's rhythm; `Offset` puts the grid of an element above under this text; `FirstLineOnly` puts only
  each paragraph's first line on it. A text aligned to the center or bottom moves by whole steps, never further than
  asked; a drop cap spans its lines as the grid sets them; a frame that cannot hold a line on the grid passes it to the
  next. The step is finite and more than 0, the offset finite. Horizontal text only.

- `SharedSurface.LatchCopy` / `LatchedCopy`: the consumer's copy of a shared surface's newest frame, made outside a
  render pass once per frame the producer finished, and sampled in place of the surface. The copy waits for the frame
  on the produce timeline and signals the consume timeline once done, so the producer writes its next frame only after
  it - a consumer that drew the surface itself again while nothing new came raced that write. A shared surface is
  now also a transfer source.
- `Entity.IconKey`: the resource key of the icon an editor shows for the entity, raising a change, so a hierarchy row
  binds its icon to it with no converter. The keys are the constants of `EntityIconKeys` (`Entity` by default, `Model`,
  `Mesh`, `Camera`, `PointLight`, `SpotLight`, `DirectionalLight`), set by whatever makes an entity of that kind: the
  camera template, the primitives, a model's import - its root and its meshes - and a `Light` itself, on whatever entity
  it is put and whenever its `Type` changes. `Duplicate` keeps the key.

- `GlyphEffect`: a range of the text drawn apart from the rest - its glyphs turned, scaled, slanted, moved or tipped in
  perspective (a 3×3 `Transform`), each about its own center or about the layout's origin, moved after that in sizes of
  each glyph (`Shift`), in a color of their own (a glyph's own colors, an emoji's, stay) and cut by a rounded rectangle
  (`GlyphClip`), where the layout put the glyphs or where the effect puts them. Only the drawing changes: the lines, the
  caret and hit testing stay where the layout put them; a glyph the layout adds - an ellipsis, a hyphen - goes with the
  character before it. The glyph run does not carry the effects: each `FontItem` says which character it draws, in which
  cell (`Place`, `Cell`), and both glyph shaders read the effects from a table of slots the caller keeps - written by
  `GlyphEffect.ToSlot` and `GlyphClip.ToSlot`, a chain per text, passed to `FontRenderer.DrawLayoutDirect` - so a
  changed effect is a slot write, not a new run. The atlas is read in perspective, and the clip
  is right under it.
- `AdamantiumCollection<T>` is an `ICollection`, so `ReadOnlyTrackingCollection<T>` hands out the collection's own
  `SyncRoot` - the lock every change is made and announced under. A view on another thread - the UI showing a world's
  entities - reads the items under it and so never sees them halfway through a change.

- `EntityManager.RootEntities` and `EntityWorld.RootEntities` are a `ReadOnlyTrackingCollection<Entity>`: a view - an
  editor's hierarchy - binds to them and follows entities as they are added and removed, as it follows an entity's
  `Dependencies`. The changes come on the world's thread.
- `Adamantium.Content.Build`: the content project, as in XNA - a library whose files are its assets.
  - **What is content.** Every file of the project but its code, its build files and hidden files
    (`AdamantiumContent`, trimmed with `Remove` as any item). A file from outside the project comes in under its
    `Link`; without one it is an error rather than a file written outside the output.
  - **The build.** Models are cooked into `.aemf` with their textures beside them, the rest is copied as it is, by
    the content compiler the package carries; a file that did not change is not built again, and what was built from a
    file taken out of the project is deleted.
  - **Where it goes.** Into `Content/{Name}/` of every project that references the content project, directly or
    through another. `{Name}` is the last segment of the project's name, or `AdamantiumContentName`.
  - **Finding it.** A generated class `{Name}Assets` has a constant for each file: its path from the application's
    folder, which `ContentManager` loads by.

- `InputWormhole.TakesKeyboard`: whether keys typed into an output go to the game now - its keyboard is on and the
  simulation is not paused - for a host that also uses keys for its own interface.
- `Adamantium.Engine.Templates`: `dotnet new adamantium-content` creates a content project; Rider and Visual Studio
  list it in New Project once the templates are installed.

- The content compiler takes the assets as a list: `build <projectDir> --files <list>`, the manifest then giving only
  their settings, and `--out` and `--cache` for where the output and the build cache go. A file no importer takes is
  copied to the output (`BuildResult.Copied`) instead of failing.

- The content compiler cleans up after itself, building from a manifest as well: what it built from an asset no longer
  listed is deleted, and a build by another version of the compiler cooks everything again. It deletes only what its
  cache says it wrote, inside the output folder. A model's texture outside the model's folder is left behind with a
  warning instead of being copied out of the output folder; an absolute reference to one inside it is made relative.
  Its warnings and errors are in MSBuild's form (`ACB001`-`ACB008`), so an IDE lists them.

- `IGraphicsDevice.TargetOrigin`: where the target being drawn into lies in the coordinates viewports and scissors are
  given in, for a target that holds one region of the surface. Callers keep setting viewports and scissors in the
  surface's coordinates; the device moves what it sends to the GPU, and a new command buffer starts at zero. The text
  shader adds it to the fragment, so a rounded clip still cuts text drawn into such a target in the right place.

- Gestures of fingers as input. A host hands an output a recognized gesture step with
  `UniverseOutput.OnGestureInput` (`GestureInput`: translation, scale and rotation).
  - **Reading it.** `InputWormhole` adds up a frame's steps: `GesturePan`, `GesturePinch` (in doublings) and
    `GestureRotation` (in degrees). They are taken while the output takes the pointer.
  - **Binding it.** `GestureAxis` (`PanX`, `PanY`, `Pinch`, `Rotate`) is a control like the mouse's axes and the
    gamepad's: `InputControl.Gesture`, written as `Gesture:Pinch`.
  - **The camera.** Two fingers moved side by side turn the camera as the mouse with its right button does (`Look`). A
    pinch zooms as the wheel does, four notches a doubling (`Zoom`). A turn of the fingers rolls the camera by the
    same angle (`Twist`, a new action). All three can be rebound in `CameraActions`.

- Windows pointer input in `Adamantium.Win32`, for touch and pen:
  - the `WM_POINTER*` messages in `WindowMessages` and `Messages.GetPointerId`;
  - `Win32Interop.GetPointerType`, `GetPointerInfo`, `GetPointerTouchInfo` and `GetPointerPenInfo`, with
    `POINTER_INFO`, `POINTER_TOUCH_INFO`, `POINTER_PEN_INFO` and their flags and masks;
  - `SetWindowFeedbackSetting` to switch off the system's touch and pen visuals for a window;
  - `CreateSyntheticPointerDevice`, `InjectSyntheticPointerInput` (`POINTER_TYPE_INFO`) and
    `DestroySyntheticPointerDevice`, which raise real `WM_POINTER` messages without the hardware.

- Paragraphs of their own in one layout, as a page has them. U+2029 (PARAGRAPH SEPARATOR) ends a line and a paragraph;
  `AttributedText.SetParagraph` gives a range its `ParagraphAttributes`: its alignment, its margins from the left and
  right edges, the room above and below it (between two only the larger is kept, none above the first nor at the top
  of a frame), its first line's indent (negative hangs it out into the margin; at the right in a right-to-left
  paragraph; a drop cap takes its place) and the height of its lines (larger text may still grow one). What it leaves
  unset comes from the layout, and a value that is no number counts as none. Line breaking by paragraph,
  justification, optical margins, wrapping by symbols, trimming, frames and the caret follow each paragraph's own.
  Frames find each line's place from where the line before ended.

- `TextLayout.Update(device, window)`: builds the quads of only the glyphs that touch a window in the layout's units,
  and asks the atlas for only theirs, so a long text shown through a viewport costs what the viewport shows;
  `QuadWindow` tells which window the last build was for (`RefreshGlyphs` keeps it), `GlyphCount` how many glyphs the
  text takes, drawn or not. `BuildGlyphRun(device, window)` builds and freezes them in one step, so two callers building
  different windows of one layout on different threads never freeze each other's.

- `IGraphicsDevice.WithholdFrame`: a frame asked to be held back before `EndDraw` acquires no swapchain image, so nothing
  is submitted or presented and the window keeps showing the last frame - for a frame drawn incomplete that the next
  one draws whole. A render-target presenter keeps its frame.

- Text decoration lines, as WPF's: `TextDecorations.Overline` at the font's ascender, and
  `TextAttributes.DecorationLines` - lines each with their own place (`TextDecorationLocation`: under, over, through
  the text or on the baseline), thickness, offset, color and dashes (`TextAdornment.Dashes`), drawn besides the
  decorations; a thicker line grows away from the letters, a thicker strikethrough stays centered.

- Baseline alignment, as WPF's: `TextAttributes.BaselineAlignment` sets a range where the layout's font sets a
  superscript or a subscript (`IFont.SuperscriptYOffset`/`SubscriptYOffset`, from 'OS/2'), at the font's ascender or
  descender, or at the top, middle or bottom of its line, its size and the line's height unchanged. A baseline shift
  adds to it; its lines and hyphens follow it.

- Line height, line stacking, line spacing and the most lines on `TextLayout`, as WPF's and Avalonia's:
  `LineHeight` sets the distance between baselines, its extra room (or what it lacks) split above and below the text;
  `LineStacking` says whether a line holding text of a larger size or a taller object still grows (`MaxHeight`, the
  default) or every line is exactly that high (`BlockLineHeight`); `LineSpacing` adds space between lines; `MaxLines`
  lays out that many lines, the rest reported by `OversetIndex`, or, with trimming, the last ends in an ellipsis.
  Frames, drop caps, wrapping by symbols, hyphenation and the caret follow them.

- Objects in a line of text, as WPF's `InlineUIContainer`: `TextAttributes.ObjectSize` on a U+FFFC OBJECT REPLACEMENT
  CHARACTER sets it as an object of that size. It advances the line by its width, stands on the baseline (a baseline
  shift raises it), raises a line it is taller than, wraps and aligns as a word does and draws nothing itself;
  `TextLayout.GetInlineObjects` tells where each one stands, for the caller to draw it there. Horizontal text only.

- U+2028 LINE SEPARATOR in laid-out text ends the line but not the paragraph, as WPF's `LineBreak`: the paragraph
  keeps its direction across it, a justified line ending in it is not stretched, it draws nothing, and the caret stands
  at the end of one line and the start of the next.

- Baseline shift, as InDesign's: `TextAttributes.BaselineShift` raises a range above its line's baseline (negative
  lowers it), its size and the line's height unchanged. Its underline, strikethrough and squiggle, the hyphens of its
  words, its tab leaders and a drop cap follow it; its background stays on the line. In vertical text an upright
  glyph moves across the line, to the right.

- `TextLayout.Revision` grows each time the text is laid out anew, so a renderer keeping one layout can tell its
  glyphs changed when its text and size did not.

- Kashida justification. A justified line of Arabic and other joined scripts whose spaces are at their maximum
  stretches its words with kashidas (`TextLayout.Kashidas`, on by default): in each word, a stretched extender
  between two joined letters, after a seen or a sad where the word has one, else nearest its end, up to an em. The
  paragraph composer counts that stretch. The extender is the glyph the font's justification table gives the script
  (`JSTF`, read for its extender glyphs: `IFont.GetJustificationExtenders`), else its tatweel (U+0640), or the
  lajanyalan for N'Ko; a kashida joins only two letters that follow one another in the text.
  `CursiveScripts.JoinsNext` and `Extender` tell whether a letter joins the next and what stretches its script.

- Where the last line of a justified paragraph and a justified line of a single word stand, as InDesign sets them:
  `TextLayout.LastLineAlignment` (at the start by default, centered, at the end, or justified) and
  `SingleWordJustification` (at the start by default, centered, at the end, or justified, its letters spread across
  the line). Start and end follow the paragraph's direction; the caret on an empty last line stands there too.

- Glyph scaling and justification alternates, as InDesign justifies. `TextLayout.GlyphScaling` (a `SpacingRange` of
  the glyphs' own width) draws glyphs at their desired width, scaled about their origins, marks with their letters.
  A justified line that cannot fill its width within its spacing first takes the wider glyphs its font offers for
  justification (`jalt`, as the wide letters of Hebrew; `JustificationAlternates`, on by default), then widens or
  narrows its glyphs within the range, and only then stretches its spaces further. `GlyphWordData.HorizontalScale`
  tells each glyph's width; hyphens and the trimming ellipsis are scaled with the text. Text broken a line at a time
  and justified now also takes a word, or a hyphenated part of one, it fits by squeezing its spaces, letters and
  glyphs within their minimums, as the paragraph composer did. `jalt=0` among a range's features keeps its glyphs.

- Word and letter spacing ranges and tracking, as InDesign's justification and character settings have them.
  `TextLayout.WordSpacing` and `LetterSpacing` (`SpacingRange`: minimum, desired and maximum shares of the font's
  space; `"80% 100% 133%"` in markup) set spaces and letter spacing at their desired widths. A justified line
  squeezes and stretches its spaces within the range first, then its letters, and only past both stretches its spaces
  further. The paragraph composer weighs both: letter spacing is glue it cannot break at. `TextLayout.Tracking` and
  `TextAttributes.Tracking` add thousandths of an em after each character. Marks stay on their letters, and Arabic
  and other joined scripts are never spaced or tracked (`CursiveScripts`).

- Vertical text. `TextLayout.WritingMode` (`WritingMode.VerticalRightToLeft`) runs lines down, as long as the text
  area is high, and stacks them from its right: ideographs and kana stand upright, shaped top to bottom in their
  vertical forms and centered on the line, as do brackets and long marks the font has vertical forms of (UAX #50 `Tr`);
  other text lies turned 90° clockwise, its em centered on the line; a run of
  one or two digits among them stands upright across the line in one em (tate-chū-yoko), narrowed to fit. Wrapping,
  hyphenation, the paragraph composer, tab stops, trimming, alignment along the line, frames and exclusions work as
  across; `GetRangeRects` and `GetAdornments` give rectangles down the lines (underlines on their right,
  strikethroughs down their middle), and `GlyphWordData.Upright` and
  `Sideways` say how each glyph stands. Drop caps, carets and hit testing are for horizontal text only. A glyph's
  `FontItem.Rotation` now turns a color glyph drawn by its paint program as well.
- Shaping top to bottom, as HarfBuzz does it: `ShapingOptions.Vertical` applies the 'vert' forms (found anywhere in
  the font when the script lacks them), leaves out the horizontal features (`kern`, `liga`, `calt`…), advances each
  glyph down by its 'vmtx' advance and offsets it from its vertical origin ('VORG', else its top plus its 'vmtx' top
  side bearing, for TrueType outlines; the font's ascender otherwise); without a 'vert' feature it falls back to
  Unicode's vertical presentation forms. GPOS values move the pen down in vertical text (`vpal`, `vkrn`), and
  Arabic-family scripts do not join in it, as in HarfBuzz. Checked against HarfBuzz on Noto Sans CJK (Japanese, kana,
  brackets and punctuation, `vpal`, Korean, Latin), on fonts without vertical metrics, on CFF Latin and on Arabic. `IFont.HasVerticalMetrics`, `GetAdvanceHeight` and `GetVerticalOriginY` give the metrics;
  `VerticalOrientations` gives the Vertical_Orientation of characters (UAX #50, Unicode 16.0): which stand upright in
  vertical text and which lie turned.
- Text frames. `TextLayout.Frames` flows text wrapped by words through rectangles in turn - columns side by side, or
  frames anywhere, as InDesign threads them - each holding as many lines as its height takes; what none has room for
  is not laid out, and `OversetIndex` tells where it starts. `TextLayout.Exclusions` are areas the text flows around:
  a line runs in the widest part of its frame they leave free, and a row they leave too little of is skipped. Lines
  break, hyphenate, justify and align within their own frame and width, a line at a time or a paragraph at a time;
  a drop cap stands at the start of the first frame; a click finds the line of the frame it falls in.
- Drop caps. `TextLayout.DropCap` (`DropCap`: how many lines, how many characters, an optional decorative font) sets
  the first characters of text wrapped by words large across its first lines, from the cap height of the first to
  the baseline of the last, the lines beside it indented by its width - a line at a time and a paragraph at a time.
  `ParagraphComposer.Break` and `BreakAnyway` take a width for each line in turn, as TeX does, keeping nodes of
  different lines apart while the widths differ; checked against every breaking of random paragraphs with narrower
  first lines.
- Tab stops. `TextLayout.TabStops` (`TabStop`: a position, a `TabAlignment` - `Left`, `Center`, `Right` or
  `Decimal` on its `AlignOn` character - and a leader) moves the text after a tab to the next stop; leaders repeat
  across the gap on one grid, so they line up from line to line. Past the last stop, tabs stop every `TabSize`
  spaces as before.
- Optical margin alignment. `TextLayout.OpticalMarginAlignment` hangs punctuation, quotes and hyphens at the edges of
  lines partly past the margin - left for left-aligned and justified lines, right for right-aligned and justified
  ones - so the edge looks straight, as InDesign's Optical Margin Alignment does.
- Japanese line breaking is checked against the kinsoku rules of JIS X 4051 (closing brackets, small kana, the
  prolonged sound mark and full stops never start a line, opening brackets never end one), a line at a time and a
  paragraph at a time.
- The paragraph composer. `TextLayout.LineBreaking` (`LineBreaking`: `Greedy` - the default, a line at a time - or
  `Paragraph`) breaks text wrapped by `WrapByWords` a paragraph at a time, as TeX and InDesign's Paragraph Composer
  do. `ParagraphComposer` is Knuth and Plass's algorithm over boxes, glue and penalties, with TeX's badness, fitness
  classes and demerits (line penalty 10, a hyphen after a hyphen 10000, a hyphenated next to last line 5000, fitness
  classes two apart 10000), and its three passes: without automatic hyphenation at tolerance 100, with it at 200,
  then with an emergency stretch, where a word wider than the line overflows. Justified text gets spaces that
  stretch by half and shrink by a third; ragged text an even edge, with a stretch of 2em at the end of each line.
  Checked against every breaking of 400 random paragraphs; a paragraph's demerits are never above those of the same
  text broken a line at a time.
- Justified lines shrink their spaces, by up to a third, when the composer set them tighter than their natural width.
- Hyphenation. `TextLayout.Hyphens` (`Hyphens`: `None`, `Manual` - the default - and `Auto`) lets a word wrapped by
  `WrapByWords` break across lines, a hyphen (U+2010, or `-` when the font lacks it) ending the line: at its soft
  hyphens (U+00AD, drawn only where a line breaks at one), and under `Auto`, in a word without any, where the
  hyphenation patterns of its language allow. `Hyphenator` runs Liang's algorithm over TeX's patterns from hyph-utf8,
  with their exception lists and the letters each language keeps at each side of a break; built in are Russian,
  English as the United States and as Britain write it, German (1996 orthography), French and Spanish, and
  `Hyphenator.Register` adds others (`Hyphenator.Load` reads any hyph-utf8 `.tex` file). The language is the run's
  `TextAttributes.Language`, else the new `TextLayout.Language`, which also picks the font's localized forms and the
  fallback fonts for plain text. Checked against the `hyphen` package, another implementation of the algorithm, on
  132 words over the six pattern files (`Tests/Adamantium.HyphenationOracle`).
- `TextTrimming` with `WrapByWords`: a word too wide for its line, which hyphenation cannot break, ends in an ellipsis
  and the text goes on below it; the last line the height leaves room for ends in one when text is left over, after a
  word or a newline (`WordEllipses` drops the word that does not fit, or cuts by characters on a line without spaces). Trimming used to apply only without wrapping or by characters.
- Arabic shaping, as HarfBuzz's Arabic shaper does it: letters take their joined forms (`isol`, `fina`, `fin2`,
  `fin3`, `medi`, `med2`, `init`, by the joining types of `ArabicShaping.txt`), the features apply in their stages
  (`ccmp`/`locl`, each form, `rlig`, `calt`, `mset`), and modifier marks such as hamza go first among the marks of
  their class (UTR #53). The same applies to Syriac, N'Ko, traditional Mongolian, Mandaic, Adlam and the other joining
  scripts. Checked against HarfBuzz on Noto Sans Arabic, Noto Naskh Arabic and Noto Nastaliq Urdu (Persian, Urdu,
  vowel marks, lam-alef, tatweel, ZWJ and ZWNJ). Not yet: the stretching of Syriac abbreviation marks (`stch` is
  applied, but its glyphs are not repeated across the word), and shaping from Unicode presentation forms for a font
  without Arabic GSUB. `TextShaper.Shape(font, text, start, end, options)` shapes a range of a string as if alone (its
  clusters and feature ranges count from `start`) with the text around it as context, so letters join across the runs
  `TextLayout` cuts a word into.
- Church Slavonic and Old Cyrillic checked against HarfBuzz on Ponomar Unicode and Monomakh Unicode: breathings,
  accents and titla stacked over letters, letter-titla, numbers under the titlo, the old letters (yuses, yat, fita,
  izhitsa, ksi, psi, koppa). They shape as HarfBuzz does with no change to the shaper.

- Text in both directions, by the Unicode Bidirectional Algorithm (UAX #9, Unicode 16.0; every case of `BidiTest.txt`
  and `BidiCharacterTest.txt` passes). `TextLayout.Direction` (`TextDirection`: `Auto` takes each paragraph's
  direction from its first strong letter) lays Hebrew and other right-to-left runs out right to left, numbers and
  Latin among them left to right, and each line in visual order after it breaks. `BidiParagraph` gives the levels of
  a paragraph. The shaper shapes right to left as HarfBuzz does: mirrored brackets (`rtlm`, `rtla`), cursive
  attachment and marks the other way, HarfBuzz's combining classes for Hebrew marks; checked against HarfBuzz on
  Noto Sans Hebrew.
- Caret and selection for text in both directions. `CaretStop.IsRightToLeft`, `Left`, `Right` and `After` (the caret
  before a right-to-left letter stands on its right edge, after it on its left). `CaretPosition` (before a character,
  or after the one before it: two places where the direction changes), `TextLayout.GetCaretPoint`, `MoveVisually`
  (one letter left or right on screen, over invisible marks), `GetLineStart` and `GetLineEnd` (the line's edges on
  screen; the end is before the newline or at the end of the text, so typing there appends), and selection by the
  screen: `GetVisualRanges` (the characters between two points on screen, which may lie apart in the text).
  `IsRightToLeftParagraph`. A paragraph separator inside a line starts a paragraph of its own.

- A variable font on the way between two sets of axis values, as an animation passes it:
  `IFont.GetInstance(from, to, progress)` lays text out at exactly the values there, and its `Blend` names the two key
  instances around it, set at every eighteenth of the axis that moves most (wght 100, 150, 200...) so animations share
  them. `TextLayout` draws such a glyph from both keys' cells (`FontItem.SecondSource`, `Second`) and `FontEffect`
  blends their true distance; the point's own glyphs never reach the atlas. Color glyphs are drawn as the nearer key.
- The rest of a variable font's axes. `FontAxis.Name` and `IsHidden` (the font's name for an axis, and whether it
  keeps out of style panels); `IFont.NamedInstances` (the styles 'fvar' names, as "Bold Condensed");
  `IFont.AxisValues` and `ElidedFallbackName` from the style attributes table ('STAT': "Light" on `wght`, "Condensed"
  on `wdth`, which ones a style's name leaves out). 'MVAR' varies a font's line, cap height, underline and strikeout
  metrics; 'FeatureVariations' swaps the lookups a feature applies at the axis values it names (heavy `$` in Roboto
  Flex), and kerning and mark positions take their deltas from GDEF's variation store. `IFont.AtOpticalSize` sets
  'opsz' to the size of the text, as CSS's `font-optical-sizing: auto`, unless it was asked for by value, and
  `TextLayout` sets each run's; `FontVariation.For` with a style sets 'ital' or 'slnt' as CSS Fonts 4 does, and
  `FontCollection.Load` takes the style. Axes, named instances, metrics and shaping at axis values are checked
  against HarfBuzz.

- What a panel of OpenType features and a glyph panel need from a font: `Feature.Name` (a stylistic set's or a
  character variant's own label from the 'name' table, otherwise the registered name), `Tooltip`, `SampleText`,
  `ParameterLabels`, `Characters` and `ValueCount` (how many alternates the feature offers); `IFont.GetName` for any
  string of the 'name' table; `IFont.GetGlyphAlternates` - every glyph a single or alternate substitution turns a glyph
  into, with the feature and value that ask for it; `IFont.GetGlyphText` - the characters a glyph stands for, through
  substitutions and ligatures. Checked against HarfBuzz.
- `TextShaper.DefaultFeatures`: the features shaping applies without being asked (`ccmp`, `locl`, `calt`, `liga`,
  `kern`…), from the same list the shaper plans with, so a settings panel can show a font's `calt` as on and its
  `ss01` as off before anything is asked for.
- `FontCollection`: fonts grouped into families from their headers alone (`name`, `OS/2`, `head`, `fvar`; collection
  files included), found by typographic, older or full name, with each face's weight, slant and width (`FontFace`).
  `Match` picks a face as CSS Fonts 4 and the browsers do: the width first, then the slant, then the weight; a variable
  font covers its range. `FontCollection.System` indexes the operating system's fonts once and keeps the index on
  disk, reading again only files changed since. `FontWeight` (1-1000, `SemiBold`, `650`), `FontStyle` and
  `FontStretch` (`Condensed`, `75%`). `Typeface.LoadSystemFont` finds a font through it instead of parsing every file's
  names.
- `TextAttributes.Font`: a face per range, such as a bold word in a regular line; each line takes the largest ascent
  and descent of its fonts. `GlyphWordData.Font`, `TextLayout.GetGlyphs`.
- `TypeParser` parses a type that has its own public static `Parse(string)` without a registered parser.
- Font fallback: a character the text's font lacks is drawn from another font, in the weight, slant and width of the
  text's (`FontFallback`, `TextLayout.Fallback`; the operating system's fallback families unless set, none with null).
  Han, kana and Hangul take the family of the text's language first (Japanese, Korean, Traditional or Simplified
  Chinese), emoji the emoji family; a grapheme (with its marks, joiners, variation selectors and emoji modifiers)
  stays in one font. A family's character map is read without loading the font (`FontFace.HasCharacter`), so only
  the font chosen is loaded. A character no font has draws the font's missing-glyph box.
- `IFont.Weight`, `Style` and `Stretch` from 'OS/2'; `IFont.GetAdvanceWidth` and `GetLeftSideBearing` from the font's own
  'hmtx'; `IFont.TryGetGlyphIndex` is public.
- `GrowingTextureArray`: a 2D texture array that grows as its layers are taken, the way a list does: `Count`,
  `Capacity`, `MaxCapacity` (the device's limit), `TryAdd`, `EnsureCapacity`; a full array is replaced by one twice as
  deep with the layers in use copied on the GPU, and the old texture is retired once no frame in flight reads it.
  `ITexture.ReadbackToImage` takes the layer to read.
- `Win32Interop.LoadImage` and `GetModuleHandle`, with `LoadImageType` and `LoadImageFlags`: an icon loaded from a
  module's resources, such as the application's own.
- `Win32Interop.EnumDisplayMonitors`, `GetMonitorInfo` and `GetWindowPlacement`, with `MONITORINFOEX`,
  `WINDOWPLACEMENT` and `MonitorEnumProc`: the monitors and their work areas, and a window's restored rectangle.
- `TextShaper.Shape` in `Adamantium.Fonts.Shaping`: text to positioned glyphs with the font's OpenType substitutions and
  positioning applied the way HarfBuzz applies them - lookups of every enabled feature in the font's order, lookup
  flags and mark filtering sets, ligatures across marks, mark attachment to bases, ligatures and other marks, cursive
  attachment, contextual and chained rules of every format. Features are set per range of text (`FontFeature`,
  parsed from `liga=0`, `ss01`, `kern[3:5]=0`), the script and language pick the font's language system, combining
  marks are reordered and composed when the font has the precomposed glyph, and each glyph keeps the UTF-16 offset of
  the text it came from.
- `TextLayout.GetCaretStops` and `CaretStop`: the caret position before each character and after the last, with the
  character's width; the characters of one glyph, such as a ligature, share its width. `GlyphWordData` carries its
  `PenX`, `Advance` and positioning offsets; `FontAtlas.RequestAsync` takes glyphs as well as text.
- `AttributedText` and `TextAttributes`: text with features, language, color, background and lines
  (`TextDecorations`: underline, strikethrough, squiggle) over ranges, laid out by `TextLayout.ProcessText`. Each glyph
  takes the color of its text on both text paths, a glyph without one the element's (`FontItem.HasOwnColor`).
  `GetAdornments` gives the backgrounds and lines to draw, `GetRangeRects` the rectangles of a range line by line (for a
  selection), and `GetLine` / `LineCount` the visual lines with their top and baseline. The fonts' underline and
  strikeout metrics are read (`IFont.UnderlinePosition`, `UnderlineThickness`, `StrikeoutPosition`, `StrikeoutSize`).
- `TextShaper.Shape` without a script splits the text where its script changes and shapes each part with its own, so
  Latin and Cyrillic in one line each get their own forms.
- `TextBoundaries` in `Adamantium.Fonts.Text`: grapheme and word boundaries by Unicode Standard Annex #29, emoji
  sequences, flags and Indic conjuncts included; it passes all of Unicode's `GraphemeBreakTest` and `WordBreakTest`.
  `TextLayout` uses them: `NextCaretStop` / `PreviousCaretStop` step over a whole grapheme, `HitTest` gives the grapheme
  under a point and where a click there puts the caret (`TextHit`), and `GetWordAt`, `NextWordStop` and
  `PreviousWordStop` give words for a double click and Ctrl+arrows.
- `TextBoundaries.LineBreaks`: where a line may or must end, by Unicode Standard Annex #14; it passes all of Unicode's
  `LineBreakTest`.
- Typed font features: `FontFeature.Ligatures`, `Capitals`, `Numerals`, `Position` and `Kerning` (`FontFeature.Ligatures.Off`,
  `FontFeature.Numerals.Tabular`), `StylisticSet(n)`, `CharacterVariant(n, value)`, `StylisticAlternates` and `Swash`.
  `FontFeature.TryParseList` checks a list such as `liga=0, ss01, cv05=2` against the OpenType feature registry: a
  lowercase tag the registry does not have is an error naming the tag most likely meant, and a private feature needs an
  uppercase letter in its tag. `ParseList` throws with the same message.
- `TextAttributes.FontSize`: a size per range of text. Each line is as tall as its largest text and its glyphs share its
  baseline; `GetLine`, `HitTest`, `GetRangeRects` and `GetAdornments` follow each line's own height.
  `GlyphWordData.FontSize` is the size a glyph is set at.
- `TextLayout.TabSize`: a tab moves the pen to the next tab stop, a multiple of that many spaces from the line's start
  (4 by default), and draws nothing. Before, it was laid out as the font's glyph for it, usually the `.notdef` box.
- Variable fonts: `IFont.Axes` (`FontAxis`: tag and range, from 'fvar') and `IFont.GetInstance`, the font at the axis
  values given (`FontVariation`, `wght=650`), with its own outlines and advances: TrueType outlines moved by 'gvar'
  (untouched points interpolated, composite offsets and phantom points varied), CFF2 outlines blended, advances from
  'HVAR' or the phantom points, coordinates mapped through 'avar'. An instance has its own `Typeface`, so its glyphs
  take their own places in the shared atlas; the same values give the same instance. Advances match HarfBuzz exactly
  and outline extents to a unit on Source Sans 3 VF (TrueType and CFF2), Bahnschrift, Segoe UI Variable and Sitka.
  `FontCollection.Load(face, weight, stretch)` sets a variable face's 'wght' and 'wdth' (`FontVariation.For`), and
  fallback fonts take the weight and width of the text's.
- Fonts load without waiting: `TypefaceStore.TryGetTypeface` and `LoadInBackground` (parsed on a worker, which raises
  `TypefaceStore.Loaded`; `LoadedCount`), `FontCollection.TryLoad`, `FontFace.TryHasCharacter`, and
  `FontFallback.FontFor(codepoint, like, language, out pending)`. With `TextLayout.LoadFontsInBackground` a character
  whose fallback font is still loading takes the room of the missing glyph and draws nothing; `HasPendingFonts` says
  to lay the text out again when the font arrives.
- `TextureAtlasGenerator.GenerateTextureForGlyphs(glyphs, ready)` hands each glyph over as soon as it is rasterized.
- Color glyphs from 'COLR' version 0 and 'CPAL': a color glyph is drawn as its layers, bottom first, each an ordinary
  glyph of its font in a color of the font's first palette, or in the text's color for the foreground entry
  (`IFont.GetColorLayers`, `ColorLayer`). The layers take places in the shared atlas and draw in the same batch as the
  text around them, so Segoe UI Emoji's emoji come out in color at any size.
- 'COLR' version 1 paint graphs read as the steps that draw them (`IFont.GetColorPaint`, `ColorPaintOperation`,
  `ColorPaintOperationKind`, `ColorCompositeMode`, `ColorFill`, `ColorStop`): clips by glyph outlines, groups
  composited with any of the format's 28 modes, and fills with a color or a linear, radial or sweep gradient,
  transforms folded in. Checked step for step against HarfBuzz on Google's COLRv1 test fonts.
- Color glyphs from 'COLR' version 1 drawn in real time: a color glyph is one quad whose pixel shader runs the glyph's
  paint program - nested clips intersect, groups composite exactly (Porter-Duff, blend and HSL modes), linear, radial
  (two-point conical) and sweep gradients with pad, repeat and reflect, interpolated premultiplied - each outline from
  the atlas's distance field, transformed. The atlas keeps one program per glyph in font units, so one serves every size
  (`FontItem.Paint`; FontEffect's `PaintProgramsAddress` and `PaintStopsAddress`). Segoe UI Emoji's emoji come out
  with their gradients, shading and translucent groups, as Edge draws them; the 28 modes of Google's test font too.
- Color glyphs from PNG images, 'CBDT'/'CBLC' (Noto Color Emoji) and 'sbix': `IFont.ColorBitmapSizes`,
  `IFont.GetColorBitmap` (`ColorBitmap`: the PNG and where it lies, in pixels of its size), the size chosen as HarfBuzz
  chooses it and checked against HarfBuzz's PNGs and extents; a font with images and no outlines loads. Such a glyph is
  one quad of its paint program, whose one step samples a color atlas of its own: premultiplied cells sized to the
  image by powers of two (Noto's 136×128 takes 256×128), each with its own mips, read trilinear at the level the size
  asks for and kept inside the cell. Images are decoded on a worker and land as outline glyphs do. Vector color
  ('COLR') wins over images where a font has both.
- 'COLR' version 1 at a variable font's axis values: every variable paint takes its deltas (the variation store, through
  the delta-set index map) - solid colors' alpha, gradients' points, radii and angles, color stops' offsets and alpha,
  and every transform. The clip list is read (`IFont.TryGetColorClipBox`, its boxes varying too) and a color glyph is
  drawn within its box. Checked against HarfBuzz on Google's variable test font, an axis of each kind set.
- Color glyphs from SVG documents ('SVG ', plain or gzipped): a glyph's drawing becomes the steps a 'COLR' version 1
  glyph is drawn with, so it draws in real time at any size - each shape (path with its arcs, rect, circle, ellipse,
  polygon) an outline clipping its fill, a solid color or a linear or radial gradient (bounding-box or user units, its
  transform, pad, reflect or repeat, stops' opacity, one gradient taking what it lacks from another), groups with their
  transforms and their opacity fading the group as a whole, `use`, `clip-path`, `fill-rule="evenodd"`, `currentColor`
  as the text's color and `var(--colorN)` from the text's palette. Strokes, masks, patterns, filters and style sheets
  are not drawn. Google's samples drawn from their SVG documents match the same drawings
  drawn from their 'COLR' version 1 graph.
- `SvgPathData` in `Adamantium.Mathematics.Svg`: `Walk` reads SVG path data into an `ISvgPathSink` as absolute steps
  (relative points, `H`/`V`, smooth curves' reflected controls and repeated commands worked out; arc flags written
  without separators included), `ArcToCubics` turns an arc into cubic curves, and `ReadNumbers` reads SVG's numbers;
  `Colors.TryGetNamed`.
- Color palettes: `TextAttributes.ColorPalette` draws a range's color glyphs in another of its font's 'CPAL' palettes
  (`IFont.ColorPalettes`, `ColorPalette` with the backgrounds version 1 says it suits, `ColorPaletteUsage`;
  `IFont.GetColorPaint` and `GetColorLayers` with a palette); a palette the font lacks draws in its first. Checked
  against HarfBuzz's paint in the second and third palettes of Google's test font.
- Emoji sequences: a variation sequence the font maps ('cmap' format 14) shapes to its own glyph, its selector dropped,
  as HarfBuzz does (`IFont.TryGetGlyphIndex(codepoint, variationSelector, out glyph)`), and a default-ignorable
  character the font's substitutions have replaced is no longer hidden or zeroed: a keycap, a joined flag, a skin tone
  and the ideographic variants of Noto Sans CJK shape as HarfBuzz shapes them. A grapheme takes one font, the one its
  first character picks; one drawn as an emoji (`EmojiPresentation.IsEmoji`: VS16 after an emoji character, a flag, a
  skin tone, a joined sequence, or an emoji by default) takes the emoji family unless the text's font has the glyph in
  color, keeping the text's font when no family has it in color; one drawn as text (VS15, or text by default, as a bare
  heart) does not prefer it (`FontFallback.FontFor(..., emoji)`). Segoe UI Emoji's sequences come out as Edge draws
  them.
- Synthesized bold and italic for a face a family lacks (`FontSynthesis`, `TextAttributes.Synthesis`): a bold moves
  the glyph's edge out in the distance field, by a 48th of the size per side at 9 pixels down to a 64th at 36, and
  advances it further by twice that; an italic slants the glyph's quad about its baseline by a quarter of its height
  (about 14 degrees), and the text takes the room its letters lean into. Both draw from the same atlas glyphs, in the
  same batch as the text around them (`FontItem.Synthesis`). `FontSynthesisRules.Needed` says what to synthesize: a
  weight of 600 or more on a lighter face, an italic or oblique style on an upright one, only as allowed.

### Fixed

- `MSDFGenerator.GenerateMSDFForExistingTextureData` filled the space away from a glyph with a distance that changed
  with where the glyph sat in its em square; it is half the em now, for every glyph.
- A character no family of a font fallback's list has - Hebrew or Arabic on a Linux whose fonts for them are not
  among the families listed there - was drawn as the missing-glyph box though a font installed for it had it. The
  fallback goes on to the collection's other families now, those sharing most words of the text's family name first.
- An effect that could not be made - a pass whose resources a descriptor model or the device cannot take - kept the
  shaders of the passes made before it.
- On the descriptor heap a storage buffer nobody bound wrote no heap index, so the shader read whatever buffer heap
  slot 0 held, and the pass did not report it unbound. It reads a buffer of zeros now
  (`IDescriptorHeapManager.FallbackBufferOffset`), as on descriptor sets, and the pass reports it.
- A hot-reloaded version of an effect that could not be made left the effect half rebuilt, its old version gone and
  its resources unbound. The running version is now built again with its values, and the new one refused.
- An effect's constant buffers never freed the native memory their bytes were in.
- A cloned effect was a copy of the object: a generated effect's parameter properties were still the original's, the
  clone shared the original's dispose lock, and disposing it took the original off its device's resources.
- A compute pass reading storage buffers through the descriptor heap before the render pass read nothing: the heaps
  were bound only when the render pass began. They are bound as the frame's command buffer begins. A storage or uniform
  buffer is made with a device address, which the heap writes its descriptor by.
- A block of buffer memory was mapped by what its first buffer asked for, not by its memory type: on a device whose
  one memory type is device-local and host-visible, a device-local vertex buffer made the block unmapped and the next
  upload through it wrote nowhere.
- The shader binary cache named a binary without the stage after it and the shader's flags, so a mesh shader made
  without a task shader could load the binary of the same code made with one.
- The effect generator's messages stood at no place and named the file twice; a compile error now stands at its line
  and column. A technique A with a pass BC and a technique AB with a pass C - or a parameter named as a pass - made two
  properties of one name, and the generated class did not compile; that is now an error naming both.
- Every effect compile - hot reload, the effect generator in the IDE - kept its Slang session, about 150 MB of
  native memory each; it is freed when the compile ends.
- A pass with a vertex shader and no pixel shader drew with the pixel shader the pass before it left bound.
- Dark text on a light background had a grey rim round bold letters and stepped slanted edges on large letters at 100%
  scale: its edge coverage was raised for light ink on dark. The coverage is now curved by the ink's lightness and the
  text's size, and taken as the share of the pixel the outline covers (four samples across it) rather than its
  distance from the pixel's middle. Shadows, glows and reflections blend as before.
- A texture larger than the device's limits - a side past its maxImageDimension1D, 2D, 3D or cube, more layers than
  its maxImageArrayLayers, past what the device makes of its format, tiling and usage - extent, layers, levels and
  bytes - or larger than one allocation may be, went to Vulkan as it was; it is refused with `NotSupportedException`
  naming the limit, before a staging buffer its size is made. A texture that fails while it is made is disposed and
  its pixels let go, leaving nothing behind; so is a shared surface, any of several, and a multisampled render
  target whose resolve texture fails. An imported shared surface the device refuses memory for no longer leaks its
  copy of the descriptor. `TotalSizeInBytes` of a texture array counts every layer.
- A BMP of 24 or 16 bits read as RGB of 8 bits, a format most devices do not sample from; it reads as RGBA.
- GIF frames came out other than Pillow and ffmpeg compose them in every animation of the tests, and a GIF's play
  count was always forever. A GIF written made palette entry 127 of each frame transparent - its graphic control
  written after the frame, for the next - and showed every frame 40 ms.
- A BMP read came out RGB but said it was BGR: written as JPEG its red and blue changed places, and a texture made of
  it showed them swapped. It says RGB now; written, BMP swaps only pixels said to be RGB into its BGR, and
  `Texture.Save` writes a B8G8R8A8 render target as it is, said to be B8G8R8A8 - every encoder reads the order the
  pixels say they are in: TGA wrote BGRA and RGB pixels with red and blue swapped, and GIF quantized BGRA and 16-bit
  pixels as if they were 8-bit RGBA. BMP, TGA and GIF write 16-bit and gray pixels too - they wrote them broken or
  threw; pixels they cannot take throw `NotSupportedException`, and TGA throws for a picture past 65535 pixels a
  side, where it wrote an empty header.
- `TextureCube` from six files read each face as long as the first: faces of other sizes or formats were read past
  their ends - and made a cube of one layer, so only its first face went up. Faces must be square, of one size and
  format, and all six go up.
- A texture made from a picture of several layers, levels or depth slices - a DDS cube, array, volume or mip chain -
  got only the first level of its first layer. Every part goes up now, each where it belongs, in one copy - from an
  `Image` and from `Texture.Load` alike - a cube made cube-compatible and viewed as a cube or an array of them (the
  device's imageCubeArray feature turned on where it has it), an array of 1D textures as one. An image of a cube
  of unequal sides is refused.
- `ViewportF.Project` and `Unproject` turned y the DirectX way round, against the engine's clip space - y down, as
  Vulkan's - and against `Vector3F.Project`, `Unproject` and `Ray.GetPickRay`: a point projected and picked through
  its pixel missed by its mirror image. They are `Vector3F`'s now.
- `Utilities` wrote and read a `char` as one ANSI byte, losing every character past ASCII; a char alone is its UTF-16
  unit, two bytes, as in managed memory. A struct holding chars goes through marshaling as its `CharSet` says.
- Saving to a file - `Image.Save`, `BitmapLoader.Save`, `IRawBitmap.Save` - truncated a file already there before an
  encoder that refuses the picture threw; the file is opened when the first byte is written, and GIF and PNG refuse
  a picture - its format, or PNG text it cannot hold - before they write one. A type nothing writes, such as
  `ImageFileType.Unknown`, throws `NotSupportedException`; it wrote nothing, and said nothing.
- `Utilities` threw for a generic struct, which marshaling cannot size; one holding no bool or char is copied as it
  is.
- `SpriteBatch` drew `BackToFront` front to back and `FrontToBack` back to front; threw at its 257th texture in one
  batch; and drew with a custom effect as points for a geometry shader it no longer has. A custom effect draws as
  the sprite effect does - four vertices a sprite, one instance each - with its parameters of the same names.
- `SpriteBatch` drew upside down: its projection took the top of the target for its bottom, as Direct3D's clip space
  has it, not the engine's. `Begin(screenSize, sortMode)` did not compile - two overloads took it. Two batches drawn
  in one frame, or one past its capacity, wrote their sprites over the ones drawn before them in the frame: every
  batch of a frame now has its own room. Under the depth test the deepest sprite was drawn in front.
- Setting scissors whose `Offset` or `Extent` was left null threw on the second set: they compare as zero.
- A vertex field of `Int4` or `Int3` went to the GPU as unsigned integers, though they are signed and `int` went as
  signed: a shader reading them as `int4` broke the vertex-input rules.
- `IRawBitmap.TotalSizeInBytes` counted a compressed format's texels as pixels; it counts whole blocks.
- `PropertyKeyCollection` never took an enum for one: it asked whether the `Type` object was an enum, which it never
  is. Enum values are kept as their underlying integers now, as `EffectPropertyCollection` keeps them.
- Writing a BMP of fewer than 40 bytes of pixels wrote past the end of its buffer and could bring the process down:
  a header was copied in after the file's headers for nothing. It is gone, and the headers' native memory is freed.
- A BMP of 24 bits whose rows are not a multiple of 4 bytes came out wrong both ways: it was written with the padding
  of its width in pixels, not bytes, and read short of its last rows' padding. A file shorter than its pixels is
  refused with `InvalidDataException`. A BMP of a palette, compressed or stored top-down came out empty, and one of
  16 bits threw `IndexOutOfRangeException`: they are read now, by the new BMP codec.
- Every PNG the engine wrote carried a `tIME` chunk of the local time it was written: the same picture saved a second
  apart came out different, and what compared files to skip rewriting them - the Linux desktop entry's icons - wrote
  them again. `tIME` is written only when `PngMetadata.Time` is set, in UTC as PNG keeps it.
- JPEG scans are decoded unit for unit as T.81 lays them out, the padding at the end of a restart interval or a scan
  thrown away: a progressive JPEG with restart intervals came out garbled or threw, a tiny gray progressive one read
  its padding as data, and a scan whose data ended without a marker ran forever.
- A grayscale JPEG came out as `R8G8B8A8_UNORM` with three bytes a pixel and no alpha written: a quarter of the picture
  missing and the rest shown transparent. It comes out whole and opaque.
- The views `Texture.GetLevelView` makes were never destroyed: every texture whose levels were drawn into one by one
  left them behind when it went.
- A texture made from an image description, and the font atlas, started in `PREINITIALIZED` layout with optimal tiling,
  which only linear tiling allows; they start undefined.
- `SetViewports` / `SetScissors` sent `vkCmdSetViewport` / `vkCmdSetScissor` at once and the draw sent them again with
  their count. With the shader-object layer the first set state its pipelines do not declare dynamic; now only the draw
  sends them.
- The shader binary cache named a stage's binary by that stage's code alone, while where it reads its resources depends
  on the other stages of the pass too: a binding added to one stage left another loading a binary that read the wrong
  place. The resource layout of the whole pass is in the name now, and the cache folder is keyed by the descriptor
  model and a format number as well - so the precompile stamp of an older cache does not count for this one.
- The effects compiler took the same binding in two descriptor sets for a clash, and gave every constant buffer set 0
  whatever set it was in.
- The editor's selection outline of an entity added in this frame - one just placed, which is selected at once - was
  drawn for one frame at the camera itself (the middle of an orthographic view, a flash over a perspective one): its
  transform for the camera was not worked out yet. `EditorOverlayProcessor.DrawOutline` outlines an entity from the
  first frame the scene has placed it for that camera.
- A D-Bus string holding a NUL ended early and the bus dropped the connection for the bad message: the NUL is now
  written as U+FFFD, as an unpaired surrogate already was.
- An ICO file is read as applications carry it: its PNG images are decoded as PNG - they were read as bitmaps, so
  garbage - and its 256-pixel image, listed with the size 0, is no longer skipped. Bitmap images of 1, 4, 8, 24 and 32
  bits are read with their palette and their transparency mask, top-down, as RGBA; of images of one size the one of
  most colors comes first. A bitmap of color masks, and a file that lists more than it holds, are refused.
- The glyph atlas every text shares was made on the render device of the first window to draw text and went with it:
  when that window closed while others stayed, their text drew from a disposed atlas and every frame failed.
  `FontAtlasStore` now makes it on the main device's resource loader, which lives as long as the application.
- A PNG decoded to another bit depth than the file's took its `SurfaceFormat` from the file's depth, so its pixels were
  read in a format they were not in.
- `HeadlessPresenter` drew into a ring of frames like a swapchain, so a frame read back after it ended was the next,
  empty slot; it keeps one frame, the one just drawn.
- A block drawing a window of a layout (`TextLayout.Update`/`BuildGlyphRun` with a window - a frame of a text, a part of
  a paragraph a page breaks) drew every glyph whose ink touched the window: the descenders of the line above it showed
  at its top edge. Across the lines a glyph is in the window by its line now, drawn whole when its line is in it and not
  at all when it is not - a drop cap in every line it spans; along them, and down vertical lines, by its ink as
  before.
- `GraphicsDevice.ClearDepthInRect` moves its rectangle by `TargetOrigin`, as scissors and viewports are moved: inside
  a layer away from the window's corner it reset the depth of the wrong place, so a translucent stroke there (a tab's
  close cross under the strip's edge fade) lost part of itself to the depth left under it.
- A font's OS/2 table is read as far as its version and length go: a version 1 table (DejaVu Sans) had its x-height,
  cap height and the fields after them read from the bytes beyond it. Where a font gives no cap height,
  `CapsHeight` is the top of its H.
- An instance of a variable font named in its `fvar` table (Noto Sans at weight 700) carries that name: its
  `FullName` and subfamily say Bold, not the default instance's Regular.
- Spaces a line running right to left starts with hung past its start, out of the frame - the right margin - when
  the line was set to its start or justified; they take their room inside it, as at the left of a line running left
  to right. A line of spaces alone in text running right to left by `TextLayout.Direction` alone stood at the left;
  it stands at the right, as in a paragraph that sets its direction.
- A shared surface imported by file descriptor - Linux - gave its descriptors to Vulkan: a successful fd import takes
  ownership of the descriptor, so the producer's own were consumed. Importing the surface again, as a resize of the view
  showing a universe does, failed with `VK_ERROR_INVALID_EXTERNAL_HANDLE` and the window stopped drawing; the producer
  also closed the descriptors a second time. The import hands Vulkan a copy (`PosixInterop.Dup`).
- A window's Vulkan surface outlived it: surfaces were kept, by window handle, until the instance went. A swapchain
  presenter now holds its surface and lets go of it when disposed (`IGraphicsDevice.ReleaseSurface`), after its
  swapchain; the last to let go destroys it. A `wl_surface` is memory libwayland frees, and a window opened after
  another could get the closed one's address - and with it the old surface, made for a window that no longer existed.
- `Universe.GpuTimeMs` added up the last GPU time of every output each frame, also of outputs hidden or not drawn in it.
- On Linux a header included by another header was not found when the effect compiled from text: the request's file
  name was taken with `Path.GetFileName` after its separators had been turned into `\`, which Linux does not split on.
- Tab stops in a paragraph running right to left were measured from the left reserve of its line; they are measured
  from the right edge, the one its lines start at - the frame's, past an exclusion - as in a left-to-right paragraph
  from the left.
- A line of spaces alone in a paragraph running right to left - an empty paragraph an editor shows by a space - stood
  at the left, its caret with it; it stands where a line of text would: at the right, in the middle or at the left, a
  justified one at its last line's place.
- An entity moved while a camera was not looked through stayed at its old place through that camera once it was: the
  world matrix kept for it still counted as computed. A moved entity now forgets them all
  (`Transform.ForgetComputedMetadata`).
- Picking a skinned mesh posed the skin of every part from the start, composing the parents of every joint, and took a
  collection lock for every child it walked: the pointer over two animated models cost the frame about 10 ms. It now
  takes the world matrices worked out for the ray's camera in the frame (`Skin.PosedBounds(mesh, camera)`,
  `Skin.Intersect(mesh, ray, camera, out distance)`), carries the box by the joints the part follows only, and walks
  the children without the lock.
- An output made on the host's thread was queued and mapped without the lock the universe's own loop reads the queue
  under (`UniversePlatform.CreateOutput`, `SwitchContext`, `RemoveOutput`).
- `EntityComponentCollection.Contains<T>` took the collection's lock on every call, which the Forward+ walk made for
  every entity every frame; it reads the components as `Get<T>` does.
- An array of textures in an effect was reflected as one texture: the element's type was read for the whole variable
  (`Adamantium.Vulkan.Spirv` 1.0.13).
- A descriptor heap slot freed with its texture or buffer was never handed out again: its frames were counted by the
  device that made the heap - the resource loader, which never draws - so they never went by. A freed slot goes
  through the deferred queue now, which waits for the frames in flight of every device sharing the heap.
- An SVG glyph's color is rounded as browsers round it: a percent is of 255 and a half goes up - `rgb(50%, 0%, 0%)`
  is 128, not 127, `rgba(0, 0, 0, 0.3)` has an alpha of 77, not 76. Its named colors take CSS's "grey" spellings and
  `rebeccapurple`, and no longer "zero"; a "#" followed by a space or another character that is no hex digit is no
  color, where it threw or was read as one.
- A drop cap selected is covered across all the lines it spans, and the caret before or after it is as tall as it
  (`TextLayout.GetRangeRects`, `GetCaretRect`), also for a text shorter than the cap; both stood as high as its first
  line only. Its background covers it across its lines too, and no underline, strikethrough or squiggle is drawn
  through it - of its own characters or of the text beside it (`GetAdornments`).

- A filled ellipse sector (`Shapes.Ellipse`, solid) that does not start at zero is filled where it is: the start was
  turned into radians and then read as degrees again, and the fill was cut at the sweep rather than at start plus sweep,
  so a quarter from twelve to three was filled as a sliver beside three o'clock.
- A skinned model cost the frame for every part of its mesh: each part's draw composed every joint's world matrix
  through all its parents again and set the whole palette. The draw takes the joints' worlds the transforms were
  worked out with for its camera this frame (`Skin.FillPalette(palette, camera)`, `Transform.GetWorldMatrixF(camera)`)
  and resets only what the last palette left; a run of parts sharing a skeleton where it stands keeps the palette set
  for the first. Two glTF BrainStems (59 parts, 18 joints each) drew in 11.8 ms a frame, now 1.2 ms.
- The render thread spent a third of its time in uncontended locks: each processor asking each entity for a component
  (`GetComponent`, `GetComponents`), the walks over an entity's children and the world's roots, and the descriptor
  heap's lookup of a texture's or a sampler's slot. These read without the lock now; only a change takes it.
- Every effect apply copied its constant buffers into the frame's pages, though most draws in a row set the same
  values: an unchanged buffer takes the copy made earlier in the frame. The camera's world position is worked out once
  per frame for the transforms' walk, not through its parents for every node.
- `EntityManager.RemoveEntity` took out only the world's own entities: a part of one's tree - a mesh of an imported
  model - stayed where it was. It now leaves its owner with its tree, announced by `EntityRemoved`, and a camera
  following it leaves the world with it. `InputService.UserControlledEntity` is let go of once it, or what holds it,
  leaves the world: the camera keys followed it into nothing.
- `Mesh.Optimize` left tangents on the old vertex numbering when it kept them; it now carries them, and keeps apart
  vertices that differ in what it does not weigh - tangents it keeps, attributes beyond, morph targets.
- A baked mesh with tangents and no bitangents came back without its tangents. A mesh given tangents or normals now
  derives its bitangents, `cross(normal, tangent) * w`, whenever it has both a vertex.
- `Mesh.CalculateTangentsAndBinormals` reversed the tangent and the bitangent on a triangle whose texture is laid on
  mirrored (the texture coordinates' determinant below zero) - after a glTF import nearly every one; the tangent is now
  also of unit length.
- `Mesh.Merge` left per-vertex data of one of the meshes beside the merged vertices; what only some of them had is
  dropped, as their colors and texture coordinates already were.
- `Entity.Duplicate` lost every entity's place: the copied `Transform` was dropped for the one each new entity comes
  with, so the copy and all under it stood at the origin, unturned. A copy now stands in its owner as the original
  stands in its own, is shown, switched and picked as it is, and an animation copied stands as the original does,
  paused or playing, with its own list of clips.
- `AnimationComponent.IsEnabled` did nothing: switched off, an animation now stands its nodes at rest once and leaves
  them be, and plays from the start when switched on. `IsPaused` stopped the pose but not the clip's clock, so play went
  on from where the clip would have got; it now goes on from where it was paused. A new `AnimationSpeed` goes on from
  the pose it finds, where it jumped the clip as if it had played at that speed from the start.
- A material pointing at an image the model does not have imports without its texture; the import of the whole model
  failed on it.
- A free third-person orbit stays where it is when its host turns: its offset is the view's, in the world, turned back
  by the host's own turn. It swung round with the host while its view stayed, so it looked away, and the rotation tool,
  used while looking through it, chased the swing and shook the camera.
- Placing an object, the pointer leaving the scene - above the horizon, past the grid - leaves the ghost where it last
  touched the scene; with an eye standing on the ground, which the pointer never meets ahead, the ghost goes on the
  ground under what the pointer shows at the camera's focus (its subject, for an orbit; `PlacementTool.UnderFocus`),
  nothing placed out of view. The ghost vanished there, and from an eye on the ground nothing could be placed.
- In a flat view the ground grid shows whole from the screen's near edge and fades only far off, as ground does toward
  a horizon in a perspective (`GroundGrid.CenterFor`, `GroundGridLayout.Center`); looking straight down it lies round
  what the camera looks at. It lay round the eye, often far behind what a flat view shows, reached by how low the view
  lay, and came out a wall of stripes or a band faded at both ends with its axis broken off. Its lines drawn edge-on
  show only in a perspective, where they fold into one line toward the horizon; a flat view squeezed them into a band.
- The ground grid's lines take their place as they are drawn, from the camera drawing them; placed in the tools'
  update, they kept the eye of before a camera moved later in the frame, and doubled beside the cells.
- Picking what to place onto - a camera's host - outlines what the click would take, and with Alt takes the whole a
  part belongs to, its root (`PlacementTool.Under`); nothing showed which part was meant, and a model's root could not
  be picked in the scene.
- `Camera.Reorbit`: an orbiting camera put back round its subject at once - a locked one turning its view with it -
  which the tools' update does after a tool moved or turned what it orbits; until its next frame the camera was swung
  along, and the view doubled while turning. A camera locked behind a turned host looks at it: its offset was turned
  by the host twice.
- `Material`, `AnimationComponent` (its key frames, speed, pause and option), `AnimationController.ControllerId` and
  `CameraBase.Velocity` say when they change (`[Bindable]`), as the other components do: an inspector showing them
  did not follow a change made elsewhere.
- The ground grid's axes, and its lines seen edge-on, are hidden behind what stands on the ground, as its cells are;
  they were drawn over it (`EditorOverlayProcessor.DrawFlatBehindScene`).
- Panning with the middle button keeps what was under the pointer under it: the camera moves by the depth of what the
  press landed on, else of its focus; it moved by its focus only, and lagged behind the pointer over a far model.
- The wheel zooms a free camera evenly: a notch makes what it looks at `ZoomStep` times larger however near or far,
  coming that much nearer the point at its focus distance - in perspective by moving, the point pushed on ahead within
  a metre of it, in an orthographic view by scaling, between a millionth and a million. It moved a perspective camera a fixed distance a notch, a little for what was far and a lot for what was
  near, while an orthographic view scaled by `ZoomStep`, so the two zoomed far apart.
- A picture laid on a generated shape reads as on screen, seen from outside: u to the right, v down, and on a flat cap
  of a cube, plane, cylinder, cone or tube its top away from a camera in front, seen from above. A rectangle's
  coordinates run from 0 to 1 across it, from its own shape, whatever transform places it; they ran from -0.5, so the
  picture's corners met in its middle. A cube's, plane's, ellipse's, polygon's and line's picture was turned upside
  down, an arc's, a cylinder's and cone's top cap and a torus mirrored, a tube upside down, its outside mirrored and its
  caps' coordinates past 1. A cylinder's and cone's side meets itself at the back, as a sphere's does; it met in
  front. (The teapot is laid as before.) A geosphere's picture comes round its back without a smear and meets at its poles: its seam was left unmade,
  and its poles took their u from the vertices' positions, on copies their triangles never used. It carries the exact
  normals of its shape, as the UV sphere does.
- A torus, and a cone's or cylinder's side, have no triangles from their last ring back to their first: they had no
  area.
- A rectangle, ellipse, polygon, arc and line show their front to a camera in front of them, and a geosphere outward
  at every subdivision, so culling the back keeps them: the flat ones were wound the other way, toward -Z, taken for
  the front by mistake, and each subdivision of a geosphere turned its triangles over.
  `Mesh.CalculateNormals` points normals out of the front, the side a triangle is clockwise on screen from, as the GPU
  takes it; they pointed out of the back - into a cube, down from a plane. An imported model is wound with the front
  its file meant clockwise (`ConverterBase` turns the winding round after the axis change, into the engine's axes):
  it kept the file's counter-clockwise front, and showed its inside once its back was culled. The scene's shading hid
  all of this, turning a normal to the eye and culling nothing; the editor's lit overlay pass does not turn it.
  An arc going counter-clockwise faces the camera too, its inner point first. A mirror keeps the front outside:
  `Mesh.ApplyTransform` turns the triangles back over, `Mesh.ReverseWinding` turns a strip over too (its first point
  of each run said twice), a rectangle placed by a mirror is turned over, and `Mesh.Merge` turns over a mirrored
  instance's triangles, listed or in a strip; a mirrored cube, arc or rectangle showed only its back.
- A camera a view is seen through, selected, takes no tool: its handles stood in its own eye, and a flat view, seeing
  behind its eye too, showed them as a disc over the whole view and turned the camera by them.
- A polyline is a band along its points, centered on them, a quad a segment listed as triangles facing the camera,
  its picture running along the whole line: it was a strip parted by restarts, on one side of the line, facing away.
- A teapot's patches read down where they face out from its axis, whatever the tessellation: one whose picture ran up
  there is turned round, u with v - its spout and bottom; the inside of the rim, under the lid, has no say. Only the
  spout's curling lip still reads up in part. A teapot carries its patches' own normals, taken a step inside a patch
  where its control points meet, in place of ones averaged off its triangles.
- A new `Material` is an opaque grey (`Material.DefaultDiffuseColor`), and one imported with no diffuse color of its
  own - its diffuse given as a texture, say - keeps it: it was clear black, which an inspector shows as nothing and a
  color picked for it kept clear. Such a material no longer follows `ForwardPlusRenderingProcessor.UntexturedColor`,
  the same grey, which only a material with a black diffuse still takes.
- A new `SphereShape` is a UV sphere: its `Type` was none of the kinds - drawn as a UV sphere, but shown as nothing.
- A capsule made by `ShapeTemplate` is held by a `CapsuleCollider` (`ProceduralShape.CreateCollider`); it was a box.
- A capsule's and a sphere's outline - what a `CapsuleCollider` and a `SphereCollider` are drawn with - is a list of
  closed loops of segments: as strips parted by restarts, which the overlay does not turn on, its loops were drawn
  joined by lines to a point past the end of its vertices.
- A `PolygonShape` and a `PolygonTemplate` stay in their size: the polygon generator takes radii, which they gave their
  size as, and came out twice as large as the other shapes. The generator's parameter is named `radii`.
- A flat view looking low at the ground grid shows it whole round what it looks at: the grid reached as far as the view
  is wide, while the screen's height covers ground the longer the lower the view, so it faded out short of the focus
  and its axes ended in the open.
- Placing with the eye on the ground - told by the eye's height now, not by how near the ground under the pointer is -
  puts the object under the pointer at the focus. The point last touched is held while the pointer is off the scene
  only while it is in view, and not when picking onto something, which a held point is not.
- Moving a free camera along an orthographic view (W and S) zooms it as moving along a perspective does; it moved the
  camera along its view, which a flat view does not show. A free camera moved along its view keeps looking at the same
  point (`FocusDistance` follows), so going flat and back keeps what it looks at as large.
- A free camera turns as an editor's does: about the world's vertical, and up or down about its own right short of
  straight up or down, so its horizon stays level; its turns about its own axes gathered a roll, and it toppled over.
  An isometric one only goes round the point it looks at, its angle down kept, and takes no roll.
- A ring of the rotation and pivot tools turns, near it, by the angle the pointer goes round the ring's center in the
  ring's own plane, so following the ring round turns a whole turn a loop, tilted or not and however fast; past two
  radii, by the pointer's way along the ring's tangent where it left, as fast however far it is pulled. By the tangent
  where it was grabbed, a pointer past a quarter turn turned the target back. A ring seen nearly edge-on, in a flat view
  too, still turns by that tangent.
- A face of the orientation cube turns the camera there for good: the camera took back its entity's old turn the
  moment the animation ended.
- The pointer over a light's, a camera's or a marker's icon outlines the icon, in a color of its own
  (`SelectionOutline.IconHoverColor`, sky blue: the icons are white), so it reads as something a click takes
  (`EntityIcons.IconOf`, `EditorOverlayProcessor.DrawOutlineAlone`).
- A group's icon in the scene is a tree of cubes, one over two, each cube three faces apart, as in a hierarchy; it was
  a folder.
- A hosted universe keeps time by its own frames, a gap without frames counting as a quarter of a second at most: it
  took the frame time of the loop hosting it, which runs at its own rate and sleeps while idle, so camera moves and
  transitions jerked and ran too fast, and the `AppTime.Fps` its services got read wrong.
- Mouse look turns a camera by the distance the mouse moved, the same at any frame rate; it was scaled by the frame
  time as well, and turned slower the faster the frames came.
- A `use` inside an 'SVG' glyph's `clipPath` takes its target's own transform, and a `clip-rule` set on the `clipPath`
  reaches the shapes in it; the clip stood where the target would without its transform, and the rule was read only
  from each shape. An 'SVG' glyph under an ancestor with `display="none"` draws nothing, and an ancestor's `clip-path`
  clips it; both were passed over. A value a property does not take (`fill="bogus"`) is dropped, as CSS drops it, the
  property inherited: such a glyph drew no fill.
- A word hyphenated is set again in its two halves, each by itself: a hyphenation point inside a ligature (of-fice,
  the ffi one glyph) is a place to break, where it was passed over, and an Arabic letter before the break takes its
  final form and the one after its initial form, where they kept the forms joining them across it. Paragraph breaking
  weighs the halves as they come out; CJK punctuation in them stays trimmed as it was, and a word with a ruby base is
  cut as it is, spread to its annotation. A line finds its hyphen by the word's own widths and sets again only the
  half at its end, not one at every hyphenation point.
- A letter shaped in two glyphs at the start of a word no longer counts as a place to break between them: the word
  was taken as two, and a line could end inside the letter.
- An object set into vertical text (U+FFFC with an `ObjectSize`) stands upright down its line: its height along the
  line, centered across it, and a line it is wider than grows to hold it. `GetInlineObjects` gives it: there were none
  down vertical lines, and a control set there was given no room.
- In frames, a line raised by larger text or a taller object keeps out of an exclusion: its room was found for its
  paragraph's line height, and its lower part could reach into an exclusion just below. The text is laid again with
  that line's room found for its height.
- A WOFF2 font with a transformed `hmtx` loads: its transformed length went unread, so every table after it was read
  from the wrong place, and the table itself - the advances, the side bearings the glyphs' left edges stand for - was
  read as a plain one.
- Shaping takes a script under its newer tag first, as HarfBuzz: Devanagari and the other Indic scripts under `dev3`,
  `dev2`, then `deva`; Myanmar under `mym2`, then `mymr`. A font with `dev2` and `DFLT` got `DFLT`.
- Hebrew in a font whose `GPOS` has no `hebr` script is not positioned by it, as HarfBuzz: its marks are placed by
  their classes around the letter.
- The `kern` table, as HarfBuzz applies it: each subtable of the text's direction (vertical lines took none, and so
  had marks placed as for a font with no positioning), a pair across the line moving the glyph up or down - and the
  rest of the line with it - where it narrowed the pair, the pairs of right-to-left text taken left to right on the
  line, and a visible ignorable between two letters passed over (`A`, ZWNJ, `V` kerned). Its subtables of classes
  (format 2) are read, and Apple's `kern` (version 1.0), which read as empty, with its compact classes (format 3) and
  its state machine (format 1), along the line and across it; with a state machine, marks keep their width, as
  HarfBuzz leaves them.
- A glyph cursively attached down a vertical line is set against its neighbor across the line, by X, and along it by
  its anchors' Y; it was set as on a horizontal line. An ignorable glyph keeps its place in such a chain.
- Down vertical lines, the components of a ligature in Old Italic, Runic and Tifinagh are taken right to left for
  their marks, as HarfBuzz takes them.
- A click between a drop cap and centered or right-aligned text beside it goes to the nearer of the two: on the first
  line it put the caret at the line's end.
- A WOFF or WOFF2 font's tables know their length, as a TrueType font's do: those read by it - `kern`, COLR version 1,
  JSTF - read as empty, so such a font lost its kerning, its color glyphs and its justification glyphs.
- An effect compiled from its file finds a header by its path from the file's folder (`#include "Includes/X.fxh"`),
  nested headers included; only those beside it were found. Headers are `.fxh` only: `.hlsl` ones beside it are no
  longer gathered. A request names a header by its whole name: `#include "Tint.fxh"` took the first header whose path
  merely ended so, `BlueTint.fxh`.

- A mesh drawn through more than one cache - the scene and the editor's overlay - is uploaded again by each when it
  changes: the first to upload cleared the mesh's `IsModified` for all, so the selection outline kept the old shape.
  `RenderGeometry` keeps the `Mesh.Version` it uploaded, makes new buffers for a changed mesh and hands the old ones
  to `AddToDeferDisposeQueue` - it destroyed them, and wrote into a live vertex buffer, while a frame in flight could
  still draw with them - and draws nothing for a mesh with no points.

- A shown collider follows a shape made again (`Collider.RefreshShownGeometry`); it kept the old box.

- A first-person camera riding on its host stays on it: `InputService` took it off the host the first frame it was
  looked through, as it takes off a camera free again.

- A tool's drag stops when the selection or the output's camera changes under it; it went on with the next selected
  entity, moving it from where the first one stood.

- Shapes with a top stand up: up is -Y, and the cone, the cylinder built on it and the teapot were generated standing
  along +Y, upside down - a cone's point at the bottom. They are generated the right way up, with no turn after; the
  engine's own cones (the move and pivot handles' arrows, the orientation cube's, the spot light's) give the diameter at
  the end they meant, and an outlined cone puts its rim at the end that has a diameter, as a solid one does.
- Flat shapes - rectangle, ellipse, polygon, arc, line - face the eye: a camera looks along +Z and they were wound to
  face it from behind, so their back was culled where they were looked at. Their triangles are wound to face -Z.
- A solid arc short of a circle no longer draws a triangle across its gap: its strip led back to the first point.
- An outlined box - a box collider drawn, the move handles' center - is its twelve edges as a line list: as a strip it
  needed primitive restart, and drawn without it the restart index joined corners that share no edge. A solid line is
  two triangles with no restart index either.
- `Camera.JumpToObject` stands where the object fills the view with `FramingMargin` to spare, keeping the way it looks:
  it stood off by the object's diameter times the field of view in degrees, far out of sight, measured the diameter
  without the object's scale, and flew into an object with no bounds.
- The editor overlay outlines and shows icons for disabled entities too: an editor works on what is switched off.
- The Syriac abbreviation mark (U+070F) stretches over its word in a font with `stch`: the pieces the feature cuts it
  into stood once each; the repeating ones are now copied to the word's width, as HarfBuzz does.
- Hebrew in a font that positions no marks (no GPOS `mark`) sets a letter and its point as the presentation form the
  font has (U+FB1D-FB4F), as HarfBuzz composes them: the point stood where the font drew it.
- A font with no GPOS places its marks around their base by their combining classes and the glyphs' boxes, as
  HarfBuzz's fallback does - above, below, to a side, stacked, on a ligature's component - in the scripts whose
  HarfBuzz shaper does so (Latin and the other default ones, Arabic, Syriac, Hebrew): a mark stood where the font drew
  it, over the space after its letter. Right to left, a mark's advance is zeroed without moving it, as in HarfBuzz.
- A text wrapped by symbols and trimmed in a fixed height stops at the bottom as one wrapped by words does: lines
  after a hard break, and a line raised by larger text, were set past it.
- Arabic in a font with no joining forms of its own (no `init`, `medi`, `fina`, `isol`) joins by the presentation
  forms the font maps (U+FB50-FDFF, U+FE70-FEFF), lam with alef and the other ligatures of HarfBuzz's fallback among
  them: the letters stood apart, in their isolated forms.
- In frames, a line raised by larger text or a taller object past the bottom of its frame goes on to the next frame
  (or, from the last, into the overset): it was given the room of its paragraph's line height and drawn past the
  frame's edge.
- The paragraph composer takes a tab as wide as it is on its line: it took one step of the default stops, so a line it
  planned with a tab to a stop far along did not fit, the layout broke it again, and the next line, cut where the plan
  said, came out loose. The spaces before the last tab of a justified line take no stretch, as justification widens
  only what follows it.
- The paragraph composer counts the letter spacing between words with no space between them - ideographs, a word after
  a hyphen - and closes them up as the greedy breaker does: it knew none of it there.
- A pair of letters across a ruby base's edge keeps the kerning the font gives it, left to right and right to left:
  shaping is cut at the edge, so a ligature never joins a base to its neighbors, and the pair lost its kerning there.
  Letters that join (Arabic) are left as they are cut.
- `MoveVisually` and `MoveWordVisually` go from line to line as the text reads, by its first paragraph: across
  paragraphs running different ways the arrows went back up at a paragraph of the other direction and came round
  again, never reaching the end.
- A combining grapheme joiner (U+034F) that keeps no marks from being reordered is skipped by lookups, as HarfBuzz
  skips it: lam, the joiner and alef made no ligature, nor a shadda and its vowel. One that does keep a mark from being
  reordered stands as a base to it, as in HarfBuzz 8.3.1: the mark was attached past it to the letter.
- A click on a drop cap below its first line finds the cap: it found the line beside it, its first or last character.
- A ruby base at the start or end of a line does not hang its punctuation past the margin with optical margin
  alignment: its annotation, spread over it, went past the margin with it.
- Text trimmed and wrapped by words in a fixed height no longer sets a line that larger text in it raises past the
  bottom: the fit was checked at the paragraph's line height, so such a line stood half shown with the ellipsis on it.
  The text is laid again up to the lines that fit, the last of them ending in the ellipsis, and a word cut to the
  ellipsis keeps no letters that would raise the last line past the bottom. A first line taller than the area by its
  own words is set all the same.
- A trimming ellipsis is set as the text it follows - its font and size, colors and decorations, and baseline shift -
  where it took the layout's font and size, its default color and the line's baseline. After a font with no full stop
  the dots are the text's own, at its own size; after upright letters of vertical text they take only their colors.
- A number keeps the ideograph or kana after it only while the two fit a line they may stand on (the widest frame,
  between the paragraph's margins, past its indent when the number opens the paragraph): `1234567890年` in a narrow
  column ran past its edge, held together.
- An empty line between two hard breaks (a line feed, U+2028 or U+2029) starts after the first of them: the line
  before took both breaks, so `"ab\n\ncd"` gave lines 0..4, 4..4, 4..6, and a caret at the start of the empty line
  stood on the line before; the breaks also took shares of the width of the letter before them. Lines are now 0..3,
  3..4, 4..6 whether or not the layout emits newline carets.
- A hidden entity (`Visible` false) hides what is under it: the scene drew its children, and the selection outline
  drew them too, as hiding was checked inside the walk's action, which could not stop the walk. Picking skips it with
  its subtree as well: a hidden model still took the pointer and stopped the ray from reaching what is behind it.
  `Entity.VisitInDepth` walks a tree whose visit says whether to go under the entity it is given.
- Curves of a glyph are cut into as many chords as keep each within 1/1024 of the glyph's size: a long curve, cut into a
  fixed five, sagged inside it - a quarter arc of radius 480 by some four units - and a cubic curve could lose its last
  chord. Arcs of SVG glyphs are cut by a quarter turn instead of an eighth.
- An instance of a variable font that varies only advances, metrics or colors - no outline variations - draws with its
  font's glyphs and atlas cells: each instance rasterized the same outlines again, and animating such an axis filled
  the atlas.
- Trimmed text in a fixed height sets only lines that fit whole: at two and a half lines high the third line stood half
  shown, its ellipsis with it. Untrimmed, a line that starts inside is still set and cut by the area, as CSS clips it.
- A composite glyph flagged `USE_MY_METRICS` takes its component's advance and origin in a variable font without
  `HVAR`, as HarfBuzz does; its own phantom points were used.
- Justification alternates (`jalt`) come from the feature of the text's own script and language, as a variable font's
  instance has it: they were taken from every script the font gives the feature for.
- `AttributedText` ranges whose length runs past the end of the text, `int.MaxValue` among them, reach the end: applied
  attributes, styles, paragraphs and ruby set over them did nothing, or gave a paragraph a length past the text.
- A number keeps the ideograph or kana after it on its line (1月, 3つ), which UAX #14 lets part.
- A space the font lacks - a subset of an emoji font - is taken from a fallback font, as Edge does, instead of the
  font's missing-glyph box. A thin or narrow no-break space the font sets by its own space stays in it.
- Threads filling an atlas inline at once (`FontAtlasStore.SynchronousFill`) fill and upload one at a time: an
  off-screen render made every thread fill inline, and two of them uploaded into one atlas at once.
- In columns and around exclusions, letters of a line set into a later frame stand on its baseline: they were rounded
  to the pixel before the line moved there by a part of one, and its underline stood a pixel off.
- Centered and right-aligned lines keep their tab stops, measured from the margin as Word does: only the text after the
  last tab is aligned, in the room after it, and the tab - its leader, underline and selection with it - reaches it;
  text a right, centered or decimal stop sets stays at the stop. The whole line moved, tab stops with it.
- An upright letter's cell down a vertical line ends where the letter does, without its tracking: a justified line
  with tracking fell short of the end by as much.
- A ruby annotation takes none of its base's text effects: it took those of the base's last letter.
- `TypeParser` no longer throws and catches an `ArgumentException` for every type whose parser returns a value: it
  asks whether the parser binds as a delegate instead of trying. A debugger stopped on each of them at start-up.
- A `Camera` rebuilds its projections in its next `Update` when its field of view, a clipping plane or its size
  changes after it was initialized. They were built once, so an edit - from an inspector - changed nothing on screen,
  while what reads `ZNear` and `ZFar` directly, the clustered lighting, moved on with it.

- Making a text style is no longer an edit of styles: text laid out by other styles is not resolved again for it.

- Ruby: a text that ends in a base spread to its annotation measures to the annotation's width, and lines are aligned
  by the room a spread base takes, not its ink, so an annotation no longer reaches past the edge; a base the trimming
  cut no longer grows its line. An effect over a base's last character reaches its annotation, which is drawn after it.

- In `Frames`, a line of larger text no longer covers the lines after it: once it is set, the lines below are placed
  under it, so they keep inside their frame, out of the exclusions and in the overset when they no longer fit.

- An imported `SharedSurface` takes the memory over from its producer in the layout the producer keeps it in, instead of
  moving it out of `Undefined`, which allows a driver to discard a frame the producer had already written.
- `MeshMetadata.Default()` sets the line's end; it wrote the end into the start a second time.
- A frame whose swapchain acquire failed - a window being resized - is submitted like a withheld one, showing nothing,
  instead of being dropped. Its work went with it, while the semaphores registered for it were signaled by the next
  frame: a shared surface's consumer told its producer a frame was read that was never copied.
- An entity changes owner only through `EntityManager.SetOwner` and `RemoveOwner`; `Entity.Owner` can no longer be set
  from outside, nor `AddDependency`/`RemoveDependency` called. `RootEntities` are the roots of the world's tree: an
  entity given an owner - a camera set to follow a model - leaves them and comes back once taken off, from the next
  frame. It stayed among the roots while being its owner's child too, so it was walked twice and a hierarchy showed it
  twice. An entity taken out of the world takes its tree along; `RemoveAllEntities` removes the roots, each with its
  tree.
- The camera no longer hangs itself under its subject or takes itself off: `InputService` does it through the manager
  when it follows a subject, and takes a camera off once it is free again. Whoever calls `SetThirdPersonCamera` hangs
  the camera first. `EntityExtentions.BringIntoView`, which could not, is removed.
- MessagePack 3.1.12, past the advisory GHSA-qhrr-8q5h-9q3h (nested arrays could run past the buffer length limits);
  3.1.7 failed every build that treats warnings as errors once the advisory was published.
- `[Affects]` can be put on a `[Bindable]` partial property. The generator read it there, but the attribute was allowed
  on fields only, so it did not compile.
- Centered and right-aligned lines after an empty line are aligned too: the alignment stopped at the first empty
  line, so `"\n\ntext"` centered stood at the left.
- An output taken out of a universe is freed - its presenter, its device and a render target's shared surfaces -
  once the device has finished the frames that used it, and a paused universe takes its removed outputs out too. They
  were kept until the universe shut down, and while it was paused they stayed in it, holding their host's controls.
  Taking the main output out hands `MainOutput` to the next one, or to none. A pause with nothing taken out touches no
  output: resuming goes on with the same outputs and their resources.
- Shader hot reload no longer loses a save that lands within one tick of the file clock after the one compiled before:
  a save is told apart by its content, not its write time, and the second one shared the first one's time and was
  skipped - the running effect stayed on the older version without a word. A report of the watcher that read the file
  before a save was taken to compile no longer puts that save back on the list.
- A texture or sampler an effect declares but has no live resource for reads the stand-in descriptor, not heap slot 0
  or a freed slot. A parameter never set wrote no index at all, so its shader read slot 0; once the texture
  there was freed, the next draw lost the device. Setting a parameter to null kept the slot of what it held before, as
  did a resource disposed while still set. `SetResource(null)` now unbinds. The warning about an unbound resource says
  the pass reads the stand-in; it no longer claims the draw is skipped.

- A content project built from the engine's source in another repository's solution cooks with the compiler of its
  configuration. The solution, not listing the compiler, handed it no configuration: it was built into
  `artifacts\bin\<tfm>`, and the cook ran a stale compiler from `artifacts\bin\<configuration>\<tfm>`, or found none.

- `ContentManager.Exists` finds an asset by a relative path. With no `RootDirectory` it looked for `/<path>`, at the
  root of the drive, and said every asset was missing.

- A universe hosted in an application that keeps running can be shut down.
  - **Its render devices leave the main device.** A device disposed by whoever holds it, as a universe output disposes
    its own, used to stay in the main device's list with its fences destroyed. The next frame start read those fences
    and the process crashed. A render device now takes itself off the list as it is disposed.
  - **Nothing shared holds it any longer.** `RenderingProcessor` no longer subscribes to the graphics device service; its
    handlers did nothing. `RenderingService` unsubscribes when it is removed or its universe shuts down
    (`UnloadContent`), and `UniversePlatform` when it is disposed. Each of the three kept a shut-down universe in memory,
    with its scene and meshes, for as long as the application ran.
  - `MainGraphicsDevice.UpdateDevice` lists the new device once. It used to be listed twice, and also under the old
    device's id.

- Opening a window no longer loses the device after many windows have been opened and closed.
  - **The cause.** Graphics queues were handed out in turn and never given back. After sixteen windows (the queues of
    the family) the next window drew on the queue of a window still open, from another thread, and the device was
    lost.
  - **Queues are leased.** A render device takes the queue the fewest devices hold and gives it back when disposed, so
    a queue is shared only when every one is taken.
  - **A shared queue is used in turn.** Every submit and present on a queue goes under its lock. A device's uploads go
    to its own graphics queue.
  - **Waiting for the device to idle waits its turn on every queue**, as Vulkan requires. `GraphicsDevice.DeviceWaitIdle`
    and the swapchain's rebuild go through `MainGraphicsDevice.DeviceWaitIdle`, which holds each queue's lock while it
    waits. They called the device directly, while other windows submitted and presented.

- A lost device is reported, so its host can replace it. `MainGraphicsDevice.IsLost` turns true and `Lost` is raised,
  once, as soon as a frame's submit or fence wait, an upload, an idle wait, an acquire or a present reports
  `ErrorDeviceLost` (`MainGraphicsDevice.Observe`). `GraphicsDeviceService` then raises `DeviceLost`, which it never did
  before, and sets `DeviceUpdateNeeded`, which a host acts on with `ChangeOrCreateMainDevice` - as the UI application
  does. Before, nothing heard of the loss: every window went on drawing on the lost device and stayed blank until the
  application was restarted.

- A root entity removed while a frame is drawn stays in the world until the frame ends, as an added one only joins it
  then. `EntityManager.RemoveEntity` took it out of `RootEntities` at once, from whichever thread removed it, while the
  render thread was going through the list. Deleting a light on the sandbox's Scene page failed the frame
  (`Collection was modified`).

- A graphics device's registry of its resources is locked. Resources made or disposed on two threads at once, such as
  textures built by a background task while the render thread works, could corrupt it. A corrupted registry miscounted
  `RegisteredResourceCount` and could crash the process when the device was disposed.

- Text aligned right threw when a line held only spaces: leading spaces before a word too long to follow them on the
  first line. A line with nothing to draw is left where it is.

- A retired GPU resource waited for every render device to begin new frames, so one device that stopped drawing - an
  off-screen one between two bakes - kept every resource retired after it, and everything those referenced, for the
  life of the application. A device with no frame in flight (`IGraphicsDevice.HasNoFramesInFlight`) no longer holds
  them back; one in the middle of recording a frame still does.

- Decoding an animated GIF threw away about a gigabyte for a 200-frame 960x540 animation: the LZW decoder grew a list
  for every frame and a list for every code, every frame kept its index stream until all of them were done, and every
  frame drawn over a part of the one before copied the whole picture's indices first. The decoder writes each frame's
  indices once, at their final size, the newest frame's stream is the only one kept, and the next frame is laid over
  it in place. The pixels are the same, to the byte. `IRawBitmap.CopyFrame` writes a frame into the caller's buffer
  without the decoder keeping it, so an animation read frame by frame (`GifImage` does it in one pass) needs one
  frame's worth of memory instead of all of them.

- `THIRD-PARTY-NOTICES.md` lists `EmojiPresentation.ucd` among the Unicode data `Adamantium.Fonts` embeds. The
  `.ucd` files are checked out with LF, the line endings they are written with, so git no longer shows them changed
  once a tool has touched them.
- Justified text: a widened space now takes its added width into its advance, so the caret, a click and a selection
  reach across the gap instead of skipping it; justified right-to-left lines run from edge to edge, the trailing
  space that ends up on their left no longer holding them off it, and the last line of a right-to-left paragraph
  stands at its start, on the right.
- A paragraph broken a paragraph at a time put a word the composer hyphenated at a line's end whole onto the next
  line, its head left alone there ("тек-" on a line of its own): the line now takes the head, as the composer chose.
- The system fallback drew Chinese and Japanese punctuation and full-width forms (、。「」（）Ａ) from the first family
  that had them - Mongolian Baiti on Windows - instead of the family the text's language reads them in; they now try
  it first, as Han and kana do.
- 'vmtx' was read with the horizontal metrics' count (`hhea`) instead of its own (`vhea`), and before `vhea`, so the
  vertical advances of most CJK fonts were wrong; `vhea` also overwrote the line spacing `hhea` gives.
- `Justify` stretched the last line of every paragraph but the text's last, a line ending at a newline; it stays as it
  is now, unless `justifyLastLine` asks otherwise.
- `WrapByWords` measured a word by the sum of its glyphs' ink, leaving out the gaps between them, so a word wider than
  the room left could stay on the line and run past the edge. It is measured from the pen to the right edge of its
  ink now.
- `WrapByWords` took a newline and the word after it for part of the word before, so `"aa Hello\nworld"` measured
  "Hello" together with "world" and could move it to a line of its own. A word ends at a newline now.
- Specks beside a glyph where its outline has a corner twice, a hair apart, as a variable font's deltas can leave it
  (Bahnschrift's "3" at weight 380): cutting the outline where it crosses itself made near copies of corners, so a
  corner looked like two loose ends and the outside leaked in along a row of texels. Cuts that fall on a corner are
  not made, and the distance field joins ends nearer than a thousandth of a unit and drops segments that short.
- Three layout tables were read wrong, so their lookups did nothing or put marks far off: the rules of a contextual
  lookup of format 1 or 2 (read without their offsets and with an empty input sequence), the anchors of a
  mark-to-mark lookup after its first mark, and the anchors of a mark-to-ligature lookup (taken from the wrong table).
  Vowel marks stacked on Arabic letters, on lam-alef and on Allah, and the contextual forms of Nastaliq, come out as
  HarfBuzz's now.
- A variable font's 'STAT' and 'MVAR' tables were never read: the parser keeps only the tables its ordering list names,
  and neither was on it. GPOS's 'FeatureVariations' offset was read as 16 bits instead of 32.
- A varied advance, a font metric and a positioning delta that land on a half round up, as HarfBuzz rounds them; they
  rounded away from zero, a unit off for a negative half.

- A render device's per-frame constants are rewound when that device begins its own frame, not when the main device's
  frame finishes. A device recording beside the window's loop - an off-screen render - had its pool rewound mid-frame,
  and its later draws overwrote the constants of its earlier ones.
- `TransitionImageLayout` moves every mip level, not only the first: a texture made with mip levels left the rest
  undefined while it said they were in its layout, and the validation layer reported the first barrier that relied on
  it.
- `IFont.GetInstance` gave the font itself for axis values when its outlines do not vary ('glyf' without 'gvar'), so
  what else varies - advances ('HVAR'), color glyphs ('COLR') - stayed at the defaults. Such a font now has its
  instances, their outlines read as they are.
- A 'COLR' version 1 outline drawn through a transform that squeezes one axis (Google's samples draw a vertical bar
  from a horizontal one scaled by 1/8 and 8) lost its antialiasing along that axis and came out half a pixel narrow: the
  field's distance became pixels by both axes' average. It becomes pixels along the outline's normal.
- A palette PNG (color type 3) did not decode: the palette was read by its byte count as if by its entries and ran
  past the chunk. It reads entry by entry, and an index past the palette is an error rather than a stray read.
- `GrowingTextureArray` copied only level 0 when it grew; an array with mips keeps every level.
- A glyph's distance field drew a faint line past an acute corner, along the extension of one of its edges, out to the
  edge of its cell, and dots in narrow gaps (the swirls of Segoe UI Emoji's ice cream; 37 glyphs of Segoe UI, 12 of
  Source Sans 3, 30 of Cascadia Code): neighboring texels' channels, interpolated, put the contour where it is not.
  Where they do, a texel past one from the outline takes the true distance in every channel, as does every texel past
  three; corners keep their channels, sharp for a bold that moves the contour out.
- A point on the line of a corner's edge, beyond the corner, could take the distance to the glyph's outline with the
  wrong sign, drawing a dot well outside the glyph (Cascadia Code's geometric shapes, such as ◆): the two edges at the corner
  were equally near but for the last digits, and only one was kept.
- `FeatureInfo.DefaultFeatureState` said Off for features shaping applies to all text unasked (`liga`, `kern`,
  `calt`, `locl`, `mark`…) and On for `size`, which shaping never applies. It comes from the shaper's own list now
  (`TextShaper.DefaultFeatures`), and `FeatureInfo.Create` no longer takes a default state.
- A text block drew no more than 4096 glyphs and dropped the rest without a word; with a quad per layer of a color
  glyph, some 65 emoji filled it. `TextLayout` has no cap now: its glyphs and the direct path's vertex buffer
  (`EnsureVertexBuffer`) grow to what the block holds.
- A line holding nothing but text of another font (a fallback emoji, a run in a smaller font) took that font's height
  and baseline: a line of emoji sat 4 pixels higher than its neighbours. A line starts from the text's own font, as a
  CSS line box starts from its strut, and only grows for taller text.
- The fonts of a collection file after the first one skipped every table they share with it, their character maps
  included, so NSimSun of simsun.ttc had no characters at all. Only outline data (`glyf`, `loca`, `CFF`) is shared now;
  each font reads its own character map, metrics, names and layout tables, and its advances and bearings come from
  its own 'hmtx' instead of the glyphs the fonts share.
- A `cmap` of format 13 (a range of characters to one glyph, as last-resort fonts map) failed the font's loading, and
  its lookup found only the first character of a range.
- Letters drawn from overlapping contours lost pieces: in Cascadia Code and Mono `n h m u a r 3 4 5` were cut and `w`
  vanished. Variable fonts and the static fonts made from them draw a stem and an arch as separate contours, and the
  overlaps were removed by flipping an "inside" flag at each crossing. A contour piece is now kept where the glyph is
  filled on one side of it and empty on the other, by the nonzero rule; an edge two contours share is kept once.
- One glyph that could not be rasterized dropped every glyph of its batch, so all the text asking for letters in that
  frame drew blank. `TextureAtlasGenerator` now leaves that glyph out, as one without an outline, and names it in
  `Typeface.ErrorMessages`; the rest of the batch lands.
- A font's names came from whichever record of its `name` table came last, often a translation: Segoe UI Bold was
  "Segoe UI Gras". The English (United States) Windows record is taken first, then any Windows one, then Unicode, then
  Mac Roman, which is read as single-byte text instead of UTF-16. `TypefaceStore` is safe to use from several threads.
- Thin slivers of other letters at the edges of glyphs, mostly at fractional sizes. Atlas cells lie edge to edge and a
  glyph's quad reached the very edge of its cell, so the texture filter mixed in the neighboring cell's field. The quad
  now stops half a texel inside its cell.
- Text in a second font drew the first font's letters. `FontAtlasStore` kept one atlas per set of rasterization
  parameters, whatever the font, and glyphs are found in it by index, so a glyph of the second font got the picture the
  first font has at that index. Every font now shares one atlas (32 MB of video memory each, so one per font would not
  scale) and a glyph is found in it by its typeface and index (`GlyphTextureData.Key`, `Typeface.Id`): `FontAtlas` is
  made without a font, `RequestAsync`, `GetGlyphData` and `GetUVCoordinatesForGlyph` take the font with the glyph,
  `FontAtlasStore.GetOrCreateFrom` takes only the parameters, and each glyph is rasterized at its own font's em.
  `IFont.Typeface` is public.
- A swapchain rebuild that failed - out of device memory, say - left the presenter without surfaces, and the next frame
  drew into them and crashed the process. `GraphicsPresenter.IsReady` now says whether the surfaces exist,
  `GraphicsDevice.BeginDraw` skips the frame while they do not, and the failed rebuild reports `OutOfDate`, so it is
  retried on the next frame. A retry no longer destroys the swapchain objects a second time.
- `ErrorOutOfDeviceMemory` when several applications, or an application and its designer previews, ran at once on a GPU
  without Resizable BAR. The device-local host-visible window (about 214 MB on such cards) is shared by every process;
  when it is full, buffers that want it now take host-visible system memory instead of failing.
- Letters of one height no longer jump a pixel apart within a line. `TextLayout` rounded each glyph's top and bottom to
  a whole pixel on their own, so a round letter, a fraction of a pixel above a flat one, could land a whole pixel above
  it. Only the line's baseline is rounded now; every glyph stands exactly where the font draws it.
- CFF delta arrays (`BlueValues`, `OtherBlues`, `StemSnapH` and the rest) were decoded backwards: each value was taken
  as the difference from the previous one instead of their sum.
- Every base glyph of a mark-to-base subtable got the anchors of the first one, so a mark sat where it belonged on one
  letter only. The GDEF mark glyph sets were read from the wrong offset.
- `Font.GetGlyphByIndex` returned the glyph at that position among the glyphs `cmap` maps, not the glyph with that
  index, so any glyph reached through a substitution - a ligature, a small capital - came back as another one.
- Glyphs drawn inside out in the distance field: a contour running against its format's direction, as a mirrored
  component of a composite glyph does, fills the same under the winding rule but read as a hole, so ☃ from Segoe UI
  Emoji grew wedges of fill and 😀 lost its mouth. Merged outline segments now all fill on the side their format
  fills on.
- A composite glyph whose components are placed by matching points was marked invalid and drawn empty; its components
  are placed on the points now.
- A CFF glyph's bounds took only its on-curve points and dropped the fraction, so a curve bulging past them was cut off
  at the edge of its atlas cell; they now take the control points too and are rounded outward.
- 32-bit deltas of an item variation store (long words) were read as 16-bit ones.

### Changed

- `Adamantium.Mathematics` and `Adamantium.Fonts` build for net10.0 alone; source generators load neither.
  `FontWeight`, `FontStretch`, `FontFeature` and the registry of OpenType features (`FeatureInfos`, `FeatureInfo`,
  `FeatureRegistration`), which markup is checked against at build, are in `Adamantium.Core` now
  (`Adamantium.Core.Fonts`, `Adamantium.Core.Fonts.Shaping`). The parsers of `Color`, `Thickness` and `Vector2` are in
  `Adamantium.Mathematics.TypeParsing`, named by `[TypeParser]` on their types as other types name theirs:
  `Adamantium.Core` no longer references the mathematics, which references it instead. `Adamantium.Fonts` takes no
  packages: WOFF and WOFF2 fonts are decompressed by the .NET runtime.
- Effects of the same data share the shaders of each pass: they are made once on the main device
  (`MainGraphicsDevice.EffectPassShaders`), for every render device, and destroyed once the last effect holding them is
  disposed or reloaded and no frame in flight can still use them. A second effect of the data costs no shader work - a
  `BrushEffect` of the UI was made in about 11 ms, its 90 shaders read again from the binary cache, and is now made in
  0.2 ms - and effects of the same data drawn one after another bind their shaders once. The compiler places each
  pass's resources (`EffectData.Pass.Resources`, `PushDataSize`, `DescriptorCount`): where the descriptor heap reads
  them in the push data, and how descriptor sets push them; and lists a pass's stages in pipeline order. Effect data
  is format version 4: an effect compiled before is refused with "Rebuild the effects" and compiled again on the next
  build. `IGraphicsDeviceFactory.CreateEffectPassShaderCache` makes the cache.
- A buffer larger than a shared block of device memory gives its memory back to the driver once it is freed and no
  frame in flight can still read it; shared blocks stay for reuse until the device goes.
- The engine's effects take typed pointers, `T* X;`, for what they read and write by address, and their parameters
  are named for what they point at: `BasicEffect` `Instances` and `Joints`; `FontEffect` `GlyphInstances`, `Transforms`,
  `PaintPrograms` and `PaintStops`; Forward+ `Lights`, `ClusterCounts`, `ClusterLights`, `Instances` and `Palettes` -
  each in place of the `...Address` it was.
- Effects have no pool. An effect holds the data it was made from (`Effect.Data`, in place of `RawEffectData`) and each
  pass its own shaders from it; `Effect(device, data)` and the generated effects take no pool. The main device's
  `ShaderHotReload` holds the live effects (`Add`, `Remove`, `GetEffects` of a device, `DisposeEffects`): a render
  device disposes those made on it as it goes, the main device the rest.
- The effects framework is written anew. An effect's techniques, a technique's passes, its parameters and constant
  buffers are each an `EffectCollection<T>`, found by index or name and walked without allocating. An effect declares a
  pass's constant buffer and the values in it itself (`Effect.DeclareConstantBuffer`); a constant buffer is its bytes
  and its declaration. A value array's elements are written and read at the stride the shaders declare, not at one
  rounded up to 16 bytes.
- `Effect.Clone` makes a new effect of the same data and copies the values and bound resources into it
  (`Effect.CopyValuesFrom`); a generated effect's `Clone` returns its own class. The effect generator also refuses a
  parameter or pass named as a member the generated class inherits.
- The effects compiler takes two resources of a pass in one binding of one set for an error, naming them; the
  reflection no longer moves one of them to a free binding in the SPIR-V (`Adamantium.Vulkan.Spirv` 2.0.0).
- Effect data (format version 3, effects are compiled again on the next build) is cut to what Vulkan has. A pass holds
  its stages as a list of `ShaderLink { Stage, Index }` - `EffectStage`, every stage the language describes - and
  its hit groups; a shader its entry point, stage, level, SPIR-V, constant buffers and resources, and a hull shader
  the size of the patch it reads. A resource is bound
  by `Set` and `Binding`, takes `Count` descriptors (0 for an array of no length, no limit of 255 any more) and has an
  `Access`: read only, written only or both, from SPIR-V's `NonWritable`/`NonReadable`. `EffectParameterType` names
  scalars and Vulkan's descriptor kinds - uniform, storage and texel buffers, sampled and storage images, samplers,
  acceleration structures; `EffectParameterClass.Pointer` is a `T*` uniform. The compiler compiles every stage, mesh
  and ray tracing included, and Slang's errors in them are said as in any other.
- A pass's stages are the device's to run: hull and domain shaders need the `tessellationShader` feature, task and
  mesh shaders `VK_EXT_mesh_shader`, each enabled when the device has it (`GraphicsAdapter.SupportsTessellationShaders`,
  `SupportsMeshShaders`, `SupportsTaskShaders`; `MainGraphicsDevice.IsTessellationEnabled`, `IsMeshShaderEnabled`,
  `IsTaskShaderEnabled`). A pass the device cannot run says so when it is applied, and so do ray-tracing passes; the
  effect's other passes run, and the refused pass's parameters are there to set. Every graphics pass binds the stages
  the device enabled and it has none for empty, in pipeline order whatever order the file names them in.
  `EffectParameter.SetResource(index, ...)` binds from that index, not from the first element.
- Uniform buffers, storage buffers, sampled images and samplers are bound by their kind: a read-only and a read-write
  storage buffer are one kind, as in Vulkan; on the descriptor heap the read-only one maps as such. A resource of
  another kind refuses its pass when applied.
- The effect compiler's types are in `Adamantium.EffectsCompiler`, the files at the project's root.
- An effect's `technique` and `pass` blocks are read by the effect compiler's own reader: comments and strings
  aware, errors at the line and column they stand on, the blocks blanked to spaces so Slang's messages keep the file's
  lines. A pass names its stages - `VertexShader`, `HullShader`, `DomainShader`, `PixelShader`; `TaskShader`,
  `MeshShader`, `PixelShader`; `ComputeShader`; `RayGenerationShader`, `MissShader`, `CallableShader` and `HitGroup`
  blocks of `ClosestHit`, `AnyHit`, `Intersection` - and nothing else: an unknown key, a stage of
  another kind of pass, a hull shader without a domain shader, two techniques or passes of one name or both without
  one, a file with no technique is an error. Every stage is compiled, and what Slang finds wrong in it is said. Slang's
  errors are said once each, where they stand in the
  file or its includes; a missing entry point at the stage that names it. A macro given to the compiler is a name
  with a value of one line and no comment. Every shader is compiled to SPIR-V 1.6.
- A skin bends with every joint it has, not the first 128: on the GPU, in picking and in its bounds, and a glTF skin
  of more joints is no longer told about. A joint index past the skin's joints leaves its vertex where it is.
- The engine's effects are written in Slang: entry points carry `[shader]`, and resources take Slang's bindings, with
  no `register()`; an effect is named after its file, so `FontEffect`'s `Name` is `FontEffect`. The editor's gizmos read their instances and a skin's joints through pointers from the frame's upload
  pages, so a draw takes any number of instances: `BasicEffect` has `InstancesAddress`, `JointsAddress`, `JointCount`,
  `LineWidth`, `Ring` and `FrontHalfOnly` for `InstanceWorld`, `InstanceColor`, `InstanceLine`, `InstanceRing` and
  `Joints`, and `GridEffect` has `Placement` for `InstanceWorld`.
- `SpriteBatch` draws a batch in one draw whatever textures its sprites have: each sprite carries its texture's place
  in the device's texture array of no length (`IDescriptorManager.TextureIndex`), on the descriptor heap and on
  descriptor sets alike, and the sprite effect reads it from `spriteTextures[]` - a custom effect does the same in
  place of a `Texture` parameter. A batch holds as many sprites as it is given, growing past its first capacity, and
  sorts them whole; sprites of one depth keep the order they were given. A sprite's depth is its layer depth as it
  is, 0 nearest, so under the depth test the nearest stays in front. A texture disposed before the batch ends is
  drawn as the device's stand-in (`IDescriptorManager.FallbackTextureIndex`). A batch's effect is disposed with it.
  `Begin(screenSize, sortMode, samplerState, effect)` takes a sampler always.
- Text flowing round `TextLayout.Exclusions` goes on right where the first exclusion across a row it cannot use ends,
  not a whole row further down: under a picture as wide as the frame the next line stands at the picture's bottom, as
  Word sets it.
- TGA is read and written by a new codec, written from Truevision TGA 2.0. It reads color-mapped, true-color and
  gray images, plain or run-length encoded - packets running on from row to row too - of 8, 15, 16, 24 and 32 bits,
  maps of 15 to 32 bits starting at any entry, in all four orientations, as RGBA8. Alpha is the bits the image
  descriptor counts, and the fourth byte of 32-bit pixels and the second of 16-bit gray even where it counts none, as
  writers leave it and Pillow and ImageMagick read it; the extension area says when they are not alpha or are
  premultiplied, which is divided out. Indices the color map has no entry for are opaque black. Against Pillow 12 and
  ImageMagick 7 the pixels are the same where they read the file as TGA 2.0 does; a file shorter than its pixels is
  refused, and so are pixels more than the data could hold. The encoder writes gray, 24-bit or 32-bit pixels by what
  the picture needs, top row first, run-length encoded unless asked not to, with the extension area and footer;
  `TgaImage.Read`, `TgaImage.Id` and the static `TgaEncoder.Encode(image, output, compress)` are its API. A
  4984×3858 picture reads in 0.1 s, 0.8 of Pillow's time.
- GIF is read and written by a new codec, written from GIF89a. It reads GIF87a and GIF89a: global and local color
  tables, interlacing, transparency, every disposal - cleared to transparent, as browsers do - frames placed anywhere
  and cut to the screen, the loop count (`NumberOfReplays`, once when the file does not say) and the comments; frames
  are decoded when asked for and composed on two kept canvases and one saved every 16 frames, as APNG's are. Against
  Pillow 12 and ffmpeg 8 every frame of the test files comes out the same, pixel for pixel, and LZW of every code
  size, 1 to 11, as giflib reads it. A file cut off, or with a block of no kind GIF has, keeps the frames before, and
  a frame its data ends early; frames are read a row at a time, so one of any size costs only what the screen shows
  and its data holds, and data that could not fill the screen is refused before anything is made. Read warm, every
  frame of the test files takes 0.2 to 1.0 of Pillow's time; the first read in a process takes longer while its code
  is compiled. The encoder writes each frame as the rectangle that changed, the pixels in it that did not
  transparent, in a table of its own - its colors exactly when 256 or fewer, else by `ColorQuantizer` - with the
  source's delays and play count; Pillow and ffmpeg read what it writes as it was. A 960×540 animation panning over
  a photograph takes 40 to 80 ms a frame undithered and 55 to 145 dithered, by the photograph - Pillow 87 for the
  busier one; one of few colors far less.
- Text runs on both sides of an exclusion in a frame, as Word's square wrapping does: a row's line goes on in the next
  stretch past the exclusion, in the order its paragraph reads - right of it, or left of it in a paragraph running
  right to left - before the next row starts. It used to run in the widest stretch only, so a picture moved past the
  middle of a column took the text from one side to the other. A line starts a row of its own when it opens a
  paragraph with room above, reads the other way, stands on a baseline grid elsewhere, would not fit the frame, or
  is one a drop cap stands beside; a stretch narrower than two ems is still left empty. `MaxLines` counts the lines,
  so two beside each other are two. `HitTest` finds the line of a row nearest the point, the caret of an empty line
  stands in its own stretch, and `TextLayout.GetLineBeyond` gives the line one row down or up keeping the caret's
  column - into the next frame, the row's first line.
- `DataBuffer` checks every read and write against its size, throwing `ArgumentOutOfRangeException` past it, and a
  buffer of its own starts zeroed. `NamedObject` and `ViewportF` are rewritten; `ViewportF` is a record struct - its
  equality exact, `Project` and `Unproject` taking the three matrices.
- `Utilities`, `DisposeCollector`, `DisposableBase`, `DisposableObject` and `DataPointer` are rewritten.
  `DisposeCollector` frees everything it holds even when something throws as it is freed, then throws what was
  thrown - one exception as it is, several in an `AggregateException`.
  `DisposableBase` is disposed once however many threads ask, `IsDisposed` from the moment disposing starts;
  `DisposableObject` locks a field of its own, no longer itself. `RemoveAndDispose(ref item)` leaves the reference
  null, as it said it did. `DataPointer` is a record struct; `Utilities.ClearMemory` takes the address, not a
  reference to it, and a size past 2 GB.
- `Image` is rewritten: one block of memory, layer by layer, level by level and depth slice by slice, each a
  `PixelBuffer` of whole texel blocks tightly packed - the order DDS keeps them in. `Description` is a property,
  `PixelBuffer` a list (`GetPixelBuffer(layer or slice, level)` and `GetPixelBuffer(layer, slice, level)` stand), and
  a picture of frames loaded is an image of a layer a frame - saved, the layers as they are now, timed as the frames
  were. `PixelBuffer` checks its strides and `GetPixel`, `SetPixel`, `GetPixels` and `SetPixels` against its size
  and takes unmanaged types; an image with a part over 2 GB is refused. `ImageDescription` and `MipMapCount` are
  record structs - the description's `RowStride` and `TotalSizeInBytes` counting whole blocks - and `SurfaceFormat` a
  readonly struct with the formats the engine names.
- DDS is read and written by a new codec, written from Microsoft's "Programming Guide for DDS". It reads 1D, 2D,
  cube and volume textures and arrays of them with every mip level: in the DX10 header the DXGI formats Vulkan has a
  color format for - depth ones as the color they hold, those with stencil refused - ASTC among them; in the older
  one its compressed formats (DXT1 to DXT5, ATI1 and ATI2, BC4 and BC5), the 16-bit and float ones it numbers, and
  masked ones - RGBA of 8 bits in either order, 16-16, 5-6-5 and 1-5-5-5 as they are, 10-10-10-2 as they are with
  its masks taken the other way round, as D3DX wrote them and Direct3D's loaders read them, 24-bit color, luminance
  with or without alpha, alpha alone, 4-4-4-4, 3-3-2 with or without alpha, 4-4 luminance-alpha and 8-bit paletted
  with or without alpha, which few devices sample from or none has, widened to RGBA. The 4:2:2 formats - DXGI's
  R8G8_B8G8 and G8R8_G8B8, the older GRGB, RGBG (as Microsoft maps Direct3D 9's formats to them) and YUY2 - read in
  the order of Vulkan's B8G8R8G8 and G8B8G8R8. A format whose alpha the file leaves out reads opaque. The encoder
  writes every layer, level and slice an image has, of any size in bytes, in the older header when its format is
  there, else in DX10 - 10-10-10-2 with red high, which DXGI has no code for, only as one 2D texture, cube or
  volume, the older header's; Pillow 12 reads what it writes in the formats it knows. A file shorter than its texels, of
  sizes that overflow, of more than Direct3D's 2048 layers or slices, or of a 1D picture of rows or a cube of unequal
  sides is refused before anything its size is made; one too large for an array once widened is refused as not
  supported, and an image of more layers or slices is not written.
- BMP is read and written by a new codec, written from Microsoft's "Bitmap Storage". It reads BITMAPCOREHEADER,
  BITMAPINFOHEADER and its V2 to V5: 1, 2, 4 and 8 bits through a color table, run-length encoded (RLE4, RLE8) or
  not; 16 and 32 bits by their channel masks, 5-5-5 and 8-8-8 when they give none; 24 bits; rows bottom up or top
  down - as RGBA8, as Pillow 12 reads them. Pixels a run-length file never sets are the table's first color; a 32-bit
  file whose alpha is nothing but zeros is opaque. A file shorter than its pixels is refused before they are made, a
  run-length one past `BitmapLoader.MaxPixels`. The encoder writes 24-bit BGR when every pixel is opaque,
  else 32 bits with a BITMAPV4HEADER whose masks say where alpha is; `BmpImage.Read` and the static
  `BmpEncoder.Encode(image, output)` are its API.
- PNG is read and written by a new codec, written from PNG's third edition and APNG. It reads every color type and
  depth, Adam7, palettes and tRNS, and APNG's frames composed as APNG says (dispose None, Background and Previous;
  blend Source and Over), decoded when first asked for; a 16-bit file comes out RGBA16 now, no longer cut to 8 bits.
  Chunks are checked by their CRC - a broken critical one refuses the file, a broken ancillary one is passed over - an
  unknown critical chunk refuses it with `NotSupportedException`, a broken animation reads as its still picture, and
  data that could not inflate to the image's size is refused before it is inflated - the still picture's too when it
  is not shown, so a few bytes cannot make an animation's canvas any size. `PngImage.Metadata` holds bKGD, gAMA,
  cHRM, sRGB, iCCP, the texts, pHYs, tIME and eXIf, and `PngEncoder.Encode` writes them back with the pixels: the
  smallest color type that holds every pixel and the background - a palette up to 256 colors - each row by the
  filter that sums least, frames as APNG, asked for one at a time; iCCP rather than sRGB when there are both, as PNG
  ranks them; two-channel pixels and metadata PNG cannot hold are refused. A keyword's stray spaces, text past a NUL
  and other slips of writers are read as PNG would have them, so what is read can be written again. Animations keep
  two canvases - two readers asking for frames in turn each go on from theirs - and one every 16 frames, up to 64 MB,
  to go back to; a frame that fails to decode leaves the canvas as it was. A file's texts and profile are cut off at
  64 MB inflated all together, broken ones counted. Against PngSuite all 163 pictures and the broken files come out
  as PNG says. On 5400×2700 pictures reading takes 0.4 to 0.85 of the old codec's time and 0.9 to 1.8 of Pillow's
  (libpng with zlib); writing a half to a sixth of the old codec's, as long for 16-bit gray.
- JPEG is decoded and encoded by a new codec, written from ITU-T T.81, JFIF (T.871) and Adobe's APP14. It reads
  baseline, extended and progressive frames of 8 and 12 bits, restart intervals and DNL, gray, YCbCr, RGB, CMYK and
  YCCK, any subsampling whose factors divide the largest - halves stretched smoothly between the samples' centers -
  and streams without Huffman tables (by T.81's K.3 ones). A picture comes out RGBA8, RGBA16 for 12 bits. The scans of
  a file with restart intervals are decoded side by side, a progressive file's inverse DCT and the colors by rows on
  the thread pool; the eighth-scale `DecodePreview` works for any file and size, and skips a progressive file's AC
  scans. TIFF reads JPEG of planes, of four samples and of 12 bits, its JPEGTables read once. Against libjpeg-turbo
  (Pillow 13, which decodes on one thread) a 10800×5400 photograph decodes in 1.3 times its time, in a fifth of it
  with restart intervals, measured on 48 cores; the encoder writes baseline JFIF with Huffman tables made for the
  picture (K.2).
- The packages' icons are the redrawn Adamantium family: the mark and the package's letters drawn in Inter.
- `MainGraphicsDevice.DescriptorHeapManager` and `GraphicsDevice.DescriptorHeapManager` are `DescriptorManager`, an
  `IDescriptorManager`; `IGraphicsDeviceFactory.CreateDescriptorHeapManager` is `CreateDescriptorManager(device,
  model)`; `IDescriptorHeapManager.BindDescriptorHeaps` is `IDescriptorManager.BindTo`. The heap's extension and
  feature are enabled only on the heap path.
- `Adamantium.Vulkan` 1.1.0 (`ShaderStageFlagBits.All` is `0x7FFFFFFF`, `AllGraphics` is there); `Adamantium.Graphics.Core`
  depends on `Adamantium.Vulkan.ShaderObjectLayer` 1.0.1, so every application carries the layer.
- `Adamantium.Vulkan` 1.2.0. On Apple's platforms - macOS, iOS, tvOS - the swapchain's surface is a Metal one
  (`VK_EXT_metal_surface`) on a `CAMetalLayer`, which `PresentationParameters.OutputHandle` is there, instead of
  MoltenVK's own `NSView` surface (`VK_MVK_macos_surface`), deprecated by it.
- `EffectPass` is abstract: the device makes a `HeapEffectPass` or a `SetsEffectPass`.
- With graphics debug on, the validation layer sits above the other layers, so it checks the engine's own calls.
- `meshTextures[]` of `ForwardPlusShading.fxh` is in a set of its own (`register(t0, space1)`).
- The instance and device extension lists of `VulkanInstance` and `MainGraphicsDevice` moved into `GraphicsSettings`.
- `GraphicsDevice.LastFenceWaitMs` - the wait for the GPU at the start of a frame - is timed only while a handle
  from the new `GraphicsDevice.MeasureFenceWait()` is held, on every device of the process, and is 0 otherwise. It
  used to be timed on every frame of every device, whether anything read it or not.
- `GlyphIntegrityProbe` is off by default: a tool for a hunt, it ran on every glyph upload, hashed the pixels and wrote
  `glyph-probe.log` beside every application - thousands of false "moved" and "rewritten" lines where an atlas holds
  several fonts or sizes. `GlyphIntegrityProbe.IsEnabled = true` switches it on while chasing glyph corruption.
- A drop cap is sized by a letter that stands for its script, not always by its own, much as CSS's `initial-letter`
  sizes it by the script's alignment: a letter with a capital by that capital, Hebrew by ה, Arabic by alef, Han and
  kana by 国, Hangul by 한, any other - Georgian too - by itself; an accented letter by its letter, composed or not -
  from the top of that letter in the first line, in the text's font, to the last line's baseline; where the cap's font
  or the text's lacks that letter, by its own. A lowercase letter or one of short ink - a Hebrew נ, an Arabic ب - is
  as large as the rest of its script, where it was blown up until its own ink spanned the lines; an accent or an
  ascender rises above the first line as it does in the text.
- `PosixInterop` carries the libc calls the Linux platform needs - `Read`, `Write`, `Poll` (with `PosixPollFd`),
  `Pipe2`, `EventFd`, `MemFdCreate`, `Mmap`, `Munmap` - and `Adamantium.Wayland` maps its keymap through it, so it
  references `Adamantium.Core`. The libwayland-client and xkbcommon imports are named in C# (`DisplayConnect`,
  `StateKeyGetUtf8`), the C names in `EntryPoint`.
- The engine finds Vulkan by itself on every platform: `VulkanInstance` registers `Adamantium.Vulkan.Loader`'s
  resolver (`VulkanDllMap.Register`) before its first call, so `vulkan-1` is `libvulkan.so.1` on Linux without an
  application doing it first - the engine's own graphics tests among them. `Adamantium.Graphics.Core` depends on
  `Adamantium.Vulkan.Loader` for it.
- `EntityIcons` gives an output's own camera no icon in any output: with several outputs, each showed the others'
  cameras as cameras of the scene.
- An output's own camera leaves the scene with the output; it stayed behind as a camera of the scene.
- Effects compile on Linux. `Adamantium.EffectsCompiler`, `Adamantium.Engine.Generators` and `Adamantium.FX` are on
  `Adamantium.Vulkan.Slang` and `Adamantium.Vulkan.Spirv` 1.1.0 - Slang 2026.17 and SPIRV-Cross from Vulkan SDK
  1.4.363.0, each platform's libraries under `runtimes/<rid>/native`. The generator loads them with `dlopen` on Linux
  and `LoadLibraryW` on Windows, takes the Slang runtime from that folder of the package, and points the bindings'
  imports at what it loaded. The libraries it extracts come from the packages at build time instead of copies kept in
  the repository; where the compiler gives the generator no location, they go to a folder of its version in the temp
  directory.
- `Skin.FillPalette` fills a `Span<Matrix4x4F>`, so a palette can be written straight into a frame's buffer of them.
  `RenderGeometry` is made by `MeshGeometryCache` from a mesh's content and never changes; `EnsureUpToDate` is gone.
- `GraphicsDevice` draws, vertex and index buffer bindings, dispatches and buffer barriers go to
  `CurrentCommandBuffer` - the recording in progress, if any - not straight to the frame's command buffer.
  `EffectPass` binds shaders and sends push data through the device (`BindShaders(stages, shaders)`, `PushData`).
  While a recording or a fingerprint is in progress, what cannot go into one throws `InvalidOperationException`:
  dispatches, barriers, beginning or suspending rendering, `EndDraw` - and `CurrentCommandBuffer` itself while a
  fingerprint is taken.
- `Colors.Get` reads with `Colors.TryParse`: `#RGB` and `#ARGB` double each digit, as WPF reads them (`#F80` was
  0x000F80, a dark blue, not orange); spaces around the color are allowed. Whatever is no color throws
  `FormatException`: a "#" with another count of digits or a character that is no hex digit (more than eight digits
  threw `OverflowException`, fewer were read as a number), an unknown name (`KeyNotFoundException`), null
  (`NullReferenceException`).
- Text shaping (`TextShaper.Shape`) rents its working memory from `ArrayPool` for the one shaping and gives it back,
  instead of keeping a buffer and a normalizer per thread (`[ThreadStatic]`); a run of more than 16384 characters
  shapes in memory of its own, let go after it, so the pool keeps no buffer that large. As fast as before; a run
  allocates two small objects more. `Adamantium.Fonts` names its `System.Buffers` dependency.
- `Skin.Joints` and `Skin.InverseBindMatrices` are the lists of its `Skeleton`, shared by every part of the mesh
  bending with it: changing them changes them for all those parts.
- `AdamantiumCollection.AsSpan` reads without the lock, for a walk each frame: while another thread changes the
  collection, an item may be seen twice or missed and a slot may read default.
- The packages that hold code or data derived from third-party work - `Adamantium.Core`, `Adamantium.Mathematics`,
  `Adamantium.Imaging`, `Adamantium.Fonts`, `Adamantium.Engine.Compiler` - carry `NOTICE` and `THIRD-PARTY-NOTICES.md`;
  the other packages have none in them and carry neither.
- A baked mesh is format version 2 (its extra data, attributes, morph targets and material variants): a `.aemf` baked
  before is refused and baked again.
- A glTF light's `Color` is its color alone, its brightness in `Intensity`; it was the two multiplied.
- A content project keeps `ACB005` - what an import does not read, such as morph targets - a warning where warnings are
  errors: it tells about the asset, not a fault of the build.
- `Adamantium.Graphics.Core.Models.CameraProjectionType.Pespective` is renamed `Perspective`. Saved `.aemf` files are
  unaffected: the value is stored as a number.
- `Mesh.IsTangetBinormalsPresent` is renamed `IsTangentBinormalsPresent`.
- `ICloneableComponent` has a new member, `CloneReferences`: a component that implements it without deriving from
  `Component` implements that too.
- `CameraBase.ZoomStep` is 1.08 a wheel notch, in either projection; 1.15 jumped.
- A vertical alignment places whole lines, as WinUI's: `Center` centers the lines' box in the area and `Bottom` puts
  the last line's bottom on its edge, where they placed the ascent above the baseline and the last baseline. A text
  whose height is its own (no height given) is not moved at all: it was moved by half its descent under `Center` and by
  its descent under `Bottom`, so its letters, selection and backgrounds came out of its size - 56 pixels for Poppins at
  320.
- Laying text out again allocates about half of what it did, and takes less time: the shaper and its normalizer keep
  their buffers between runs, and a layout keeps its list of shaped glyphs (for text of up to 16,384 characters). A
  paragraph of 1300 characters in Segoe UI laid out again went from 543 KB to 237 KB, in Source Sans 3 from 663 KB to
  357 KB; what is left is mostly a `GlyphWordData` per glyph.
- An imported model is placed as its file has it, as Unity does: `EntityImportTemplate` puts the model's own origin
  where it is placed - the root's position is its position in the file plus the one given - and the root scales and
  turns about that origin; the tools' handles sit there too. It moved the model's center there instead, through a
  pivot that shifted the position: the F-15, its center 122 centimeters above its origin, stood at y = 122, and setting
  its y to 0 sent it up. Each part below the root still turns about its own center.
- A universe's output camera opens at (0, -3, -20), three metres above the ground, looking at the scene's center.
- The observable properties of `Entity` and of the components - `Light`, `Transform`, `CameraBase`, `Collider`,
  `CapsuleCollider`, `ActivatableComponent`, `MeshMetadata` - are generated by the MVVM generator (`[Bindable]`), their
  side effects in `OnXChanged` hooks and `Transform.Scale` raised through `[Affects]`; they behave as before.
  `Entity.Owner`, `Component.Owner` and `CameraBase.TanFov` stay written by hand: the first two need the owner they
  replace, the last has a protected setter.
- `PropertyChangedBase` is gone: what notifies is a `[ViewModel]` - `Entity`, `MeshMetadata`, `EntityService`,
  `GraphicsDevice`, `GraphicsPresenter`, `UniverseOutput`, `GraphicsDeviceService`, `ConversionConfig`,
  `ModelConverter` - with the MVVM generator now in Graphics.Core, Graphics, Multiverse and the compiler. What never
  raised a change - `NamedObject`, `EntityServiceManager`, `Universe`, `UniverseOutputDescription` - no longer
  pretends to, and `IBuffer`/`ITexture` lose a `PropertyChanged` nothing raised. `ConversionConfig` marks itself
  changed on any setting it is given, once the setting really changes.
- `AdamantiumViewModel` implements `INotifyPropertyChanged` itself instead of deriving from `PropertyChangedBase`; it
  keeps `SetProperty` and the virtual `RaisePropertyChanged`. The MVVM generator knows a base that raises the change by
  what it can do - a `RaisePropertyChanged(string)` the class can call - not by the name `PropertyChangedBase`, so a
  `[Bindable]` field under any notifying base is generated.
- `IGraphicsDevice` has `GraphicsQueueLock`, the lock every submit and present on its queue goes under; an implementer
  adds it.

- `EntityManager.RemoveEntity` takes a root entity out of `RootEntities` when the frame ends, no longer at once. Code that
  needs it gone sooner calls `EntityWorld.ForceUpdate`.

- `FileSystemContentResolver` takes a relative asset path from the application's folder, not from the working
  directory, and `CookedContentResolver` looks for a cooked model beside the application under the model's own path
  (`Content/Game/Models/ship.dae` → `Content/Game/Models/ship.dae.aemf`), where a content project puts it. An app
  started from another folder finds its content all the same. **Migrating:** cooked models copied by hand into the
  application's `Content/` folder under their source path (`Content/Models/ship.dae.aemf` for `Models/ship.dae`) are
  no longer found there; the source is loaded instead. Move the assets into a content project.

- Text lays out two to three times faster and allocates a third as much, the result unchanged: a lookup skips the
  glyphs none of its subtables covers, a contextual rule allocates only for a glyph it covers, a font no longer locks
  to look up a glyph it has, the Latin range of the Unicode tables is read from an array, and centered, right-aligned
  and justified text groups its glyphs by line once instead of scanning them all for every line.

- `TextLayout` is split by stage, its public API unchanged: shaping (`GlyphRunShaper`), composing lines
  (`LineComposer`), the glyph quads (`GlyphQuadBuilder`) and caret, hit and selection queries (`TextNavigator`), over
  one immutable result per layout (`LaidOutText`) and a snapshot of the settings it was laid out with. Laid out the
  same, to the bit, across 14,688 combinations of text, script, wrapping, trimming and alignment and every special
  setting. `GetTextData` before the first layout returns no glyphs instead of throwing.

- Justified text keeps its spaces within InDesign's word spacing by default: 80% to 133% of the space. Before,
  spaces could squeeze to two thirds and stretch without limit; the paragraph composer weighed them at two thirds to
  one and a half.
- `HorizontalTextAlignment.Left` puts a line at its start and `Right` at its end, as WPF does: a right-to-left
  paragraph aligned `Left` stands on the right edge. Left-to-right text is aligned as before.
- `TextLayout.HitTest` beside a line stands on the line's edge on screen: on the side its paragraph ends, before the
  newline or at the end of the text (`IsTrailing` false); on the other, the outer edge of the outermost grapheme. The
  caret stop at the end of text that mixes directions stands where its last paragraph ends: the right edge of a
  left-to-right one, the left of a right-to-left one; so does the empty line after a right-to-left paragraph's
  newline. `GetLine` and `LineCount` no longer walk every caret stop.
- An instance of a variable font makes its glyphs as they are asked for, instead of all of them: Roboto Flex's
  instance took 1.2 ms and 720 KB (and set off collections that held frames for 15 ms), and takes 0.03 ms and 21 KB.
- A glyph's distance field takes about a third of the work it did, with the same bytes (checked on 7498 glyphs of ten
  fonts, TrueType, CFF, variable, CJK and emoji): one grid searches every channel at once, by squared distance, from
  the bounds the texel before it left; a texel far enough from the outline that its colors are replaced by the true
  distance anyway is not searched for them; and the rows of a glyph are computed in bands on several threads. One
  glyph went from 6-10 ms to 1.3-1.6 ms, a parallel atlas of Segoe UI from 2.1 s to 0.9 s.
- `Adamantium.EffectsCompiler`, `Adamantium.Engine.Generators` and `Adamantium.FX` are on `Adamantium.Vulkan.Slang`
  1.0.12: the same Slang compiler, in the package that also ships `slangd`, its language server.
- Glyphs reach the atlas one by one as they are rasterized, the heaviest started first and spread over the workers, so
  a few complex glyphs no longer hold back the rest of a batch: the sandbox's text page went from 8.6 s of glyph
  generation to 1.1 s. The distance field of a glyph looks only at the outline segments near each texel (a grid of
  them) instead of all of them, with the same result; ☃ from Segoe UI Emoji took 4.8 s and takes 0.3 s.
- `TypefaceStore` parses each file once without holding one lock for all of them: files parse in parallel, and a font
  already loaded is found while another parses.
- The font atlas grows instead of filling up: it starts with two layers (8 MB of video memory instead of 32) and doubles
  as glyphs need room, up to the device's limit, where it used to overwrite its last layer past some 1800 glyphs.
  `FontAtlas.LayerCount` and `LayerCapacity`; `AtlasLayerCount` is gone, `InitialLayerCount` takes its place.
- Lines are as tall as the font says: ascent plus descent plus line gap (`IFont.LineAscent`, `LineDescent`, `LineGap`),
  the baseline half the gap and the ascent below the line's top, taken as HarfBuzz and the browsers take them (the
  typographic metrics when the font sets USE_TYPO_METRICS, the horizontal header's otherwise). Before, the baseline
  stood almost at the bottom of the line and descenders hung below it, so a background or a selection stopped at the
  baseline; and a font with no line gap was laid out with a gap as tall as the font - Source Sans and Cascadia Code at
  more than twice their size. Segoe UI's lines are now 1.33 of its size apart, as on Windows, instead of 1.13.
- `TextLayout` with `TextWrapping.WrapByWords` wraps where Unicode allows a line to end, not only at spaces: after a
  hyphen, between ideographs, never before a closing parenthesis or a comma, never at a no-break space.
- `TextLayout` shapes its text with `TextShaper`: the font's ligatures, contextual alternates, mark positioning and
  kerning apply, and a character the font lacks draws its `.notdef` box instead of nothing. A glyph is no longer one
  character: `GetTextData` lists glyphs with the UTF-16 offset of their first character, and caret positions come from
  `GetCaretStops`.
- `IFont.FeatureCatalog` replaces `FeatureService`: the features a font offers per script (`FontScript`) and language
  system (`FontLanguage`), each with its table and parameters, as the font lists them. Before, the features of every
  script were merged by name and the first script's won, so a Serbian or Bulgarian `locl` was lost behind the Latin one.
  The catalog is read-only: which features apply belongs to the text being shaped (`ShapingOptions`), not to the font.
- Memory blocks in a small heap are a 64th of it, at least 4 MB, instead of an 8th: a process takes about half as much of
  the shared window as before (a designer preview 40 MB instead of 80).
- Running out of memory in every type a buffer allows reports the size, the memory type and the heap.
- Every new memory block is logged at the Debug level with its type, heap and the heap's total in blocks.

### Removed

- `FeatureInfo.DefaultFeatureState` and `FeatureState`: `TextShaper.DefaultFeatures` says which features shaping
  applies unasked.
- `EffectPool` and `EffectPoolEventArgs`, `Effect.Pool`, `IGraphicsDevice.DefaultEffectPool`, `AddEffectPool` and
  `RemoveEffectPool`, `GraphicsDevice.EffectPools`, `ShaderHotReload.Track` (an effect is held from its making), and
  `EffectPass.ShaderStages` with its unused shader modules.
- From the effects framework, what nothing used: `EffectParameterCollection`, `EffectTechniqueCollection`,
  `EffectPassCollection`, `EffectConstantBufferCollection`, `EffectConstantBufferKey` and `NamedObjectsCollection`;
  `EffectParameter.GetMatrix`, `GetMatrixArray`, `GetValueArray`, `SetRawValue` and `Index`;
  `EffectConstantBuffer.IsDirty`, `CheckForChanges`, `Parameters`, `Id` and its equality; `Effect.Initialized`,
  `Reloaded`, `OnApplyDelegate`, `OnApplyCallback`, `OnApply`, `Initialize`, `OnInitialized`, `Load`,
  `GetOrCreateConstantBuffer` and `DeclareValueParameter`; `IEffectResourceLinker.GetBoundResources` and
  `AddBoundResource`; `IEffectPass.UnApply`, which did nothing. A pass's stages hold its parameters themselves, not
  copies of their set and binding, and nothing builds the descriptor-set layout bindings nobody read.
- `EffectCompilerFlags`, the `flags` of `EffectCompiler.Compile` and `CompileFromFile`, and
  `EffectData.CompilerArguments.CompilerFlags`: Direct3D compiler flags Slang does not take.
- From effect data, what Direct3D 11 had and Vulkan does not: input signatures (`Signature`, `Semantic`), stream
  output, sub-passes, pass properties (`EffectPropertyCollection`, `PropertyKey`, `PropertyKeyCollection`,
  `EffectPass.PropertiesKey`), shader import and export, default values, `Shader.Hashcode` and `CompilerFlags`,
  `EffectShaderType`, the pipeline dictionary, interface parameter classes, the D3D parameter types and
  `EffectResourceType` with its D3D table, `ShareConstantBuffers` with the pool's shared constant buffers, the pool's
  unused shader-module cache (`GetOrCompileShader`), and the unused `EffectTechnique`, `ShaderLanguage` and
  `ShaderModel` enumerations.
- From the effect language: `EffectName` (an effect is named after its file), `Profile`, `GeometryShader`, the blend,
  rasterizer and depth state keys, and the rest of the FX syntax no effect used. The `sampler` spelling is no longer
  defined for Slang: write `SamplerState`. The device no longer asks for the geometry shader feature.
- `SceneData.Controller.MaxDrawnJoints` and `ForwardPlusRenderingProcessor.MaxJoints`: a skin has no cap on its joints.
- The `BasicEffect` passes nothing drew - the `Render` technique and `Basic`'s `Default`, `Textured`, `Lit` and
  `VertexColored` - with the `World`, `ShaderTexture` and `SampleType` parameters only they read.
- `SpriteSortMode.Texture`: a batch of any textures is one draw, so there is nothing to sort them for. With it
  `SpriteEffect.Texture`: the effect reads `SpriteTextures`, the array of no length.
- With the old `Image`: `PixelBufferArray`, `MipMapDescription`, `AnimatedImageDescription`, `FlipBufferOptions`
  and `PixelBuffer.FlipBuffer`, `Image.ImageLoadDelegate` and `ImageSaveDelegate`, `Image.GetMipMapDescription`,
  `ToDataBox`, `DefaultImage`, `ApplyPixelBuffer`, `New(description, byte[])` and `New(description, IRawBitmap)`,
  `PixelBuffer`'s `MipMapDescription`, `XOffset`, `YOffset` and the `GetPixels`/`SetPixels` overloads by offset and
  count; the `SurfaceFormat` classes beyond `R8`, `R8G8B8`, `R8G8B8A8`, `B8G8R8A8` and `R16G16B16A16`; the
  `makeACopy` of `Image.Load` and the file type of `Image.Load(Stream)`, which nothing read.
- With the old DDS codec: `DdsFlags`, `FourCC` and `ResourceOptionFlags`; `DdsHelper` and `BmpHelper` are static
  classes, `LoadFromMemory` and `SaveToStream` all they have.
- With the old TGA codec: `TgaDecoder`, `TgaConversionFlags`, `TgaEncoder.SaveToTgaStream` and `TgaImage`'s
  constructor and `PixelBuffer`.
- With the old GIF codec: `GifDecoder`, `GIFHelper` (`GifHelper` stands for it), `ScreenDescriptor`,
  `GifImageDescriptor`, `GraphicControlExtension`, `ApplicationExtension`, `DisposalMethod` (`GifDisposal` stands
  for it), the old `GifFrame` class, and `GifImage`'s `GlobalColorTable`, `CurrentFrame`, `ColorDepth`,
  `Descriptor`, `ApplicationExtension`, `AddFrame` and `DecodeFrame`; the whole `Adamantium.Imaging.PaletteQuantizer`
  namespace - its quantizers, ditherers, color caches, `ImageBuffer`, pixel types and `Extend` - which
  `ColorQuantizer` stands for.
- `DataBuffer.Create`, its constructor from a `DataPointer`, `GetRange` into an array, and `Set` from a pointer or
  part of an array; `ViewportF`'s `Project` and `Unproject` by reference to one matrix.
- `DataBox`; from `Utilities`: `ReadStream`, `IsEnum`, `IsMemoryAligned`, `GetCustomAttribute(s)`, `Join`,
  `ToLittleEndian`, `SwapEndianness`, `GetBytesWithReversedEndian`, `Dispose`, `GetBitsCount`, `ReverseByte`,
  `IsTypeInheritFrom`, `ToVector2`, `ToVector3` and `Compare` of `ICollection` and of `IEnumerator` - the .NET ones
  stand for them; `Compare` of `IEnumerable` stays.
- With the old PNG codec: `PngDecoder`, `PngCompressor`, `PngDecoderSettings`, `PngEncoderSettings`,
  `PNGStreamReader`, `TXTItem`, `ITextItem` (`PngText` stands for both), `Adam7`, `Crc32`, `Adler32`, `BitHelper`,
  `PngDecodeException` and `PngEncoderException` - broken files fail with `InvalidDataException` and
  `NotSupportedException` - and the enums `PngColorType`, `InterlaceMethod`, `FilterStrategy`, `CompressionLevel`,
  `RenderingIntent` (`PngRenderingIntent` stands for it) and `Unit`.
- With the old JPEG codec: the `Adamantium.Imaging.Jpeg.Decoder` and `Encoder` namespaces (`JpegDecoder` is in
  `Adamantium.Imaging.Jpeg`), `PreviewNotAvailableException` (every file previews), `JpegDecoder.BlockUpsamplingMode`
  and `ExtractHeaders`, `JpegHeader`, the JPEG filters and resizer, `ComponentsBuffer`, `ComponentBufferType`,
  `RawBitmapExtension.ToComponentsBuffer` and `PixelBuffer.GetComponents`.
- `ADAM_FRAMES_IN_FLIGHT`: the frames in flight are `GraphicsSettings.FramesInFlight`.
- `AnimationComponent.StartingKeyFrame` and `EndingKeyFrame`: nothing read what was set in them, and a key's number
  is no range of a clip whose channels key at their own times. A clip is chosen by name and played by speed, pause and
  `Option`.
- `CameraBase.WheelVelocity`: a wheel notch zooms by `ZoomStep` now, near or far, not a distance of travel.
- `Camera.OrthoScaleFactor`, a field only the orthographic projection divided its size by; `CameraBase.OrthoScale`
  zooms it now. `CameraBase.IsometricProjection` and `Camera.BuildIsometricProjection`: an oblique matrix that flattened
  depth to nothing and had no near or far plane. `CameraProjectionType.Isometric`: the isometric angle is
  `CameraBase.IsIsometric` now, apart from the projection; an orthographic projection with it is the isometric view.
- `MeshMetadata` and `MeshData.Metadata` / `MetadataChanged`, with the primitive templates' `FillMetadata`: one bag of
  every shape's numbers that the templates filled and nothing read. A shape's parameters are its `ProceduralShape` now.
- `FontRenderer.UseDirectTextDraw`, `DrawLayout`, `SetState`, `RestoreState` and `RenderScale`: the old way of drawing a
  text block into a target of its own and laying that over the element. Nothing turned it on; text is drawn straight
  into the pass (`DrawLayoutDirect`) or in the batch (`DrawBatch`).
- `TextLayout.VertexBuffer`, `EnsureVertexBuffer`, `TryBakeWorldGlyphs` and `EffectPadding`: nothing used them - a
  per-block draw uploads the frozen glyph run into a buffer of its own, and the batch packs it.
- `MainGraphicsDevice.GetAvailableGraphicsQueue`, `GetAvailableTransferQueue` and `UsedGraphicsQueues`: a queue is
  leased with `LeaseGraphicsQueue` and given back with `ReturnGraphicsQueue`.

- The assets generator of `Adamantium.Engine.Generators`, which gave a project a constant for each model, image and
  font among its additional files (`{Project}Assets`). Assets live in a content project now, whose
  `Adamantium.Content.Build` generates `{Name}Assets` for all of its files.

- `IFont.Baseline`: a made-up metric (`UnitsPerEm - Ascender + LineGap + CapsHeight`); the baseline of a line is
  `LineGap / 2 + LineAscent` below its top.
- The feature application `TextShaper` replaced: `GlyphLayoutContainer`, `IGlyphSubstitutions`, `IGlyphPositioning`,
  `GlyphLayoutData`, `GlyphPosition`, `Glyph.Layout`, `IFont.NotDefLayoutData`, `Feature.Apply`, `Feature.IsEnabled`,
  `FeatureService.EnableFeature` / `ApplyFeature`, and the `SubstituteGlyphs` / `PositionGlyph` methods of lookup
  subtables. They applied one feature at a time with the enabled state kept in the font, and had no implementation for
  contextual subtables.

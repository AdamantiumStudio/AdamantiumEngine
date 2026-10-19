# Test data credits

- **RiggedFigureExt.gltf**, **RiggedFigureKhr.glb**, **RiggedFigure/**, **RiggedFigureDraco/** — RiggedFigure from the
  Khronos [glTF Sample Assets](https://github.com/KhronosGroup/glTF-Sample-Assets), © 2017 Cesium,
  [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). `RiggedFigure/` and `RiggedFigureDraco/` are its `glTF`
  and `glTF-Draco` variants as published; the other two are compressed with gltfpack 1.3 (`-c -cf -ce ext` and
  `-cc -ce khr`).
- **MeshoptVectors.json** — data encoded by the meshoptimizer 1.3 encoder and decoded by its reference decoder
  (`meshopt_decoder_reference.js`).
- **DracoVectors.json** — meshes encoded by the draco3d 1.5.9 encoder, and files from the Draco repository's
  `testdata` (© The Draco Authors, [Apache 2.0](https://www.apache.org/licenses/LICENSE-2.0)); the expected output of
  each is what the draco3d 1.5.9 decoder gives.
- **DracoBroken/** — broken Draco files made for these tests.

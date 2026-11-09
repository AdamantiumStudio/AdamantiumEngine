#!/usr/bin/env python3
# Makes the JPEG 2000 test files and what each should decode to, with Pillow (OpenJPEG inside) as the independent
# coder and reader. Every <name>.jp2 or <name>.j2k gets <name>.rgba8, the pixels Pillow reads from it as RGBA, or
# <name>.gray16 for 16-bit gray, little-endian. Run from this folder with Python, Pillow, numpy and Glymur (which
# sets what Pillow does not: code-block modes and packet markers; it can run on the OpenJPEG Pillow carries). Pillow
# misreads subsampled components, so that file's pixels come from Glymur, each sample covering its 2 by 2 of the grid.
# <name>@r<R>l<L>.rgba8 is the picture without the R highest resolutions and with the first L quality layers (0 for
# all), from Glymur too: Pillow rounds a reduced size where OpenJPEG takes the ceiling.
import os
import glymur
import numpy
from PIL import Image

os.chdir(os.path.dirname(os.path.abspath(__file__)))
source = Image.open("../1.png").convert("RGB").crop((120, 90, 277, 183))
gray = source.convert("L")
rgba = source.copy()
rgba.putalpha(gray.point(lambda v: 255 - v // 2))
deep = gray.point(lambda v: v * 257, "I").convert("I;16")

cases = {
    "gray-lossless": (gray, {}),
    "rgb-lossless": (source, {}),
    "rgb-lossless-no-transform": (source, {"mct": 0}),
    "rgb-lossy": (source, {"irreversible": True, "quality_mode": "rates", "quality_layers": [12]}),
    "gray-lossy": (gray, {"irreversible": True, "quality_mode": "rates", "quality_layers": [8]}),
    "rgb-layers": (source, {"irreversible": True, "quality_mode": "rates", "quality_layers": [60, 20, 6]}),
    "rgb-rlcp": (source, {"progression": "RLCP", "quality_mode": "rates", "quality_layers": [30, 10, 1]}),
    "rgb-rpcl": (source, {"progression": "RPCL", "precinct_size": (32, 32), "codeblock_size": (8, 8)}),
    "rgb-pcrl": (source, {"progression": "PCRL", "precinct_size": (64, 32), "codeblock_size": (16, 8)}),
    "rgb-cprl": (source, {"progression": "CPRL", "precinct_size": (32, 64), "codeblock_size": (8, 16)}),
    "rgb-tiles": (source, {"tile_size": (64, 48)}),
    "rgb-tiles-offset": (source, {"tile_size": (40, 40), "offset": (7, 5), "tile_offset": (3, 2)}),
    "rgb-tiles-lossy": (source, {"tile_size": (48, 64), "irreversible": True, "quality_mode": "rates",
                                  "quality_layers": [10]}),
    "gray-one-resolution": (gray, {"num_resolutions": 1}),
    "gray-two-resolutions": (gray, {"num_resolutions": 2}),
    "gray-many-resolutions": (gray, {"num_resolutions": 7}),
    "gray-wide-blocks": (gray, {"codeblock_size": (64, 4)}),
    "rgba-lossless": (rgba, {}),
    "gray16-lossless": (deep, {}),
    "rgb-codestream": (source, {"no_jp2": True}),
}

# Code-block style bits (T.800, table A.19): 1 bypass, 2 reset, 4 terminate all, 8 vertically causal,
# 16 predictable termination, 32 segmentation symbols.
coded = {
    "rgb-bypass": (source, {"modesw": 1}),
    "rgb-reset": (source, {"modesw": 2}),
    "rgb-terminate-all": (source, {"modesw": 4}),
    "rgb-causal": (source, {"modesw": 8}),
    "rgb-predictable": (source, {"modesw": 16}),
    "rgb-segmentation": (source, {"modesw": 32}),
    "rgb-all-modes": (source, {"modesw": 63}),
    "rgb-all-modes-layers": (source, {"modesw": 63, "irreversible": True, "cratios": [40, 10, 2]}),
    "rgb-bypass-layers": (source, {"modesw": 1, "cratios": [30, 8, 1]}),
    "rgb-packet-markers": (source, {"sop": True, "eph": True, "cratios": [20, 5, 1]}),
    "gray-markers-precincts": (gray, {"sop": True, "eph": True, "psizes": [(32, 32), (16, 16)],
                                      "cbsize": (8, 8), "prog": "RPCL"}),
    "rgb-subsampled": (source.resize((79, 47)), {"subsam": (2, 2)}),
}

for name, (image, options) in coded.items():
    glymur.Jp2k(name + ".jp2", data=numpy.asarray(image), **options)

# A palette image: 6-bit indices into 64 colors, mapped by a component mapping box (T.800, I.5.3.4 and I.5.3.5).
indices = numpy.asarray(gray.point(lambda v: v // 4))
plain = glymur.Jp2k("palette-plain.jp2", data=indices)
colors = numpy.array([[(i * 4) % 256, (i * 37) % 256, 255 - i * 4] for i in range(64)], dtype=numpy.uint8)
boxes = plain.box
header = next(box for box in boxes if box.box_id == "jp2h")
for box in header.box:
    if box.box_id == "colr":
        box.colorspace = glymur.core.SRGB
header.box.append(glymur.jp2box.PaletteBox(colors, bits_per_component=(8, 8, 8), signed=(False, False, False)))
header.box.append(glymur.jp2box.ComponentMappingBox(component_index=(0, 0, 0), mapping_type=(1, 1, 1),
                                                    palette_index=(0, 1, 2)))
plain.wrap("palette.jp2", boxes=boxes)
os.remove("palette-plain.jp2")
coded["palette"] = None

for name, (image, options) in cases.items():
    extension = ".j2k" if options.get("no_jp2") else ".jp2"
    image.save(name + extension, **options)

partial = {
    "rgb-layers": [(1, 0), (2, 0), (0, 1), (0, 2), (1, 1)],
    "rgb-lossless": [(1, 0), (3, 0), (5, 0)],
    "gray-many-resolutions": [(4, 0), (6, 0)],
}
for name, steps in partial.items():
    for reduce, layers in steps:
        source_file = glymur.Jp2k(name + ".jp2")
        source_file.layer = layers
        step = 1 << reduce
        pixels = source_file[::step, ::step]
        mode = "L" if pixels.ndim == 2 else "RGB"
        decoded = Image.fromarray(pixels, mode)
        with open("%s@r%dl%d.rgba8" % (name, reduce, layers), "wb") as output:
            output.write(decoded.convert("RGBA").tobytes())
        print(name, "reduce", reduce, "layers", layers, decoded.size)

for name in list(cases) + list(coded):
    extension = ".j2k" if name == "rgb-codestream" else ".jp2"
    if name == "rgb-subsampled":
        planes = glymur.Jp2k(name + extension)[:]
        grid = planes.repeat(2, axis=0).repeat(2, axis=1)[:93, :157]
        Image.fromarray(grid, "RGB").save(name + ".png")
        decoded = Image.open(name + ".png")
        decoded.load()
        os.remove(name + ".png")
        with open(name + ".rgba8", "wb") as output:
            output.write(decoded.convert("RGBA").tobytes())
        print(name, "glymur", planes.shape)
        continue
    decoded = Image.open(name + extension)
    decoded.load()
    if decoded.mode == "I;16":
        with open(name + ".gray16", "wb") as output:
            output.write(decoded.tobytes("raw", "I;16"))
    else:
        with open(name + ".rgba8", "wb") as output:
            output.write(decoded.convert("RGBA").tobytes())
    print(name, decoded.mode, decoded.size)

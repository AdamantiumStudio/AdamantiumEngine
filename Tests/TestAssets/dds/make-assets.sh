#!/usr/bin/env bash
# Makes the DDS test files. Pillow writes the flat ones - masked RGBA, RGB, luminance, DXT1, DXT5 and BC3 in a DX10
# header - and the RGBA it reads them to. The rest are written here as Microsoft's "Programming Guide for DDS" lays
# them out - mip chains, a cube, an array, a volume - every part filled with its own byte, layer * 16 + level * 4 +
# depth slice, so where each part is read from is plain. Run from this folder; needs Python with Pillow 12.
set -euo pipefail
cd "$(dirname "$0")"

python3 -I -c "
import random, struct
from PIL import Image

rnd = random.Random(7)
im = Image.new('RGBA', (36, 20))
im.putdata([(rnd.randrange(256), rnd.randrange(256), rnd.randrange(256), rnd.choice([0, 128, 255])) for _ in range(720)])
for name, picture, kwargs in (('rgba.dds', im, {}), ('rgb.dds', im.convert('RGB'), {}), ('gray.dds', im.convert('L'), {}),
                              ('dxt1.dds', im, {'pixel_format': 'DXT1'}), ('dxt5.dds', im, {'pixel_format': 'DXT5'}),
                              ('bc3-dx10.dds', im, {'pixel_format': 'BC3'})):
    picture.save(name, **kwargs)
    open(name.replace('.dds', '.rgba'), 'wb').write(Image.open(name).convert('RGBA').tobytes())


def blocks(format_bytes, block, width, height):
    return ((width + block - 1) // block) * format_bytes * ((height + block - 1) // block)


def write(name, width, height, depth, levels, layers, fourcc, masks, dx10, part_size, cube=False):
    flags = 0x1 | 0x2 | 0x4 | 0x1000 | 0x20000 | (0x800000 if depth > 1 else 0)
    header = struct.pack('<4sIIIIIII44x', b'DDS ', 124, flags, height, width, 0, depth if depth > 1 else 0, levels)
    if fourcc:
        pixel = struct.pack('<II4s20x', 32, 0x4, fourcc)
    else:
        pixel = struct.pack('<IIIIIIII', 32, 0x41, 0, 32, *masks)
    caps2 = (0x200 | 0xFC00) if cube and not dx10 else (0x200000 if depth > 1 else 0)
    caps = struct.pack('<IIIII', 0x1000 | 0x8 | 0x400000, caps2, 0, 0, 0)
    data = bytearray()
    for layer in range(layers):
        for level in range(levels):
            w, h, d = max(1, width >> level), max(1, height >> level), max(1, depth >> level)
            for z in range(d):
                data += bytes([layer * 16 + level * 4 + z]) * part_size(w, h)
    extra = b''
    if dx10:
        dxgi, dimension, array = dx10
        extra = struct.pack('<IIIII', dxgi, dimension, 0x4 if cube else 0, array, 0)
    open(name, 'wb').write(header + pixel + caps + extra + data)


rgba = lambda w, h: w * h * 4
write('mips-dxt1.dds', 64, 32, 1, 7, 1, b'DXT1', None, None, lambda w, h: blocks(8, 4, w, h))
write('cube-rgba.dds', 8, 8, 1, 2, 6, None, (0xFF, 0xFF00, 0xFF0000, 0xFF000000), None, rgba, cube=True)
write('array-bc7.dds', 16, 8, 1, 2, 3, b'DX10', None, (98, 3, 3), lambda w, h: blocks(16, 4, w, h))
write('volume-r8.dds', 4, 4, 4, 3, 1, b'DX10', None, (61, 4, 1), lambda w, h: w * h)
"

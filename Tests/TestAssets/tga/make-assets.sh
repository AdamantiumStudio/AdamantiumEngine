#!/usr/bin/env bash
# Makes the TGA test files and the RGBA each decodes to by a reader independent of the engine: Pillow for what it
# reads as TGA 2.0 says, ImageMagick for 5-5-5 pixels with their alpha bit, and TGA 2.0's own arithmetic for
# premultiplied alpha and a color map that starts past entry 0, which neither reads so. Run from this folder; needs
# Python with Pillow 12 and ImageMagick 7.
set -euo pipefail
cd "$(dirname "$0")"

python3 -I -c "
import random, struct, subprocess
from PIL import Image

rnd = random.Random(5)
w, h = 37, 23


def picture(mode):
    im = Image.new('RGBA', (w, h))
    im.putdata([(rnd.randrange(256), rnd.randrange(256), rnd.randrange(256), rnd.choice([0, 90, 255]))
                for _ in range(w * h)])
    return im.convert('RGB').quantize(200) if mode == 'P' else im.convert(mode)


def reference(name, rgba):
    open(name.replace('.tga', '.rgba'), 'wb').write(rgba)


for name, mode, rle, orientation in (('palette-rle-bottom.tga', 'P', True, -1), ('gray-alpha-top.tga', 'LA', False, 1),
                                     ('rgba-rle-bottom.tga', 'RGBA', True, -1), ('gray-rle-top.tga', 'L', True, 1)):
    picture(mode).save(name, compression='tga_rle' if rle else None, orientation=orientation)
    reference(name, Image.open(name).convert('RGBA').tobytes())


def header(image_type, depth, descriptor, map_type=0, map_first=0, map_length=0, map_bits=0, id_bytes=b''):
    return bytes([len(id_bytes), map_type, image_type]) + struct.pack('<HHB', map_first, map_length, map_bits) + \
        struct.pack('<HHHHBB', 0, 0, w, h, depth, descriptor) + id_bytes


# 5-5-5 pixels with their alpha bit, top row first; ImageMagick reads the bit as alpha.
data = b''.join(struct.pack('<H', rnd.randrange(1 << 16)) for _ in range(w * h))
open('rgb555-alpha.tga', 'wb').write(header(2, 16, 0x21) + data)
reference('rgb555-alpha.tga', subprocess.run(['magick', 'rgb555-alpha.tga', '-depth', '8', 'rgba:-'],
                                             capture_output=True, check=True).stdout)

# Rows right to left, bottom row first, with an image ID.
rgb = bytes(rnd.randrange(256) for _ in range(w * h * 3))
open('right-to-left.tga', 'wb').write(header(2, 24, 0x10, id_bytes=b'right-to-left') + rgb)
reference('right-to-left.tga', Image.open('right-to-left.tga').convert('RGBA').tobytes())

# Premultiplied alpha (extension area, attributes type 4): colors divided by alpha, rounded.
pixels, expected = bytearray(), bytearray()
for _ in range(w * h):
    a = rnd.choice([0, 64, 128, 255])
    b, g, r = rnd.randrange(a + 1), rnd.randrange(a + 1), rnd.randrange(a + 1)
    pixels += bytes([b, g, r, a])
    expected += bytes([min(255, (c * 255 + a // 2) // a) if 0 < a < 255 else c for c in (r, g, b)] + [a])
body = header(2, 32, 0x28) + bytes(pixels)
extension = bytearray(495)
struct.pack_into('<H', extension, 0, 495)
extension[494] = 4
open('premultiplied.tga', 'wb').write(body + extension + struct.pack('<II', len(body), 0) + b'TRUEVISION-XFILE.\0')
reference('premultiplied.tga', bytes(expected))

# 16-bit indices into a 24-bit map whose first entry is 10: an index is the entry's own number.
entries = [bytes(rnd.randrange(256) for _ in range(3)) for _ in range(300)]
indices = [10 + rnd.randrange(300) for _ in range(w * h)]
open('map-from-10.tga', 'wb').write(header(1, 16, 0x20, 1, 10, 300, 24) + b''.join(entries)
                                    + b''.join(struct.pack('<H', i) for i in indices))
reference('map-from-10.tga', b''.join(bytes([e[2], e[1], e[0], 255]) for e in (entries[i - 10] for i in indices)))
"

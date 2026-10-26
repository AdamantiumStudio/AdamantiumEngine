#!/usr/bin/env bash
# Makes the BMP test files and the RGBA each decodes to by a reader independent of the engine: Pillow for most,
# ImageMagick for those it writes (RLE4, the BITMAPV5HEADER with alpha). Run from this folder; needs Python with
# Pillow 12 and ImageMagick 7.
set -euo pipefail
cd "$(dirname "$0")"

python3 -I -c "
import random, struct, subprocess
from PIL import Image

rnd = random.Random(3)
w, h = 37, 23


def picture(mode, colors=None):
    im = Image.new('RGB', (w, h))
    im.putdata([(rnd.randrange(256), rnd.randrange(256), rnd.randrange(256)) for _ in range(w * h)])
    if mode == 'P':
        return im.quantize(colors)
    if mode == 'RGBA':
        im = im.convert('RGBA')
        im.putalpha(Image.frombytes('L', (w, h), bytes(rnd.choice([0, 90, 255]) for _ in range(w * h))))
    return im.convert(mode)


def reference(name, rgba):
    open(name.replace('.bmp', '.rgba'), 'wb').write(rgba)


# Pillow's own: a palette of 2 and 200 colors, gray, RGB.
for name, im in (('palette1.bmp', picture('1')), ('palette8.bmp', picture('P', 200)), ('gray.bmp', picture('L')),
                 ('rgb24.bmp', picture('RGB'))):
    im.save(name)
    reference(name, Image.open(name).convert('RGBA').tobytes())

# ImageMagick's: 16-bit 5-6-5 and 32-bit with alpha, in BITMAPV5HEADERs.
src = 'src.png'
picture('RGBA').save(src)
subprocess.run(['magick', src, '-alpha', 'off', '-define', 'bmp:subtype=RGB565', 'rgb565.bmp'], check=True)
subprocess.run(['magick', src, 'rgba-v5.bmp'], check=True)
for name in ('rgb565.bmp', 'rgba-v5.bmp'):
    reference(name, subprocess.run(['magick', name, '-depth', '8', 'rgba:-'], capture_output=True, check=True).stdout)


# Written here as the Bitmap Storage page lays them out: 4-bit indices plain, RLE4 and RLE8 - runs, raw stretches,
# row ends and the bitmap's end - each read as its indices through its table.
def indexed(name, bits, compression, palette, rows_of_indices):
    table = b''.join(bytes([b, g, r, 0]) for r, g, b in palette)
    body = bytearray()
    for row in reversed(rows_of_indices):
        if compression == 0:
            packed = bytearray()
            for x in range(0, w, 2):
                packed.append(row[x] << 4 | (row[x + 1] if x + 1 < w else 0))
            body += packed + bytes((-len(packed)) % 4)
            continue
        x = 0
        while x < w:
            run = 1
            while x + run < w and run < 255 and row[x + run] == row[x]:
                run += 1
            if run >= 3 or w - x < 3:
                body += bytes([run, row[x] | (row[x] << 4 if bits == 4 else 0)])
                x += run
                continue
            raw = row[x:x + min(w - x, 255, 9)]
            if bits == 8:
                body += bytes([0, len(raw)]) + bytes(raw) + bytes(len(raw) % 2)
            else:
                nibbles = bytearray(raw[i] << 4 | (raw[i + 1] if i + 1 < len(raw) else 0) for i in range(0, len(raw), 2))
                body += bytes([0, len(raw)]) + nibbles + bytes(len(nibbles) % 2)
            x += len(raw)
        body += bytes([0, 0])
    if compression:
        body += bytes([0, 1])
    offset = 14 + 40 + len(table)
    info = struct.pack('<IiiHHIIiiII', 40, w, h, 1, bits, compression, len(body), 2835, 2835, len(palette), 0)
    open(name, 'wb').write(b'BM' + struct.pack('<IHHI', offset + len(body), 0, 0, offset) + info + table + body)
    reference(name, b''.join(bytes([*palette[i], 255]) for row in rows_of_indices for i in row))


for name, bits, compression, colors in (('palette4.bmp', 4, 0, 16), ('rle4.bmp', 4, 2, 16), ('rle8.bmp', 8, 1, 60)):
    palette = [tuple(rnd.randrange(256) for _ in range(3)) for _ in range(colors)]
    rows = [[(x // 4 + y) % colors if x % 7 else rnd.randrange(colors) for x in range(w)] for y in range(h)]
    indexed(name, bits, compression, palette, rows)
    magick = subprocess.run(['magick', name, '-depth', '8', 'rgba:-'], capture_output=True, check=True).stdout
    assert magick == open(name.replace('.bmp', '.rgba'), 'rb').read(), name + ' reads otherwise in ImageMagick'

# Top down: the 24-bit file with its height negated and rows reversed.
data = bytearray(open('rgb24.bmp', 'rb').read())
at = struct.unpack_from('<I', data, 10)[0]
stride = (w * 3 + 3) & ~3
rows = [data[at + r * stride: at + (r + 1) * stride] for r in range(h)]
struct.pack_into('<i', data, 22, -h)
data[at:] = b''.join(reversed(rows))
open('top-down.bmp', 'wb').write(data)
reference('top-down.bmp', open('rgb24.rgba', 'rb').read())
subprocess.run(['rm', src], check=True)
"

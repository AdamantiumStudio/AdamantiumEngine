#!/usr/bin/env bash
# Makes the synthetic GIF test files - disposals, transparency, local color tables, interlacing, a file cut off. Run
# from this folder; needs Python with Pillow 12. Their expected frames are hashes in GifCodecTests, of what Pillow and
# ffmpeg both decode.
set -euo pipefail
cd "$(dirname "$0")"

python3 -I -c "
import random
from PIL import Image, ImageDraw


def frames(n, w, h, alpha, seed):
    rnd = random.Random(seed)
    result = []
    for k in range(n):
        im = Image.new('RGBA', (w, h), (0, 0, 0, 0) if alpha else (rnd.randrange(256), 40, 90, 255))
        d = ImageDraw.Draw(im)
        for _ in range(4):
            x, y = rnd.randrange(w - 10), rnd.randrange(h - 10)
            d.rectangle((x, y, x + rnd.randrange(3, w // 2), y + rnd.randrange(3, h // 2)),
                        fill=(rnd.randrange(256), rnd.randrange(256), rnd.randrange(256), 255))
        result.append(im)
    return result


def animation(name, disposal, alpha):
    fs = frames(6, 60, 40, alpha, disposal * 2 + alpha)
    fs[0].save(name, save_all=True, append_images=fs[1:], duration=[30, 40, 50, 60, 70, 80], loop=3,
               disposal=disposal, optimize=False)


animation('dispose-background.gif', 2, True)
animation('dispose-previous.gif', 3, True)

# Many colors, a table of its own for each frame.
rnd = random.Random(7)
fs = []
for k in range(4):
    im = Image.new('RGB', (64, 48))
    im.putdata([(rnd.randrange(256), rnd.randrange(256), rnd.randrange(256)) for _ in range(64 * 48)])
    fs.append(im)
fs[0].save('local-tables.gif', save_all=True, append_images=fs[1:], duration=100, loop=0)

frames(1, 61, 37, False, 11)[0].convert('RGB').save('interlaced-still.gif', interlace=True)

# An opaque animation cut off in its last frame's data.
animation('whole.gif', 1, False)
data = open('whole.gif', 'rb').read()
open('truncated.gif', 'wb').write(data[: len(data) - 40])
"
rm whole.gif

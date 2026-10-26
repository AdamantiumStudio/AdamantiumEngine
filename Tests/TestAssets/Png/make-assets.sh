#!/usr/bin/env bash
# Makes the PNG test files and what each decodes to by a reader independent of the engine. Run from this folder;
# needs ImageMagick 7, ffmpeg, and Python with Pillow 13.
set -euo pipefail
cd "$(dirname "$0")"

# Adam7: interlaced RGBA, with Pillow's pixels.
magick ../1.png -crop 37x29+100+80 +repage -alpha on -interlace PNG -define png:color-type=6 adam7.png
python3 -I -c "
from PIL import Image
open('adam7.rgba8', 'wb').write(Image.open('adam7.png').convert('RGBA').tobytes())"

# APNG: five frames each disposed of as Previous and blended Over - ffmpeg's frames, as Pillow pastes Over by its
# alpha rather than compositing as APNG says.
python3 -I -c "
from PIL import Image, ImageDraw
frames = []
for k in range(5):
    im = Image.new('RGBA', (40, 30), (0, 0, 0, 0))
    draw = ImageDraw.Draw(im)
    draw.ellipse((k * 5, k * 3, k * 5 + 15, k * 3 + 12), fill=(255 * (k % 2), 100 + k * 30, 50, 128 + k * 25))
    draw.rectangle((30 - k * 4, 5, 38 - k * 4, 20), fill=(20, 200, 255, 255 if k % 2 else 90))
    frames.append(im)
frames[0].save('apng-over-previous.png', save_all=True, append_images=frames[1:], duration=[50, 60, 70, 80, 90],
               loop=2, disposal=2, blend=1)"
ffmpeg -hide_banner -loglevel error -y -i apng-over-previous.png -f rawvideo -pix_fmt rgba apng-over-previous.rgba8

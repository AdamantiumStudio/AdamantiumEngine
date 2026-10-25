"""Makes the JPEG test files Pillow (libjpeg inside) writes for the decoder's tests. Run from this folder with Pillow:
python3 -I make-assets.py. The progressive, restart and gray-*.jpg files predate this script."""
from PIL import Image

# Without optimizing, libjpeg writes T.81's K.3 Huffman tables - what a stream that leaves them out is decoded by.
source = Image.open("../1.png").convert("RGB").crop((100, 80, 148, 112))
source.save("color-standard-tables.jpg", quality=90, optimize=False, subsampling=2)

# Quarters of pure cyan, magenta, yellow and black; Pillow marks CMYK with Adobe's APP14 and inverts the inks.
cmyk = Image.new("CMYK", (16, 16))
for (x, y), ink in {(0, 0): (255, 0, 0, 0), (8, 0): (0, 255, 0, 0), (0, 8): (0, 0, 255, 0),
                    (8, 8): (0, 0, 0, 255)}.items():
    cmyk.paste(ink, (x, y, x + 8, y + 8))
cmyk.save("cmyk-adobe.jpg", quality=95)

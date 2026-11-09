#!/usr/bin/env python3
# Makes small pictures that carry this folder's ICC profiles - PNG (RGB, gray), JPEG (RGB, CMYK), TIFF (RGB, CMYK) -
# and, in <name>.srgb, the sRGB littleCMS makes of the samples Pillow decodes from each, perceptual, its pipeline
# evaluated as it is: what a reader applying the profile shows; and pages-rgb.tif, 40 pages each carrying the same
# profile, and photo-premultiplied.tif, RGB with associated opacity. Run after make-profiles.py, with Python, Pillow and the
# system's liblcms2 (or the library LCMS2 names).
import ctypes
import ctypes.util
import os
import struct

from PIL import Image

os.chdir(os.path.dirname(os.path.abspath(__file__)))

lcms = ctypes.CDLL(os.environ.get("LCMS2") or ctypes.util.find_library("lcms2"))
lcms.cmsOpenProfileFromMem.restype = ctypes.c_void_p
lcms.cmsOpenProfileFromMem.argtypes = [ctypes.c_char_p, ctypes.c_uint32]
lcms.cmsCreate_sRGBProfile.restype = ctypes.c_void_p
lcms.cmsCreateTransform.restype = ctypes.c_void_p
lcms.cmsCreateTransform.argtypes = [ctypes.c_void_p, ctypes.c_uint32, ctypes.c_void_p, ctypes.c_uint32,
                                    ctypes.c_uint32, ctypes.c_uint32]
lcms.cmsDoTransform.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_char_p, ctypes.c_uint32]
pixel_types = {"RGB": 4 << 16 | 3 << 3 | 1, "CMYK": 6 << 16 | 4 << 3 | 1, "L": 3 << 16 | 1 << 3 | 1}
exact = 0x0100 | 0x0004
srgb = lcms.cmsCreate_sRGBProfile()

width, height = 64, 48


def picture(mode):
    rgb = Image.new("RGB", (width, height))
    rgb.putdata([(int(255 * x / (width - 1)), int(255 * y / (height - 1)),
                  int(255 * (1 - abs(x / (width - 1) - y / (height - 1))))) for y in range(height) for x in range(width)])
    if mode == "RGB":
        return rgb
    if mode == "L":
        return rgb.convert("L")
    colors = rgb.tobytes()
    cmyk = Image.new("CMYK", (width, height))
    cmyk.putdata([(255 - colors[3 * i], 255 - colors[3 * i + 1], 255 - colors[3 * i + 2], (i % width * 4) % 256)
                  for i in range(width * height)])
    return cmyk


def reference(name, mode, profile):
    decoded = Image.open(name)
    decoded.load()
    if decoded.mode != mode:
        raise SystemExit(f"{name} decodes as {decoded.mode}, not {mode}")
    samples = decoded.tobytes()
    source = lcms.cmsOpenProfileFromMem(profile, len(profile))
    transform = lcms.cmsCreateTransform(source, pixel_types[mode], srgb, pixel_types["RGB"], 0, exact)
    output = ctypes.create_string_buffer(3 * width * height)
    lcms.cmsDoTransform(transform, samples, output, width * height)
    with open(name + ".srgb", "wb") as file:
        file.write(output.raw)


pictures = [
    ("photo-rgb.png", "RGB", "rgb-gamma.icc", {}),
    ("photo-gray.png", "L", "gray-table.icc", {}),
    ("photo-rgb.jpg", "RGB", "rgb-gamma.icc", {"quality": 100, "subsampling": 0}),
    ("photo-cmyk.jpg", "CMYK", "cmyk-ab.icc", {"quality": 100, "subsampling": 0}),
    ("photo-rgb.tif", "RGB", "rgb-gamma.icc", {}),
    ("photo-cmyk.tif", "CMYK", "cmyk-ab.icc", {}),
]
with open("rgb-gamma.icc", "rb") as file:
    gamma = file.read()
pages = [Image.new("RGB", (200, 200), (40 * (i % 6), 90, 255 - 5 * i)) for i in range(40)]
pages[0].save("pages-rgb.tif", save_all=True, append_images=pages[1:], compression="tiff_deflate",
              icc_profile=gamma)
print("pages-rgb.tif", len(pages), "pages")

premultiplied = Image.new("RGBA", (width, height))
premultiplied.putdata([(int(r * a / 255), int(g * a / 255), int(b * a / 255), a) for (r, g, b), a in
                       zip(zip(*[iter(picture("RGB").tobytes())] * 3),
                           [55 + (i * 7) % 201 for i in range(width * height)])])
premultiplied.save("photo-premultiplied.tif", icc_profile=gamma)
# Pillow writes ExtraSamples 2, unassociated; the samples are associated (1), so the tag is set by hand.
with open("photo-premultiplied.tif", "r+b") as file:
    tiff = bytearray(file.read())
    ifd = struct.unpack_from("<I", tiff, 4)[0]
    for entry in range(struct.unpack_from("<H", tiff, ifd)[0]):
        at = ifd + 2 + 12 * entry
        if struct.unpack_from("<H", tiff, at)[0] == 338:
            struct.pack_into("<H", tiff, at + 8, 1)
    file.seek(0)
    file.write(tiff)
print("photo-premultiplied.tif")

for name, mode, profile_name, options in pictures:
    with open(profile_name, "rb") as file:
        profile = file.read()
    picture(mode).save(name, icc_profile=profile, **options)
    reference(name, mode, profile)
    print(name, mode, profile_name)

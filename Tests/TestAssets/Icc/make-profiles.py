#!/usr/bin/env python3
# Makes the ICC test profiles - written here field by field from ICC.1:2022 - and what each converts sample colors to,
# with littleCMS as the independent reader: <name>.icc and <name>.json, the samples and the sRGB littleCMS gives them for
# each intent - and, as "1bpc", relative colorimetric with black point compensation - its pipeline evaluated as it is
# (no resampled shortcut, no white fixed onto white). Run with Python; littleCMS is the system's liblcms2, or the
# library LCMS2 names. Not 2.19: it lost the neutral black point (restored after it).
import ctypes
import ctypes.util
import json
import math
import os
import struct

os.chdir(os.path.dirname(os.path.abspath(__file__)))


def s15(value):
    return struct.pack(">i", int(round(value * 65536)))


def xyz(x, y, z):
    return b"XYZ \0\0\0\0" + s15(x) + s15(y) + s15(z)


def curv_gamma(gamma):
    return b"curv\0\0\0\0" + struct.pack(">IH", 1, int(round(gamma * 256))) + b"\0\0"


def curv_table(values):
    data = b"curv\0\0\0\0" + struct.pack(">I", len(values)) + b"".join(struct.pack(">H", v) for v in values)
    return data + b"\0" * (-len(data) % 4)


def para(kind, *parameters):
    return b"para\0\0\0\0" + struct.pack(">HH", kind, 0) + b"".join(s15(p) for p in parameters)


def text_description(text):
    raw = text.encode("ascii") + b"\0"
    data = b"desc\0\0\0\0" + struct.pack(">I", len(raw)) + raw + struct.pack(">II", 0, 0) + b"\0\0\0" + b"\0" * 64
    return data + b"\0" * (-len(data) % 4)


def mluc(text):
    raw = text.encode("utf-16-be")
    data = b"mluc\0\0\0\0" + struct.pack(">II", 1, 12) + b"enUS" + struct.pack(">II", len(raw), 28) + raw
    return data + b"\0" * (-len(data) % 4)


def grid_nodes(inputs, points):
    total = points ** inputs
    for index in range(total):
        coords = []
        rest = index
        for _ in range(inputs):
            coords.append(rest % points)
            rest //= points
        yield [c / (points - 1) for c in reversed(coords)]


def lut16(inputs, points, function, entries=256, outputs=3, matrix=(1, 0, 0, 0, 1, 0, 0, 0, 1)):
    data = b"mft2\0\0\0\0" + struct.pack(">BBBB", inputs, outputs, points, 0)
    data += b"".join(s15(v) for v in matrix)
    data += struct.pack(">HH", entries, entries)
    for channel in range(inputs):
        data += b"".join(struct.pack(">H", int(round(65535 * (i / (entries - 1)) ** (1 + 0.1 * channel))))
                         for i in range(entries))
    for node in grid_nodes(inputs, points):
        data += b"".join(struct.pack(">H", max(0, min(65535, int(round(v * 65535))))) for v in function(node))
    for channel in range(outputs):
        data += b"".join(struct.pack(">H", int(round(65535 * i / (entries - 1)))) for i in range(entries))
    return data


def lut8(inputs, points, function):
    data = b"mft1\0\0\0\0" + struct.pack(">BBBB", inputs, 3, points, 0)
    data += b"".join(s15(v) for v in (1, 0, 0, 0, 1, 0, 0, 0, 1))
    for channel in range(inputs):
        data += bytes(int(round(255 * (i / 255) ** (1 + 0.15 * channel))) for i in range(256))
    for node in grid_nodes(inputs, points):
        data += bytes(max(0, min(255, int(round(v * 255)))) for v in function(node))
    for channel in range(3):
        data += bytes(range(256))
    return data


def lut_a_to_b(inputs, points, function, precision=2, matrix=None, m_curves=True):
    curves_a = [para(0, 1 + 0.2 * c) for c in range(inputs)]
    curves_m = [curv_table([int(round(65535 * (i / 15) ** (0.9 if c == 0 else 1))) for i in range(16)])
                for c in range(3)]
    curves_b = [curv_gamma(1.0) for _ in range(3)]
    clut = bytes([points] * inputs + [0] * (16 - inputs)) + bytes([precision, 0, 0, 0])
    for node in grid_nodes(inputs, points):
        for v in function(node):
            v = max(0.0, min(1.0, v))
            clut += struct.pack(">H", int(round(v * 65535))) if precision == 2 else bytes([int(round(v * 255))])
    clut += b"\0" * (-len(clut) % 4)
    parts = {}
    body = b""
    offset = 32

    def place(name, blob):
        nonlocal body
        parts[name] = offset + len(body)
        body += blob

    place("b", b"".join(curves_b))
    if matrix:
        place("matrix", b"".join(s15(v) for v in matrix))
    if m_curves:
        place("m", b"".join(curves_m))
    place("clut", clut)
    place("a", b"".join(curves_a))
    header = b"mAB \0\0\0\0" + struct.pack(">BBH", inputs, 3, 0)
    header += struct.pack(">IIIII", parts["b"], parts.get("matrix", 0), parts.get("m", 0), parts["clut"], parts["a"])
    return header + body


def lut_b_to_a(points, function):
    curves_b = [curv_gamma(1.0) for _ in range(3)]
    curves_a = [para(0, 1 / (1 + 0.1 * c)) for c in range(4)]
    clut = bytes([points] * 3 + [0] * 13) + bytes([2, 0, 0, 0])
    for node in grid_nodes(3, points):
        clut += b"".join(struct.pack(">H", int(round(max(0.0, min(1.0, v)) * 65535))) for v in function(node))
    offset_b = 32
    offset_clut = offset_b + sum(len(c) for c in curves_b)
    offset_a = offset_clut + len(clut)
    header = b"mBA \0\0\0\0" + struct.pack(">BBH", 3, 4, 0)
    header += struct.pack(">IIIII", offset_b, 0, 0, offset_clut, offset_a)
    return header + b"".join(curves_b) + clut + b"".join(curves_a)


def cmyk_of_lab(node):
    l, a, b = node
    k = max(0.0, (0.6 - l) / 0.6) ** 1.3
    c = (1 - l) * 0.7 + max(0.0, 0.5 - a) * 0.6
    m = (1 - l) * 0.7 + max(0.0, a - 0.5) * 0.6
    y = (1 - l) * 0.65 + max(0.0, b - 0.5) * 0.6
    return [c, m, y, k]


def profile(version, profile_class, space, pcs, tags, intent=0):
    major, minor = version
    count = len(tags)
    offset = 128 + 4 + 12 * count
    table = b""
    data = b""
    for signature, blob in tags:
        blob += b"\0" * (-len(blob) % 4)
        table += signature.encode("ascii") + struct.pack(">II", offset + len(data), len(blob))
        data += blob
    size = offset + len(data)
    header = struct.pack(">I", size) + b"lcms" + bytes([major, minor << 4, 0, 0]) + profile_class.encode("ascii")
    header += space.encode("ascii") + pcs.encode("ascii") + bytes(12) + b"acsp" + b"MSFT" + bytes(4)
    header += bytes(8) + bytes(8) + struct.pack(">I", intent) + s15(0.9642) + s15(1.0) + s15(0.8249)
    header += b"lcms" + bytes(16) + bytes(28)
    assert len(header) == 128
    return header + struct.pack(">I", count) + table + data


def description(version, text):
    return text_description(text) if version[0] < 4 else mluc(text)


D50 = (0.9642, 1.0, 0.8249)
srgb_colorants = [("rXYZ", xyz(0.4360747, 0.2225045, 0.0139322)), ("gXYZ", xyz(0.3850649, 0.7168786, 0.0971045)),
                  ("bXYZ", xyz(0.1430804, 0.0606169, 0.7141733))]
adobe_like = [("rXYZ", xyz(0.6097, 0.3111, 0.0195)), ("gXYZ", xyz(0.2053, 0.6257, 0.0609)),
              ("bXYZ", xyz(0.1492, 0.0632, 0.7446))]


# Every table takes the device's white to the PCS white, as a profile's relative tables do: littleCMS fixes white onto
# white, and a table that misses it would be compared with that fix rather than with itself.
def lab_of_rgb(node):
    r, g, b = node
    l = 0.15 + 0.85 * (0.3 * r + 0.55 * g + 0.15 * b)
    a = 0.5 + 0.35 * (r - g) * (1 - 0.3 * b)
    bb = 0.5 + 0.35 * (0.5 * (r + g) - b)
    return [l, a, bb]


def lab_of_cmyk(node):
    c, m, y, k = node
    ink = 1 - k * 0.9
    l = (1 - 0.65 * max(c, m, y) * 0.8 - 0.1 * (c + m + y) / 3) * ink
    a = 0.5 + 0.3 * (m - c * 0.6) * ink
    b = 0.5 + 0.3 * (y - (c + m) * 0.3) * ink
    return [max(0.0, l), a, b]


def xyz_of_rgb(node):
    r, g, b = node
    x = 0.4360747 * r + 0.3850649 * g + 0.1430804 * b
    y = 0.2225045 * r + 0.7168786 * g + 0.0606169 * b
    z = 0.0139322 * r + 0.0971045 * g + 0.7141733 * b
    return [x * 32768 / 65535, y * 32768 / 65535, z * 32768 / 65535]


def xyz_of_cmyk(node):
    l, a, b = lab_of_cmyk(node)
    fy = (l * 100 + 16) / 116
    fx, fz = fy + (a * 255 - 128) / 500, fy - (b * 255 - 128) / 200

    def inverse(t):
        return t ** 3 if t > 6 / 29 else 3 * (6 / 29) ** 2 * (t - 4 / 29)

    return [w * inverse(f) * 32768 / 65535 for w, f in zip(D50, (fx, fy, fz))]


def cmyk_of_xyz(node):
    y = node[1] * 65535 / 32768
    l = (116 * (y ** (1 / 3) if y > 216 / 24389 else y * 24389 / 27 / 116 + 16 / 116) - 16) / 100
    return cmyk_of_lab([l, 0.5 + (node[0] - node[2]) * 0.2, 0.5])


v2, v4 = (2, 1), (4, 3)
profiles = {
    "rgb-gamma": profile(v2, "mntr", "RGB ", "XYZ ", [("desc", description(v2, "gamma 2.2")),
                                                       ("wtpt", xyz(0.9505, 1.0, 1.089))]
                         + adobe_like + [("rTRC", curv_gamma(2.2)), ("gTRC", curv_gamma(2.2)),
                                         ("bTRC", curv_gamma(2.2))]),
    "rgb-para": profile(v4, "mntr", "RGB ", "XYZ ", [("desc", description(v4, "sRGB-like")), ("wtpt", xyz(*D50))]
                        + srgb_colorants + [("rTRC", para(3, 2.4, 1 / 1.055, 0.055 / 1.055, 1 / 12.92, 0.04045)),
                                            ("gTRC", para(4, 2.2, 0.95, 0.05, 0.08, 0.03, 0.01, 0.002)),
                                            ("bTRC", para(2, 1.8, 0.9, 0.1, 0.02))]),
    "rgb-table": profile(v2, "mntr", "RGB ", "XYZ ", [("desc", description(v2, "tables")), ("wtpt", xyz(*D50))]
                         + srgb_colorants + [(t, curv_table([int(round(65535 * (i / 63) ** g)) for i in range(64)]))
                                             for t, g in (("rTRC", 1.9), ("gTRC", 2.3), ("bTRC", 2.0))]),
    "rgb-blue-black": profile(v2, "mntr", "RGB ", "XYZ ", [("desc", description(v2, "blue black")),
                              ("wtpt", xyz(*D50))]
                              + srgb_colorants + [("rTRC", curv_gamma(2.2)), ("gTRC", curv_gamma(2.2)),
                                                  ("bTRC", curv_table([int(round(65535 * (0.05 + 0.95 * (i / 63) ** 2.2)))
                                                                       for i in range(64)]))]),
    "gray-table": profile(v2, "mntr", "GRAY", "XYZ ", [("desc", description(v2, "gray")), ("wtpt", xyz(*D50)),
                          ("kTRC", curv_table([int(round(65535 * (i / 31) ** 1.8)) for i in range(32)]))]),
    "rgb-lut16": profile(v2, "scnr", "RGB ", "Lab ", [("desc", description(v2, "lut16")), ("wtpt", xyz(*D50)),
                         ("A2B0", lut16(3, 9, lab_of_rgb))]),
    "cmyk-lut8": profile(v2, "prtr", "CMYK", "Lab ", [("desc", description(v2, "lut8")), ("wtpt", xyz(*D50)),
                         ("A2B0", lut8(4, 5, lab_of_cmyk))]),
    "cmyk-ab": profile(v4, "prtr", "CMYK", "Lab ", [("desc", description(v4, "lutAtoB")), ("wtpt", xyz(*D50)),
                       ("A2B0", lut_a_to_b(4, 7, lab_of_cmyk)), ("A2B1", lut_a_to_b(4, 7, lab_of_cmyk)),
                       ("B2A0", lut_b_to_a(9, cmyk_of_lab))]),
    "cmyk-xyz-lut16": profile(v2, "prtr", "CMYK", "XYZ ", [("desc", description(v2, "lut16 XYZ")),
                              ("wtpt", xyz(*D50)), ("A2B0", lut16(4, 9, xyz_of_cmyk)),
                              ("A2B1", lut16(4, 9, xyz_of_cmyk)),
                              ("B2A0", lut16(3, 9, cmyk_of_xyz, outputs=4,
                                             matrix=(0.9, 0.05, 0.05, 0.03, 0.94, 0.03, 0.02, 0.03, 0.95)))]),
    "cmyk-ab-bytes": profile(v4, "prtr", "CMYK", "Lab ", [("desc", description(v4, "lutAtoB 8")),
                             ("wtpt", xyz(*D50)), ("A2B0", lut_a_to_b(4, 6, lab_of_cmyk, precision=1,
                                                                        m_curves=False))]),
    "rgb-ab-xyz": profile(v4, "mntr", "RGB ", "XYZ ", [("desc", description(v4, "lutAtoB XYZ")),
                          ("wtpt", xyz(*D50)), ("A2B0", lut_a_to_b(3, 9, xyz_of_rgb, m_curves=False,
                                                                    matrix=(0.95, 0.03, 0.02, 0.02, 0.96, 0.02,
                                                                            0.01, 0.02, 0.97, 0.0, 0.0, 0.0)))]),
    "rgb-intents": profile(v4, "scnr", "RGB ", "Lab ", [("desc", description(v4, "intents")),
                           ("wtpt", xyz(0.9505, 1.0, 1.089)),
                           ("A2B0", lut_a_to_b(3, 5, lab_of_rgb)),
                           ("A2B1", lut_a_to_b(3, 5, lambda n: lab_of_rgb([n[1], n[2], n[0]]))),
                           ("A2B2", lut_a_to_b(3, 5, lambda n: lab_of_rgb([n[2], n[0], n[1]])))]),
}

lcms = ctypes.CDLL(os.environ.get("LCMS2") or ctypes.util.find_library("lcms2"))
lcms.cmsGetEncodedCMMversion.restype = ctypes.c_int
lcms.cmsOpenProfileFromMem.restype = ctypes.c_void_p
lcms.cmsOpenProfileFromMem.argtypes = [ctypes.c_char_p, ctypes.c_uint32]
lcms.cmsCreate_sRGBProfile.restype = ctypes.c_void_p
lcms.cmsCreateTransform.restype = ctypes.c_void_p
lcms.cmsCreateTransform.argtypes = [ctypes.c_void_p, ctypes.c_uint32, ctypes.c_void_p, ctypes.c_uint32,
                                    ctypes.c_uint32, ctypes.c_uint32]
lcms.cmsDoTransform.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_char_p, ctypes.c_uint32]
lcms.cmsDeleteTransform.argtypes = [ctypes.c_void_p]
lcms.cmsCloseProfile.argtypes = [ctypes.c_void_p]
# 8-bit pixel types: colour space << 16 | channels << 3 | bytes (lcms2.h).
pixel_types = {"RGB": 4 << 16 | 3 << 3 | 1, "CMYK": 6 << 16 | 4 << 3 | 1, "L": 3 << 16 | 1 << 3 | 1}
NO_WHITE_ON_WHITE_FIXUP, NO_OPTIMIZE, BLACK_POINT_COMPENSATION = 0x0004, 0x0100, 0x2000
print("littleCMS", lcms.cmsGetEncodedCMMversion())
srgb = lcms.cmsCreate_sRGBProfile()
samples = {
    "RGB": [[r, g, b] for r in (0, 37, 128, 200, 255) for g in (0, 37, 128, 200, 255) for b in (0, 37, 128, 200, 255)],
    "CMYK": [[c, m, y, k] for c in (0, 64, 160, 255) for m in (0, 64, 160, 255) for y in (0, 64, 160, 255)
             for k in (0, 90, 255)],
    "L": [[v] for v in range(0, 256, 15)],
}
for name, data in profiles.items():
    with open(name + ".icc", "wb") as output:
        output.write(data)
    source = lcms.cmsOpenProfileFromMem(data, len(data))
    space = data[16:20].decode("ascii")
    mode = {"RGB ": "RGB", "CMYK": "CMYK", "GRAY": "L"}[space]
    inputs = samples[mode]
    pixels = bytes(v for sample in inputs for v in sample)
    results = {}
    exact = NO_OPTIMIZE | NO_WHITE_ON_WHITE_FIXUP

    def converted(intent, flags):
        transform = lcms.cmsCreateTransform(source, pixel_types[mode], srgb, pixel_types["RGB"], intent, flags)
        output = ctypes.create_string_buffer(3 * len(inputs))
        lcms.cmsDoTransform(transform, pixels, output, len(inputs))
        lcms.cmsDeleteTransform(transform)
        return [list(output.raw[3 * i:3 * i + 3]) for i in range(len(inputs))]

    for intent in (0, 1, 2, 3):
        results[str(intent)] = converted(intent, exact)
    results["1bpc"] = converted(1, exact | BLACK_POINT_COMPENSATION)
    lcms.cmsCloseProfile(source)
    with open(name + ".json", "w") as output:
        json.dump({"inputs": inputs, "outputs": results}, output)
    print(name, mode, len(inputs))

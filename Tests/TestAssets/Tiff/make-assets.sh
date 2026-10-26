#!/usr/bin/env bash
# Makes the TIFF test files and what each should decode to, with ImageMagick (libtiff inside) as the independent reader.
# Every <name>.tif gets <name>.rgba8 (or .rgba16 / .rgbaf), the pixels ImageMagick reads from it, oriented, RGBA,
# least significant byte first. Run from this folder; needs ImageMagick 7 and Python with Pillow (which writes the
# Photometric YCbCr JPEG and the CCITT run-length files ImageMagick does not).
set -euo pipefail
cd "$(dirname "$0")"

magick ../1.png -crop 61x37+120+90 +repage \
  \( -size 37x61 gradient:white-gray20 -rotate 90 \) -alpha off -compose CopyOpacity -composite \
  -type TrueColorAlpha -depth 8 PNG32:base.png
magick base.png -alpha off -type TrueColor -depth 8 PNG24:base-rgb.png
magick base.png -colorspace gray -alpha off -type Grayscale -depth 8 PNG8:base-gray.png

rgba8() { magick "$1.tif[0]" -auto-orient -alpha on -depth 8 "rgba:$1.rgba8"; }
rgba16() { magick "$1.tif[0]" -auto-orient -alpha on -depth 16 -endian LSB "rgba:$1.rgba16"; }
rgbaf() { magick "$1.tif[0]" -auto-orient -alpha on -define quantum:format=floating-point -depth 32 -endian LSB \
  "rgba:$1.rgbaf"; }

magick base-rgb.png -compress none -define tiff:rows-per-strip=5 rgb8-none-strips.tif; rgba8 rgb8-none-strips
magick base-rgb.png -compress lzw -define tiff:predictor=2 rgb8-lzw-predictor.tif; rgba8 rgb8-lzw-predictor
magick base.png -compress zip rgba8-deflate.tif; rgba8 rgba8-deflate
magick base.png -compress zip -define tiff:alpha=associated rgba8-associated.tif; rgba8 rgba8-associated
magick base-rgb.png -compress rle -endian MSB rgb8-packbits-big-endian.tif; rgba8 rgb8-packbits-big-endian
magick base-rgb.png -compress lzw -define tiff:tile-geometry=16x16 rgb8-tiled-lzw.tif; rgba8 rgb8-tiled-lzw
magick base-rgb.png -interlace plane -compress lzw rgb8-planar.tif; rgba8 rgb8-planar
magick base-rgb.png -depth 16 -compress zip -define tiff:predictor=2 rgb16-deflate-predictor.tif
rgba16 rgb16-deflate-predictor
magick base-rgb.png -depth 16 -endian MSB -compress none rgb16-big-endian.tif; rgba16 rgb16-big-endian
magick base-gray.png -compress lzw gray8.tif; rgba8 gray8
magick base-gray.png -depth 4 -compress none gray4.tif; rgba8 gray4
magick base-gray.png -depth 2 -compress none gray2.tif; rgba8 gray2
magick base-gray.png -type bilevel -compress none bilevel.tif; rgba8 bilevel
magick base-gray.png -type bilevel -compress none -define tiff:photometric=min-is-white bilevel-min-is-white.tif
rgba8 bilevel-min-is-white
magick base-gray.png -type bilevel -compress Group4 bilevel-g4.tif; rgba8 bilevel-g4
magick base-gray.png -type bilevel -compress Fax bilevel-g3.tif; rgba8 bilevel-g3
magick base-gray.png -type bilevel -compress Fax -define tiff:group3-options=1 bilevel-g3-2d.tif; rgba8 bilevel-g3-2d
magick base-gray.png -type bilevel -compress Group4 -define tiff:fill-order=lsb bilevel-g4-lsb.tif
rgba8 bilevel-g4-lsb
magick base-rgb.png -compress jpeg -quality 92 jpeg-rgb.tif; rgba8 jpeg-rgb
magick base-gray.png -compress jpeg -quality 92 jpeg-gray.tif; rgba8 jpeg-gray
magick base-rgb.png -compress jpeg -quality 92 -define tiff:tile-geometry=32x32 jpeg-tiled.tif; rgba8 jpeg-tiled
magick base-rgb.png -interlace plane -compress jpeg -quality 92 jpeg-planar.tif; rgba8 jpeg-planar
magick base.png -compress jpeg -quality 92 jpeg-rgba.tif; rgba8 jpeg-rgba
python3 -I -c "
from PIL import Image
source = Image.open('base-rgb.png').convert('RGB')
source.convert('YCbCr').save('jpeg-ycbcr.tif', compression='jpeg', quality=92)
source.convert('1').save('bilevel-ccitt-rle.tif', compression='tiff_ccitt')
"
rgba8 jpeg-ycbcr; rgba8 bilevel-ccitt-rle
magick base-rgb.png -colorspace Lab -compress none lab8.tif; rgba8 lab8
magick base-rgb.png -type palette -compress lzw palette8.tif; rgba8 palette8
magick base-rgb.png -colors 12 -type palette -compress none palette4.tif; rgba8 palette4
magick base-rgb.png -colorspace cmyk -compress zip cmyk8.tif; rgba8 cmyk8
magick base-gray.png -define quantum:format=floating-point -depth 32 -compress zip -define tiff:predictor=3 \
  gray-float32-predictor.tif; rgbaf gray-float32-predictor
magick base.png -define quantum:format=floating-point -depth 32 -compress lzw rgba-float32.tif; rgbaf rgba-float32
magick base-rgb.png -define quantum:format=floating-point -depth 16 -compress none rgb-float16.tif; rgbaf rgb-float16
magick base-rgb.png -compress zip TIFF64:rgb8-bigtiff.tif; rgba8 rgb8-bigtiff
for turn in top-right bottom-right bottom-left left-top right-top right-bottom left-bottom; do
  magick base-rgb.png -orient "$turn" -compress lzw "orient-$turn.tif"; rgba8 "orient-$turn"
done

magick base-rgb.png \( base-rgb.png -negate \) \( base-rgb.png -flop \) -compress lzw pages.tif
for page in 0 1 2; do magick "pages.tif[$page]" -alpha on -depth 8 "rgba:pages-$page.rgba8"; done
magick base-rgb.png \( base-rgb.png -resize 50% \) \( base-gray.png -depth 16 \) \( base-rgb.png -negate \) \
  -compress lzw pages-mixed.tif
magick base-rgb.png -resize 300x180! -compress jpeg -quality 60 PTIF:pyramid.tif

rm base.png base-rgb.png base-gray.png

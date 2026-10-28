using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Adamantium.Fonts.Common;
using Adamantium.Mathematics;

namespace Adamantium.Fonts.TextureGeneration
{
    public static class MSDFGenerator
    {
        private const double NearBandTexels = 1.0;
        private const double FarBandTexels = 3.0;
        private const double MisleadingTexels = 0.75;
        private const double ColorsUnusedBeyondTexels = FarBandTexels + 1.5;
        private const int MinBandRows = 8;
        private const double VertexTolerance = 1e-3;

        public static void GenerateGlyphData(this Glyph glyph, GlyphTextureData textureData, double pxRange, ushort unitsPerEm)
        {
            GenerateMSDFForExistingTextureData(glyph, textureData, pxRange, unitsPerEm);
        }
        
        public static GlyphTextureData PrepareData(this Glyph glyph, uint size, ushort unitsPerEm, uint margin, bool useProportionalSize = true)
        {
            return CalculateBasicTextureData(glyph, size, unitsPerEm, margin, useProportionalSize);
        }
        
        // --- PREPROCESSORS ---
        private static List<List<MsdfGlyphSegment>> SplitToRawContours(List<MsdfGlyphSegment> segments)
        {
            var res = new List<List<MsdfGlyphSegment>>();
            var contour = new List<MsdfGlyphSegment>();

            for (var i = 0; i < segments.Count - 1; ++i)
            {
                var currentSegment = segments[i];
                var nextSegment = segments[i + 1];

                contour.Add(currentSegment);

                if (!GlyphSegmentsMath.IsSegmentsConnected(ref currentSegment.Segment, ref nextSegment.Segment))
                {
                    res.Add(contour);
                    contour = new List<MsdfGlyphSegment>();
                }

                if (i == segments.Count - 2)
                {
                    contour.Add(nextSegment);
                    res.Add(contour);
                }
            }

            return res;
        }

        private static bool FindFirstSharpAngle(List<MsdfGlyphSegment> contour, int angleThreshold, out int startIndex)
        {
            for (int i = startIndex = 0; i < contour.Count; i++)
            {
                var currentSeg = contour[i];

                if (i == contour.Count - 1)
                {
                    startIndex = 0;
                }
                else
                {
                    startIndex = i + 1;
                }

                var nextSeg = contour[startIndex];

                if (GlyphSegmentsMath.GetSegmentsAngle(currentSeg.Segment, nextSeg.Segment) < angleThreshold)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<Contour> SplitToEdgedContours(List<MsdfGlyphSegment> segments)
        {
            var angleThreshold = 135;

            var res = new List<Contour>();
            var rawContours = SplitToRawContours(segments);

            foreach (var contour in rawContours)
            {
                var edgedContour = new Contour();
                var startIndex = 0;

                if (!FindFirstSharpAngle(contour, angleThreshold, out startIndex))
                {
                    var currentEdge = new Edge();
                    currentEdge.Segments = contour;
                    edgedContour.Edges.Add(currentEdge);
                }
                else
                {
                    var currentEdge = new Edge();
                    var index = startIndex;
                    var cnt = 0;
                    while (cnt < contour.Count)
                    {
                        var currentSeg = contour[index];
                        currentEdge.Segments.Add(currentSeg);
                        index++;

                        if (index == contour.Count)
                        {
                            index = 0;
                        }

                        var nextSeg = contour[index];

                        if (GlyphSegmentsMath.GetSegmentsAngle(currentSeg.Segment, nextSeg.Segment) < angleThreshold)
                        {
                            edgedContour.Edges.Add(currentEdge);
                            currentEdge = new Edge();
                        }

                        cnt++;
                    }
                }

                res.Add(edgedContour);
            }

            return res;
        }

        // --- MAIN FUNCS ---
        // edge is a list of connected segments which have no sharp corners within them
        private static void ColorEdges(List<MsdfGlyphSegment> segments)
        {
            var contours = SplitToEdgedContours(segments);

            segments.Clear();

            foreach (var contour in contours)
            {
                var currentColor = Color.FromRgba(255, contour.Edges.Count == 1 ? (byte)255 : (byte)0, 255, 255);

                foreach (var edge in contour.Edges)
                {
                    for (int i = 0; i < edge.Segments.Count; i++)
                    {
                        var currentSeg = edge.Segments[i];
                        currentSeg.MsdfColor = currentColor;
                        //currentSeg.MsdfColor = currentSeg.Direction.Length() < segmentLengthThreshold ? Colors.White : currentColor;

                        segments.Add(currentSeg);
                    }

                    currentColor = currentColor == Color.FromRgba(255, 255, 0, 255)
                        ? Color.FromRgba(0, 255, 255, 255)
                        : Color.FromRgba(255, 255, 0, 255);
                }
            }
        }

        private static SegmentGrid CreateChannelGrid(List<MsdfGlyphSegment> segments)
        {
            var lines = new List<LineSegment2D>(segments.Count);
            var channels = new List<int>(segments.Count);
            foreach (var segment in segments)
            {
                var mask = SegmentGrid.TrueDistanceChannel;
                if (MsdfGeneratorHelper.ApplyColorMask(segment.MsdfColor, true, false, false) != Colors.Black)
                {
                    mask |= 1;
                }

                if (MsdfGeneratorHelper.ApplyColorMask(segment.MsdfColor, false, true, false) != Colors.Black)
                {
                    mask |= 2;
                }

                if (MsdfGeneratorHelper.ApplyColorMask(segment.MsdfColor, false, false, true) != Colors.Black)
                {
                    mask |= 4;
                }

                lines.Add(segment.Segment);
                channels.Add(mask);
            }

            return new SegmentGrid(lines, channels);
        }

        private static ColoredDistance GetColoredDistances(SegmentSearch search, List<LineSegment2D>[] closest,
            Vector2 point, double range, double pxRange, bool isTtf)
        {
            // there can be up to two closest segments in case if point is close to segments' connection
            // we will store both and then determine the signed pseudo-distance
            // if these two signed pseudo-distances have different signs, use the one with negative, because the point is outside
            search.FindNearest(point, closest, SegmentGrid.TrueDistanceChannel);

            var coloredDistance = new ColoredDistance();
            coloredDistance.AlphaDistance = Normalized(
                GlyphSegmentsMath.GetSignedDistanceToSegmentsJoint(closest[3], point, false), range, isTtf);

            if (Texels(coloredDistance.AlphaDistance, pxRange) > ColorsUnusedBeyondTexels)
            {
                coloredDistance.RedDistance = coloredDistance.AlphaDistance;
                coloredDistance.GreenDistance = coloredDistance.AlphaDistance;
                coloredDistance.BlueDistance = coloredDistance.AlphaDistance;
                return coloredDistance;
            }

            search.FindNearest(point, closest, SegmentGrid.ColorChannels);

            coloredDistance.RedDistance = Normalized(
                GlyphSegmentsMath.GetSignedDistanceToSegmentsJoint(closest[0], point, true), range, isTtf);
            coloredDistance.GreenDistance = Normalized(
                GlyphSegmentsMath.GetSignedDistanceToSegmentsJoint(closest[1], point, true), range, isTtf);
            coloredDistance.BlueDistance = Normalized(
                GlyphSegmentsMath.GetSignedDistanceToSegmentsJoint(closest[2], point, true), range, isTtf);

            return coloredDistance;
        }

        private static double Normalized(double distance, double range, bool isTtf) =>
            (isTtf ? -distance : distance) / range + 0.5;

        public static GlyphTextureData CalculateBasicTextureData(
            Glyph glyph,
            uint originalSize,
            ushort unitsPerEm,
            uint margin,
            bool useProportionalSize = true)
        {
            if (!useProportionalSize)
            {
                return new GlyphTextureData(originalSize, originalSize, glyph.Index, margin,
                    glyph.RelatedCharacters.FirstOrDefault());
            }
            var glyphBoundingRectangle = glyph.BoundingRectangle;
            var widthRatio = (double)(glyphBoundingRectangle.Width) / unitsPerEm;
            var heightRatio = (double)(glyphBoundingRectangle.Height) / unitsPerEm;
            var size = new Size(Math.Ceiling(originalSize * widthRatio),
                Math.Ceiling(originalSize * heightRatio));
            
            var textureData = new GlyphTextureData((uint)size.Width, (uint)size.Height, glyph.Index, margin, glyph.RelatedCharacters.FirstOrDefault());

            return textureData;
        }

        /// <summary>
        /// Generates MSDF texture
        /// </summary>
        /// <param name="glyph">glyph to process</param>
        /// <param name="originalSize">Width and height of MSDF texture</param>
        /// <param name="pxRange">Pixel range for generation</param>
        /// <param name="unitsPerEm">Size of glyph width and height in em</param>
        /// <param name="margin"></param>
        /// <returns>MSDF color data in for of single-dimension array</returns>
        internal static List<MsdfGlyphSegment> MsdfSegments(List<LineSegment2D> glyphSegments)
        {
            var segments = new List<MsdfGlyphSegment>(glyphSegments.Count);
            var contourStart = 0;
            foreach (var segment in glyphSegments)
            {
                var start = segment.Start;
                if (segments.Count > contourStart && IsNear(start, segments[segments.Count - 1].Segment.End))
                {
                    start = segments[segments.Count - 1].Segment.End;
                }
                else if (segments.Count > contourStart)
                {
                    contourStart = segments.Count;
                }

                var end = segment.End;
                if (segments.Count > contourStart && IsNear(end, segments[contourStart].Segment.Start))
                {
                    end = segments[contourStart].Segment.Start;
                }

                if (!IsNear(start, end))
                {
                    segments.Add(new MsdfGlyphSegment(start, end));
                }
            }

            return segments;
        }

        private static bool IsNear(Vector2 point, Vector2 other) =>
            Math.Abs(point.X - other.X) < VertexTolerance && Math.Abs(point.Y - other.Y) < VertexTolerance;

        public static GlyphTextureData GenerateDirectMSDF(
            this Glyph glyph,
            uint originalSize,
            double pxRange,
            ushort unitsPerEm,
            uint margin)
        {
            var glyphSegments = glyph.GetMergedOutlineSegments();
            if (glyphSegments.Count == 0)
            {
                return null;
            }

            var segments = MsdfSegments(glyphSegments);
            if (segments.Count == 0)
            {
                return null;
            }

            var glyphBoundingRectangle = glyph.BoundingRectangle;

            // One scale for X and Y against the em, not the font's max-glyph bbox, which outliers inflate; a glyph
            // larger than the em is scaled down uniformly to fit its cell.
            var unitScale = (double)originalSize / unitsPerEm;
            var glyphWidth = glyphBoundingRectangle.Width * unitScale;
            var glyphHeight = glyphBoundingRectangle.Height * unitScale;
            var overflow = Math.Max(glyphWidth, glyphHeight) / originalSize;
            if (overflow > 1.0)
            {
                glyphWidth /= overflow;
                glyphHeight /= overflow;
            }
            var size = new Size(
                Math.Max(1, Math.Ceiling(glyphWidth)),
                Math.Max(1, Math.Ceiling(glyphHeight)));

            // 1. Color all segments
            ColorEdges(segments);

            // 2. Signed-distance normalization. 'range' (in glyph units) is the distance that maps to one
            // normalized unit; GetRange ties it to pxRange so the field saturates ~pxRange/2 texels off the
            // contour.
            var scaleX = size.Width / glyphBoundingRectangle.Width;
            var scaleY = size.Height / glyphBoundingRectangle.Height;
            var range = MsdfGeneratorHelper.GetRange(pxRange, scaleX, scaleY);

            // Tight per-glyph bitmap = body + the field margin (= pxRange/2) on each side. The field is
            // computed across the whole bitmap, so it is fully filled: glyph body + the outside ramp in the
            // margin. The margin is exactly pxRange/2 wide, so the ramp stays a gradient to the edge (no flat
            // black border). All derived from the params - nothing hardcoded.
            var marginPx = (int)margin;
            var fullWidth = (int)size.Width + marginPx * 2;
            var fullHeight = (int)size.Height + marginPx * 2;
            var textureData = new GlyphTextureData((uint)size.Width, (uint)size.Height, glyph.Index, margin,
                glyph.RelatedCharacters.FirstOrDefault());

            var coloredDistances = new ColoredDistance[fullWidth, fullHeight];
            var isTtf = glyph.OutlineType == OutlineType.TrueType;
            var grid = CreateChannelGrid(segments);
            var bandHeight = Math.Max(MinBandRows, (fullHeight + Environment.ProcessorCount - 1) / Environment.ProcessorCount);
            var bands = (fullHeight + bandHeight - 1) / bandHeight;

            Parallel.For(0, bands, band =>
            {
                var search = new SegmentSearch(grid);
                List<LineSegment2D>[] closest = [[], [], [], []];
                for (var y = band * bandHeight; y < Math.Min(fullHeight, (band + 1) * bandHeight); ++y)
                {
                    for (var x = 0; x < fullWidth; ++x)
                    {
                        // Texels [margin, margin+size) map onto the glyph bbox; the margin ring extrapolates
                        // just outside it.
                        var cx = x - marginPx;
                        var cy = y - marginPx;
                        var samplingPoint =
                            new Vector2(glyphBoundingRectangle.Width / size.Width * (cx + 0.5) + glyphBoundingRectangle.X,
                                glyphBoundingRectangle.Height - (glyphBoundingRectangle.Height / size.Height * (cy + 0.5) -
                                                                 glyphBoundingRectangle.Y));

                        // Full multichannel field everywhere (no collapse to the true distance): the R/G/B
                        // pseudo-distances are the actual MSDF and must fill the cell - that's the colored field.
                        coloredDistances[x, y] = GetColoredDistances(search, closest, samplingPoint, range, pxRange, isTtf);
                    }
                }
            });

            // 4. Fix artifacts: away from the outline the true distance stands in for channels that mislead - past an
            // acute corner an edge's extension holds a channel at the contour's level, drawn as a line out to the cell's
            // edge - and far from it for all of them. Near the outline corners keep their channels, for a bold or a
            // weight that moves the contour too. (The simple clash detector, FixArtifacts, stays off: it collapsed
            // legitimate corners.)
            UseTrueDistanceAwayFromOutline(coloredDistances, fullWidth, fullHeight, pxRange);

            // 5. Normalize MSDF/SDF to [0 .. 255], written contiguously over the whole bitmap.
            var index = 0;
            for (var y = 0; y < fullHeight; y++)
            {
                for (var x = 0; x < fullWidth; x++)
                {
                    var distance = coloredDistances[x, y];
                    textureData.Pixels[index + 0] = MsdfGeneratorHelper.PixelFloatToByte(distance.RedDistance);
                    textureData.Pixels[index + 1] = MsdfGeneratorHelper.PixelFloatToByte(distance.GreenDistance);
                    textureData.Pixels[index + 2] = MsdfGeneratorHelper.PixelFloatToByte(distance.BlueDistance);
                    textureData.Pixels[index + 3] = MsdfGeneratorHelper.PixelFloatToByte(distance.AlphaDistance);
                    index += 4;
                }
            }

            return textureData;
        }
        
        public static void GenerateMSDFForExistingTextureData(
            Glyph glyph,
            GlyphTextureData textureData,
            double pxRange,
            ushort unitsPerEm)
        {
            var glyphSegments = glyph.GetMergedOutlineSegments();
            if (glyphSegments.Count == 0)
            {
                return;
            }

            var segments = MsdfSegments(glyphSegments);
            if (segments.Count == 0)
            {
                return;
            }

            // 1. Color all segments
            ColorEdges(segments);

            var glyphBoundingRectangle = glyph.BoundingRectangle;

            var size = textureData.BoundingRect.Size;

            // 2. Generate colored pseudo-distance field
            var coloredDistances = new ColoredDistance[(int)size.Width, (int)size.Height];

            var scaleX = size.Width / glyphBoundingRectangle.Width;
            var scaleY = size.Height / glyphBoundingRectangle.Height;

            var range = MsdfGeneratorHelper.GetRange(pxRange, scaleX, scaleY);

            var additionalSpace = glyphBoundingRectangle.Width * 0.5;

            ColoredDistance minColoredDistance;

            var value = -(unitsPerEm / 2) / range + 0.5;
            minColoredDistance.RedDistance = value;
            minColoredDistance.GreenDistance = value;
            minColoredDistance.BlueDistance = value;
            minColoredDistance.AlphaDistance = value;

            var search = new SegmentSearch(CreateChannelGrid(segments));
            List<LineSegment2D>[] closest = [[], [], [], []];
            var paddingX = glyphBoundingRectangle.Width * 0.1f;
            var paddingY = glyphBoundingRectangle.Height * 0.1f;
            glyphBoundingRectangle.Width += (int)(2 * paddingX);
            glyphBoundingRectangle.Height += (int)(2 * paddingY);
            //var paddingX = 0;
            //var paddingY = 0;

            for (var y = 0; y < size.Height; ++y)
            {
                for (var x = 0; x < size.Width; ++x)
                {
                    // determine the closest segment to current sampling point
                    //var samplingPoint =
                    //    new Vector2(glyphBoundingRectangle.Width / size.Width * (x + 0.5) + glyphBoundingRectangle.X,
                    //        glyphBoundingRectangle.Height - (glyphBoundingRectangle.Height / size.Height * (y + 0.5) -
                    //                                         glyphBoundingRectangle.Y));
                    var samplingPoint =
                            new Vector2(glyphBoundingRectangle.Width / size.Width * (x + 0.5) + glyphBoundingRectangle.X - paddingX,
                                glyphBoundingRectangle.Height - (glyphBoundingRectangle.Height / size.Height * (y + 0.5) -
                                                                 glyphBoundingRectangle.Y + paddingY));

                    if (samplingPoint.X >= glyphBoundingRectangle.X - additionalSpace &&
                        samplingPoint.X <= glyphBoundingRectangle.Right + additionalSpace &&
                        samplingPoint.Y >= glyphBoundingRectangle.Y - additionalSpace &&
                        samplingPoint.Y <= glyphBoundingRectangle.Bottom + additionalSpace)
                    {
                        coloredDistances[x, y] = GetColoredDistances(search, closest, samplingPoint, range, pxRange, glyph.OutlineType == OutlineType.TrueType);
                    }
                    else
                    {
                        coloredDistances[x, y] = minColoredDistance;
                    }
                }
            }

            // 3. Fix artifacts, as GenerateDirectMSDF does.
            UseTrueDistanceAwayFromOutline(coloredDistances, (int)size.Width, (int)size.Height, pxRange);

            // 4. Normalize MSDF and SDF to [0 .. 255] range
            var margin = (int)textureData.Margin;
            var rowStride = (size.Width + (margin * 2)) * 4;
            for (var y = 0; y < size.Height; y++)
            {
                var index = (int)(rowStride * (y + margin)) + (margin * 4);
                for (var x = 0; x < size.Width; x++)
                {
                    var distance = coloredDistances[x, y];
                    var red = MsdfGeneratorHelper.PixelFloatToByte(distance.RedDistance);
                    var green = MsdfGeneratorHelper.PixelFloatToByte(distance.GreenDistance);
                    var blue = MsdfGeneratorHelper.PixelFloatToByte(distance.BlueDistance);
                    var alpha = MsdfGeneratorHelper.PixelFloatToByte(distance.AlphaDistance);

                    textureData.Pixels[index + 0] = red;
                    textureData.Pixels[index + 1] = green;
                    textureData.Pixels[index + 2] = blue;
                    textureData.Pixels[index + 3] = alpha;

                    index += 4;
                }
            }
        }
        
        // --- ARTIFACT FIXING ---
        // true - no collision, false - collision
        private static bool CheckNeighbor(ColoredDistance neighbor, ColoredDistance current, double threshold)
        {
            var cnt = 0;

            // Distances are normalized (/range + 0.5), so the contour is at 0.5: inside >= 0.5, outside < 0.5.
            bool isNeighborRedPositive = neighbor.RedDistance >= 0.5;
            bool isNeighborGreenPositive = neighbor.GreenDistance >= 0.5;
            bool isNeighborBluePositive = neighbor.BlueDistance >= 0.5;

            bool isCurrentRedPositive = current.RedDistance >= 0.5;
            bool isCurrentGreenPositive = current.GreenDistance >= 0.5;
            bool isCurrentBluePositive = current.BlueDistance >= 0.5;

            if (isNeighborRedPositive ^ isCurrentRedPositive &&
                Math.Abs(neighbor.RedDistance - current.RedDistance) > threshold)
            {
                ++cnt;
            }

            if (isNeighborGreenPositive ^ isCurrentGreenPositive &&
                Math.Abs(neighbor.GreenDistance - current.GreenDistance) > threshold)
            {
                ++cnt;
            }

            if (isNeighborBluePositive ^ isCurrentBluePositive &&
                Math.Abs(neighbor.BlueDistance - current.BlueDistance) > threshold)
            {
                ++cnt;
            }

            return cnt < 2;
        }

        // true - no collision, false - collision
        private static bool CheckForCollision(List<ColoredDistance> neighbors, ColoredDistance current, double threshold)
        {
            foreach (var neighbor in neighbors)
            {
                if (!CheckNeighbor(neighbor, current, threshold))
                {
                    return false;
                }
            }

            return true;
        }

        private static void UseTrueDistanceAwayFromOutline(ColoredDistance[,] data, int width, int height, double pxRange)
        {
            var replace = new bool[width, height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    replace[x, y] |= Texels(data[x, y].AlphaDistance, pxRange) > FarBandTexels;
                    if (x + 1 < width)
                    {
                        MarkMisleading(data, replace, pxRange, x, y, x + 1, y);
                    }

                    if (y + 1 < height)
                    {
                        MarkMisleading(data, replace, pxRange, x, y, x, y + 1);
                    }

                    if (x + 1 < width && y + 1 < height)
                    {
                        MarkMisleading(data, replace, pxRange, x, y, x + 1, y + 1);
                    }
                }
            }

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (!replace[x, y])
                    {
                        continue;
                    }

                    var distance = data[x, y];
                    distance.RedDistance = distance.AlphaDistance;
                    distance.GreenDistance = distance.AlphaDistance;
                    distance.BlueDistance = distance.AlphaDistance;
                    data[x, y] = distance;
                }
            }
        }

        private static void MarkMisleading(ColoredDistance[,] data, bool[,] replace, double pxRange, int x0, int y0,
            int x1, int y1)
        {
            double red = 0, green = 0, blue = 0, alpha = 0;
            for (var y = y0; y <= y1; y++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    ref var distance = ref data[x, y];
                    red += distance.RedDistance;
                    green += distance.GreenDistance;
                    blue += distance.BlueDistance;
                    alpha += distance.AlphaDistance;
                }
            }

            var count = (x1 - x0 + 1) * (y1 - y0 + 1);
            var median = MsdfGeneratorHelper.Median(red, green, blue) / count;
            alpha /= count;
            var wrongSide = median > 0.5 != alpha > 0.5;
            if (!wrongSide && Texels(alpha, pxRange) - Texels(median, pxRange) <= MisleadingTexels)
            {
                return;
            }

            for (var y = y0; y <= y1; y++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    replace[x, y] |= Texels(data[x, y].AlphaDistance, pxRange) > NearBandTexels;
                }
            }
        }

        private static double Texels(double normalized, double pxRange) => Math.Abs(normalized - 0.5) * pxRange;

        private static void FixArtifacts(ColoredDistance[,] data, int width, int height)
        {
            // Clash threshold in normalized units (field is /range + 0.5, so ~0.33 change per texel at
            // PixelRange=3). Corners legitimately flip 2 channels by ~0.33; only flag clearly larger jumps
            // as real clashes, otherwise normal edges/corners get destroyed. Retune if PixelRange changes.
            var threshold = 0.5;
            var correctionList = new List<CorrectionLocation>();

            for (var y = 1; y < height - 1; y++)
            {
                for (var x = 1; x < width - 1; x++)
                {
                    var current = data[x, y];
                    var neighbors = new List<ColoredDistance>
                    {
                        // get 8 neighbors of the current pixel
                        data[x - 1, y - 1],
                        data[x, y - 1],
                        data[x + 1, y - 1],
                        data[x - 1, y],
                        data[x + 1, y],
                        data[x - 1, y + 1],
                        data[x, y + 1],
                        data[x + 1, y + 1]
                    };

                    if (CheckForCollision(neighbors, current, threshold)) continue;

                    var correction = new CorrectionLocation
                    {
                        X = x,
                        Y = y
                    };

                    correctionList.Add(correction);
                }
            }

            foreach (var correction in correctionList)
            {
                var pixel = data[correction.X, correction.Y];
                var median = MsdfGeneratorHelper.Median(pixel.RedDistance, pixel.GreenDistance, pixel.BlueDistance);

                pixel.RedDistance = pixel.GreenDistance = pixel.BlueDistance = median;

                data[correction.X, correction.Y] = pixel;
            }
        }
    }
}
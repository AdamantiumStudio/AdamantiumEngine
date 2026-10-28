using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Exceptions;
using Adamantium.Fonts.Parsers.CFF;
using Adamantium.Fonts.Tables.CFF;
using Adamantium.Fonts.TextureGeneration;
using Adamantium.Mathematics;
using Matrix3x2 = Adamantium.Mathematics.Matrix3x2<double>;
using Vector2 = Adamantium.Mathematics.Vector2<double>;

namespace Adamantium.Fonts
{
    public class Glyph
    {
        private const double SideProbe = 1e-3;
        private const double VertexTolerance = 1e-3;

        private readonly object lockObject = new object();

        private HashSet<UInt32> uniqueUnicodes;
        private List<UInt32> unicodes;
        private List<Outline> outlines;
        private bool isSplitOnSegments;
        private List<LineSegment2D> mergedOutlinesSegments;
        private readonly Dictionary<uint, SampledOutline[]> sampledOutlinesCache;
        private IGlyphOutlineSource outlineSource;
        private bool isLoadingOutlines;
        private bool isInvalid;
        private bool isComposite;
        private Rectangle<int> boundingRectangle;
        private byte[] instructions;

        internal IReadOnlyCollection<Outline> Outlines
        {
            get
            {
                EnsureOutlines();
                return outlines.AsReadOnly();
            }
        }

        public uint Index { get; }
        public OutlineType OutlineType { get; internal set; }
        public double LeftSideBearingMultiplier { get; private set; }
        public double TopSideBearingMultiplier { get; private set; }
        public double AdvanceWidthMultiplier { get; private set; }
        
        public Vector2<double> CenterToBaseLineMultiplier { get; private set; }
        public bool IsEmpty { get; }
        public uint Unicode => unicodes.FirstOrDefault();
        public IReadOnlyCollection<UInt32> Unicodes => unicodes.AsReadOnly();
        public string Name { get; internal set; }
        internal UInt32 SID { get; set; }
        /// <summary>True when the glyph's data could not be read; it then has no outlines.</summary>
        public bool IsInvalid
        {
            get
            {
                EnsureOutlines();
                return isInvalid;
            }
            set => isInvalid = value;
        }

        public ushort AdvanceWidth { get; internal set; }
        public ushort AdvanceHeight { get; internal set; }
        public short LeftSideBearing { get; internal set; }
        public short TopSideBearing { get; internal set; }

        /// <summary>True for a TrueType glyph built from other glyphs.</summary>
        public bool IsComposite
        {
            get
            {
                EnsureOutlines();
                return isComposite;
            }
            internal set => isComposite = value;
        }

        public Rectangle<int> BoundingRectangle
        {
            get
            {
                EnsureOutlines();
                return boundingRectangle;
            }
            internal set => boundingRectangle = value;
        }

        public GlyphClassDefinition ClassDefinition { get; internal set; }
        internal List<CompositeGlyphComponent> CompositeGlyphComponents;

        public bool HasOutlines
        {
            get
            {
                EnsureOutlines();
                return outlines.Count > 0;
            }
        }

        internal byte[] Instructions
        {
            get
            {
                EnsureOutlines();
                return instructions;
            }
        }
        
        public List<char> RelatedCharacters { get; }
        
        internal static Glyph EmptyGlyph(uint index)
        {
            return new Glyph(index, true, OutlineType.Unknown);
        }

        public Glyph(uint index, OutlineType outlineType)
        {
            Index = index;
            Name = String.Empty;
            outlines = new List<Outline>();
            sampledOutlinesCache = new Dictionary<uint, SampledOutline[]>();
            unicodes = new List<uint>();
            uniqueUnicodes = new HashSet<uint>();
            CompositeGlyphComponents = new List<CompositeGlyphComponent>();
            OutlineType = outlineType;
            RelatedCharacters = new List<char>();
        }

        private Glyph(uint index, bool isEmpty, OutlineType outlineType) : this(index, outlineType)
        {
            IsEmpty = isEmpty;
        }

        public void CalculateEmRelatedMultipliers(ushort unitsPerEm)
        {
            LeftSideBearingMultiplier = (double)LeftSideBearing / unitsPerEm;
            TopSideBearingMultiplier = (double)TopSideBearing / unitsPerEm;
            AdvanceWidthMultiplier = (double)AdvanceWidth / unitsPerEm;

            if (AdvanceWidthMultiplier == 0.0)
            {
                AdvanceWidthMultiplier = (double)AdvanceWidth / unitsPerEm;
            }
            
            var emSquare = new Rectangle<int>(0, 0, unitsPerEm, unitsPerEm);
            var diff = emSquare.Center - BoundingRectangle.Center;
            CenterToBaseLineMultiplier = new Vector2<double>(diff.X / unitsPerEm, diff.Y / unitsPerEm);
        }

        public Vector3F[] Sample(byte rate)
        {
            if (IsEmpty)
            {
                return Array.Empty<Vector3F>();
            }
            
            if (rate == 0) rate = 1;

            lock (lockObject)
            {
                if (!isSplitOnSegments)
                {
                    SplitOnSegments();
                }

                if (!sampledOutlinesCache.TryGetValue(rate, out var sampledOutlines))
                {
                    sampledOutlines = this.GenerateOutlines(rate);
                    sampledOutlinesCache[rate] = sampledOutlines;
                }
                var points = RemoveSelfIntersections(sampledOutlines);

                //AutoHint();

                return points;
            }
        }

        private void SplitOnSegments()
        {
            if (OutlineType == OutlineType.TrueType)
            {
                SplitOnSegmentsTTF();
            }
            else
            {
                SplitOnSegmentsCFF();
            }

            isSplitOnSegments = true;
        }

        private void SplitOnSegmentsTTF()
        {
            foreach (var outline in Outlines)
            {
                var segment = new OutlineSegment();
                
                // outline should start from non-control point
                if (outline.Points[0].IsControl)
                {
                    segment.AddPoint(outline.Points.Last());
                }
                
                outline.AddSegment(segment);
                for (var index = 0; index < outline.Points.Count; index++)
                {
                    var point = outline.Points[index];

                    switch (segment.Points.Count)
                    {
                        case 0:
                            segment.AddPoint(point);
                            continue;
                        case 1 when !point.IsControl:
                            segment.AddPoint(point);
                            segment = new OutlineSegment();
                            segment.Points.Add(point);
                            outline.AddSegment(segment);
                            break;
                        case 1:
                            segment.Points.Add(point);
                            break;
                        case 2 when !point.IsControl:
                            segment.AddPoint(point);
                            segment = new OutlineSegment();
                            segment.Points.Add(point);
                            outline.AddSegment(segment);
                            break;
                        case 2:
                        {
                            // 2 points in a row is control points. So, we need to calculate half point and
                            // add it as last segment point
                            var prevPoint = segment.Points[1];
                            var halfPointX = (prevPoint.X + point.X) / 2;
                            var halfPointY = (prevPoint.Y + point.Y) / 2;
                            var lastPoint = new Vector2<double>(halfPointX, halfPointY);
                            segment.AddPoint(lastPoint);
                            segment = new OutlineSegment();
                            segment.AddPoint(lastPoint);
                            segment.AddPoint(point);
                            outline.AddSegment(segment);
                            break;
                        }
                    }
                    
                    if (IsLastSegment())
                    {
                        segment.Points.Add(outline.Segments[0].Points[0]);
                    }

                    bool IsLastSegment()
                    {
                        return index == outline.Points.Count - 1;
                    }
                }
            }
        }
        
        private void SplitOnSegmentsCFF()
        {
            foreach (var outline in Outlines)
            {
                // we assume that the outline starting from non-control point
                if (outline.Points[0].IsControl)
                {
                    throw new OutlineException("First point of outline should not be control point");
                }
                
                var segment = new List<Vector2<double>>();
                
                for (var index = 0; index < outline.Points.Count; index++)
                {
                    var point = outline.Points[index];
                    segment.Add(point);

                    if (!point.IsControl && segment.Count > 1) // segment is closed
                    {
                        outline.Segments.Add(new OutlineSegment(segment));
                        segment = new List<Vector2<double>>();
                        segment.Add(point); // add the same non-control point as start of new segment
                    }
                }
                
                // currently segment must contain exactly one point
                if (segment.Count != 1)
                {
                    throw new Exception($"Segment must contain 1 point currently, actual points count = {segment.Count}");
                }

                // add the first point of current outline as the last point of the last segment
                // but only if these two points are not equal
                // in some cases outline assumes we build this list segment (like in 'A')
                // but in some cases outline's last and first points are equal (like in 'O'), so we do not need to build this last segment
                if (segment[0] != outline.Points[0])
                {
                    segment.Add(outline.Points[0]);
                    outline.Segments.Add(new OutlineSegment(segment));
                }
            }
        }

        internal void AddOutline(Outline outline)
        {
            outlines.Add(outline);
        }

        internal void AddComponentOutlines(Func<uint, Glyph> glyphAt)
        {
            foreach (var component in CompositeGlyphComponents)
            {
                var componentGlyph = glyphAt(component.SimpleGlyphIndex);
                var componentOutlines = componentGlyph.TransformBasicOutlines(component.TransformMatrix);
                if (component.IsAnchored)
                {
                    AnchorOutlines(componentOutlines, component.ParentPoint, component.ChildPoint);
                }

                outlines.AddRange(componentOutlines);
            }
        }

        private void AnchorOutlines(List<Outline> componentOutlines, int parentPoint, int childPoint)
        {
            var parent = outlines.SelectMany(o => o.Points).Skip(parentPoint).Take(1).ToArray();
            var child = componentOutlines.SelectMany(o => o.Points).Skip(childPoint).Take(1).ToArray();
            if (parent.Length == 0 || child.Length == 0)
            {
                return;
            }

            var dx = parent[0].X - child[0].X;
            var dy = parent[0].Y - child[0].Y;
            foreach (var outline in componentOutlines)
            {
                for (var i = 0; i < outline.Points.Count; i++)
                {
                    var point = outline.Points[i];
                    outline.Points[i] = new OutlinePoint(point.X + dx, point.Y + dy, point.IsControl);
                }
            }
        }

        public SampledOutline[] TransformOutlines(Matrix3x2<double> matrix, byte rate)
        {
            var transformedOutlines = new List<SampledOutline>();
            if (sampledOutlinesCache.TryGetValue(rate, out var outlines))
            {
                foreach (var outline in outlines)
                {
                    var points = TransformPoints(outline.Points, matrix);
                    
                    var transformedOutline = new SampledOutline(points);
                    transformedOutlines.Add(transformedOutline);
                }
            }
            
            return transformedOutlines.ToArray();
        }

        public List<Outline> TransformBasicOutlines(Matrix3x2<double> matrix)
        {
            List<Outline> transformedOutlines = new List<Outline>();
            foreach (var outline in Outlines)
            {
                var transformOutline = TransformOutline(outline.Points, matrix);
                transformedOutlines.Add(transformOutline);
            }

            return transformedOutlines;
        }

        public List<LineSegment2D> GetMergedOutlineSegments() => new(mergedOutlinesSegments);

        /// <summary>The outline sampled at <paramref name="rate"/>, as one set of segments with no self-intersections:
        /// sampled and copied under the glyph's lock, so a glyph sampled on another thread at the same time - the
        /// atlas rasterizing it - is read whole. Empty for a glyph with no outline.</summary>
        public List<LineSegment2D> GetMergedOutlineSegments(byte rate)
        {
            if (IsEmpty)
            {
                return [];
            }

            lock (lockObject)
            {
                Sample(rate);
                return new List<LineSegment2D>(mergedOutlinesSegments ?? []);
            }
        }

        internal void SetOutlinesForRate(byte rate, SampledOutline[] outlines)
        {
            sampledOutlinesCache[rate] = outlines;
        }
        
        private Vector2<double>[] TransformPoints(IEnumerable<Vector2<double>> points, Matrix3x2<double> matrix)
        {
            var transformedPoints = new List<Vector2<double>>();
            foreach (var point in points)
            {
                var transformed = Vector2<double>.Transform(point, matrix);
                transformedPoints.Add(transformed);
            }

            return transformedPoints.ToArray();
        }
        
        private Outline TransformOutline(IEnumerable<OutlinePoint> points, Matrix3x2<double> matrix)
        {
            var transformedOutline = new Outline();
            foreach (var point in points)
            {
                var transformed = Vector2<double>.Transform((Vector2<double>)point, matrix);
                transformedOutline.Points.Add(new OutlinePoint(transformed, point.IsControl));
            }

            return transformedOutline;
        }

        internal void SetUnicodes(IEnumerable<UInt32> unicodeSet)
        {
            foreach (var unicode in unicodeSet)
            {
                if (uniqueUnicodes.Add(unicode))
                {
                    unicodes.Add(unicode);
                }
            }
        }

        internal Glyph RecalculateBounds(bool includeControlPoints = false)
        {
            if (IsEmpty) return this;

            var allPoints = Outlines.SelectMany(x => x.Points).Where(x => includeControlPoints || !x.IsControl).ToArray();
            if (allPoints.Length == 0)
                allPoints = Outlines.SelectMany(x => x.Points).ToArray();
            if (allPoints.Length == 0)
            {
                BoundingRectangle = Rectangle.CreateFromCorners<int>(0, 0, 0, 0);
                return this;
            }
            var minX = (int)Math.Floor(allPoints.Min(x => x.X));
            var minY = (int)Math.Floor(allPoints.Min(x => x.Y));
            var maxX = (int)Math.Ceiling(allPoints.Max(x => x.X));
            var maxY = (int)Math.Ceiling(allPoints.Max(x => x.Y));
            BoundingRectangle = Rectangle.CreateFromCorners<int>(minX, minY, maxX, maxY);

            return this;
        }

        internal void SetInstructions(byte[] glyphInstructions)
        {
            instructions = glyphInstructions;
        }
        
        public override string ToString()
        {
            return $"Name: {Name} Index: {Index}, SID: {SID} Unicodes: {string.Join(", ", Unicodes)}";
        }

        internal static Glyph Create(uint index, OutlineType outlineType)
        {
            return new Glyph(index, outlineType);
        }

        internal void SetOutlineSource(IGlyphOutlineSource source)
        {
            outlineSource = source;
        }

        private void EnsureOutlines()
        {
            var source = Volatile.Read(ref outlineSource);
            if (source == null)
            {
                return;
            }

            lock (source)
            {
                if (outlineSource == null || isLoadingOutlines)
                {
                    return;
                }

                isLoadingOutlines = true;
                try
                {
                    source.LoadOutlines(this);
                }
                catch (Exception)
                {
                    outlines.Clear();
                    boundingRectangle = default;
                    isInvalid = true;
                }
                finally
                {
                    isLoadingOutlines = false;
                    Volatile.Write(ref outlineSource, null);
                }
            }
        }

        internal Glyph FillOutlines(List<Command> commands, VariationRegionList regionList = null, float[] variationPoint = null)
        {
            var interpreter = new CommandInterpreter();
            Outline outline = null;

            foreach (var command in commands)
            {
                if (command.IsNewOutline())
                {
                    outline = new Outline();
                    AddOutline(outline);
                }

                var pts = interpreter.GetOutlinePoints(command, regionList, variationPoint);
                outline?.Points.AddRange(pts);
            }

            return this;
        }

        private Vector3F[] RemoveSelfIntersections(in SampledOutline[] outlines)
        {
            // Built apart and then put in place whole: the outline is read without the lock while it is made again.
            var merged = new List<LineSegment2D>();
            var contours = outlines.Where(x => x.Segments is { Length: > 0 }).Select(x => x.Segments).ToArray();
            var segments = contours.SelectMany(x => x).ToArray();
            var contourOf = contours.SelectMany((x, index) => Enumerable.Repeat(index, x.Length)).ToArray();
            var cuts = new List<(double At, Vector2<double> Point)>[segments.Length];
            var crossed = new bool[contours.Length];

            for (var i = 0; i < segments.Length; i++)
            {
                for (var j = i + 1; j < segments.Length; j++)
                {
                    var first = segments[i];
                    var second = segments[j];
                    if (Collision2D.SegmentSegmentIntersection(ref first, ref second, out var point))
                    {
                        if (AddCut(cuts, segments, i, point) | AddCut(cuts, segments, j, point))
                        {
                            crossed[contourOf[i]] = crossed[contourOf[j]] = true;
                        }
                    }
                    else if (Overlap(first, second))
                    {
                        AddCut(cuts, segments, i, second.Start);
                        AddCut(cuts, segments, i, second.End);
                        AddCut(cuts, segments, j, first.Start);
                        AddCut(cuts, segments, j, first.End);
                        crossed[contourOf[i]] = crossed[contourOf[j]] = true;
                    }
                }
            }

            var fillsOnLeft = OutlineType != OutlineType.TrueType;
            var whole = new bool?[contours.Length];
            var reversed = new bool[contours.Length];
            for (var c = 0; c < contours.Length; c++)
            {
                if (!crossed[c])
                {
                    var longest = contours[c][0];
                    foreach (var segment in contours[c])
                    {
                        if (segment.Direction.Length() > longest.Direction.Length())
                        {
                            longest = segment;
                        }
                    }

                    whole[c] = IsBoundary(segments, longest.Start, longest.End, out var filledOnLeft);
                    reversed[c] = filledOnLeft != fillsOnLeft;
                }
            }

            var kept = new HashSet<(Vector2<double> Start, Vector2<double> End)>();
            var pieces = new List<(double At, Vector2<double> Point)>();
            for (var i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (Vector2<double>.NearEqual(segment.Start, segment.End))
                {
                    continue;
                }

                if (whole[contourOf[i]] is { } boundary)
                {
                    if (boundary)
                    {
                        merged.Add(reversed[contourOf[i]] ? new LineSegment2D(segment.End, segment.Start) : segment);
                    }

                    continue;
                }

                pieces.Clear();
                pieces.Add((0, segment.Start));
                pieces.Add((1, segment.End));
                if (cuts[i] != null)
                {
                    pieces.AddRange(cuts[i]);
                }

                pieces.Sort((left, right) => left.At.CompareTo(right.At));
                for (var k = 1; k < pieces.Count; k++)
                {
                    var start = pieces[k - 1].Point;
                    var end = pieces[k].Point;
                    if (start != end && IsBoundary(segments, start, end, out var filledOnLeft) && kept.Add((start, end)))
                    {
                        merged.Add(filledOnLeft == fillsOnLeft
                            ? new LineSegment2D(start, end)
                            : new LineSegment2D(end, start));
                    }
                }
            }

            mergedOutlinesSegments = merged;
            var points = new List<Vector3F>();

            foreach (var segment in merged)
            {
                points.Add(new Vector3F((float)segment.Start.X, (float)segment.Start.Y, 0));
                points.Add(new Vector3F((float)segment.End.X, (float)segment.End.Y, 0));
            }

            return points.ToArray();
        }

        private static bool AddCut(List<(double At, Vector2<double> Point)>[] cuts, LineSegment2D[] segments, int index,
            Vector2<double> point)
        {
            var at = At(segments[index], point);
            if (at <= 0 || at >= 1 || IsNear(point, segments[index].Start) || IsNear(point, segments[index].End))
            {
                return false;
            }

            (cuts[index] ??= []).Add((at, point));
            return true;
        }

        private static bool IsNear(Vector2<double> point, Vector2<double> vertex) =>
            Math.Abs(point.X - vertex.X) < VertexTolerance && Math.Abs(point.Y - vertex.Y) < VertexTolerance;

        private static double At(LineSegment2D segment, Vector2<double> point)
        {
            var direction = segment.Direction;
            var squared = direction.X * direction.X + direction.Y * direction.Y;
            if (squared == 0)
            {
                return 0;
            }

            var offset = point - segment.Start;
            return (offset.X * direction.X + offset.Y * direction.Y) / squared;
        }

        private static bool Overlap(LineSegment2D first, LineSegment2D second)
        {
            if (Vector2<double>.Determinant(first.Direction, second.Direction) != 0
                || Vector2<double>.Determinant(first.Direction, second.Start - first.Start) != 0)
            {
                return false;
            }

            var start = At(first, second.Start);
            var end = At(first, second.End);
            return Math.Max(Math.Min(start, end), 0) < Math.Min(Math.Max(start, end), 1);
        }

        private static bool IsBoundary(LineSegment2D[] segments, Vector2<double> start, Vector2<double> end, out bool filledOnLeft)
        {
            var direction = end - start;
            var side = new Vector2<double>(-direction.Y, direction.X) * (SideProbe / direction.Length());
            var middle = (start + end) * 0.5;
            filledOnLeft = IsFilled(segments, middle + side);
            return filledOnLeft != IsFilled(segments, middle - side);
        }

        private static bool IsFilled(LineSegment2D[] segments, Vector2<double> point)
        {
            var winding = 0;
            foreach (var segment in segments)
            {
                var side = Vector2<double>.Determinant(segment.End - segment.Start, point - segment.Start);
                if (segment.Start.Y <= point.Y && segment.End.Y > point.Y && side > 0)
                {
                    winding++;
                }
                else if (segment.End.Y <= point.Y && segment.Start.Y > point.Y && side < 0)
                {
                    winding--;
                }
            }

            return winding != 0;
        }

        private void AutoHint()
        {
            var indicesToHint = new List<int>();

            for (var i = 0; i < mergedOutlinesSegments.Count; i++)
            {
                if (mergedOutlinesSegments[i].Start.X == mergedOutlinesSegments[i].End.X ||
                    mergedOutlinesSegments[i].Start.Y == mergedOutlinesSegments[i].End.Y)
                {
                    indicesToHint.Add(i);
                }
            }
            
            foreach (var index in indicesToHint)
            {
                var prevIndex = index > 0 ? index - 1 : mergedOutlinesSegments.Count - 1;
                var currentIndex = index;
                var nextIndex = index < (mergedOutlinesSegments.Count - 1) ? index + 1 : 0;

                var prevSegment = mergedOutlinesSegments[prevIndex];
                var currentSegment = mergedOutlinesSegments[currentIndex];
                var nextSegment = mergedOutlinesSegments[nextIndex];

                var hintedCurrentStart = new Vector2<double>();
                var hintedCurrentEnd = new Vector2<double>();
                var hintedValue = 0.0;
                
                // vertical stem
                if (currentSegment.Start.X == currentSegment.End.X)
                {
                    hintedValue = Math.Round(currentSegment.Start.X);
                    hintedCurrentStart = new Vector2<double>(hintedValue, currentSegment.Start.Y);
                    hintedCurrentEnd = new Vector2<double>(hintedValue, currentSegment.End.Y);
                }
                
                // horizontal stem
                if (currentSegment.Start.Y == currentSegment.End.Y)
                {
                    hintedValue = Math.Round(currentSegment.Start.Y);
                    hintedCurrentStart = new Vector2<double>(currentSegment.Start.X, hintedValue);
                    hintedCurrentEnd = new Vector2<double>(currentSegment.End.X, hintedValue);
                }
                
                var hintedCurrentSegment = new LineSegment2D(hintedCurrentStart, hintedCurrentEnd);
                mergedOutlinesSegments[currentIndex] = hintedCurrentSegment;

                if (GlyphSegmentsMath.IsSegmentsConnected(ref prevSegment, ref currentSegment))
                {
                    var hintedPrevSegment = new LineSegment2D(prevSegment.Start, hintedCurrentStart);
                    mergedOutlinesSegments[prevIndex] = hintedPrevSegment;
                }
                    
                if (GlyphSegmentsMath.IsSegmentsConnected(ref currentSegment, ref nextSegment))
                {
                    var hintedNextSegment = new LineSegment2D(hintedCurrentEnd, nextSegment.End);
                    mergedOutlinesSegments[nextIndex] = hintedNextSegment;
                }
            }
        }

        public void GetSegments(out List<Vector3F> vertexList, out List<Color> colorList)
        {
            vertexList = new List<Vector3F>();
            colorList = new List<Color>();
            
            foreach (var mergedSegment in mergedOutlinesSegments)
            {
                var newStart = new Vector3F((float)mergedSegment.Start.X, (float)mergedSegment.Start.Y, 0);
                var newEnd = new Vector3F((float)mergedSegment.End.X, (float)mergedSegment.End.Y, 0);
                
                vertexList.Add(newStart);
                vertexList.Add(newEnd);
                
                colorList.Add(Colors.Red);
                colorList.Add(Colors.Red);
            }
        }
    }
}
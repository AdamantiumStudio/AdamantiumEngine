using System;
using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Fonts;

/// <summary>A cluster of text put on a path: where its middle stands and the way the path runs there, in radians from
/// the X axis toward Y.</summary>
public readonly record struct GlyphOnPath(Vector2<double> Point, double Angle);

/// <summary>
/// Text of one line laid along a polyline - a street's name along the street, a caption round a curve: each cluster of
/// the laid out text (<see cref="TextLayout.GetClusters"/>) stood on the path where its share of the text's width falls,
/// turned the way the path runs there. Placing works out where the clusters go, so a caller can test them - for room,
/// for covering other text - before it draws; <see cref="Effect"/> then makes the <see cref="GlyphEffect"/> that draws
/// each one there. The path is in the space the text is drawn in: a caller with a curve in 3D projects its points
/// first, and the text faces the viewer.
/// </summary>
public static class TextAlongPath
{
    /// <summary>Puts <paramref name="clusters"/> along <paramref name="path"/> into <paramref name="placed"/>, one
    /// each, in their order: the middle of the text, <paramref name="textWidth"/> wide as laid out and drawn
    /// <paramref name="scale"/> times that, <paramref name="middle"/> along the path from its first point. With
    /// <paramref name="keepUpright"/> a path that ends left of where it starts is read from its end, so the text does
    /// not stand on its head. False, and nothing placed, where it does not fit: the path is shorter than the text either
    /// side of the middle, or turns, all its bends between two neighboring clusters added up, by more than
    /// <paramref name="maxTurn"/> radians. Clusters of more than one line, and a middle, scale, width or turn that is not
    /// a number or is below nothing, place nothing either.</summary>
    public static bool TryPlace(ReadOnlySpan<GlyphCluster> clusters, double textWidth, ReadOnlySpan<Vector2<double>> path,
        double middle, double scale, double maxTurn, bool keepUpright, List<GlyphOnPath> placed)
    {
        placed.Clear();
        if (path.Length < 2 || clusters.IsEmpty || !double.IsFinite(middle) || !(scale > 0) || !(textWidth >= 0) ||
            !(maxTurn >= 0) || !OneLine(clusters))
        {
            return false;
        }

        var total = MathHelper.Polyline.Length(path);
        var half = textWidth * scale / 2;
        if (middle - half < 0 || middle + half > total)
        {
            return false;
        }

        var reversed = keepUpright && path[^1].X < path[0].X;
        var walk = new PathWalk(path);
        var previous = double.NaN;
        foreach (var cluster in clusters)
        {
            var shift = (cluster.X - textWidth / 2) * scale;
            var point = walk.At(reversed ? middle - shift : middle + shift, out var direction, out var turned);
            if (!double.IsNaN(previous) && Math.Abs(turned - previous) > maxTurn)
            {
                placed.Clear();
                return false;
            }

            previous = turned;
            var angle = reversed ? Math.Atan2(-direction.Y, -direction.X) : Math.Atan2(direction.Y, direction.X);
            placed.Add(new GlyphOnPath(point, angle));
        }

        return true;
    }

    /// <summary>The effect that draws <paramref name="cluster"/> where it was <paramref name="placed"/>: its glyphs
    /// <paramref name="scale"/> times their laid out size and turned with the path, the point of the layout at the
    /// cluster's middle across and <paramref name="acrossY"/> down - the middle of the line's glyphs, say - put on the
    /// path's point.</summary>
    public static GlyphEffect Effect(GlyphCluster cluster, GlyphOnPath placed, double scale, double acrossY)
    {
        var cos = Math.Cos(placed.Angle) * scale;
        var sin = Math.Sin(placed.Angle) * scale;
        var x = cluster.X;
        var transform = new Matrix3x3F((float)cos, (float)sin, 0, (float)-sin, (float)cos, 0,
            (float)(placed.Point.X - x * cos + acrossY * sin), (float)(placed.Point.Y - x * sin - acrossY * cos), 1);
        return new GlyphEffect(cluster.Start, cluster.Length, transform, false, null);
    }

    private static bool OneLine(ReadOnlySpan<GlyphCluster> clusters)
    {
        foreach (var cluster in clusters)
        {
            if (cluster.Line != clusters[0].Line)
            {
                return false;
            }
        }

        return true;
    }

    // A cursor along the path, moved either way from where it last stood, so clusters in order cost one walk of it;
    // it keeps how much the path has turned, every bend counted, from its first segment to the one it stands on.
    private ref struct PathWalk
    {
        private readonly ReadOnlySpan<Vector2<double>> _path;
        private int _end;
        private double _from;
        private double _length;
        private Vector2<double> _direction;
        private double _turned;

        public PathWalk(ReadOnlySpan<Vector2<double>> path)
        {
            _path = path;
            _end = 0;
            _from = 0;
            _turned = 0;
            if (Segment(1, +1) is var first and > 0)
            {
                Stand(first);
            }
        }

        public Vector2<double> At(double distance, out Vector2<double> direction, out double turned)
        {
            if (_end == 0)
            {
                direction = default;
                turned = 0;
                return _path[0];
            }

            while (distance > _from + _length && Segment(_end + 1, +1) is var next and > 0)
            {
                var direction0 = _direction;
                _from += _length;
                Stand(next);
                _turned += Turn(direction0, _direction);
            }

            while (distance < _from && Segment(_end - 1, -1) is var back and > 0)
            {
                var direction0 = _direction;
                Stand(back);
                _from -= _length;
                _turned -= Turn(_direction, direction0);
            }

            direction = _direction;
            turned = _turned;
            return _path[_end - 1] + _direction * Math.Clamp(distance - _from, 0, _length);
        }

        // The end of the first segment with a length from `end` on, going `step`; 0 for none.
        private readonly int Segment(int end, int step)
        {
            for (; end >= 1 && end < _path.Length; end += step)
            {
                if ((_path[end] - _path[end - 1]).Length() > 0)
                {
                    return end;
                }
            }

            return 0;
        }

        private void Stand(int end)
        {
            var step = _path[end] - _path[end - 1];
            _end = end;
            _length = step.Length();
            _direction = step / _length;
        }

        private static double Turn(Vector2<double> from, Vector2<double> to) =>
            Math.Abs(Math.Atan2(from.X * to.Y - from.Y * to.X, from.X * to.X + from.Y * to.Y));
    }
}

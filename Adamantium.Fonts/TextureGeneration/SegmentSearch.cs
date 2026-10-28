using System;
using System.Collections.Generic;
using Adamantium.Fonts.Common;
using Adamantium.Mathematics;

namespace Adamantium.Fonts.TextureGeneration;

/// <summary>Finds the segments of a <see cref="SegmentGrid"/> nearest a point, in any of its channels. Each search
/// starts from the answer to the one before, so points asked for in order along a row cost the least; one per
/// thread.</summary>
internal sealed class SegmentSearch
{
    private const double TieTolerance = 1e-9;
    private const double CandidateTolerance = 1e-8;

    private readonly SegmentGrid grid;
    private readonly int[] visited;
    private readonly double[] bestSquared = new double[SegmentGrid.ChannelCount];
    private readonly double[] keepWithin = new double[SegmentGrid.ChannelCount];
    private readonly double[] keepWithinSquared = new double[SegmentGrid.ChannelCount];
    private readonly double[] keepSquaredByChannels =
        [-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1];
    private readonly double[] lastBest =
        [double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity];
    private readonly double[] lastX = new double[SegmentGrid.ChannelCount];
    private readonly double[] lastY = new double[SegmentGrid.ChannelCount];
    private readonly List<int>[] candidates = [[], [], [], []];
    private readonly List<double>[] candidateDistances = [[], [], [], []];
    private readonly List<int> ties = [];
    private double widestKeepSquared;
    private int stamp;

    /// <summary>Searches the grid.</summary>
    public SegmentSearch(SegmentGrid grid)
    {
        this.grid = grid;
        visited = new int[grid.Segments.Length];
    }

    /// <summary>Fills nearest[c], for each channel c whose bit is set in wanted, with the segments of that channel
    /// nearest the point, in the order of their indices; more than one when their distances tie.</summary>
    public void FindNearest(Vector2<double> point, List<LineSegment2D>[] nearest, int wanted = SegmentGrid.AllChannels)
    {
        for (var channel = 0; channel < SegmentGrid.ChannelCount; channel++)
        {
            if ((wanted & (1 << channel)) == 0)
            {
                continue;
            }

            nearest[channel].Clear();
            candidates[channel].Clear();
            candidateDistances[channel].Clear();
            bestSquared[channel] = double.PositiveInfinity;
            var moved = Math.Sqrt((point.X - lastX[channel]) * (point.X - lastX[channel]) +
                                  (point.Y - lastY[channel]) * (point.Y - lastY[channel]));
            var bound = lastBest[channel] + moved;
            keepWithin[channel] = bound + CandidateTolerance * Math.Max(bound, 1);
            keepWithinSquared[channel] = keepWithin[channel] * keepWithin[channel];
        }

        var open = grid.PresentChannels & wanted;
        if (open == 0)
        {
            return;
        }

        stamp++;
        RefreshKeep(open);
        var reach = Math.Sqrt(widestKeepSquared);
        var x0 = 0;
        var x1 = grid.Columns - 1;
        var y0 = 0;
        var y1 = grid.Rows - 1;
        if (!double.IsInfinity(reach))
        {
            x0 = grid.Column(point.X - reach);
            x1 = grid.Column(point.X + reach);
            y0 = grid.Row(point.Y - reach);
            y1 = grid.Row(point.Y + reach);
        }

        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                Visit(y * grid.Columns + x, point, open);
            }
        }

        for (var channel = 0; channel < SegmentGrid.ChannelCount; channel++)
        {
            if ((wanted & (1 << channel)) == 0)
            {
                continue;
            }

            lastX[channel] = point.X;
            lastY[channel] = point.Y;
            lastBest[channel] = Math.Sqrt(bestSquared[channel]);
            ResolveTies(channel, point);
            foreach (var index in ties)
            {
                nearest[channel].Add(grid.Segments[index]);
            }
        }
    }

    private void Visit(int cell, Vector2<double> point, int open)
    {
        var startX = grid.StartX;
        var startY = grid.StartY;
        var directionX = grid.DirectionX;
        var directionY = grid.DirectionY;
        var inverseLengthSquared = grid.InverseLengthSquared;
        var channels = grid.EntryChannels;
        for (var k = grid.CellStart[cell]; k < grid.CellStart[cell + 1]; k++)
        {
            var px = point.X - startX[k];
            var py = point.Y - startY[k];
            var dx = directionX[k];
            var dy = directionY[k];
            var t = Math.Max(0, Math.Min(1, (px * dx + py * dy) * inverseLengthSquared[k]));
            var qx = px - dx * t;
            var qy = py - dy * t;
            var distanceSquared = qx * qx + qy * qy;
            if (distanceSquared <= keepSquaredByChannels[channels[k]])
            {
                Offer(k, distanceSquared, open);
            }
        }
    }

    private void Offer(int entry, double distanceSquared, int open)
    {
        var members = grid.EntryChannels[entry] & open;
        var index = grid.EntrySegment[entry];
        if (visited[index] == stamp)
        {
            return;
        }

        visited[index] = stamp;
        var narrowed = false;
        for (var channel = 0; channel < SegmentGrid.ChannelCount; channel++)
        {
            if ((members & (1 << channel)) == 0 || distanceSquared > keepWithinSquared[channel])
            {
                continue;
            }

            candidates[channel].Add(index);
            candidateDistances[channel].Add(distanceSquared);
            if (distanceSquared < bestSquared[channel])
            {
                bestSquared[channel] = distanceSquared;
                var within = Math.Sqrt(distanceSquared);
                within += CandidateTolerance * Math.Max(within, 1);
                if (within < keepWithin[channel])
                {
                    keepWithin[channel] = within;
                    keepWithinSquared[channel] = within * within;
                    narrowed = true;
                }
            }
        }

        if (narrowed)
        {
            RefreshKeep(open);
        }
    }

    private void RefreshKeep(int open)
    {
        var widest = -1.0;
        foreach (var members in grid.ChannelSets)
        {
            var keep = -1.0;
            for (var channel = 0; channel < SegmentGrid.ChannelCount; channel++)
            {
                if ((members & open & (1 << channel)) != 0 && keepWithinSquared[channel] > keep)
                {
                    keep = keepWithinSquared[channel];
                }
            }

            keepSquaredByChannels[members] = keep;
            widest = Math.Max(widest, keep);
        }

        widestKeepSquared = widest;
    }

    private void ResolveTies(int channel, Vector2<double> point)
    {
        ties.Clear();
        var found = candidates[channel];
        var distances = candidateDistances[channel];
        for (var i = 0; i < found.Count; i++)
        {
            if (distances[i] <= keepWithinSquared[channel])
            {
                ties.Add(found[i]);
            }
        }

        if (ties.Count < 2)
        {
            return;
        }

        ties.Sort();
        var finalists = ties.ToArray();
        ties.Clear();
        var best = double.MaxValue;
        foreach (var index in finalists)
        {
            var distance = GlyphSegmentsMath.GetDistanceToSegment(grid.Segments[index], point);
            var tolerance = TieTolerance * Math.Max(Math.Min(best, distance), 1);
            if (distance < best - tolerance)
            {
                best = distance;
                ties.Clear();
                ties.Add(index);
            }
            else if (distance <= best + tolerance)
            {
                best = Math.Min(best, distance);
                ties.Add(index);
            }
        }
    }
}

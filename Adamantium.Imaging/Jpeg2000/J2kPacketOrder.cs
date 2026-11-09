using System;
using System.Collections.Generic;

namespace Adamantium.Imaging.Jpeg2000;

/// <summary>The order of a tile's packets (ITU-T T.800, B.12): its progression, or the progressions of its POC one
/// after another, each packet once.</summary>
internal static class J2kPacketOrder
{
    public static List<J2kPacket> Of(J2kTileComponent[] components, (int X0, int Y0, int X1, int Y1) tile,
        int layers, J2kProgression progression, IReadOnlyList<J2kProgressionChange> changes, J2kLimits limits)
    {
        long total = 0;
        foreach (var component in components)
        {
            foreach (var resolution in component.Resolutions)
            {
                total += (long)resolution.PrecinctsAcross * resolution.PrecinctsDown * layers;
            }
        }

        limits.Spend(total * 8);
        var all = new List<J2kPacket>((int)total);
        for (var c = 0; c < components.Length; c++)
        {
            var component = components[c];
            var levels = component.Style.Levels;
            foreach (var resolution in component.Resolutions)
            {
                var count = resolution.PrecinctsAcross * resolution.PrecinctsDown;
                var scale = levels - resolution.Level;
                for (var p = 0; p < count; p++)
                {
                    var (y, x) = Place(resolution, p, scale, component.Info, tile);
                    for (var l = 0; l < layers; l++)
                    {
                        all.Add(new J2kPacket(l, resolution.Level, c, p, y, x));
                    }
                }
            }
        }

        if (changes.Count == 0)
        {
            all.Sort(Comparer(progression));
            return all;
        }

        var order = new List<J2kPacket>(all.Count);
        var taken = new HashSet<J2kPacket>();
        foreach (var change in changes)
        {
            limits.Spend(all.Count);
            var part = all.FindAll(packet => packet.Resolution >= change.FirstResolution &&
                                             packet.Resolution < change.EndResolution &&
                                             packet.Component >= change.FirstComponent &&
                                             packet.Component < change.EndComponent &&
                                             packet.Layer < change.EndLayer && !taken.Contains(packet));
            part.Sort(Comparer(change.Order));
            foreach (var packet in part)
            {
                taken.Add(packet);
                order.Add(packet);
            }
        }

        var rest = all.FindAll(packet => !taken.Contains(packet));
        rest.Sort(Comparer(progression));
        order.AddRange(rest);
        return order;
    }

    private static (long Y, long X) Place(J2kResolution resolution, int precinct, int scale, J2kComponentInfo info,
        (int X0, int Y0, int X1, int Y1) tile)
    {
        var px = resolution.PrecinctX0 + precinct % resolution.PrecinctsAcross;
        var py = resolution.PrecinctY0 + precinct / resolution.PrecinctsAcross;
        var x = (long)px << resolution.PrecinctWidthExponent;
        var y = (long)py << resolution.PrecinctHeightExponent;
        var cutX = x < resolution.X0;
        var cutY = y < resolution.Y0;
        var gridX = cutX ? tile.X0 : (x << scale) * info.Dx;
        var gridY = cutY ? tile.Y0 : (y << scale) * info.Dy;
        return (Math.Max(gridY, tile.Y0), Math.Max(gridX, tile.X0));
    }

    private static Comparison<J2kPacket> Comparer(J2kProgression progression) => progression switch
    {
        J2kProgression.Lrcp => (a, b) => Compare(a.Layer, b.Layer, a.Resolution, b.Resolution, a.Component,
            b.Component, a.Precinct, b.Precinct, 0, 0),
        J2kProgression.Rlcp => (a, b) => Compare(a.Resolution, b.Resolution, a.Layer, b.Layer, a.Component,
            b.Component, a.Precinct, b.Precinct, 0, 0),
        J2kProgression.Rpcl => (a, b) => Compare(a.Resolution, b.Resolution, a.Y, b.Y, a.X, b.X, a.Component,
            b.Component, a.Layer, b.Layer),
        J2kProgression.Pcrl => (a, b) => Compare(a.Y, b.Y, a.X, b.X, a.Component, b.Component, a.Resolution,
            b.Resolution, a.Layer, b.Layer),
        _ => (a, b) => Compare(a.Component, b.Component, a.Y, b.Y, a.X, b.X, a.Resolution, b.Resolution, a.Layer,
            b.Layer),
    };

    private static int Compare(long a1, long b1, long a2, long b2, long a3, long b3, long a4, long b4, long a5,
        long b5)
    {
        var c = a1.CompareTo(b1);
        if (c == 0)
        {
            c = a2.CompareTo(b2);
        }

        if (c == 0)
        {
            c = a3.CompareTo(b3);
        }

        if (c == 0)
        {
            c = a4.CompareTo(b4);
        }

        return c != 0 ? c : a5.CompareTo(b5);
    }
}

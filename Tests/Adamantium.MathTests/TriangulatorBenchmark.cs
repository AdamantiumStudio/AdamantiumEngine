using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Triangulation;
using NUnit.Framework;

namespace Adamantium.MathTests
{
    /// <summary>
    /// Re-runnable triangulator benchmark. Category "Benchmark" so it's excluded from normal runs.
    /// Run: dotnet test -c Release --filter "TestCategory=Benchmark"
    /// </summary>
    [Category("Benchmark")]
    public class TriangulatorBenchmark
    {
        [Test]
        public void Bench()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Build: {(IsDebug() ? "DEBUG (ignore!)" : "RELEASE")}");
            sb.AppendLine();

            sb.AppendLine("== Circle (convex, single contour) ==");
            double prev = 0;
            foreach (var n in new[] { 64, 128, 256, 512, 1024, 2048 })
            {
                var pts = Circle(n);
                var (ms, min, tris) = Measure(() => Single(pts), 2, n <= 512 ? 15 : 5);
                var ratio = prev > 0 ? $"  x{ms / prev:F2}" : "";
                sb.AppendLine($"  n={n,5}  tris={tris,6}  median={ms,9:F3} ms  min={min,9:F3} ms{ratio}");
                prev = ms;
            }

            sb.AppendLine();
            sb.AppendLine("== Gear (concave, non-self-int, single contour) ==");
            foreach (var n in new[] { 128, 256, 512, 1024 })
            {
                var pts = Gear(n);
                var (ms, min, tris) = Measure(() => Single(pts), 2, n <= 512 ? 10 : 5);
                sb.AppendLine($"  n={n,5}  tris={tris,6}  median={ms,9:F3} ms  min={min,9:F3} ms");
            }

            sb.AppendLine();
            sb.AppendLine("== Annulus (outer ring + inner hole, multi-contour merge -> earcut-with-holes) ==");
            foreach (var n in new[] { 64, 256, 1024 })
            {
                var (ms, min, tris) = Measure(() => Annulus(n), 2, n <= 256 ? 10 : 5);
                sb.AppendLine($"  n/ring={n,5}  tris={tris,6}  median={ms,9:F3} ms  min={min,9:F3} ms");
            }

            sb.AppendLine();
            sb.AppendLine("== Many separate squares (containers / merge path) ==");
            foreach (var k in new[] { 4, 16, 64, 256 })
            {
                var (ms, min, tris) = Measure(() => Squares(k), 2, 5);
                sb.AppendLine($"  squares={k,4}  tris={tris,6}  median={ms,9:F3} ms  min={min,9:F3} ms");
            }

            // The geometry an animated Button re-triangulates every frame: a rounded rectangle (4 arc corners +
            // 4 straight edges), one convex contour -> the FanTriangulate fast path. This is the case the resize
            // animation hammers, so it must be ~free. tess = arc segments per corner (engine default is 20).
            sb.AppendLine();
            sb.AppendLine("== Rounded rect (button border, single convex contour -> fan) ==");
            foreach (var tess in new[] { 4, 8, 16, 20, 32 })
            {
                var pts = RoundedRect(150, 80, 8, tess);
                var (ms, min, tris) = Measure(() => Single(pts), 5, 50);
                sb.AppendLine($"  tess={tess,3}  pts={pts.Length,4}  tris={tris,4}  median={ms,9:F4} ms  min={min,9:F4} ms");
            }

            // One second of resize animation @ 60 fps: 60 distinct widths, each triangulated once. Reports the
            // total per-frame-equivalent cost so we know whether triangulation alone could stall the animation.
            sb.AppendLine();
            sb.AppendLine("== Rounded rect resize sweep (60 frames @ tess=20) ==");
            {
                var frames = new Vector2<double>[60][];
                for (int i = 0; i < 60; i++) frames[i] = RoundedRect(120 + i, 80, 8, 20);
                var (ms, min, _) = Measure(() =>
                {
                    foreach (var f in frames) Single(f).FillIndirect();
                    return Single(frames[0]); // returned poly is re-filled by Measure; the sweep above is the work
                }, 2, 20);
                sb.AppendLine($"  60 triangulations  median={ms,9:F3} ms  min={min,9:F3} ms  (per frame ~{ms / 60:F4} ms)");
            }

            // THE actual per-frame cost of an animated Button's BORDER. The border is a ring drawn as
            // CombinedGeometry.Exclude(outer rounded rect, inner rounded rect), which triangulates via the
            // SCANLINE (Polygon.FillDirect), NOT the fast nesting path. Same shape as the Annulus case above, but
            // routed through the slow path - so this is the apples-to-apples cost the Border pays every frame.
            sb.AppendLine();
            sb.AppendLine("== Border ring (CombinedGeometry.Exclude -> scanline / FillDirect) ==");
            foreach (var tess in new[] { 4, 8, 16, 20 })
            {
                var (ms, min, tris) = MeasureDirect(() => BorderRing(tess), 3, 20);
                sb.AppendLine($"  tess={tess,3}  median={ms,9:F3} ms  min={min,9:F3} ms  tris={tris,4}");
            }

            // For contrast: the SAME ring fed to the fast nesting path (FillIndirect -> earcut-with-holes). This is
            // what the border WOULD cost if CombinedGeometry routed clean (non-crossing) nesting through it.
            sb.AppendLine();
            sb.AppendLine("== Border ring (same shape -> fast nesting / FillIndirect) ==");
            foreach (var tess in new[] { 4, 8, 16, 20 })
            {
                var (ms, min, tris) = Measure(() => BorderRingFast(tess), 3, 20);
                sb.AppendLine($"  tess={tess,3}  median={ms,9:F3} ms  min={min,9:F3} ms  tris={tris,4}");
            }

            // CONTOURS THAT CROSS - the shape of an exported icon: a body with cells cut out of it by running them
            // the other way round, plus one shape overlapping the outline. Nesting alone never reaches this; it is the
            // planar fill's own cost, and the case that used to fall to the scanline.
            sb.AppendLine();
            sb.AppendLine("== Crossing contours (body + cells + an overlap -> planar fill) ==");
            foreach (var cells in new[] { 4, 16, 64, 144 })
            {
                var (ms, min, tris) = Measure(() => Grid(cells), 2, cells <= 64 ? 10 : 5);
                sb.AppendLine($"  cells={cells,4}  tris={tris,6}  median={ms,9:F3} ms  min={min,9:F3} ms");
            }

            var text = sb.ToString();
            try { System.IO.File.WriteAllText(@"C:\Temp\tri_bench.txt", text); } catch { }
            TestContext.Progress.WriteLine(text);
            Assert.Pass("\n" + text);
        }

        static Vector2<double>[] Circle(int n, double r = 100)
        {
            var p = new Vector2<double>[n];
            for (int i = 0; i < n; i++) { double a = 2 * Math.PI * i / n; p[i] = new Vector2<double>(r * Math.Cos(a), r * Math.Sin(a)); }
            return p;
        }

        static Polygon Annulus(int n)
        {
            var p = new Polygon { FillRule = FillRule.EvenOdd };
            p.AddContour(new MeshContour(Circle(n, 100)));
            p.AddContour(new MeshContour(Circle(n, 50)));
            return p;
        }

        // Faithful copy of Shapes.Rectangle.GenerateRoundCorner: tess+1 points per 90-degree corner, snapped to
        // 3 decimals, in the engine's corner order (TL -180, TR 90, BR 0, BL -90). Produces the exact contour the
        // RectangleRenderUnit feeds the triangulator for a rounded border.
        static Vector2<double>[] RoundedRect(double w, double h, double radius, int tess)
        {
            radius = Math.Min(radius, Math.Min(w, h) / 2);
            double halfW = w / 2, halfH = h / 2;
            var v = new List<Vector2<double>>();

            void Corner(double startDeg, Vector2<double> center)
            {
                double step = -(float)double.DegreesToRadians(90.0 / tess);
                double a = (float)double.DegreesToRadians(startDeg);
                for (int i = 0; i <= tess; i++)
                {
                    var x = center.X + radius * Math.Cos(a);
                    var y = center.Y - radius * Math.Sin(a);
                    a += step;
                    v.Add(Vector2<double>.Round(new Vector2<double>(x, y), 3));
                }
            }

            Corner(-180, new Vector2<double>(-halfW + radius, -halfH + radius));
            Corner(90, new Vector2<double>(halfW - radius, -halfH + radius));
            Corner(0, new Vector2<double>(halfW - radius, halfH - radius));
            Corner(-90, new Vector2<double>(-halfW + radius, halfH - radius));
            return v.ToArray();
        }

        // Outer + inner rounded rect (inner inset by a 2px border thickness, winding reversed so NonZero leaves the
        // interior as a hole = a ring). Returns the merged points+segments exactly as CombinedGeometry hands them to
        // Polygon.FillDirect for the Border's stroke.
        static (List<GeometryIntersection> pts, List<GeometrySegment> segs) BorderRing(int tess)
        {
            var outer = new MeshContour(RoundedRect(150, 80, 8, tess), true, true);
            var innerPts = RoundedRect(146, 76, 6, tess);
            Array.Reverse(innerPts);   // opposite winding -> NonZero treats the interior as a hole
            var inner = new MeshContour(innerPts, true, true);

            var pts = new List<GeometryIntersection>(outer.GeometryPoints);
            pts.AddRange(inner.GeometryPoints);
            var segs = new List<GeometrySegment>(outer.Segments);
            segs.AddRange(inner.Segments);
            return (pts, segs);
        }

        // Same outer+inner ring, but as two plain contours on one Polygon -> FillIndirect picks the fast nesting path.
        static Polygon BorderRingFast(int tess)
        {
            var p = new Polygon { FillRule = FillRule.EvenOdd };
            p.AddContour(new MeshContour(RoundedRect(150, 80, 8, tess)));
            p.AddContour(new MeshContour(RoundedRect(146, 76, 6, tess)));
            return p;
        }

        static Vector2<double>[] Gear(int n, double r0 = 60, double r1 = 100)
        {
            var p = new Vector2<double>[n];
            for (int i = 0; i < n; i++) { double a = 2 * Math.PI * i / n; double r = (i % 2 == 0) ? r0 : r1; p[i] = new Vector2<double>(r * Math.Cos(a), r * Math.Sin(a)); }
            return p;
        }

        static Polygon Single(Vector2<double>[] pts)
        {
            var p = new Polygon { FillRule = FillRule.EvenOdd };
            p.AddContour(new MeshContour(pts));
            return p;
        }

        // A body with k cells cut out of it (each wound the other way) and one triangle across its edge.
        static Polygon Grid(int cells)
        {
            var p = new Polygon { FillRule = FillRule.NonZero };
            var side = (int)Math.Ceiling(Math.Sqrt(cells));
            var step = 100.0 / (side + 1);

            p.AddContour(new MeshContour(new[] {
                new Vector2<double>(0, 0), new Vector2<double>(100, 0), new Vector2<double>(100, 100), new Vector2<double>(0, 100) }));

            for (var i = 0; i < cells; i++)
            {
                double x = step * (i % side) + step / 2, y = step * (i / side) + step / 2;
                var w = step * 0.6;

                p.AddContour(new MeshContour(new[] {
                    new Vector2<double>(x, y), new Vector2<double>(x, y + w), new Vector2<double>(x + w, y + w), new Vector2<double>(x + w, y) }));
            }

            p.AddContour(new MeshContour(new[] {
                new Vector2<double>(90, 40), new Vector2<double>(130, 40), new Vector2<double>(90, 70) }));

            return p;
        }

        static Polygon Squares(int k)
        {
            var p = new Polygon { FillRule = FillRule.EvenOdd };
            int side = (int)Math.Ceiling(Math.Sqrt(k));
            for (int i = 0; i < k; i++)
            {
                double x = (i % side) * 30, y = (i / side) * 30;
                p.AddContour(new MeshContour(new[] {
                    new Vector2<double>(x, y), new Vector2<double>(x + 20, y), new Vector2<double>(x + 20, y + 20), new Vector2<double>(x, y + 20) }));
            }
            return p;
        }

        // Variant of Measure for the scanline path, which is driven by FillDirect(points, segments) rather than
        // FillIndirect() on contours. The inputs are rebuilt each run (FillDirect mutates/rounds them in place).
        static (double median, double min, int tris) MeasureDirect(
            Func<(List<GeometryIntersection> pts, List<GeometrySegment> segs)> build, int warmup, int runs)
        {
            for (int i = 0; i < warmup; i++) { var (p, s) = build(); new Polygon(FillRule.NonZero).FillDirect(p, s); }
            var times = new List<double>();
            int tris = 0;
            for (int i = 0; i < runs; i++)
            {
                var (p, s) = build();
                GC.Collect(); GC.WaitForPendingFinalizers();
                var sw = Stopwatch.StartNew();
                var v = new Polygon(FillRule.NonZero).FillDirect(p, s);
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);
                tris = v.Count / 3;
            }
            times.Sort();
            return (times[times.Count / 2], times[0], tris);
        }

        static (double median, double min, int tris) Measure(Func<Polygon> build, int warmup, int runs)
        {
            for (int i = 0; i < warmup; i++) build().FillIndirect();
            var times = new List<double>();
            int tris = 0;
            for (int i = 0; i < runs; i++)
            {
                var poly = build();
                GC.Collect(); GC.WaitForPendingFinalizers();
                var sw = Stopwatch.StartNew();
                var v = poly.FillIndirect();
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);
                tris = v.Count / 3;
            }
            times.Sort();
            return (times[times.Count / 2], times[0], tris);
        }

        static bool IsDebug()
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }
}

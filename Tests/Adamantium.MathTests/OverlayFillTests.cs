using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Triangulation;
using NUnit.Framework;

namespace Adamantium.MathTests
{
    /// <summary>
    /// Rings that cross, touch, run along one another or come within rounding of it, filled through the overlay: every
    /// point the fill rule fills is covered once, every other point not at all, each triangle clockwise and of some area.
    /// </summary>
    public class OverlayFillTests
    {
        static readonly FillRule[] Rules = { FillRule.EvenOdd, FillRule.NonZero };

        // Two triangles whose tips lie a little more than the snap distance apart: the cuts bend the edges near them.
        [TestCaseSource(nameof(Rules))]
        public void TipsJustApart_BothFill(FillRule rule)
        {
            var rings = new[]
            {
                new[] { V(64.0000157515805, 47.99994705295045), V(95.9999314631633, -6.064538560372098E-05), V(64.0000653474822, 32.0000193525607) },
                new[] { V(47.9998761443542, 16.00013296738544), V(95.9999370906363, 5.879418780505385E-05), V(-1.8021879376853755E-05, 48.0000513058008) },
            };

            Assert.That(Rounded(rings)[0][1], Is.Not.EqualTo(Rounded(rings)[1][1]), "the tips stay apart as contours keep them");
            Assert.That(Faults(RingFill.Fill(GeometryOverlay.Outline(rings, rule), FillRule.NonZero), rings, rule), Is.Empty);
            Assert.That(Faults(Fill(rule, rings), Rounded(rings), rule), Is.Empty);
        }

        [TestCaseSource(nameof(Rules))]
        public void ARingWithTwoPointsJustApart_Fills(FillRule rule)
        {
            var rings = new[]
            {
                new[] { V(79.9999518623967, 80.0000990732585), V(1.2242622076181053E-04, 48.00005798690125), V(31.99985124137496, 64.0001266196894),
                    V(3.5843027190232197E-05, 48.00006418346686), V(64.0000086433774, 79.9998742026393) },
            };

            Assert.That(Rounded(rings)[0][1], Is.Not.EqualTo(Rounded(rings)[0][3]), "the two points stay apart as contours keep them");
            Assert.That(Faults(Fill(rule, rings), Rounded(rings), rule), Is.Empty);
        }

        [TestCaseSource(nameof(Rules))]
        public void ATriangleWithASliverOnItsSide_Fills(FillRule rule)
        {
            var rings = new[]
            {
                new[] { V(15.999945371151085, 48.00001531562466), V(79.99992789358944, 47.999985145645816), V(80.0000306667593, 64.00003252196467) },
                new[] { V(96.00004495342772, 48.00000278018494), V(96.00006612329332, 48.00000180721036), V(47.99996860873078, 47.99995393705203) },
            };

            Assert.That(Faults(RingFill.Fill(GeometryOverlay.Outline(rings, rule), FillRule.NonZero), rings, rule), Is.Empty);
        }

        // Holes meeting at one point, which a face's ear clipping does not take: the face is cut into strips instead.
        [TestCaseSource(nameof(Rules))]
        public void HolesMeetingAtOnePoint_AreCutOut(FillRule rule)
        {
            var cases = new[]
            {
                new[] { Rect(0, 0, 100, 100), Tri(31, 78, 40, 69, 50, 50), Tri(32, 57, 22, 77, 50, 50), Tri(66, 35, 7, 41, 50, 50) },
                new[] { Rect(0, 0, 100, 100), Tri(57, 57, 71, 59, 50, 50), Tri(25, 80, 36, 79, 50, 50), Tri(14, 57, 20, 76, 50, 50), Tri(60, 46, 37, 51, 50, 50) },
                new[] { Rect(0, 0, 100, 100), Tri(44, 60, 86, 69, 50, 50), Tri(17, 79, 24, 74, 50, 50), Tri(49, 29, 10, 48, 50, 50), Tri(60, 19, 62, 11, 50, 50) },
            };

            foreach (var rings in cases)
            {
                Assert.That(Faults(Fill(rule, rings), rings, rule), Is.Empty);
            }
        }

        [Test]
        public void CrossingRingsWhoseFacesTouch_FillOnce()
        {
            var rings = new[]
            {
                new[] { V(96, 16), V(0, 32), V(64, 64) },
                new[] { V(0, 32), V(64, 48), V(48, 32), V(96, 64) },
                new[] { V(32, 96), V(96, 16), V(48, 16), V(64, 48), V(48, 48), V(32, 16) },
            };

            foreach (var rule in Rules)
            {
                Assert.That(Faults(Fill(rule, rings), rings, rule), Is.Empty, rule.ToString());
            }
        }

        // A contour of two points - a line in a filled path - fills nothing, and does not change how the rest fills.
        [TestCaseSource(nameof(Rules))]
        public void AContourOfTwoPoints_LeavesTheRestAsItIs(FillRule rule)
        {
            var hole = Tri(60, 30, 30, 60, 0, 0);
            var rings = new[] { Rect(0, 0, 100, 100), rule == FillRule.NonZero ? Reversed(hole) : hole };
            var p = new Polygon { FillRule = rule };
            foreach (var ring in rings) p.AddContour(new MeshContour(ring));
            p.AddContour(new MeshContour(new[] { V(10, 90), V(20, 90) }));
            Assert.That(Faults(p.FillIndirect(), rings, rule), Is.Empty);
        }

        // A ring passing one point twice, one turning back along itself, one traced round twice.
        [TestCaseSource(nameof(Rules))]
        public void RingsPassingAPointTwice_FillAsTheyRun(FillRule rule)
        {
            var cases = new[]
            {
                new[] { new[] { V(0, 0), V(100, 0), V(50, 50), V(100, 100), V(0, 100), V(50, 50) } },
                new[] { new[] { V(0, 0), V(100, 0), V(100, 100), V(50, 100), V(50, 150), V(50, 100), V(0, 100) } },
                new[] { new[] { V(0, 0), V(100, 0), V(100, 100), V(0, 100), V(0, 0), V(100, 0), V(100, 100), V(0, 100) } },
            };

            foreach (var rings in cases)
            {
                Assert.That(Faults(Fill(rule, rings), rings, rule), Is.Empty);
            }
        }

        // Rings on a grid, their corners pushed off it by a little - from far below rounding to well above the snap
        // distance - so they nearly meet, meet, and cross, at every scale and far from the origin.
        [Test]
        public void JitteredRings_FillAsTheRuleSays()
        {
            var random = new Random(20261112);
            var wrong = new List<string>();
            foreach (var jitter in new[] { 1e-11, 1e-7, 1e-6, 1e-5, 1e-4 })
            {
                for (var n = 0; n < 60; n++)
                {
                    var rings = new Vector2<double>[1 + random.Next(3)][];
                    for (var r = 0; r < rings.Length; r++)
                    {
                        rings[r] = new Vector2<double>[3 + random.Next(4)];
                        for (var k = 0; k < rings[r].Length; k++)
                        {
                            rings[r][k] = V(16 * random.Next(7) + jitter * 100 * (random.NextDouble() - 0.5),
                                16 * random.Next(7) + jitter * 100 * (random.NextDouble() - 0.5));
                        }
                    }

                    foreach (var rule in Rules)
                    {
                        var faults = Faults(RingFill.Fill(GeometryOverlay.Outline(rings, rule), FillRule.NonZero), rings, rule);
                        if (faults.Count > 0 && wrong.Count < 6) wrong.Add($"{rule} jitter {jitter}: {Describe(rings)} - {faults[0]}");
                    }
                }
            }

            Assert.That(wrong, Is.Empty);
        }

        [Test]
        public void FarFromTheOrigin_TrianglesHaveAreaAndTurnOneWay()
        {
            var offset = 1e9;
            var rings = new[]
            {
                new[] { V(offset, offset), V(offset + 100, offset), V(offset + 100, offset + 100), V(offset, offset + 100) },
                new[] { V(offset + 50, offset + 50), V(offset + 150, offset + 40), V(offset + 60, offset + 140) },
                new[] { V(offset + 20, offset), V(offset + 40, offset + 30), V(offset, offset + 20) },
            };

            foreach (var rule in Rules)
            {
                var tris = RingFill.Fill(GeometryOverlay.Outline(rings, rule), FillRule.NonZero);
                Assert.That(Faults(tris, rings, rule), Is.Empty, rule.ToString());
                for (var t = 0; t + 2 < tris.Count; t += 3)
                {
                    var (a, b, c) = ((Vector2<double>)tris[t], (Vector2<double>)tris[t + 1], (Vector2<double>)tris[t + 2]);
                    Assert.That(MathHelper.Polyline.IsClockwise(a, b, c), Is.True, $"{rule}: triangle {t / 3}");
                }
            }
        }

        // Far from the origin the products of coordinates lose the digits of the shape: areas are measured from a corner.
        [TestCase(1e9, FillRule.EvenOdd)]
        [TestCase(1e9, FillRule.NonZero)]
        [TestCase(-3e8, FillRule.NonZero)]
        public void SquaresTouchingFarFromTheOrigin_BothFill(double o, FillRule rule)
        {
            var rings = new[] { Rect(o, o, o + 1, o + 1), Rect(o + 1, o, o + 2, o + 1) };
            Assert.That(Faults(Fill(rule, rings), rings, rule), Is.Empty);
            Assert.That(Faults(RingFill.Fill(GeometryOverlay.Outline(rings, rule), FillRule.NonZero), rings, rule), Is.Empty);
        }

        [TestCase(3e7)]
        [TestCase(1e8)]
        [TestCase(1e9)]
        public void AHoleFarFromTheOrigin_StaysEmpty_UnderNonZero(double o)
        {
            var rings = new[] { Rect(o, o, o + 1, o + 1), Reversed(Rect(o + 0.25, o + 0.25, o + 0.75, o + 0.75)) };
            Assert.That(Faults(Fill(FillRule.NonZero, rings), rings, FillRule.NonZero), Is.Empty);
        }

        // What is close is welded by the size of the whole: a detail a hundred-thousandth of it is kept.
        [TestCaseSource(nameof(Rules))]
        public void ADetailFarSmallerThanTheWhole_IsKept(FillRule rule)
        {
            var hole = Rect(100.1, 100.1, 100.4, 100.4);
            var rings = new[] { Rect(0, 0, 1e7, 1e7), Rect(100, 100, 100.5, 100.5), rule == FillRule.NonZero ? hole : Reversed(hole) };
            var tris = Fill(rule, rings);
            var winding = (Func<Vector2<double>, int>)(p => Winding(rings[0], p) + Winding(rings[1], p) + Winding(rings[2], p));
            foreach (var p in new[] { V(100.22, 100.31), V(100.05, 100.27), V(50.3, 50.7) })
            {
                var w = winding(p);
                var filled = rule == FillRule.NonZero ? w != 0 : (w & 1) != 0;
                Assert.That(Covers(tris, p), Is.EqualTo(filled ? 1 : 0), $"({p.X}, {p.Y})");
            }
        }

        // A ring with a coordinate not a finite number is left out; the others fill as without it.
        [TestCaseSource(nameof(Rules))]
        public void ARingNotOfNumbers_IsLeftOut(FillRule rule)
        {
            var shape = new[] { V(0, 0), V(10, 0), V(10, 5), V(5, 5), V(5, 10), V(0, 10) };
            foreach (var bad in new[] { double.PositiveInfinity, double.NaN })
            {
                var rings = new[] { shape, new[] { V(20, 0), V(bad, 0), V(25, 10) } };
                Assert.That(Faults(RingFill.Fill(GeometryOverlay.Outline(rings, rule), FillRule.NonZero), new[] { shape }, rule), Is.Empty, $"{bad}");
                Assert.That(RingContacts.AnyMeet(rings), Is.False);
            }
        }

        [Test]
        public void ANullRing_IsLeftOut()
        {
            var rings = new[] { null, Rect(0, 0, 10, 10) };
            Assert.That(RingContacts.AnyMeet(rings), Is.False);
            Assert.That(GeometryOverlay.Outline(rings, FillRule.EvenOdd), Has.Count.EqualTo(1));
            Assert.That(RingFill.Fill(rings, FillRule.EvenOdd), Has.Count.EqualTo(6));
        }

        [Test]
        public void AnUnknownMode_IsRefused()
        {
            Assert.That(() => GeometryOverlay.Combine(new RingArea([Rect(0, 0, 1, 1)], FillRule.EvenOdd),
                new RingArea([], FillRule.EvenOdd), (AreaCombineMode)7), Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        // A face cut into trapezoids - its holes meet at a point - gets about as many as it has corners, not as many as
        // its corners squared.
        [Test]
        public void AFaceCutIntoTrapezoids_GetsTrianglesInProportionToItsCorners()
        {
            foreach (var steps in new[] { 250, 1000 })
            {
                var outer = new List<Vector2<double>> { V(0, 0) };
                for (var k = 0; k < steps; k++)
                {
                    outer.Add(V(200 + k, 2.0 * k));
                    outer.Add(V(200 + k, 2.0 * k + 1));
                }

                outer.Add(V(0, 2.0 * steps));
                var rings = new[] { outer.ToArray(), Tri(31, 78, 40, 69, 50, 50), Tri(32, 57, 22, 77, 50, 50), Tri(66, 35, 7, 41, 50, 50) };
                var tris = RingFill.Fill(rings, FillRule.EvenOdd);
                Assert.That(tris.Count / 3, Is.LessThan(8 * outer.Count), $"{steps} steps");
                Assert.That(Faults(tris, rings, FillRule.EvenOdd), Is.Empty, $"{steps} steps");
            }
        }

        [Test]
        public void CombineModes_FillAsTheirRuleSays()
        {
            var random = new Random(77);
            var modes = new (AreaCombineMode Mode, Func<bool, bool, bool> Fills)[]
            {
                (AreaCombineMode.Union, (a, b) => a || b), (AreaCombineMode.Intersect, (a, b) => a && b),
                (AreaCombineMode.Exclude, (a, b) => a && !b), (AreaCombineMode.Xor, (a, b) => a ^ b),
            };
            var wrong = new List<string>();
            for (var n = 0; n < 150; n++)
            {
                var first = RandomRings(random);
                var second = RandomRings(random);
                foreach (var ruleA in Rules)
                {
                    foreach (var ruleB in Rules)
                    {
                        foreach (var (name, fills) in modes)
                        {
                            var combined = GeometryOverlay.Combine(new RingArea(first, ruleA), new RingArea(second, ruleB), name);
                            var tris = RingFill.Fill(combined, FillRule.NonZero);
                            var fault = CombineFault(tris, first, ruleA, second, ruleB, fills) ??
                                        TurnFault(combined, first, ruleA, second, ruleB, fills);
                            if (fault != null && wrong.Count < 5) wrong.Add($"{name} {ruleA}/{ruleB}: {Describe(first)} with {Describe(second)} - {fault}");
                        }
                    }
                }
            }

            Assert.That(wrong, Is.Empty);
        }

        // Three families of thin strips crossing at shared points: past a point three edges pass, the sweep keeps them in
        // order.
        [TestCaseSource(nameof(Rules))]
        public void StripsCrossingThreeAtAPoint_FillAsTheRuleSays(FillRule rule)
        {
            var rings = new List<Vector2<double>[]>();
            for (var k = 0; k < 12; k++)
            {
                var c = 10.0 * k;
                rings.Add(new[] { V(c, -5), V(c + 1, -5), V(c + 1, 125), V(c, 125) });
                rings.Add(new[] { V(-5, c), V(125, c), V(125, c + 1), V(-5, c + 1) });
                rings.Add(new[] { V(c - 60, -60), V(c - 59, -60), V(c + 61, 60), V(c + 60, 60) });
            }

            var array = rings.ToArray();
            Assert.That(Faults(Fill(rule, array), array, rule), Is.Empty);
        }

        // Rings one inside another, keeping clear, each turning either way: the ring straight around each is found by the
        // edge below its leftmost corner.
        [Test]
        public void RingsNestedDeep_FillAsTheRuleSays()
        {
            var random = new Random(5);
            foreach (var rule in Rules)
            {
                var rings = new List<Vector2<double>[]>();
                for (var k = 0; k < 40; k++)
                {
                    var r = 100 - 2.3 * k;
                    var ring = new Vector2<double>[48 + k % 7];
                    for (var i = 0; i < ring.Length; i++)
                    {
                        var a = 2 * Math.PI * i / ring.Length + k;
                        ring[i] = V(r * Math.Cos(a) + 0.3 * k, r * Math.Sin(a));
                    }

                    rings.Add(random.Next(2) == 0 ? ring : Reversed(ring));
                }

                rings.Add(Rect(150, -10, 170, 10));
                rings.Add(Reversed(Rect(155, -5, 165, 5)));
                var array = rings.ToArray();
                Assert.That(RingContacts.AnyMeet(array), Is.False);
                Assert.That(Faults(Fill(rule, array), array, rule), Is.Empty, rule.ToString());
            }
        }

        // Many rings along one line - on one base, sharing a side, all ending at one point: the edges on a line are cut
        // along it once, however many lie there.
        [TestCaseSource(nameof(Rules))]
        public void RingsAlongOneLine_FillAsTheRuleSays(FillRule rule)
        {
            var based = new List<Vector2<double>[]>();
            for (var i = 0; i < 40; i++) based.Add(Rect(i, 0, i + 40, 1 + i % 7));
            var side = new List<Vector2<double>[]>();
            for (var i = 0; i < 40; i++) side.Add(new[] { V(0, i), V(80 - i, i), V(80 - i, 80 - i), V(0, 80 - i) });
            var diagonal = new List<Vector2<double>[]>();
            for (var i = 0; i < 30; i++) diagonal.Add(new[] { V(i, i), V(i + 30, i + 30), V(i + 30, i + 33) });
            var fan = new List<Vector2<double>[]>();
            for (var i = 0; i < 60; i++)
            {
                var a0 = Math.PI / 2 + Math.PI * i / 60;
                var a1 = Math.PI / 2 + Math.PI * (i + 0.4) / 60;
                fan.Add(new[] { V(0, 0), V(100 * Math.Cos(a1), 100 * Math.Sin(a1)), V(100 * Math.Cos(a0), 100 * Math.Sin(a0)) });
            }

            foreach (var rings in new[] { based, side, diagonal, fan })
            {
                var array = rings.ToArray();
                Assert.That(Faults(Fill(rule, array), array, rule), Is.Empty);
            }
        }

        // A sliver ring whose corners lie a weld apart from another's: a corner given twice is one point both times.
        [TestCaseSource(nameof(Rules))]
        public void ASliverBesideOtherRings_TakesNothingFromThem(FillRule rule)
        {
            var rings = new[]
            {
                new[] { V(0, 4), V(2, 3), V(5, 4) },
                new[] { V(1, 4), V(1.5, 6), V(0.5, 6) },
                new[] { V(1 + 0.4e-9, 4), V(1 + 0.7e-9, 4), V(4, 5) },
            };
            Assert.That(Faults(RingFill.Fill(rings, rule), rings, rule), Is.Empty);
            // Large enough that the weld is longer than the step contours round to, so the corners stay apart until welded.
            var scaled = Array.ConvertAll(rings, ring => Array.ConvertAll(ring, p => p * 5e7));
            scaled[2] = new[] { V(5e7 + 0.002, 2e8), V(5e7 + 0.003, 2e8), V(2e8, 2.5e8) };
            Assert.That(Rounded(scaled)[2].Distinct().Count(), Is.EqualTo(3), "the sliver's corners stay apart as contours keep them");
            Assert.That(Faults(Fill(rule, scaled), Rounded(scaled), rule), Is.Empty);
        }

        // Points a little more than a weld apart, each near the edge between the other two: they are welded, not cut
        // round and round.
        [Test]
        public void PointsNearEachOthersEdges_AreWelded()
        {
            var direct = new[]
            {
                new[] { V(3.000000000479706, 1.0000000002955733), V(0.9999999995632302, 2.999999999504001), V(2.9999999995225837, 2.0000000002896754) },
                new[] { V(1.000000000127578, 2.999999999723007), V(4.999999999996373, 0.999999999513035), V(1.7647505303680667E-10, 3.9503238810926326E-10) },
            };
            var rounded = new[]
            {
                new[] { V(159999.99999, 119999.99997), V(160000.00001, 120000.00001), V(79999.99997, -0.00001) },
                new[] { V(160000.00003, 120000.00002), V(80000.00003, 40000), V(-0.00001, 199999.99997), V(160000.00002, 119999.99999) },
            };
            foreach (var rule in Rules)
            {
                Assert.That(Faults(RingFill.Fill(direct, rule), direct, rule), Is.Empty, rule.ToString());
                Assert.That(Faults(Fill(rule, rounded), rounded, rule), Is.Empty, rule.ToString());
            }
        }

        // Coordinates whose squares would leave the doubles are brought near one by a power of two, which loses no digit.
        [TestCase(1e160)]
        [TestCase(1e300)]
        [TestCase(1e-200)]
        [TestCase(1e-310)]
        public void HugeOrTinyCoordinates_Fill(double s)
        {
            var clear = new[] { Rect(0, 0, 4, 4), Tri(1, 1, 2, 3, 3, 1) };
            var crossing = new[] { Rect(0, 0, 2, 2), Rect(1, 1, 3, 3) };
            foreach (var rule in Rules)
            {
                foreach (var (name, rings) in new[] { ("clear", clear), ("crossing", crossing) })
                {
                    var tris = RingFill.Fill(Array.ConvertAll(rings, ring => Array.ConvertAll(ring, p => p * s)), rule);
                    for (var i = 0; i < tris.Count; i++) tris[i] /= s;
                    Assert.That(Faults(tris, rings, rule), Is.Empty, $"{name} {rule}");
                }
            }
        }

        // Ends nearer than the weld across x - one ring ending where the other begins, a hair further on.
        [Test]
        public void RingsNearlyMeetingAcrossX_Meet()
        {
            Assert.That(RingContacts.AnyMeet(new[] { Tri(-10, 0, 0, 0, -5, 5), Tri(1e-12, 0, 10, 0, 5, 5) }), Is.True);
            Assert.That(RingContacts.AnyMeet(new[] { Tri(-10, -1, 0, 0, -10, 1), Tri(1e-12, -5, 1.0000001e-12 + 1e-13, 5, 10, 0) }), Is.True);
            Assert.That(RingContacts.AnyMeet(new[] { Tri(-10, 0, 0, 0, -5, 5), Tri(1e-3, 0, 10, 0, 5, 5) }), Is.False);
        }

        [Test]
        public void ContoursThatAreNoRings_FillNothing()
        {
            foreach (var contours in new[]
                     {
                         new[] { new[] { V(0, 0), V(double.NaN, 0), V(1, 1) } },
                         new[] { new[] { V(0, 0), V(double.PositiveInfinity, 0), V(1, 1) }, new[] { V(0, 0), V(1, 1) } },
                         new[] { new[] { V(0, 0), V(1, 1) }, new[] { V(0, 1), V(1, 0) } },
                     })
            {
                var p = new Polygon();
                foreach (var contour in contours) p.AddContour(new MeshContour(contour));
                Assert.That(p.FillIndirect(), Is.Empty);
            }
        }

        // Rings of the two areas that keep clear of one another are combined by how they nest, without cutting; the
        // result must fill as the mode says all the same.
        [Test]
        public void CombineModes_OfRingsKeepingClear_FillAsTheirRuleSays()
        {
            var random = new Random(91);
            var modes = new (AreaCombineMode Mode, Func<bool, bool, bool> Fills)[]
            {
                (AreaCombineMode.Union, (a, b) => a || b), (AreaCombineMode.Intersect, (a, b) => a && b),
                (AreaCombineMode.Exclude, (a, b) => a && !b), (AreaCombineMode.Xor, (a, b) => a ^ b),
            };
            var wrong = new List<string>();
            for (var n = 0; n < 200; n++)
            {
                var (first, second) = ClearRings(random);
                Assert.That(RingContacts.AnyMeet(first.Concat(second).ToArray()), Is.False, "the rings keep clear");
                foreach (var ruleA in Rules)
                {
                    foreach (var ruleB in Rules)
                    {
                        foreach (var (mode, fills) in modes)
                        {
                            var combined = GeometryOverlay.Combine(new RingArea(first, ruleA), new RingArea(second, ruleB), mode);
                            var tris = RingFill.Fill(combined, FillRule.NonZero);
                            var fault = CombineFault(tris, first, ruleA, second, ruleB, fills) ??
                                        TurnFault(combined, first, ruleA, second, ruleB, fills);
                            if (fault != null && wrong.Count < 5) wrong.Add($"{mode} {ruleA}/{ruleB}: {Describe(first)} with {Describe(second)} - {fault}");
                        }
                    }
                }
            }

            Assert.That(wrong, Is.Empty);
        }

        // A corner as far from the other ring's edge as the weld reaches is welded onto it, as the overlay does, rather
        // than taken as clear by the nesting shortcut: the two measure nearness a little differently at that edge.
        [Test]
        public void ACornerAtTheWeldDistance_IsWeldedOntoTheEdge()
        {
            var a = new[] { V(0, 0), V(7.7, 0), V(0, 5.929) };
            var b = new[] { V(3.8500000004697723, 2.964500000610094), V(7.7, 7.7), V(6.930000000000001, 7.7) };
            var union = GeometryOverlay.Combine(new RingArea([a], FillRule.EvenOdd), new RingArea([b], FillRule.EvenOdd), AreaCombineMode.Union);
            Assert.That(union.Sum(ring => ring.Length), Is.EqualTo(7), "the hypotenuse is cut at the corner");
        }

        // Shapes rounded to a step meet across a gap of less than that step: a weld that long closes it, the least weld
        // does not.
        [Test]
        public void AWeldGiven_ClosesAGapShorterThanIt()
        {
            var left = new RingArea([Rect(0, 0, 60, 100)], FillRule.EvenOdd);
            var right = new RingArea([Rect(60.000001, 0, 120, 100)], FillRule.EvenOdd);
            Assert.Multiple(() =>
            {
                Assert.That(GeometryOverlay.Combine(left, right, AreaCombineMode.Union), Has.Count.EqualTo(2));
                Assert.That(GeometryOverlay.Combine(left, right, AreaCombineMode.Union, 1e-4), Has.Count.EqualTo(1));
                Assert.That(() => GeometryOverlay.Combine(left, right, AreaCombineMode.Union, double.NaN), Throws.InstanceOf<ArgumentOutOfRangeException>());
            });
        }

        // The weld scales with huge coordinates as they are brought near one and back.
        [Test]
        public void AWeldGiven_HoldsForHugeCoordinates()
        {
            const double s = 1e120;
            var left = new RingArea([Rect(0, 0, 60 * s, 100 * s)], FillRule.EvenOdd);
            var right = new RingArea([Rect(60.000001 * s, 0, 120 * s, 100 * s)], FillRule.EvenOdd);
            Assert.That(GeometryOverlay.Combine(left, right, AreaCombineMode.Union, 1e-4 * s), Has.Count.EqualTo(1));
        }

        // Two corners at one place along an edge, either side of it within the weld, cut it in the order they were
        // found.
        [Test]
        public void CutsAtOnePlaceAlongAnEdge_KeepTheOrderTheyWereFound()
        {
            const double m = 96e-10;
            var a = Rect(0, 0, 96, 50);
            var up = new[] { V(30, 0.9 * m), V(50, 90), V(52, 90) };
            var down = new[] { V(30, -0.9 * m), V(31, -40), V(28, -40) };
            var union = GeometryOverlay.Combine(new RingArea([a], FillRule.EvenOdd), new RingArea([up, down], FillRule.EvenOdd), AreaCombineMode.Union);
            var ring = union.Single(r => Array.IndexOf(r, V(0, 0)) >= 0);
            var at = Array.IndexOf(ring, V(0, 0));
            Assert.That(ring[(at + 1) % ring.Length], Is.EqualTo(down[0]));
        }

        // An open contour that ends where it began keeps its last point, so its stroke runs its last leg; a closed one
        // drops it, the closing side already being there.
        [Test]
        public void AnOpenContourEndingAtItsStart_KeepsItsLastPoint()
        {
            var points = new[] { V(0, 0), V(10, 0), V(10, 10), V(0, 0) };
            Assert.Multiple(() =>
            {
                Assert.That(new MeshContour(points, isGeometryClosed: false).Points, Has.Length.EqualTo(4));
                Assert.That(new MeshContour(points, isGeometryClosed: true).Points, Has.Length.EqualTo(3));
            });
        }

        // Squares around a few centres, each a different size and turned either way, dealt to the two areas at random:
        // nested within a centre, apart across centres, never meeting.
        static (Vector2<double>[][] First, Vector2<double>[][] Second) ClearRings(Random random)
        {
            var first = new List<Vector2<double>[]>();
            var second = new List<Vector2<double>[]>();
            var centres = new[] { V(24, 24), V(72, 24), V(24, 72), V(72, 72) };
            foreach (var centre in centres)
            {
                var sizes = Enumerable.Range(1, 10).OrderBy(_ => random.Next()).Take(random.Next(4)).ToList();
                foreach (var size in sizes)
                {
                    var half = 2.1 * size + 0.37;
                    var square = Rect(centre.X - half, centre.Y - half, centre.X + half, centre.Y + half);
                    if (random.Next(2) == 0) square = Reversed(square);
                    (random.Next(2) == 0 ? first : second).Add(square);
                }
            }

            // Small rectangles in columns down the middle band, off centre in their cells: rings sharing an x, met by the
            // sweep at one place, and edges running straight up.
            for (var column = 0; column < 4; column++)
            {
                for (var row = 0; row < 3; row++)
                {
                    if (random.Next(2) == 0) continue;
                    var (x, y) = (2 + 24 * column, 47.2 + 0.6 * row);
                    var cell = random.Next(2) == 0 ? Rect(x, y, x + 1.5 + random.Next(3), y + 0.4) : Reversed(Rect(x, y, x + 2, y + 0.4));
                    (random.Next(2) == 0 ? first : second).Add(cell);
                }
            }

            return (first.ToArray(), second.ToArray());
        }

        static Vector2<double>[][] RandomRings(Random random)
        {
            var rings = new Vector2<double>[1 + random.Next(2)][];
            for (var r = 0; r < rings.Length; r++)
            {
                rings[r] = new Vector2<double>[3 + random.Next(3)];
                for (var k = 0; k < rings[r].Length; k++) rings[r][k] = V(16 * random.Next(7), 16 * random.Next(7));
            }

            return rings;
        }

        static string CombineFault(List<Vector3<double>> tris, Vector2<double>[][] first, FillRule ruleA, Vector2<double>[][] second,
            FillRule ruleB, Func<bool, bool, bool> fills)
        {
            for (var j = 0; j < 41; j++)
            {
                for (var i = 0; i < 41; i++)
                {
                    var pt = V(96.0 * (i + 0.3719) / 41, 96.0 * (j + 0.6143) / 41);
                    if (Array.Exists(first, ring => Distance(ring, pt) < 0.1) || Array.Exists(second, ring => Distance(ring, pt) < 0.1)) continue;
                    int wa = 0, wb = 0;
                    foreach (var ring in first) wa += Winding(ring, pt);
                    foreach (var ring in second) wb += Winding(ring, pt);
                    var inA = ruleA == FillRule.NonZero ? wa != 0 : (wa & 1) != 0;
                    var inB = ruleB == FillRule.NonZero ? wb != 0 : (wb & 1) != 0;
                    var covered = Covers(tris, pt);
                    if (covered != (fills(inA, inB) ? 1 : 0)) return $"({pt.X}, {pt.Y}) covered {covered} times";
                }
            }

            return null;
        }

        static List<Vector3<double>> Fill(FillRule rule, Vector2<double>[][] rings)
        {
            var p = new Polygon { FillRule = rule };
            foreach (var ring in rings) p.AddContour(new MeshContour(ring));
            return p.FillIndirect();
        }

        // The contour rounds to MathHelper.Coordinates: the truth is the rings as it keeps them.
        static Vector2<double>[][] Rounded(Vector2<double>[][] rings) =>
            Array.ConvertAll(rings, ring => Array.ConvertAll(ring, p => MathHelper.Coordinates.Round(p)));

        // Points covered other than the rule says, or twice; points within a thousandth of the size of an edge are left
        // out.
        static List<string> Faults(List<Vector3<double>> tris, Vector2<double>[][] rings, FillRule rule)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var ring in rings)
                foreach (var p in ring)
                    (minX, minY, maxX, maxY) = (Math.Min(minX, p.X), Math.Min(minY, p.Y), Math.Max(maxX, p.X), Math.Max(maxY, p.Y));
            var size = Math.Max(maxX - minX, maxY - minY);
            var faults = new List<string>();
            for (var j = 0; j < 61; j++)
            {
                for (var i = 0; i < 61; i++)
                {
                    var pt = V(minX + (maxX - minX) * (i + 0.3719) / 61, minY + (maxY - minY) * (j + 0.6143) / 61);
                    if (Array.Exists(rings, ring => Distance(ring, pt) < size * 1e-3)) continue;
                    var winding = 0;
                    foreach (var ring in rings) winding += Winding(ring, pt);
                    var filled = rule == FillRule.NonZero ? winding != 0 : (winding & 1) != 0;
                    var covered = Covers(tris, pt);
                    if (covered != (filled ? 1 : 0) && faults.Count < 3)
                        faults.Add($"({pt.X}, {pt.Y}) should be {(filled ? "filled" : "empty")}, covered {covered} times");
                }
            }

            return faults;
        }

        static int Covers(List<Vector3<double>> tris, Vector2<double> p)
        {
            var count = 0;
            for (var t = 0; t + 2 < tris.Count; t += 3)
            {
                var (a, b, c) = ((Vector2<double>)tris[t], (Vector2<double>)tris[t + 1], (Vector2<double>)tris[t + 2]);
                var d1 = Vector2<double>.Determinant(b - a, p - a);
                var d2 = Vector2<double>.Determinant(c - b, p - b);
                var d3 = Vector2<double>.Determinant(a - c, p - c);
                if ((d1 > 0 && d2 > 0 && d3 > 0) || (d1 < 0 && d2 < 0 && d3 < 0)) count++;
            }

            return count;
        }

        // The combined rings turn with their kept side on the left: around a kept point they wind once, around any other
        // not at all.
        static string TurnFault(List<Vector2<double>[]> combined, Vector2<double>[][] first, FillRule ruleA, Vector2<double>[][] second,
            FillRule ruleB, Func<bool, bool, bool> fills)
        {
            for (var j = 0; j < 41; j++)
            {
                for (var i = 0; i < 41; i++)
                {
                    var pt = V(96.0 * (i + 0.3719) / 41, 96.0 * (j + 0.6143) / 41);
                    if (Array.Exists(first, ring => Distance(ring, pt) < 0.1) || Array.Exists(second, ring => Distance(ring, pt) < 0.1)) continue;
                    int wa = 0, wb = 0, wc = 0;
                    foreach (var ring in first) wa += Winding(ring, pt);
                    foreach (var ring in second) wb += Winding(ring, pt);
                    foreach (var ring in combined) wc += Winding(ring, pt);
                    var inA = ruleA == FillRule.NonZero ? wa != 0 : (wa & 1) != 0;
                    var inB = ruleB == FillRule.NonZero ? wb != 0 : (wb & 1) != 0;
                    if (wc != (fills(inA, inB) ? 1 : 0)) return $"({pt.X}, {pt.Y}) wound {wc} times";
                }
            }

            return null;
        }

        static int Winding(Vector2<double>[] ring, Vector2<double> p)
        {
            var winding = 0;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                var (a, b) = (ring[j], ring[i]);
                var side = (b.X - a.X) * (p.Y - a.Y) - (p.X - a.X) * (b.Y - a.Y);
                if (a.Y <= p.Y && b.Y > p.Y && side > 0) winding++;
                else if (b.Y <= p.Y && a.Y > p.Y && side < 0) winding--;
            }

            return winding;
        }

        static double Distance(Vector2<double>[] ring, Vector2<double> p)
        {
            var best = double.MaxValue;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                var (a, d) = (ring[j], ring[i] - ring[j]);
                var length = d.X * d.X + d.Y * d.Y;
                var t = length > 0 ? Math.Clamp(((p.X - a.X) * d.X + (p.Y - a.Y) * d.Y) / length, 0, 1) : 0;
                best = Math.Min(best, (a + d * t - p).Length());
            }

            return best;
        }

        static string Describe(Vector2<double>[][] rings) =>
            string.Join(" | ", Array.ConvertAll(rings, ring => "{" + string.Join(" ", Array.ConvertAll(ring, p => $"({p.X:R};{p.Y:R})")) + "}"));

        static Vector2<double>[] Reversed(Vector2<double>[] ring)
        {
            var copy = (Vector2<double>[])ring.Clone();
            Array.Reverse(copy);
            return copy;
        }

        static Vector2<double> V(double x, double y) => new Vector2<double>(x, y);

        static Vector2<double>[] Tri(double x1, double y1, double x2, double y2, double x3, double y3) => new[] { V(x1, y1), V(x2, y2), V(x3, y3) };

        static Vector2<double>[] Rect(double x0, double y0, double x1, double y1) => new[] { V(x0, y0), V(x1, y0), V(x1, y1), V(x0, y1) };
    }
}

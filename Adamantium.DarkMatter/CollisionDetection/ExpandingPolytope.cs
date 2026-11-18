using System;
using System.Collections.Generic;

namespace Adamantium.DarkMatter.CollisionDetection;

/// <summary>How deep two overlapping cores go into each other, by EPA (the expanding polytope algorithm): the face of
/// their difference nearest the origin, the polytope grown toward it from GJK's last simplex, in double as GJK is.
/// Kept from pair to pair, so it allocates nothing once warm.</summary>
internal sealed class ExpandingPolytope
{
    private const int MaxIterations = 64;

    // The starting tetrahedron and a point an iteration.
    private const int MaxVertices = MaxIterations + 4;
    private const double RelativeTolerance = 1e-6;

    private readonly List<SupportVertex> _vertices = new(MaxVertices);
    private readonly List<PolytopeFace> _faces = new(2 * MaxVertices);
    private readonly List<int> _horizon = new(64);

    /// <summary>Grows the polytope from <paramref name="simplex"/> to the face of the difference nearest the origin: the
    /// normal from the first core to the second, how far the second must move along it to part them, and the deepest
    /// points of each. For cores that only just touch - <paramref name="touching"/> - the depth is none and the face's
    /// plane one the whole difference stands behind, but of several such it may be any. False when no polytope can be
    /// made or grown that far. <paramref name="tolerance"/> is how near it counts as no depth.</summary>
    public bool TryExpand(in PlacedCore first, in PlacedCore second, GjkSimplex simplex, out Vector3F normal,
        out float depth, out Vector3F onFirst, out Vector3F onSecond, out bool touching, out float tolerance)
    {
        normal = default;
        depth = 0;
        onFirst = default;
        onSecond = default;
        touching = false;
        tolerance = 0;
        _vertices.Clear();
        _faces.Clear();
        for (var index = 0; index < simplex.Count; index++)
        {
            _vertices.Add(simplex.Vertices[index]);
        }

        if (!TryCompleteTetrahedron(first, second))
            return false;

        var scale = 0.0;
        foreach (var vertex in _vertices)
        {
            scale = Math.Max(scale, vertex.Point.Length());
        }

        var nearTolerance = RelativeTolerance * Math.Max(scale, 1e-3);
        tolerance = (float)nearTolerance;
        if (!AddFace(0, 1, 2) || !AddFace(0, 3, 1) || !AddFace(0, 2, 3) || !AddFace(1, 3, 2))
            return false;

        // Grown to its last iteration, the nearest face stands, as near as it got - as Jolt and Bullet take it. A
        // face reached exactly has the whole difference behind its plane, so its normal holds at no depth too.
        var found = default(PolytopeFace);
        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var nearest = FindNearestFace();
            if (nearest < 0)
                return false;

            found = _faces[nearest];
            if (iteration == MaxIterations - 1)
                break;

            var vertex = PlacedCore.SupportOfDifference(first, second, found.Normal);
            var reach = Vector3<double>.Dot(vertex.Point, found.Normal);
            if (reach - found.Distance <= nearTolerance)
                break;

            // A face that cannot be made would leave the polytope open: grown no further, nothing is known.
            var added = _vertices.Count;
            _vertices.Add(vertex);
            if (!GrowToward(added, nearTolerance))
                return false;
        }

        // Face distances only grow, so a pair still at no depth is one that only just touches.
        touching = found.Distance <= nearTolerance;
        normal = found.Normal.As<float>();
        depth = (float)Math.Max(found.Distance, 0);
        FindWitnesses(found, out onFirst, out onSecond);
        return true;
    }

    // Makes the simplex a tetrahedron: points along the directions it does not reach yet.
    private bool TryCompleteTetrahedron(in PlacedCore first, in PlacedCore second)
    {
        if (_vertices.Count == 1)
        {
            ReadOnlySpan<int> signs = [1, -1];
            for (var axis = 0; axis < 3 && _vertices.Count == 1; axis++)
            {
                foreach (var sign in signs)
                {
                    var direction = AxisMath.GetUnitAxis(axis).As<double>() * sign;
                    var vertex = PlacedCore.SupportOfDifference(first, second, direction);
                    if ((vertex.Point - _vertices[0].Point).LengthSquared() > 1e-18)
                    {
                        _vertices.Add(vertex);
                        break;
                    }
                }
            }
        }

        if (_vertices.Count == 2)
        {
            var line = _vertices[1].Point - _vertices[0].Point;
            if (line.LengthSquared() < 1e-18)
                return false;

            var unitLine = Vector3<double>.Normalize(line);
            var across = AxisMath.GetPerpendicular(unitLine.As<float>()).As<double>();
            var turn = Quaternion.CreateFromAxisAngle(unitLine, Math.PI / 3);
            for (var step = 0; step < 6 && _vertices.Count == 2; step++)
            {
                var vertex = PlacedCore.SupportOfDifference(first, second, across);
                var offLine = Vector3<double>.Cross(vertex.Point - _vertices[0].Point, line);
                if (offLine.LengthSquared() > 1e-18 * line.LengthSquared())
                    _vertices.Add(vertex);

                across = Vector3<double>.Transform(across, turn);
            }
        }

        if (_vertices.Count == 3)
        {
            var faceNormal = Vector3<double>.Cross(_vertices[1].Point - _vertices[0].Point,
                _vertices[2].Point - _vertices[0].Point);
            if (faceNormal.LengthSquared() < 1e-30)
                return false;

            var vertex = PlacedCore.SupportOfDifference(first, second, faceNormal);
            if (Math.Abs(Vector3<double>.Dot(vertex.Point - _vertices[0].Point, faceNormal)) < 1e-15 * faceNormal.Length())
                vertex = PlacedCore.SupportOfDifference(first, second, -faceNormal);

            _vertices.Add(vertex);
        }

        if (_vertices.Count != 4)
            return false;

        // Wound so that each face added from the corners turns outward.
        var a = _vertices[0].Point;
        var volume = Vector3<double>.Dot(Vector3<double>.Cross(_vertices[1].Point - a, _vertices[2].Point - a),
            _vertices[3].Point - a);
        if (Math.Abs(volume) < 1e-18)
            return false;

        if (volume > 0)
        {
            var swapped = _vertices[1];
            _vertices[1] = _vertices[2];
            _vertices[2] = swapped;
        }

        return true;
    }

    private bool AddFace(int a, int b, int c)
    {
        var pointA = _vertices[a].Point;
        var normal = Vector3<double>.Cross(_vertices[b].Point - pointA, _vertices[c].Point - pointA);
        var length = normal.Length();
        if (!(length > 1e-18))
            return false;

        normal /= length;
        _faces.Add(new PolytopeFace(a, b, c, normal, Vector3<double>.Dot(normal, pointA)));
        return true;
    }

    private int FindNearestFace()
    {
        var nearest = -1;
        var distance = double.MaxValue;
        for (var index = 0; index < _faces.Count; index++)
        {
            if (_faces[index].Distance < distance)
            {
                distance = _faces[index].Distance;
                nearest = index;
            }
        }

        return nearest;
    }

    // Takes away the faces the new point sees and closes the hole with faces from the edge of it to the point; false
    // when the hole cannot be closed.
    private bool GrowToward(int added, double tolerance)
    {
        var point = _vertices[added].Point;
        _horizon.Clear();
        for (var index = _faces.Count - 1; index >= 0; index--)
        {
            var face = _faces[index];
            if (Vector3<double>.Dot(face.Normal, point) - face.Distance <= tolerance * 0.01)
                continue;

            AddHorizonEdge(face.A, face.B);
            AddHorizonEdge(face.B, face.C);
            AddHorizonEdge(face.C, face.A);
            _faces[index] = _faces[^1];
            _faces.RemoveAt(_faces.Count - 1);
        }

        if (_horizon.Count == 0)
            return false;

        for (var index = 0; index < _horizon.Count; index += 2)
        {
            if (!AddFace(_horizon[index], _horizon[index + 1], added))
                return false;
        }

        return true;
    }

    // An edge two seen faces share is inside the hole, not on its edge: the second time it comes, it goes.
    private void AddHorizonEdge(int from, int to)
    {
        for (var index = 0; index < _horizon.Count; index += 2)
        {
            if (_horizon[index] == to && _horizon[index + 1] == from)
            {
                _horizon.RemoveAt(index + 1);
                _horizon.RemoveAt(index);
                return;
            }
        }

        _horizon.Add(from);
        _horizon.Add(to);
    }

    // The points of each core at the point of the face nearest the origin's foot on its plane - inside the triangle,
    // so a face among ties whose plane the origin meets off its edge still gives points on the shapes (Ericson, 5.1.5).
    private void FindWitnesses(in PolytopeFace face, out Vector3F onFirst, out Vector3F onSecond)
    {
        var a = _vertices[face.A];
        var b = _vertices[face.B];
        var c = _vertices[face.C];
        var projected = face.Normal * face.Distance;
        FindNearestWeights(a.Point, b.Point, c.Point, projected, out var weightA, out var weightB, out var weightC);
        onFirst = (a.OnFirst.As<double>() * weightA + b.OnFirst.As<double>() * weightB + c.OnFirst.As<double>() * weightC)
            .As<float>();
        onSecond = (a.OnSecond.As<double>() * weightA + b.OnSecond.As<double>() * weightB
                    + c.OnSecond.As<double>() * weightC).As<float>();
    }

    private static void FindNearestWeights(Vector3<double> a, Vector3<double> b, Vector3<double> c, Vector3<double> p,
        out double weightA, out double weightB, out double weightC)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        var d1 = Vector3<double>.Dot(ab, ap);
        var d2 = Vector3<double>.Dot(ac, ap);
        weightA = 1;
        weightB = 0;
        weightC = 0;
        if (d1 <= 0 && d2 <= 0)
            return;

        var bp = p - b;
        var d3 = Vector3<double>.Dot(ab, bp);
        var d4 = Vector3<double>.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3)
        {
            weightA = 0;
            weightB = 1;
            return;
        }

        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            weightB = d1 / (d1 - d3);
            weightA = 1 - weightB;
            return;
        }

        var cp = p - c;
        var d5 = Vector3<double>.Dot(ab, cp);
        var d6 = Vector3<double>.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6)
        {
            weightA = 0;
            weightC = 1;
            return;
        }

        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            weightC = d2 / (d2 - d6);
            weightA = 1 - weightC;
            return;
        }

        var va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
        {
            weightC = (d4 - d3) / (d4 - d3 + (d5 - d6));
            weightA = 0;
            weightB = 1 - weightC;
            return;
        }

        var sum = va + vb + vc;
        if (!(sum > 0))
            return;

        weightB = vb / sum;
        weightC = vc / sum;
        weightA = 1 - weightB - weightC;
    }
}

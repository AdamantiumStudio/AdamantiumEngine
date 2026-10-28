using System.Linq;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests
{
    /// <summary>
    /// The basis change lives in two places: <see cref="Mesh.ChangeCoordinateSystem"/> moves the vertices, while
    /// <see cref="AxisConversion"/> moves the bones and the animation key frames. They must not drift apart, or the
    /// skeleton ends up flipped relative to its model again. This is where they are checked against each other.
    /// </summary>
    [TestFixture]
    public class AxisConversionTests
    {
        private static readonly (UpAxis From, UpAxis To)[] Pairs =
        {
            (UpAxis.Z_UP, UpAxis.Y_UP_RH),
            (UpAxis.Z_UP, UpAxis.Y_UP_LH),
            (UpAxis.Z_UP, UpAxis.Y_DOWN_RH),
            (UpAxis.Y_UP_RH, UpAxis.Y_UP_LH),
            (UpAxis.Y_UP_RH, UpAxis.Y_DOWN_RH),
            (UpAxis.Y_UP_RH_FRONT_Z, UpAxis.Y_DOWN_RH),
        };

        [Test]
        public void Matrix_MovesAPointExactlyAsTheMeshDoes([ValueSource(nameof(Pairs))] (UpAxis From, UpAxis To) pair)
        {
            // An asymmetric point: on (1,1,1) an axis swap is indistinguishable from identity
            var point = new Vector3<double>(2, 3, 5);

            var mesh = new Mesh(PrimitiveType.TriangleList) { UpAxis = pair.From };
            mesh.SetPoints([point]);
            mesh.ChangeCoordinateSystem(pair.To);

            var byMatrix = Vector3F.TransformCoordinate(point.As<float>(), AxisConversion.Between(pair.From, pair.To));

            Assert.That(mesh.Points[0].As<float>(), Is.EqualTo(byMatrix).Using<Vector3F>(
                (a, b) => (a - b).Length() < 0.0001f ? 0 : 1),
                $"{pair.From} -> {pair.To}: the mesh and the matrix sent the point to different places");
        }

        private static readonly (UpAxis From, UpAxis To)[] IntoTheEngine = Pairs.Where(p => p.To == UpAxis.Y_DOWN_RH).ToArray();

        /// <summary>A normal the importer works out itself points out of the side the file meant as the front. As
        /// ConverterBase does it: the axis change, then the winding turned round - a file's front is the side it is
        /// counter-clockwise from, the engine's the side it is clockwise from - and only then the normal, which
        /// Optimize was told not to compute.</summary>
        [Test]
        public void ANormalDerivedOnImport_PointsOutOfTheFilesFront(
            [ValueSource(nameof(IntoTheEngine))] (UpAxis From, UpAxis To) pair)
        {
            var mesh = Triangle(pair.From);
            var front = Vector3F.Normalize(
               Vector3F.TransformNormal(Vector3F.Normalize(Facing(mesh)), AxisConversion.Between(pair.From, pair.To)));

            mesh.ChangeCoordinateSystem(pair.To);
            mesh.ReverseWinding();
            mesh.CalculateNormals();

            Assert.That(Vector3F.Dot(front, Vector3F.Normalize(mesh.Normals[0])), Is.GreaterThan(0.99f),
                $"{pair.From} -> {pair.To}");
        }

        /// <summary>The mesh and the matrix agreeing proves nothing if both are wrong: the file's up has to come out as
        /// the engine's, which is -Y. A Z_UP model used to stand on its head.</summary>
        [Test]
        public void TheFilesUp_BecomesTheEnginesUp([ValueSource(nameof(IntoTheEngine))] (UpAxis From, UpAxis To) pair)
        {
            var up = pair.From == UpAxis.Z_UP ? new Vector3<double>(0, 0, 1) : new Vector3<double>(0, 1, 0);

            var mesh = new Mesh(PrimitiveType.TriangleList) { UpAxis = pair.From };
            mesh.SetPoints([up]);
            mesh.ChangeCoordinateSystem(pair.To);

            Assert.That((mesh.Points[0].As<float>() - new Vector3F(0, -1, 0)).Length(), Is.LessThan(0.0001f),
                $"{pair.From} -> {pair.To}");
        }

        /// <summary>A model has to face where the engine's models face, -Z. Blender and Max put a model's front to -Y
        /// of a Z_UP file, glTF to +Z; a Z_UP model used to turn its back.</summary>
        [TestCase(UpAxis.Z_UP, 0, -1, 0)]
        [TestCase(UpAxis.Y_UP_RH_FRONT_Z, 0, 0, 1)]
        public void TheFilesFront_BecomesTheEnginesFront(UpAxis from, double x, double y, double z)
        {
            var mesh = new Mesh(PrimitiveType.TriangleList) { UpAxis = from };
            mesh.SetPoints([new Vector3<double>(x, y, z)]);
            mesh.ChangeCoordinateSystem(UpAxis.Y_DOWN_RH);

            Assert.That((mesh.Points[0].As<float>() - new Vector3F(0, 0, -1)).Length(), Is.LessThan(0.0001f));
        }

        /// <summary>A triangle has to keep facing the way it faced. Whatever the axis change does to the points, the
        /// side the winding names must come out as the mapped original side - which is only true if the winding is
        /// reversed exactly when the change flips handedness, and left alone when it is a plain rotation.</summary>
        [Test]
        public void TheAxisChangeKeepsATriangleFacingTheSameWay(
            [ValueSource(nameof(Pairs))] (UpAxis From, UpAxis To) pair)
        {
            var mesh = Triangle(pair.From);
            var before = Vector3F.Normalize(Facing(mesh));

            mesh.ChangeCoordinateSystem(pair.To);
            var after = Vector3F.Normalize(Facing(mesh));

            var expected = Vector3F.Normalize(
               Vector3F.TransformNormal(before, AxisConversion.Between(pair.From, pair.To)));

            Assert.That(Vector3F.Dot(after, expected), Is.GreaterThan(0.99f),
                $"{pair.From} -> {pair.To}: the triangle ended up facing the other way");
        }

        //Which side the winding names
        private static Vector3F Facing(Mesh mesh)
        {
            var a = mesh.Points[mesh.Indices[0]].As<float>();
            var b = mesh.Points[mesh.Indices[1]].As<float>();
            var c = mesh.Points[mesh.Indices[2]].As<float>();
            return Vector3F.Cross(b - a, c - a);
        }

        private static Mesh Triangle(UpAxis upAxis)
        {
            var mesh = new Mesh(PrimitiveType.TriangleList) { UpAxis = upAxis };
            mesh.SetPoints([new Vector3<double>(0, 0, 0), new Vector3<double>(2, 0, 0), new Vector3<double>(0, 3, 0)]);
            mesh.SetIndices([0, 1, 2]);
            return mesh;
        }

        [Test]
        public void Matrix_IsIdentityForAnUnsupportedPair()
        {
            // Changing nothing beats mangling it in silence
            Assert.That(AxisConversion.Between(UpAxis.Y_DOWN_RH, UpAxis.Z_UP), Is.EqualTo(Matrix4x4F.Identity));
            Assert.That(AxisConversion.Between(UpAxis.Z_UP, UpAxis.Z_UP), Is.EqualTo(Matrix4x4F.Identity));
        }

        [Test]
        public void Convert_KeepsTheTranslationOfATransform([ValueSource(nameof(Pairs))] (UpAxis From, UpAxis To) pair)
        {
            // Moving a transform must send its translation exactly where the matrix sends a point
            var translation = new Vector3F(2, 3, 5);
            var conversion = AxisConversion.Between(pair.From, pair.To);

            var moved = AxisConversion.Convert(Matrix4x4.CreateTranslation<float>(translation), conversion);
            moved.Decompose(out _, out _, out var position);

            var expected = Vector3F.TransformCoordinate(translation, conversion);
            Assert.That((position - expected).Length(), Is.LessThan(0.0001f), $"{pair.From} -> {pair.To}");
        }
    }
}

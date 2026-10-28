using Adamantium.Core;
using Adamantium.Mathematics;
using Microsoft.VisualStudio.TestPlatform.Utilities;
using NUnit.Framework;

namespace Adamantium.MathTests
{
    public class Vector2DTests
    {
        [Test]
        public void IsVectorsNotCollinear()
        {
            var vector = new Vector2<double>(1, 0);
            var vector2 = new Vector2<double>(1, -26.94);
            var isCollinear = vector.IsCollinear(vector2);
            Assert.IsFalse(isCollinear, "Vectors are collinear");
        }
        
        [Test]
        public void IsVectorsCollinear()
        {
            var vector = new Vector2<double>(5, 50);
            var vector2 = new Vector2<double>(6, 60);
            var isCollinear = vector.IsCollinear(vector2);
            Assert.IsTrue(isCollinear, "Vectors are NOT collinear");
        }
        
        [Test]
        public void GetHashSetTest()
        {
            var v1 = new Vector2<double>(100, 200);
            var v2 = new Vector2<double>(200, 100);

            var h1 = v1.GetHashCode();
            var h2 = v2.GetHashCode();

            Assert.AreNotEqual(h1, h2);
        }
    }
}
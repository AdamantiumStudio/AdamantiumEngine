using System;
using Adamantium.Engine.Tools;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class GroundGridTests
{
    // Far from home the eye's place cannot go to the GPU as it is. Each level's lines are drawn from the eye's place
    // modulo that level's cells: worked out as the shader does, in floats, a point near the eye lies as far from the
    // nearest line as it does in the world - for cells a tenth of a unit wide too, where one phase for every level, or
    // the eye's place itself, leaves a float far too coarse to tell.
    [Test]
    public void FarFromHome_ThePointsNearTheEyeLieAsFarFromTheLinesAsInTheWorld()
    {
        var scene = new ToolScene();
        scene.LookFrom(new Vector3<double>(12_345_670, -10, -3_000_004), new Vector3<double>(12_345_670, 0, -2_999_994));
        var grid = new GroundGrid { CellSize = 0.1f };
        var phases = new Vector4F[GroundGrid.Levels / 2];
        grid.PhasesFor(scene.Camera, phases);
        var eye = scene.Camera.WorldPosition;

        var worst = 0.0;
        for (var offset = -5f; offset <= 5f; offset += 0.0137f)
        {
            worst = Math.Max(worst, Math.Abs(InShader(phases[0].X, offset, 0.1f) - InWorld(eye.X + offset, 0.1)));
            worst = Math.Max(worst, Math.Abs(InShader(phases[0].Y, offset, 0.1f) - InWorld(eye.Z + offset, 0.1)));
            worst = Math.Max(worst, Math.Abs(InShader(phases[0].Z, offset, 1f) - InWorld(eye.X + offset, 1)));
        }

        Assert.That(worst, Is.LessThan(1e-3), "share of a cell between the drawn and the true distance to a line");
    }

    [Test]
    public void NearHome_TheAxesCrossWhereTheWorldsDo_AndTheGridLiesOnTheGround()
    {
        var scene = new ToolScene();
        scene.LookFrom(new Vector3<double>(3, -10, 4), new Vector3<double>(3, 0, 14));
        var grid = new GroundGrid();

        var layout = grid.LayoutFor(scene.Camera);

        Assert.Multiple(() =>
        {
            Assert.That(layout.Axes.X, Is.EqualTo(-3f));
            Assert.That(layout.Axes.Y, Is.EqualTo(-4f));
            Assert.That(layout.Placement.M42, Is.EqualTo(10f), "y = 0 lies ten units below an eye at y = -10");
            Assert.That(layout.Reach, Is.EqualTo(600f), "sixty times the height");
        });
    }

    // Looking low, a flat view's height covers a long way of ground: the grid still lies whole round what it looks at.
    [Test]
    public void AFlatViewLookingLow_ReachesWellPastWhatItLooksAt()
    {
        var scene = new ToolScene();
        scene.Camera.ProjectionTransitionTime = 0;
        scene.Camera.ProjectionType = Adamantium.ECS.Components.CameraProjectionType.Ortho;
        scene.LookFrom(new Vector3<double>(0, -10, 0), new Vector3<double>(0, 0, 57));
        var grid = new GroundGrid();

        var layout = grid.LayoutFor(scene.Camera);

        var lookedAt = new Vector2F(0, 57);
        var center = layout.Center + new Vector2F((float)scene.Camera.WorldPosition.X, (float)scene.Camera.WorldPosition.Z);
        Assert.That(Vector2F.Distance(center, lookedAt), Is.LessThan(layout.Reach / 2));
    }

    [Test]
    public void ARaisedGrid_LiesAtItsY_AndReachesByTheEyesHeightAboveIt()
    {
        var scene = new ToolScene();
        scene.LookFrom(new Vector3<double>(3, -10, 4), new Vector3<double>(3, 0, 14));
        var grid = new GroundGrid { PlaneY = -4 };

        var layout = grid.LayoutFor(scene.Camera);

        Assert.Multiple(() =>
        {
            Assert.That(layout.Placement.M42, Is.EqualTo(6f), "y = -4 lies six units below an eye at y = -10");
            Assert.That(layout.Reach, Is.EqualTo(360f), "sixty times those six");
        });
    }

    [Test]
    public void ACellSizeThatIsNoSize_DrawsUnitCells()
    {
        var scene = new ToolScene();
        var grid = new GroundGrid { CellSize = float.NaN, ShownCellPoints = -3 };
        var phases = new Vector4F[GroundGrid.Levels / 2];

        Assert.Multiple(() =>
        {
            Assert.That(grid.LayoutFor(scene.Camera).Cell, Is.EqualTo(1f));
            Assert.That(() => grid.PhasesFor(scene.Camera, phases), Throws.Nothing);
            Assert.That(grid.ShownCellPointsOrDefault, Is.EqualTo(10f));
        });
    }

    private static double InShader(float phase, float offset, float cell)
    {
        var cells = (phase + offset) / cell - 0.5f;
        return Math.Abs(cells - MathF.Floor(cells) - 0.5f);
    }

    private static double InWorld(double at, double cell)
    {
        var cells = at / cell - 0.5;
        return Math.Abs(cells - Math.Floor(cells) - 0.5);
    }
}

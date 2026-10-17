using System;

namespace Adamantium.Graphics.Fonts;

/// <summary>A grid the baselines of a text stand on, as InDesign's baseline grid: lines every <paramref name="Step"/>,
/// one of them <paramref name="Offset"/> below the layout's top (any of them; it repeats). A line is moved down to the
/// first grid line at or below its own baseline, never up, so lines of every size and the columns of a text keep the
/// grid's rhythm. Horizontal text only.</summary>
/// <param name="Step">How far apart the grid's lines are, in the layout's units; finite and at least 1e-6.</param>
/// <param name="Offset">Where one of the grid's lines lies below the layout's top: the grid of an element above, seen
/// from this text. Finite.</param>
/// <param name="FirstLineOnly">Only the first line of each paragraph goes to the grid, as InDesign's "align only the
/// first line"; the lines after it keep their own spacing from it.</param>
public sealed record BaselineGrid(double Step, double Offset = 0, bool FirstLineOnly = false)
{
    private readonly double _step = ValidStep(Step);
    private readonly double _offset = ValidOffset(Offset);

    public double Step
    {
        get => _step;
        init => _step = ValidStep(value);
    }

    public double Offset
    {
        get => _offset;
        init => _offset = ValidOffset(value);
    }

    private const double LeastStep = 1e-6;

    private static double ValidStep(double step) => step >= LeastStep && !double.IsPositiveInfinity(step)
        ? step
        : throw new ArgumentOutOfRangeException(nameof(Step), step, "A grid's step is finite and at least 1e-6.");

    private static double ValidOffset(double offset) => !double.IsNaN(offset) && !double.IsInfinity(offset)
        ? offset
        : throw new ArgumentOutOfRangeException(nameof(Offset), offset, "A grid's offset is finite.");
}
